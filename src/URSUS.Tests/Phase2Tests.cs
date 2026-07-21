using Rhino.Geometry;
using URSUS.Analysis;
using URSUS.Caching;
using URSUS.DataSources;
using URSUS.Execution;
using URSUS.Config;
using URSUS.Visualization;
using URSUS.Geometry;
using URSUS.Net;
using URSUS.Setup;
using System.Net;

namespace URSUS.Tests;

internal static class Phase2Tests
{
    [Test]
    internal static void SetupSeoulValidation_DefaultAndValidateAllDoNotSendKeyOverHttp()
    {
        const string key = "seoul-secret-key-12345";
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"VwsmAdstrdRepopW\":{}}")
            });
        using var validator = new ApiKeyValidator(handler);

        // Legacy calls with an explicitly defaulted token must remain unambiguous.
        var single = validator.ValidateSeoulKeyAsync(key, default(CancellationToken))
            .GetAwaiter().GetResult();
        AssertEx.False(single.IsValid);
        AssertEx.Equal(ApiKeyValidator.ValidationErrorKind.RemoteValidationSkipped,
            single.ErrorKind);
        AssertEx.False(single.Message.Contains(key, StringComparison.Ordinal));

        var all = validator.ValidateAllAsync(new Dictionary<string, string>
        {
            [ApiKeyProvider.KEY_SEOUL] = key,
        }, default(CancellationToken)).GetAwaiter().GetResult();
        AssertEx.Equal(1, all.Count);
        AssertEx.Equal(ApiKeyValidator.ValidationErrorKind.RemoteValidationSkipped,
            all[0].Result.ErrorKind);
        AssertEx.Equal(0, handler.Calls);
    }

    [Test]
    internal static void SetupSeoulValidation_ExplicitOptInControlsNetworkAndRedactsFailures()
    {
        const string key = "seoul-secret-key-12345";
        var successHandler = new RecordingHandler(request =>
        {
            AssertEx.Equal(Uri.UriSchemeHttp, request.RequestUri!.Scheme);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"VwsmAdstrdRepopW\":{}}")
            };
        });
        using (var validator = new ApiKeyValidator(successHandler))
        {
            var result = validator.ValidateSeoulKeyAsync(key, new TransportPolicy(true),
                    default)
                .GetAwaiter().GetResult();
            AssertEx.True(result.IsValid);
            AssertEx.Equal(1, successHandler.Calls);

            var all = validator.ValidateAllAsync(new Dictionary<string, string>
            {
                [ApiKeyProvider.KEY_SEOUL] = key,
            }, new TransportPolicy(true), default).GetAwaiter().GetResult();
            AssertEx.Equal(1, all.Count);
            AssertEx.True(all[0].Result.IsValid);
            AssertEx.Equal(2, successHandler.Calls);
        }

        var failureHandler = new RecordingHandler(_ =>
            throw new HttpRequestException($"request failed for {key}"));
        using (var validator = new ApiKeyValidator(failureHandler))
        {
            var result = validator.ValidateSeoulKeyAsync(key, new TransportPolicy(true),
                    default)
                .GetAwaiter().GetResult();
            AssertEx.False(result.IsValid);
            AssertEx.Equal(ApiKeyValidator.ValidationErrorKind.NetworkError, result.ErrorKind);
            AssertEx.False(result.Message.Contains(key, StringComparison.Ordinal));
        }
    }

    [Test]
    internal static void RunCoordinator_RequiresEdgeAndDiscardsSupersededResult()
    {
        var pending = new List<TaskCompletionSource<SolverResult>>();
        var canceled = new List<CancellationToken>();
        var coordinator = new RunCoordinator((_, token, _) =>
        {
            canceled.Add(token);
            var source = new TaskCompletionSource<SolverResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add(source);
            return source.Task;
        });
        var request = Request("A");

        AssertEx.Equal(null, coordinator.ObserveRun(false, request));
        long first = coordinator.ObserveRun(true, request)!.Value;
        AssertEx.Equal(null, coordinator.ObserveRun(true, request));
        coordinator.ObserveRun(false, request);
        long second = coordinator.ObserveRun(true, Request("B"))!.Value;
        AssertEx.True(second > first);
        AssertEx.True(canceled[0].IsCancellationRequested);

        pending[0].SetResult(Result("11110101"));
        Thread.Sleep(10);
        AssertEx.Equal(RunState.Running, coordinator.Status.State);
        pending[1].SetResult(Result("11110102"));
        coordinator.CurrentTask!.GetAwaiter().GetResult();
        AssertEx.Equal(RunState.Succeeded, coordinator.Status.State);
        AssertEx.Equal("11110102", coordinator.LastSuccessfulSnapshot!.DistrictIndex.Single());
    }

    [Test]
    internal static void CancelledGenerationPreservesLastSuccessfulSnapshot()
    {
        int calls = 0;
        var coordinator = new RunCoordinator((_, token, _) =>
        {
            calls++;
            if (calls == 1) return Task.FromResult(Result("11110101"));
            return Task.Delay(TimeSpan.FromSeconds(10), token).ContinueWith(
                _ => Result("11110102"), token, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        });
        var request = Request("A");
        coordinator.ObserveRun(false, request);
        coordinator.ObserveRun(true, request);
        coordinator.CurrentTask!.GetAwaiter().GetResult();
        coordinator.ObserveRun(false, request);
        coordinator.ObserveRun(true, request);
        coordinator.ObserveCancel(false);
        AssertEx.True(coordinator.ObserveCancel(true));
        try { coordinator.CurrentTask!.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }

        AssertEx.Equal(RunState.Canceled, coordinator.Status.State);
        AssertEx.Equal("11110101", coordinator.LastSuccessfulSnapshot!.DistrictIndex.Single());
    }

    [Test]
    internal static void GenerationScopedCancellationReachesTheOwningExecutionOnly()
    {
        var completion = new TaskCompletionSource<SolverResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        var coordinator = new RunCoordinator((_, token, _) =>
        {
            observed = token;
            return completion.Task;
        });
        var request = Request("A");
        coordinator.ObserveRun(false, request);
        long generation = coordinator.ObserveRun(true, request)!.Value;

        AssertEx.False(coordinator.CancelGeneration(generation + 1));
        AssertEx.False(observed.IsCancellationRequested);
        AssertEx.True(coordinator.CancelGeneration(generation));
        AssertEx.True(observed.IsCancellationRequested);
        completion.SetResult(Result("11110101"));
        coordinator.CurrentTask!.GetAwaiter().GetResult();
        AssertEx.Equal(RunState.Canceled, coordinator.Status.State);
    }

    [Test]
    internal static void QueryChangeWhileTrueMarksStaleWithoutStartingFetch()
    {
        int fetches = 0;
        var coordinator = new RunCoordinator((_, _, _) =>
        {
            fetches++;
            return Task.FromResult(Result("11110101"));
        });
        var first = Request("A");
        coordinator.ObserveRun(false, first);
        coordinator.ObserveRun(true, first);
        coordinator.CurrentTask!.GetAwaiter().GetResult();

        AssertEx.Equal(null, coordinator.ObserveRun(true, Request("B")));
        AssertEx.Equal(1, fetches);
        AssertEx.True(coordinator.Status.IsStale);
    }

    [Test]
    internal static void QueryFingerprintIncludesTransportPeriodForceAndSpatialFieldsButNotWeights()
    {
        var baseline = Request("A", new[] { 1.0 });
        AssertEx.Equal(baseline.QueryFingerprint, Request("A", new[] { 99.0 }).QueryFingerprint);
        AssertEx.False(baseline.QueryFingerprint == Request("A", force: true).QueryFingerprint);
        AssertEx.False(baseline.QueryFingerprint == Request("A", period: "2025Q4").QueryFingerprint);
        AssertEx.False(baseline.QueryFingerprint == Request("A", insecure: true).QueryFingerprint);
        AssertEx.False(baseline.QueryFingerprint == Request("A", radius: 9).QueryFingerprint);
        AssertEx.False(baseline.QueryFingerprint == Request("B").QueryFingerprint);
    }

    [Test]
    internal static void RunAsync_CancellationPropagatesWithoutLegacyFallback()
    {
        var registry = new DataSourceRegistry();
        registry.Register(new EmptySource());
        var boundary = new BlockingBoundarySource();
        registry.RegisterBoundary(boundary);
        var solver = new URSUSSolver(new ApiKeyProvider(new Dictionary<string, string>
        {
            [ApiKeyProvider.KEY_VWORLD] = "secret",
        }), registry);
        using var cancellation = new CancellationTokenSource();
        Task<SolverResult> running = solver.RunAsync(new AnalysisRequest(
            Array.Empty<string>(), address1: "서울", keyFingerprint: "keys"), cancellation.Token);
        AssertEx.True(boundary.Started.Wait(TimeSpan.FromSeconds(1)));
        cancellation.Cancel();
        AssertEx.Throws<OperationCanceledException>(() => running.GetAwaiter().GetResult());
        AssertEx.Equal(1, boundary.Calls);
    }

    [Test]
    internal static void CancelRequestWinsAgainstIgnoredTokenSuccessAndFault()
    {
        foreach (bool fault in new[] { false, true })
        {
            var completion = new TaskCompletionSource<SolverResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var coordinator = new RunCoordinator((_, _, _) => completion.Task);
            var request = Request("A");
            coordinator.ObserveRun(false, request);
            coordinator.ObserveRun(true, request);
            coordinator.ObserveCancel(false);
            coordinator.ObserveCancel(true);
            if (fault) completion.SetException(new InvalidOperationException("late fault"));
            else completion.SetResult(Result("11110102"));
            coordinator.CurrentTask!.GetAwaiter().GetResult();
            AssertEx.Equal(RunState.Canceled, coordinator.Status.State);
            AssertEx.Equal(null, coordinator.LastSuccessfulSnapshot);
        }
    }

    [Test]
    internal static void QueryChangeDuringRunRemainsStaleAfterCompletion()
    {
        var completion = new TaskCompletionSource<SolverResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new RunCoordinator((_, _, _) => completion.Task);
        coordinator.ObserveRun(false, Request("A"));
        coordinator.ObserveRun(true, Request("A"));
        coordinator.ObserveRun(true, Request("B"));
        completion.SetResult(Result("11110101"));
        coordinator.CurrentTask!.GetAwaiter().GetResult();
        AssertEx.True(coordinator.Status.IsStale);
    }

    [Test]
    internal static void RestoredSnapshotFingerprintKeepsLaterFailedQueryStale()
    {
        int calls = 0;
        var coordinator = new RunCoordinator((_, _, _) =>
        {
            calls++;
            return calls == 1
                ? Task.FromResult(Result("11110101"))
                : Task.FromException<SolverResult>(new InvalidOperationException("failed B"));
        });
        AnalysisRequest requestA = Request("A");
        AnalysisRequest requestB = Request("B");
        coordinator.ObserveRun(false, requestA);
        coordinator.ObserveRun(true, requestA);
        coordinator.CurrentTask!.GetAwaiter().GetResult();
        coordinator.ObserveRun(false, requestB);
        coordinator.ObserveRun(true, requestB);
        coordinator.CurrentTask!.GetAwaiter().GetResult();
        AssertEx.Equal(RunState.Faulted, coordinator.Status.State);

        coordinator.ObserveQuery(requestA);
        coordinator.MarkObservedCurrent(requestA.QueryFingerprint);
        AssertEx.False(coordinator.Status.IsStale);
        AssertEx.Equal(requestA.QueryFingerprint, coordinator.Status.QueryFingerprint);
        coordinator.ObserveQuery(requestB);
        AssertEx.True(coordinator.Status.IsStale);
    }

    [Test]
    internal static void FalseDuringRunDoesNotMakeCompletionStaleAndInvalidEdgeCanBeConsumed()
    {
        var completion = new TaskCompletionSource<SolverResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new RunCoordinator((_, _, _) => completion.Task);
        AssertEx.False(coordinator.ObserveRunSignal(true)); // persisted true on reopen
        AssertEx.False(coordinator.ObserveRunSignal(false));
        AssertEx.True(coordinator.ObserveRunSignal(true)); // consumed even if later validation fails
        AssertEx.False(coordinator.ObserveRunSignal(true));
        AssertEx.False(coordinator.ObserveRunSignal(false));
        AssertEx.True(coordinator.ObserveRunSignal(true));
        coordinator.ObserveQuery(Request("A"));
        coordinator.StartObserved(Request("A"));
        coordinator.ObserveRunSignal(false);
        completion.SetResult(Result("11110101"));
        coordinator.CurrentTask!.GetAwaiter().GetResult();
        AssertEx.False(coordinator.Status.IsStale);

        coordinator.MarkObservedInvalid();
        AssertEx.True(coordinator.Status.IsStale);
        coordinator.ObserveQuery(Request("A"));
        AssertEx.False(coordinator.Status.IsStale);
    }

    [Test]
    internal static void SynchronousExecutorPublishesRunningBeforeSucceededOutsideStateLock()
    {
        var states = new List<RunState>();
        var coordinator = new RunCoordinator((_, _, _) => Task.FromResult(Result("11110101")));
        coordinator.Changed += status =>
        {
            states.Add(status.State);
            _ = coordinator.Status;
        };
        var request = Request("A");
        coordinator.ObserveRun(false, request);
        coordinator.ObserveRun(true, request);
        AssertEx.True(states.Contains(RunState.Running));
        AssertEx.Equal(RunState.Succeeded, states.Last());
    }

    [Test]
    internal static void AnalysisRequestDefensivelyCopiesAndRejectsDuplicateDataSets()
    {
        var sets = new List<string> { URSUSSolver.DS_AVG_INCOME };
        var weights = new List<double> { 1 };
        var request = new AnalysisRequest(sets, weights, "A");
        sets[0] = URSUSSolver.DS_TRANSIT;
        weights[0] = 9;
        AssertEx.Equal(URSUSSolver.DS_AVG_INCOME, request.DataSets.Single());
        AssertEx.Near(1, request.Weights!.Single());
        AssertEx.Throws<ArgumentException>(() => new AnalysisRequest(
            new[] { URSUSSolver.DS_AVG_INCOME, URSUSSolver.DS_AVG_INCOME }));
    }

    [Test]
    internal static void LowerLateProgressCannotRegressFractionOrStage()
    {
        var clock = new MutableClock();
        IProgress<AnalysisProgress>? reported = null;
        var completion = new TaskCompletionSource<SolverResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new RunCoordinator((_, _, progress) =>
        {
            reported = progress;
            return completion.Task;
        }, clock);
        var request = Request("A");
        coordinator.ObserveRun(false, request);
        coordinator.ObserveRun(true, request);
        reported!.Report(new AnalysisProgress(0.5, "new"));
        clock.UtcNow += TimeSpan.FromSeconds(1);
        reported.Report(new AnalysisProgress(0.2, "old"));
        AssertEx.Near(0.5, coordinator.Status.Progress);
        AssertEx.Equal("new", coordinator.Status.Stage);
        completion.SetResult(Result("11110101"));
    }

    [Test]
    internal static void DisposeCancelsCurrentGenerationAndSuppressesLateEvents()
    {
        CancellationToken token = default;
        var completion = new TaskCompletionSource<SolverResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new RunCoordinator((_, captured, _) =>
        {
            token = captured;
            return completion.Task;
        });
        int events = 0;
        coordinator.Changed += _ => events++;
        var request = Request("A");
        coordinator.ObserveRun(false, request);
        coordinator.ObserveRun(true, request);
        coordinator.Dispose();
        int before = events;
        AssertEx.True(token.IsCancellationRequested);
        completion.SetResult(Result("11110101"));
        Thread.Sleep(10);
        AssertEx.Equal(before, events);
        AssertEx.Throws<ObjectDisposedException>(() => coordinator.ObserveRun(false, request));
    }

    [Test]
    internal static void CancelCompletionDisposeRaceDoesNotThrow()
    {
        var completion = new TaskCompletionSource<SolverResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new RunCoordinator((_, _, _) => completion.Task);
        coordinator.Changed += status =>
        {
            if (status.State != RunState.CancelRequested) return;
            completion.TrySetResult(Result("11110102"));
            coordinator.CurrentTask!.GetAwaiter().GetResult();
        };
        var request = Request("A");
        coordinator.ObserveRun(false, request);
        coordinator.ObserveRun(true, request);
        coordinator.ObserveCancel(false);
        AssertEx.True(coordinator.ObserveCancel(true));
        AssertEx.Equal(RunState.Canceled, coordinator.Status.State);
    }

    [Test]
    internal static void NoOpRunObservationsDoNotPublishChangedEvents()
    {
        var coordinator = new RunCoordinator((_, _, _) => Task.FromResult(Result("11110101")));
        int events = 0;
        coordinator.Changed += _ => events++;
        var request = Request("A");
        coordinator.ObserveRun(false, request);
        coordinator.ObserveRun(false, request);
        AssertEx.Equal(0, events);
        coordinator.ObserveRun(true, request);
        int afterRun = events;
        coordinator.ObserveRun(true, request);
        coordinator.ObserveRun(false, request);
        coordinator.ObserveRun(false, request);
        AssertEx.Equal(afterRun, events);
    }

    [Test]
    internal static void VisualizationResolutionAndActualMeshAreBudgeted()
    {
        double resolution = VisualizationBudget.ClampResolution(0.01, 100_000, 100_000,
            VisualizationQuality.Preview);
        AssertEx.True(VisualizationBudget.EstimateVertices(100_000, 100_000, resolution)
            <= VisualizationBudget.PreviewVertexLimit);
        AssertEx.Throws<InvalidOperationException>(() => VisualizationBudget.EnsureActualWithinBudget(
            VisualizationBudget.PreviewVertexLimit + 1, 1, VisualizationQuality.Preview));
        AssertEx.Throws<InvalidOperationException>(() => VisualizationBudget.EnsureActualWithinBudget(
            1, VisualizationBudget.FinalFaceLimit + 1, VisualizationQuality.Final));
        AssertEx.Throws<InvalidOperationException>(() => VisualizationBudget.EnsureActualWithinBudget(
            220_000, 440_000, VisualizationQuality.Final));
        AssertEx.Throws<InvalidOperationException>(() => VisualizationBudget.EnsureEstimatedWithinBudget(
            1000, 1000, 100, 100_000, VisualizationQuality.Final));
    }

    [Test]
    internal static void ExactIdwMatchesReferenceAndObservesCancellation()
    {
        var points = new[] { new Coordinate2D(0, 0), new Coordinate2D(10, 0), new Coordinate2D(0, 10) };
        var values = new[] { 0.0, 10.0, 20.0 };
        double actual = ExactIdw.Evaluate(new Coordinate2D(3, 4), points, values, 2.5);
        double numerator = 0, denominator = 0;
        for (int i = 0; i < points.Length; i++)
        {
            double distance = Math.Sqrt(Math.Pow(3 - points[i].X, 2) + Math.Pow(4 - points[i].Y, 2));
            double weight = 1 / Math.Pow(distance, 2.5);
            numerator += weight * values[i]; denominator += weight;
        }
        AssertEx.Near(numerator / denominator, actual, 1e-12);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        AssertEx.Throws<OperationCanceledException>(() => ExactIdw.Evaluate(
            new Coordinate2D(1, 1), points, values, 2.5, canceled.Token));
    }

    [Test]
    internal static void LegendAndUnitContractsHandleDecimalsEqualMissingAndScaleSquared()
    {
        AssertEx.Equal(2, LegendContract.ClampSteps(-1));
        AssertEx.Equal(20, LegendContract.ClampSteps(99));
        AssertEx.Equal("0.125", LegendContract.Format(0.125, normalized: true));
        AssertEx.Equal("No data", LegendContract.Format(double.NaN, normalized: false, "원"));
        AssertEx.Near(0.5, LegendContract.Normalize(7, 7, 7));
        var millimeters = DocumentUnitScale.FromMetersPerDocumentUnit(0.001);
        AssertEx.Near(1000, millimeters.LengthScale);
        AssertEx.Near(1_000_000, millimeters.AreaScale);
        AssertEx.True(DocumentUnitScale.FromMetersPerDocumentUnit(0).Warning != null);
    }

    [Test]
    internal static void MeshCacheEvictsByEntryAndByteBudgetAndDisposesValues()
    {
        using var cache = new BoundedLruCache<string, DisposableProbe>(2, 100);
        var first = new DisposableProbe();
        var second = new DisposableProbe();
        var third = new DisposableProbe();
        cache.Add("a", first, 40);
        cache.Add("b", second, 40);
        AssertEx.True(cache.TryGet("a", out _));
        cache.Add("c", third, 40);
        AssertEx.True(second.Disposed);
        AssertEx.False(first.Disposed);
        var huge = new DisposableProbe();
        cache.Add("huge", huge, 101);
        AssertEx.True(huge.Disposed);
        AssertEx.Equal(0, cache.Count);
    }

    [Test]
    internal static void ProductionIdwIgnoresMissingSamplesAndCacheReturnsOwnedDuplicates()
    {
        var points = new List<Point3d>
        {
            new(0, 0, 0), new(1000, 0, 0), new(0, 1000, 0), new(1000, 1000, 0),
        };
        var values = new List<double> { 0, 1, double.NaN, 3 };
        using var cache = new VisualizationMeshCache();
        var visualizer = new IDWVisualizer(points, values, 500, 2.5, 0.5, 0.5,
            8, 4, null, null, false, "원", cache);
        double expected = ExactIdw.Evaluate(new Coordinate2D(500, 500),
            new[] { new Coordinate2D(0, 0), new Coordinate2D(1000, 0), new Coordinate2D(1000, 1000) },
            new[] { 0.0, 1.0, 3.0 }, 2.5);
        AssertEx.Near(expected, visualizer.Evaluate(new Point3d(500, 500, 0)), 1e-12);
        AssertEx.True(visualizer.BuildLegendLabels().Any(label =>
            label.Contains("원", StringComparison.Ordinal)));
        AssertEx.True(visualizer.BuildLegendLabels().Contains("No data"));
        AssertEx.False(visualizer.CreateCacheKey("s", VisualizationQuality.Preview, 1) ==
            visualizer.CreateCacheKey("s", VisualizationQuality.Final, 1));
        var otherStyle = new IDWVisualizer(points, values, 500, 2.5, 0.5, 0.5,
            8, 5, null, null, false, "원", cache);
        AssertEx.False(visualizer.CreateCacheKey("s", VisualizationQuality.Preview, 1) ==
            otherStyle.CreateCacheKey("s", VisualizationQuality.Preview, 1));

        AssertEx.Throws<ArgumentException>(() => new IDWVisualizer(points,
            new List<double> { double.NaN, double.NaN, double.NaN, double.NaN }));
        if (!OperatingSystem.IsWindows()) return;

        var boundary = new Polyline(new[]
        {
            new Point3d(0,0,0), new Point3d(1000,0,0), new Point3d(1000,1000,0),
            new Point3d(0,1000,0), new Point3d(0,0,0),
        }).ToPolylineCurve();
        var first = visualizer.Build(boundary, CancellationToken.None,
            VisualizationQuality.Preview, "snapshot-a", 1);
        var second = visualizer.Build(boundary, CancellationToken.None,
            VisualizationQuality.Preview, "snapshot-a", 1);
        AssertEx.Equal(1, cache.Count);
        AssertEx.True(first.LegendDots.Any(dot => dot.Text.Contains("원", StringComparison.Ordinal)));
        AssertEx.True(first.LegendDots.Any(dot => dot.Text == "No data"));
        AssertEx.False(ReferenceEquals(first.Mesh, second.Mesh));
        first.Mesh.Dispose(); first.FlatMesh.Dispose(); first.LegendMesh.Dispose();
        second.Mesh.Dispose(); second.FlatMesh.Dispose(); second.LegendMesh.Dispose();
        boundary.Dispose();
    }

    [Test]
    internal static void VisualizationCacheRejectsUseAfterDispose()
    {
        var cache = new VisualizationMeshCache();
        cache.Dispose();
        AssertEx.Throws<ObjectDisposedException>(() => cache.TryGet("x", out _));
    }

    [Test]
    internal static void ChoroplethPlanPreservesMultipartHolesMissingAndRawLegendRange()
    {
        static BoundaryRing Ring(params (double x, double y)[] points)
            => new(points.Select(point => new Coordinate2D(point.x, point.y)).ToArray());
        var first = BoundaryTopology.Create(new[]
        {
            new BoundaryPart(
                Ring((0,0), (10,0), (10,10), (0,10), (0,0)),
                new[] { Ring((2,2), (2,4), (4,4), (4,2), (2,2)) }),
            new BoundaryPart(Ring((20,0), (25,0), (25,5), (20,5), (20,0)),
                Array.Empty<BoundaryRing>()),
        });
        var second = BoundaryTopology.Create(new[]
        {
            new BoundaryPart(Ring((30,0), (35,0), (35,5), (30,5), (30,0)),
                Array.Empty<BoundaryRing>()),
        });
        var layer = new SnapshotLayer("income", "원", new Dictionary<string, double>
        {
            ["11110101"] = 1000,
            ["11110102"] = double.NaN,
        }, null, DateTimeOffset.UnixEpoch, AcquisitionOrigin.Network,
            DeliveryOrigin.Network, 0.5);
        var snapshot = new AnalysisSnapshot(new[] { "11110101", "11110102" }, new[] { layer },
            new Dictionary<string, BoundaryTopology>
            {
                ["11110101"] = first,
                ["11110102"] = second,
            });

        var plan = ChoroplethPlanner.Create(snapshot, "income", VisualizationMode.Extrusion, 50);
        AssertEx.Equal("income", plan.LayerId);
        AssertEx.Near(1000, plan.Minimum);
        AssertEx.Near(1000, plan.Maximum);
        AssertEx.Equal(2, plan.Districts[0].Topology.Parts.Count);
        AssertEx.Equal(1, plan.Districts[0].Topology.Parts[0].Holes.Count);
        AssertEx.Near(25, plan.Districts[0].Height);
        AssertEx.True(plan.Districts[1].IsMissing);
        AssertEx.Equal("11110102", plan.MissingCodes.Single());
        AssertEx.False(string.IsNullOrWhiteSpace(plan.SnapshotKey));

        var overlay = ChoroplethPlanner.Create(snapshot, "", VisualizationMode.Choropleth, 0,
            new[] { 0.25, 0.75 });
        AssertEx.Equal("overlay", overlay.LayerId);
        AssertEx.True(overlay.IsNormalized);
        AssertEx.Near(0.25, overlay.Minimum);
        AssertEx.Near(0.75, overlay.Maximum);
        AssertEx.Throws<ArgumentException>(() => ChoroplethPlanner.Create(snapshot, "",
            VisualizationMode.Choropleth, 0, new[] { 0.5 }));

        var shuffled = new AnalysisSnapshot(new[] { "11110102", "11110101" }, new[] { layer },
            new Dictionary<string, BoundaryTopology>
            {
                ["11110101"] = first,
                ["11110102"] = second,
            });
        var projected = ChoroplethPlanner.Create(shuffled, "", VisualizationMode.Choropleth, 0,
            new[] { 0.9, 0.1 });
        AssertEx.Near(0.9, projected.Districts.Single(d => d.Code == "11110102").RawValue);
        AssertEx.Near(0.1, projected.Districts.Single(d => d.Code == "11110101").RawValue);
    }

    [Test]
    internal static void SnapshotWeightsRecomputeOverlayWithoutSourceDependency()
    {
        var first = new SnapshotLayer("a", null, new Dictionary<string, double>
        {
            ["11110101"] = 0, ["11110102"] = 10,
        }, null, DateTimeOffset.UnixEpoch, AcquisitionOrigin.Network, DeliveryOrigin.Network, 1);
        var second = new SnapshotLayer("b", null, new Dictionary<string, double>
        {
            ["11110101"] = 10, ["11110102"] = 0,
        }, null, DateTimeOffset.UnixEpoch, AcquisitionOrigin.Network, DeliveryOrigin.Network, 1);
        var snapshot = new AnalysisSnapshot(new[] { "11110101", "11110102" },
            new[] { first, second });

        var aOnly = SnapshotDerivedCalculator.Recompute(snapshot,
            new[] { "a", "b" }, new[] { 1.0, 0.0 });
        var bOnly = SnapshotDerivedCalculator.Recompute(snapshot,
            new[] { "a", "b" }, new[] { 0.0, 1.0 });
        AssertEx.Near(0, aOnly.Values["11110101"]);
        AssertEx.Near(1, aOnly.Values["11110102"]);
        AssertEx.Near(1, bOnly.Values["11110101"]);
        AssertEx.Near(0, bOnly.Values["11110102"]);
    }

    [Test]
    internal static void SolverAndVisualizerPortManifestsRemainAppendOnly()
    {
        string solver = File.ReadAllText(FindRepositoryFile(
            "src", "URSUS.GH", "URSUSSolverComponent.cs"));
        string visualizer = File.ReadAllText(FindRepositoryFile(
            "src", "URSUS.GH", "VisualizerComponent.cs"));

        AssertEx.True(solver.Contains("private const int IN_CANCEL = 12;", StringComparison.Ordinal));
        AssertEx.True(solver.Contains("AddBooleanParameter(\"Cancel\", \"CXL\"", StringComparison.Ordinal));
        AssertEx.True(solver.Contains("AddTextParameter(\"Status\", \"ST\"", StringComparison.Ordinal));
        AssertEx.True(solver.Contains("AddGenericParameter(\"Snapshot\", \"S\"", StringComparison.Ordinal));
        AssertEx.Equal(1, Count(solver, "TaskList.Add(CompleteGenerationAsync"));

        AssertEx.True(visualizer.Contains(
            "new Guid(\"392dfc85-c773-4489-938a-188fb90acb50\")", StringComparison.Ordinal));
        AssertEx.True(visualizer.Contains(
            "GH_TaskCapableComponent<VisualizationTaskOutput>", StringComparison.Ordinal));
        AssertEx.True(visualizer.Contains("CancellationToken generationToken = CancelToken",
            StringComparison.Ordinal));
        AssertEx.True(visualizer.Contains("ComputeSafe(input, generationToken), generationToken",
            StringComparison.Ordinal));
        AssertEx.True(visualizer.Contains("AddIntegerParameter(\"Mode\", \"Mode\"", StringComparison.Ordinal));
        AssertEx.True(visualizer.Contains("GH_ParamAccess.item, 0);", StringComparison.Ordinal));
        AssertEx.True(visualizer.Contains("AddGenericParameter(\"Snapshot\", \"S\"", StringComparison.Ordinal));
        AssertEx.True(visualizer.Contains("pManager[12].Optional = true;", StringComparison.Ordinal));
        AssertEx.True(visualizer.Contains("AddTextParameter(\"Layer Id\", \"Layer\"", StringComparison.Ordinal));
        AssertEx.True(visualizer.Contains("AddTextParameter(\"Missing Codes\", \"Missing\"", StringComparison.Ordinal));
        AssertEx.True(visualizer.Contains("AddTextParameter(\"Status\", \"ST\"", StringComparison.Ordinal));
        AssertEx.True(visualizer.Contains("ChoroplethPlanner.Create(input.Snapshot, input.LayerId",
            StringComparison.Ordinal));
        AssertEx.True(visualizer.Contains("CreateDomainRegions", StringComparison.Ordinal));
        string adapter = File.ReadAllText(FindRepositoryFile(
            "src", "URSUS.GH", "SnapshotVisualizationAdapter.cs"));
        AssertEx.True(adapter.Contains("|tol:{tolerance:R}", StringComparison.Ordinal));
        AssertEx.True(adapter.Contains("VisualizationBudget.EnsureTopologyWithinBudget(",
            StringComparison.Ordinal));
        string idw = File.ReadAllText(FindRepositoryFile(
            "src", "URSUS", "Visualization", "IDWVisualizer.cs"));
        AssertEx.True(idw.Contains("foreach (IReadOnlyList<Curve> region in regions)",
            StringComparison.Ordinal));
        AssertEx.True(solver.Contains("cancellationToken.Register(", StringComparison.Ordinal));
        AssertEx.True(solver.Contains("_coordinator.CancelGeneration(generation)",
            StringComparison.Ordinal));
        AssertEx.True(solver.Contains("TryTakePending(observedIdentity)",
            StringComparison.Ordinal));
        AssertEx.True(solver.Contains("bool cachedInputsAreCurrent", StringComparison.Ordinal));
        AssertEx.True(solver.Contains("_coordinator.MarkObservedCurrent(_lastOutput!.QueryFingerprint)",
            StringComparison.Ordinal));
        AssertEx.True(solver.Contains("_lastOutput.WeightFingerprint != weightFingerprint",
            StringComparison.Ordinal));
        AssertEx.True(solver.Contains("foreach (string warning in recovered.Result.Warnings)",
            StringComparison.Ordinal));
        AssertEx.True(solver.Contains("private static Curve? ScaleCurve(Curve? curve",
            StringComparison.Ordinal));
        AssertEx.Equal(1, Count(solver, "CsvExporter.WriteToFile(csv, savedPath)"));
        AssertEx.True(solver.Contains("RhinoMath.UnitScale(UnitSystem.Meters",
            StringComparison.Ordinal));
    }

    private static AnalysisRequest Request(
        string address, IReadOnlyList<double>? weights = null, bool force = false,
        string? period = null, bool insecure = false, double radius = 5)
        => new(new[] { URSUSSolver.DS_AVG_INCOME }, weights, address, null, radius,
            new TransportPolicy(insecure), force,
            period == null ? QueryIntent.Latest : QueryIntent.ExplicitPeriod,
            period, null, "keys-v1");

    private static int Count(string source, string value)
    {
        int count = 0, position = 0;
        while ((position = source.IndexOf(value, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += value.Length;
        }
        return count;
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        throw new FileNotFoundException(string.Join('/', parts));
    }

    private static SolverResult Result(string code)
    {
        var snapshot = new AnalysisSnapshot(new[] { code }, Array.Empty<SnapshotLayer>());
        return new SolverResult(new(), new(), new(), new(), new(), new(), null!)
        {
            Snapshot = snapshot,
        };
    }

    private sealed class BlockingBoundarySource : IBoundaryDataSource
    {
        public int Calls { get; private set; }
        public ManualResetEventSlim Started { get; } = new(false);
        public DataSourceMetadata Metadata { get; } = MetadataFor("boundary", DataCategory.Boundary);
        public DataSourceError? ValidateConfiguration() => null;
        public async Task<DataResult<BoundaryDataSet>> FetchBoundariesAsync(
            DataQuery query, CancellationToken cancellationToken = default)
        {
            Calls++;
            Started.Set();
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public int Calls { get; private set; }

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_respond(request));
        }
    }

    private sealed class EmptySource : IDataSource
    {
        public DataSourceMetadata Metadata { get; } = MetadataFor("dummy", DataCategory.Other);
        public DataSourceError? ValidateConfiguration() => null;
        public Task<DataResult<DistrictDataSet>> FetchAsync(
            DataQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(DataResult<DistrictDataSet>.Success(
                DistrictDataSet.FromDictionary(new()), DataOrigin.Embedded, TimeSpan.Zero));
    }

    private static DataSourceMetadata MetadataFor(string id, DataCategory category) => new()
    {
        Id = id,
        DisplayName = id,
        Description = id,
        Category = category,
        Provider = "test",
    };

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class DisposableProbe : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
