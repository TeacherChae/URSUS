using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Reflection;
using URSUS.Analysis;
using URSUS.Caching;
using URSUS.DataSources;
using URSUS.Geometry;
using URSUS.Net;
using URSUS.Parsers;
using URSUS.Resources;
using URSUS.Utils;

namespace URSUS.Tests;

internal static class Phase1Tests
{
    [Test]
    internal static void SumMetric_IsNotDuplicatedAcrossLegalDistricts()
    {
        var mapped = DistrictMetricMapper.MapAdministrativeToLegal(
            new Dictionary<string, IReadOnlyList<string>> { ["A"] = new[] { "L1", "L2" } },
            new Dictionary<string, double> { ["A"] = 100 },
            MetricSemantics.Sum);

        AssertEx.Near(50, mapped.Values["L1"]);
        AssertEx.Near(50, mapped.Values["L2"]);
        AssertEx.Equal(MappingQuality.EstimatedEqualSplit, mapped.Quality);
    }

    [Test]
    internal static void MeanMetric_MapsWithDeclaredPolicy()
    {
        var mapped = DistrictMetricMapper.MapAdministrativeToLegal(
            new Dictionary<string, IReadOnlyList<string>> { ["A"] = new[] { "L1", "L2" } },
            new Dictionary<string, double> { ["A"] = 100 },
            MetricSemantics.Mean);

        AssertEx.Near(100, mapped.Values["L1"]);
        AssertEx.Near(100, mapped.Values["L2"]);
        AssertEx.Equal(MappingQuality.AssumedUniform, mapped.Quality);
    }

    [Test]
    internal static void LatestClosedPeriod_IsSelectedAndCompletenessReported()
    {
        var selected = ClosedPeriodSelector.SelectLatestQuarter(
            new[]
            {
                new PeriodCoverage("2025Q4", 426, 426),
                new PeriodCoverage("2026Q1", 400, 426),
                new PeriodCoverage("2026Q2", 426, 426),
            },
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        AssertEx.Equal("2026Q1", selected.PeriodId);
        AssertEx.False(selected.IsComplete);
        AssertEx.Equal(400, selected.ObservedCount);
    }

    [Test]
    internal static void SeoulExpectedLeafSet_IsVersionedAndStable()
    {
        AssertEx.Equal(426, SeoulExpectedDistricts.Ids.Count);
        AssertEx.True(SeoulExpectedDistricts.Ids.All(id => id.Length == 8 && id.StartsWith("11")));
        AssertEx.True(!string.IsNullOrWhiteSpace(SeoulExpectedDistricts.Version));
    }

    [Test]
    internal static void Wgs84_ToEpsg5179_MatchesKnownControlPointsWithinOneMeter()
    {
        var seoul = Epsg5179.FromWgs84(126.9784, 37.5665);
        AssertEx.Near(953936.490, seoul.X, 1.0);
        AssertEx.Near(1952031.885, seoul.Y, 1.0);

        var busan = Epsg5179.FromWgs84(129.0756, 35.1796);
        AssertEx.Near(1143467.380, busan.X, 1.0);
        AssertEx.Near(1688281.982, busan.Y, 1.0);
    }

    [Test]
    internal static void TypedBounds_NormalizeToWgs84WithoutCrsAmbiguity()
    {
        var projected = Epsg5179.FromWgs84(126.9784, 37.5665);
        var bounds = new SpatialBounds(projected.X, projected.Y, projected.X, projected.Y,
            CoordinateReferenceSystem.Epsg5179);
        var wgs = bounds.ToWgs84();

        AssertEx.Near(126.9784, wgs.MinX, 1e-5);
        AssertEx.Near(37.5665, wgs.MinY, 1e-5);
        AssertEx.Equal(CoordinateReferenceSystem.Wgs84, wgs.Crs);
    }

    [Test]
    internal static void TopologyAreaAndCentroid_UseAllPartsMinusHoles()
    {
        var topology = BoundaryTopology.Create(new[]
        {
            new BoundaryPart(
                Ring((0,0), (10,0), (10,10), (0,10), (0,0)),
                new[] { Ring((2,2), (2,4), (4,4), (4,2), (2,2)) }),
            new BoundaryPart(Ring((20,0), (24,0), (24,4), (20,4), (20,0)), Array.Empty<BoundaryRing>()),
        });

        AssertEx.Near(112, topology.Area);
        AssertEx.Near((100 * 5 - 4 * 3 + 16 * 22) / 112.0, topology.Centroid.X);
        AssertEx.Near((100 * 5 - 4 * 3 + 16 * 2) / 112.0, topology.Centroid.Y);
        AssertEx.True(topology.Parts.All(part => part.Outer.SignedArea > 0));
        AssertEx.True(topology.Parts.SelectMany(part => part.Holes).All(hole => hole.SignedArea < 0));
    }

    [Test]
    internal static void Topology_RejectsOpenOrDegenerateRingsAndNormalizesOrientation()
    {
        AssertEx.Throws<ArgumentException>(() => new BoundaryRing(new[]
        {
            new Coordinate2D(0, 0), new Coordinate2D(1, 0), new Coordinate2D(1, 1),
        }));
        AssertEx.Throws<ArgumentException>(() => Ring((0,0), (1,0), (2,0), (0,0)));
    }

    [Test]
    internal static void GeoJson_PreservesMultiPolygonAndHoles()
    {
        var geometry = JsonNode.Parse("""
        {
          "type":"MultiPolygon",
          "coordinates":[
            [[[126.90,37.50],[126.91,37.50],[126.91,37.51],[126.90,37.51],[126.90,37.50]],
             [[126.902,37.502],[126.902,37.504],[126.904,37.504],[126.904,37.502],[126.902,37.502]]],
            [[[127.00,37.50],[127.01,37.50],[127.01,37.51],[127.00,37.51],[127.00,37.50]]]
          ]
        }
        """);
        var topology = GeoJsonBoundaryParser.Parse(geometry);
        AssertEx.Equal(2, topology.Parts.Count);
        AssertEx.Equal(1, topology.Parts[0].Holes.Count);
        AssertEx.True(topology.Area > 0);

        var partiallyInvalid = JsonNode.Parse("""
        {
          "type":"Polygon",
          "coordinates":[
            [[126.90,37.50],[126.91,37.50],[126.91,37.51],[126.90,37.51],[126.90,37.50]],
            [[126.902,37.502],[126.902,37.502],[126.902,37.502],[126.902,37.502]]
          ]
        }
        """);
        var retained = GeoJsonBoundaryParser.Parse(partiallyInvalid, out var warnings);
        AssertEx.Equal(1, retained.Parts.Count);
        AssertEx.Equal(0, retained.Parts[0].Holes.Count);
        AssertEx.Equal(1, warnings.Count);
        AssertEx.Throws<BoundaryTopologyParseException>(() => GeoJsonBoundaryParser.Parse(
            JsonNode.Parse("""{"type":"Polygon","coordinates":[[[126.9,37.5],[126.91,37.5],[126.9,37.5]]]}""")));
    }

    [Test]
    internal static void SnapshotAndTopologyCollectionsCannotBeMutatedThroughArrayCasts()
    {
        var ring = Ring((0,0), (1,0), (1,1), (0,1), (0,0));
        var topology = BoundaryTopology.Create(new[]
        {
            new BoundaryPart(ring, Array.Empty<BoundaryRing>()),
        });
        var snapshot = new AnalysisSnapshot(new[] { "11110101" },
            Array.Empty<SnapshotLayer>(), new Dictionary<string, BoundaryTopology>
            {
                ["11110101"] = topology,
            });

        AssertEx.False(snapshot.DistrictIndex is string[]);
        AssertEx.False(snapshot.ProjectionOrder is string[]);
        AssertEx.False(topology.Parts is BoundaryPart[]);
        AssertEx.False(topology.Parts is List<BoundaryPart>);
        AssertEx.False(ring.Points is Coordinate2D[]);
    }

    [Test]
    internal static void CacheKey_CanonicalizesAllQueryFieldsIndependentOfOrderAndCulture()
    {
        string previous = CultureInfo.CurrentCulture.Name;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ko-KR");
            var first = PersistentCacheKey.Create("source", 2, QueryIntent.Latest,
                new Dictionary<string, string> { ["radius"] = "1.5", ["b"] = "2" },
                new[] { "11110102", "11110101" }, CoordinateReferenceSystem.Epsg5179);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var second = PersistentCacheKey.Create("source", 2, QueryIntent.Latest,
                new Dictionary<string, string> { ["b"] = "2", ["radius"] = "1.5" },
                new[] { "11110101", "11110102" }, CoordinateReferenceSystem.Epsg5179);
            AssertEx.Equal(first.Value, second.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(previous);
        }
    }

    [Test]
    internal static void ForceRefresh_ReplacesPersistentEntryUsedByNextNormalRead()
    {
        WithTemporaryDirectory(directory =>
        {
            var cache = new AtomicCacheStore(directory);
            var key = PersistentCacheKey.Create("x", 1, QueryIntent.Latest, null, null,
                CoordinateReferenceSystem.Epsg5179);
            int fetches = 0;
            Func<CancellationToken, Task<string>> fetch = _ => Task.FromResult((++fetches).ToString());

            AssertEx.Equal("1", cache.GetOrFetchAsync(key, false, TimeSpan.FromDays(1), fetch).GetAwaiter().GetResult().Value);
            AssertEx.Equal("2", cache.GetOrFetchAsync(key, true, TimeSpan.FromDays(1), fetch).GetAwaiter().GetResult().Value);
            var normal = cache.GetOrFetchAsync(key, false, TimeSpan.FromDays(1), fetch).GetAwaiter().GetResult();
            AssertEx.Equal("2", normal.Value);
            AssertEx.Equal(DeliveryOrigin.Cache, normal.DeliveryOrigin);
            AssertEx.Equal(2, fetches);
        });
    }

    [Test]
    internal static void FailedForceRefreshPreservesPreviousValidEntry()
    {
        WithTemporaryDirectory(directory =>
        {
            var cache = new AtomicCacheStore(directory);
            var key = PersistentCacheKey.Create("x", 1, QueryIntent.Latest, null, null,
                CoordinateReferenceSystem.Epsg5179);
            cache.GetOrFetchAsync(key, false, TimeSpan.FromDays(1), _ => Task.FromResult("good"))
                .GetAwaiter().GetResult();
            AssertEx.Throws<InvalidOperationException>(() => cache.GetOrFetchAsync<string>(key, true,
                TimeSpan.FromDays(1), _ => throw new InvalidOperationException("failed"))
                .GetAwaiter().GetResult());
            AssertEx.Equal("good", cache.GetOrFetchAsync(key, false, TimeSpan.FromDays(1),
                _ => Task.FromResult("bad")).GetAwaiter().GetResult().Value);
        });
    }

    [Test]
    internal static void CorruptCacheRefetchesAtomicallyWithoutTemporaryFiles()
    {
        WithTemporaryDirectory(directory =>
        {
            var cache = new AtomicCacheStore(directory);
            var key = PersistentCacheKey.Create("corrupt", 1, QueryIntent.Latest,
                null, null, CoordinateReferenceSystem.Epsg5179);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, key.Value + ".json"), "{partial");
            int fetches = 0;
            var repaired = cache.GetOrFetchAsync(key, false, TimeSpan.FromDays(1),
                _ => Task.FromResult($"good-{++fetches}"))
                .GetAwaiter().GetResult();
            AssertEx.Equal("good-1", repaired.Value);
            AssertEx.Equal(DeliveryOrigin.Network, repaired.DeliveryOrigin);
            var hit = cache.GetOrFetchAsync(key, false, TimeSpan.FromDays(1),
                _ => Task.FromResult("wrong")).GetAwaiter().GetResult();
            AssertEx.Equal("good-1", hit.Value);
            AssertEx.Equal(DeliveryOrigin.Cache, hit.DeliveryOrigin);
            AssertEx.Equal(0, Directory.GetFiles(directory, "*.tmp").Length);
        });
    }

    [Test]
    internal static void NormalAndForceOriginsSerializeWithRefreshWinningPersistentState()
    {
        WithTemporaryDirectory(directory =>
        {
            var cache = new AtomicCacheStore(directory);
            var key = PersistentCacheKey.Create("ordered", 1, QueryIntent.Latest,
                null, null, CoordinateReferenceSystem.Epsg5179);
            var firstStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<CacheRead<string>> normal = cache.GetOrFetchAsync(key, false,
                TimeSpan.FromDays(1), async _ =>
                {
                    firstStarted.TrySetResult(true);
                    await releaseFirst.Task;
                    return "normal-old";
                });
            firstStarted.Task.GetAwaiter().GetResult();
            Task<CacheRead<string>> force = cache.GetOrFetchAsync(key, true,
                TimeSpan.FromDays(1), _ => Task.FromResult("force-new"));
            releaseFirst.TrySetResult(true);
            Task.WhenAll(normal, force).GetAwaiter().GetResult();
            AssertEx.Equal("normal-old", normal.Result.Value);
            AssertEx.Equal("force-new", force.Result.Value);
            AssertEx.Equal("force-new", cache.GetOrFetchAsync(key, false,
                TimeSpan.FromDays(1), _ => Task.FromResult("wrong"))
                .GetAwaiter().GetResult().Value);
        });

        WithTemporaryDirectory(directory =>
        {
            var cache = new AtomicCacheStore(directory);
            var key = PersistentCacheKey.Create("force-first", 1, QueryIntent.Latest,
                null, null, CoordinateReferenceSystem.Epsg5179);
            var forceStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseForce = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<CacheRead<string>> force = cache.GetOrFetchAsync(key, true,
                TimeSpan.FromDays(1), async _ =>
                {
                    forceStarted.TrySetResult(true);
                    await releaseForce.Task;
                    return "force-new";
                });
            forceStarted.Task.GetAwaiter().GetResult();
            int normalFetches = 0;
            Task<CacheRead<string>> normal = cache.GetOrFetchAsync(key, false,
                TimeSpan.FromDays(1), _ =>
                {
                    normalFetches++;
                    return Task.FromResult("wrong");
                });
            releaseForce.TrySetResult(true);
            Task.WhenAll(force, normal).GetAwaiter().GetResult();
            AssertEx.Equal("force-new", normal.Result.Value);
            AssertEx.Equal(0, normalFetches);
        });
    }

    [Test]
    internal static void CacheEnvelope_PreservesOriginalRetrievedAtOnHitAndSeparatesOrigins()
    {
        WithTemporaryDirectory(directory =>
        {
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var clock = new FrozenClock(now);
            var cache = new AtomicCacheStore(directory, clock);
            var key = PersistentCacheKey.Create("x", 1, QueryIntent.Latest, null, null,
                CoordinateReferenceSystem.Epsg5179);
            var first = cache.GetOrFetchAsync(key, false, TimeSpan.FromDays(2), _ => Task.FromResult("v"))
                .GetAwaiter().GetResult();
            clock.UtcNow = now.AddDays(1);
            var hit = cache.GetOrFetchAsync(key, false, TimeSpan.FromDays(2), _ => Task.FromResult("wrong"))
                .GetAwaiter().GetResult();
            AssertEx.Equal(first.RetrievedAt, hit.RetrievedAt);
            AssertEx.Equal(AcquisitionOrigin.Network, hit.AcquisitionOrigin);
            AssertEx.Equal(DeliveryOrigin.Cache, hit.DeliveryOrigin);
            AssertEx.Near(24, hit.CacheAge.TotalHours);
        });
    }

    [Test]
    internal static void ConcurrentSameKeyFetch_IsCoalesced()
    {
        WithTemporaryDirectory(directory =>
        {
            var cache = new AtomicCacheStore(directory);
            var key = PersistentCacheKey.Create("coalesce", 1, QueryIntent.Latest, null, null,
                CoordinateReferenceSystem.Epsg5179);
            int fetches = 0;
            async Task<string> Fetch(CancellationToken token)
            {
                Interlocked.Increment(ref fetches);
                await Task.Delay(40, token);
                return "shared";
            }

            var tasks = Enumerable.Range(0, 8)
                .Select(_ => cache.GetOrFetchAsync(key, false, TimeSpan.FromDays(1), Fetch))
                .ToArray();
            Task.WhenAll(tasks).GetAwaiter().GetResult();
            AssertEx.Equal(1, fetches);
            AssertEx.True(tasks.All(task => task.Result.Value == "shared"));
        });
    }

    [Test]
    internal static void LastCancelledWaiter_CancelsOriginFetch()
    {
        WithTemporaryDirectory(directory =>
        {
            var cache = new AtomicCacheStore(directory);
            var key = PersistentCacheKey.Create("cancel", 1, QueryIntent.Latest, null, null,
                CoordinateReferenceSystem.Epsg5179);
            var originCancelled = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<string> Fetch(CancellationToken token)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(5), token); }
                catch (OperationCanceledException) { originCancelled.TrySetResult(true); throw; }
                return "unexpected";
            }
            using var cts = new CancellationTokenSource(30);
            AssertEx.Throws<OperationCanceledException>(() => cache.GetOrFetchAsync(
                key, false, TimeSpan.FromDays(1), Fetch, cts.Token).GetAwaiter().GetResult());
            AssertEx.True(originCancelled.Task.Wait(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    internal static void CancelledInflight_IsRemovedSoSameKeyCanRetry()
    {
        WithTemporaryDirectory(directory =>
        {
            var cache = new AtomicCacheStore(directory);
            var key = PersistentCacheKey.Create("cancel-retry", 1, QueryIntent.Latest, null, null,
                CoordinateReferenceSystem.Epsg5179);
            using var cts = new CancellationTokenSource(20);
            AssertEx.Throws<OperationCanceledException>(() => cache.GetOrFetchAsync(
                key, false, TimeSpan.FromDays(1),
                token => Task.Delay(TimeSpan.FromSeconds(5), token).ContinueWith(_ => "never", token),
                cts.Token).GetAwaiter().GetResult());

            var retry = cache.GetOrFetchAsync(key, false, TimeSpan.FromDays(1),
                _ => Task.FromResult("recovered")).GetAwaiter().GetResult();
            AssertEx.Equal("recovered", retry.Value);
        });
    }

    [Test]
    internal static void HttpPipeline_RespectsMaxConcurrencyAndCancellation()
    {
        var handler = new ConcurrencyHandler();
        var pipeline = new HttpPipeline(new HttpClient(handler), maxConcurrency: 2, maxRetries: 0,
            requestTimeout: TimeSpan.FromSeconds(3));
        var tasks = Enumerable.Range(0, 6)
            .Select(i => pipeline.GetStringAsync(new Uri($"https://example.test/{i}"), CancellationToken.None))
            .ToArray();
        Task.WhenAll(tasks).GetAwaiter().GetResult();
        AssertEx.True(handler.MaxObserved <= 2);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        AssertEx.Throws<OperationCanceledException>(() => pipeline.GetStringAsync(
            new Uri("https://example.test/cancel"), cts.Token).GetAwaiter().GetResult());
    }

    [Test]
    internal static void HttpPipeline_HonorsRetryAfterAndOverallDeadlineWithInjectedTime()
    {
        var clock = new FrozenClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var delay = new AdvancingDelay(clock);
        var retryHandler = new RetryAfterHandler(TimeSpan.FromSeconds(2), succeedOnAttempt: 2);
        var pipeline = new HttpPipeline(new HttpClient(retryHandler), 1, 2,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), clock, delay);

        string value = pipeline.GetStringAsync(new Uri("https://example.test/retry"),
            CancellationToken.None).GetAwaiter().GetResult();
        AssertEx.Equal("ok", value);
        AssertEx.Equal(2, retryHandler.RequestCount);
        AssertEx.Equal(TimeSpan.FromSeconds(2), delay.Delays.Single());

        var deadlineHandler = new RetryAfterHandler(TimeSpan.FromSeconds(4), succeedOnAttempt: 99);
        var deadlineDelay = new AdvancingDelay(clock);
        var deadlinePipeline = new HttpPipeline(new HttpClient(deadlineHandler), 1, 5,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(3), clock, deadlineDelay);
        AssertEx.Throws<TimeoutException>(() => deadlinePipeline.GetStringAsync(
            new Uri("https://example.test/deadline"), CancellationToken.None).GetAwaiter().GetResult());
        AssertEx.Equal(1, deadlineHandler.RequestCount);
    }

    [Test]
    internal static void HttpPipeline_DisposesRetryResponseBeforeBackoff()
    {
        var clock = new FrozenClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var handler = new TrackingRetryHandler();
        var delay = new InspectingDelay(clock, () => handler.RetryContent?.IsDisposed == true);
        var pipeline = new HttpPipeline(new HttpClient(handler), 1, 1,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), clock, delay);

        AssertEx.Equal("ok", pipeline.GetStringAsync(new Uri("https://example.test/dispose"),
            CancellationToken.None).GetAwaiter().GetResult());
        AssertEx.True(delay.ConditionObserved, "retry response must be disposed before backoff");
    }

    [Test]
    internal static void ProviderTotalMismatchCannotProduceLandOrZoningSuccess()
    {
        const string land = "{\"referLandPrices\":{\"resultCode\":\"000\",\"totalCount\":2," +
            "\"referLandPrice\":[{\"ldCode\":\"1111010100\",\"pblntfPclnd\":\"100\"}]}}";
        var landParser = new LandPriceApiParser("secret",
            new HttpPipeline(new HttpClient(new StaticHandler(land)), maxRetries: 0));
        AssertEx.Throws<InvalidOperationException>(() => landParser.GetLandPriceByLegalDistrictAsync(
            new List<string> { "11110101" }).GetAwaiter().GetResult());

        const string zoning = "{\"landUses\":{\"resultCode\":\"000\",\"totalCount\":2," +
            "\"landUse\":[{\"ldCode\":\"1111010100\",\"prposAreaDstrcCodeNm\":\"일반상업지역\"}]}}";
        var zoningParser = new ZoningApiParser("secret",
            new HttpPipeline(new HttpClient(new StaticHandler(zoning)), maxRetries: 0));
        AssertEx.Throws<InvalidOperationException>(() => zoningParser.GetZoningHistogramByDistrictAsync(
            new List<string> { "11110101" }).GetAwaiter().GetResult());
    }

    [Test]
    internal static void VworldNumberMatchedMismatchCannotBeCachedAsComplete()
    {
        const string body = """
        {"type":"FeatureCollection","numberMatched":2,"numberReturned":0,"features":[]}
        """;
        var parser = new VworldApiParser("secret", null,
            new HttpPipeline(new HttpClient(new StaticHandler(body)), maxRetries: 0));
        AssertEx.Throws<InvalidOperationException>(() => parser.GetLegalDistrictsByBoundsAsync(
            new SpatialBounds(126.9, 37.5, 126.91, 37.51,
                CoordinateReferenceSystem.Wgs84)).GetAwaiter().GetResult());
    }

    [Test]
    internal static void DataGoKrFailsClosedWithoutHttpsAndAllSecretsAreRedacted()
    {
        AssertEx.Throws<InvalidOperationException>(() => TransportPolicy.Default.EnsureAllowed(
            new Uri("http://apis.data.go.kr/path?serviceKey=secret"), ProviderKind.DataGoKr));
        string redacted = SecretRedactor.Redact(
            "https://apis.data.go.kr/path?serviceKey=secret&key=other");
        AssertEx.False(redacted.Contains("secret", StringComparison.Ordinal));
        AssertEx.False(redacted.Contains("other", StringComparison.Ordinal));
        string seoul = SecretRedactor.Redact(
            "http://openapi.seoul.go.kr:8088/seoul-secret/xml/service/1/2");
        AssertEx.False(seoul.Contains("seoul-secret", StringComparison.Ordinal));
    }

    [Test]
    internal static void InsecureSeoulOptIn_IsTypedAndDefaultOff()
    {
        AssertEx.Throws<InvalidOperationException>(() => TransportPolicy.Default.EnsureAllowed(
            new Uri("http://openapi.seoul.go.kr:8088/key/xml/x/1/2"), ProviderKind.Seoul));
        new TransportPolicy(true).EnsureAllowed(
            new Uri("http://openapi.seoul.go.kr:8088/key/xml/x/1/2"), ProviderKind.Seoul);
    }

    [Test]
    internal static void XmlParser_ProjectsFieldsAndDetectsDuplicateStableIdentity()
    {
        const string xml = "<root><list_total_count>2</list_total_count>" +
            "<row><ADSTRD_CD>A</ADSTRD_CD><STDR_YYQU_CD>2026Q1</STDR_YYQU_CD><V>1</V><IGNORED>x</IGNORED></row>" +
            "<row><ADSTRD_CD>A</ADSTRD_CD><STDR_YYQU_CD>2026Q1</STDR_YYQU_CD><V>1</V></row></root>";
        var page = SeoulXmlStreamParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)),
            new[] { "ADSTRD_CD", "STDR_YYQU_CD", "V" },
            row => $"{row["ADSTRD_CD"]}|{row["STDR_YYQU_CD"]}|{row["V"]}");
        AssertEx.Equal(2, page.TotalCount);
        AssertEx.False(page.IsComplete);
        AssertEx.True(page.HasDuplicateIdentity,
            string.Join(";", page.Rows.Select(row => string.Join(",", row.Select(pair => $"{pair.Key}={pair.Value}")))));
        AssertEx.True(page.Rows.All(row => !row.ContainsKey("IGNORED")));
    }

    [Test]
    internal static void SeoulAsyncParser_SelectsLatestClosedPeriodWithoutTaskRunOrAllRowMaterialization()
    {
        const string xml = "<root><list_total_count>2</list_total_count>" +
            "<row><ADSTRD_CD>11110515</ADSTRD_CD><STDR_YYQU_CD>2026Q1</STDR_YYQU_CD><MT_AVRG_INCOME_AMT>100</MT_AVRG_INCOME_AMT></row>" +
            "<row><ADSTRD_CD>11110515</ADSTRD_CD><STDR_YYQU_CD>2026Q2</STDR_YYQU_CD><MT_AVRG_INCOME_AMT>999</MT_AVRG_INCOME_AMT></row></root>";
        var handler = new StaticHandler(xml);
        var parser = new DataSeoulApiParser("secret",
            new HttpPipeline(new HttpClient(handler), maxRetries: 0),
            new FrozenClock(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)));
        var result = parser.GetAvgIncomeByAdstrdAsync(new DataQuery
        {
            TransportPolicy = new TransportPolicy(true),
        }).GetAwaiter().GetResult();

        AssertEx.Equal("2026Q1", result.Observation.PeriodId);
        AssertEx.Near(100, result.Values["11110515"]);
        AssertEx.Equal(1, handler.RequestCount);
        AssertEx.True(result.PaginationComplete);
        AssertEx.Equal(425, result.Observation.MissingIds.Count);
        AssertEx.False(result.Observation.MissingIds.Contains("11110515", StringComparer.Ordinal));
    }

    [Test]
    internal static void ZoningHistogramTransform_IsVersionedDefaultOffAndUnknownMissing()
    {
        var histogram = new ZoningCategoryHistogram(new Dictionary<string, int>
        {
            ["commercial"] = 2,
            ["green"] = 1,
            ["unknown-new"] = 10,
        });
        AssertEx.False(ZoningOrdinalTransform.DefaultEnabled);
        var transformed = ZoningOrdinalTransform.V1.Transform(histogram);
        AssertEx.Near((5 * 2 + 1) / 3.0, transformed!.Value);
        AssertEx.Equal("zoning-ordinal-v1", transformed.TransformVersion);
        AssertEx.Equal(null, ZoningOrdinalTransform.V1.Transform(
            new ZoningCategoryHistogram(new Dictionary<string, int> { ["unknown"] = 2 })));
    }

    [Test]
    internal static void ZoningSourcePreservesCategoriesAndRequiresExplicitOrdinalOptIn()
    {
        WithTemporaryDirectory(directory =>
        {
            const string body = "{\"landUses\":{\"resultCode\":\"000\",\"totalCount\":1," +
                "\"landUse\":[{\"ldCode\":\"1111010100\"," +
                "\"prposAreaDstrcCodeNm\":\"일반상업지역\"}]}}";
            var handler = new StaticHandler(body);
            var source = new ZoningDataSource(
                new URSUS.Config.ApiKeyProvider(new Dictionary<string, string>
                {
                    [URSUS.Config.ApiKeyProvider.KEY_DATA_GO_KR] = "secret",
                }),
                new HttpPipeline(new HttpClient(handler), maxRetries: 0),
                new AtomicCacheStore(directory));
            var baseline = source.FetchAsync(new DataQuery
            {
                DistrictCodes = new[] { "11110101" },
            }).GetAwaiter().GetResult();

            AssertEx.True(baseline.IsSuccess);
            AssertEx.Equal(0, baseline.Data!.Records.Count);
            AssertEx.Equal(1, baseline.Data.CategoricalHistograms.Count);
            AssertEx.True(baseline.Data.Warnings.Single().Contains("disabled",
                StringComparison.Ordinal));

            var optedIn = source.FetchAsync(new DataQuery
            {
                DistrictCodes = new[] { "11110101" },
                Parameters = new Dictionary<string, string>
                {
                    [ZoningDataSource.PARAM_ENABLE_ORDINAL_TRANSFORM] = "true",
                },
            }).GetAwaiter().GetResult();
            AssertEx.True(optedIn.IsSuccess);
            AssertEx.Near(5, optedIn.Data!.Records["11110101"].Value);
            AssertEx.Equal(1, handler.RequestCount,
                "transform opt-in must reuse the same raw histogram cache");
        });
    }

    [Test]
    internal static void BuiltInSourceCancellationPropagatesAndDefaultClientsAreShared()
    {
        WithTemporaryDirectory(directory =>
        {
            var source = new SeoulAvgIncomeDataSource(
                new URSUS.Config.ApiKeyProvider(new Dictionary<string, string>
                {
                    [URSUS.Config.ApiKeyProvider.KEY_SEOUL] = "secret",
                }),
                new HttpPipeline(new HttpClient(new StaticHandler("<root/>")), maxRetries: 0),
                cache: new AtomicCacheStore(directory));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            AssertEx.Throws<OperationCanceledException>(() => source.FetchAsync(new DataQuery
            {
                DistrictCodes = new[] { "11110101" },
                TransportPolicy = new TransportPolicy(true),
            }, cancellation.Token).GetAwaiter().GetResult());
        });

        foreach (string relative in new[]
        {
            Path.Combine("DataSources", "SeoulOpenDataSourceBase.cs"),
            Path.Combine("DataSources", "LandPriceDataSource.cs"),
            Path.Combine("DataSources", "ZoningDataSource.cs"),
            Path.Combine("DataSources", "VWorldBoundaryDataSource.cs"),
        })
        {
            string source = File.ReadAllText(FindRepositoryFile("src", "URSUS", relative));
            AssertEx.True(source.Contains(
                "catch (OperationCanceledException) { throw; }", StringComparison.Ordinal));
        }

        string netRoot = Path.GetDirectoryName(FindRepositoryFile(
            "src", "URSUS", "Net", "HttpClientLifetime.cs"))!;
        string productRoot = Directory.GetParent(netRoot)!.FullName;
        string[] directAllocations = Directory.GetFiles(productRoot, "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("HttpClientLifetime.cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("new HttpClient(",
                StringComparison.Ordinal))
            .ToArray();
        AssertEx.Equal(0, directAllocations.Length,
            "product defaults must reuse the process HttpClient");
    }

    [Test]
    internal static void LandSource_QueryKeyedCacheSeparatesSubsetsAndForceRefreshes()
    {
        WithTemporaryDirectory(directory =>
        {
            var handler = new LandPriceHandler();
            var pipeline = new HttpPipeline(new HttpClient(handler), maxRetries: 0);
            var source = new LandPriceDataSource(
                new URSUS.Config.ApiKeyProvider(new Dictionary<string, string>
                {
                    [URSUS.Config.ApiKeyProvider.KEY_DATA_GO_KR] = "secret",
                }), pipeline,
                new FrozenClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                new AtomicCacheStore(directory));

            DataResult<DistrictDataSet> Fetch(string code, bool force = false) => source.FetchAsync(new DataQuery
            {
                DistrictCodes = new[] { code }, ForceRefresh = force,
            }).GetAwaiter().GetResult();

            AssertEx.True(Fetch("11110101").IsSuccess);
            AssertEx.True(Fetch("11140101").IsSuccess);
            AssertEx.Equal(2, handler.RequestCount);
            var hit = Fetch("11110101");
            AssertEx.Equal(2, handler.RequestCount);
            AssertEx.Equal(DeliveryOrigin.Cache, hit.DeliveryOrigin);
            AssertEx.True(Fetch("11110101", force: true).IsSuccess);
            AssertEx.Equal(3, handler.RequestCount);
        });
    }

    [Test]
    internal static void LandSourceExplicitPeriodControlsProviderYearAndObservation()
    {
        WithTemporaryDirectory(directory =>
        {
            var handler = new LandPriceHandler();
            var source = new LandPriceDataSource(
                new URSUS.Config.ApiKeyProvider(new Dictionary<string, string>
                {
                    [URSUS.Config.ApiKeyProvider.KEY_DATA_GO_KR] = "secret",
                }),
                new HttpPipeline(new HttpClient(handler), maxRetries: 0),
                new FrozenClock(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)),
                new AtomicCacheStore(directory));

            var result = source.FetchAsync(new DataQuery
            {
                DistrictCodes = new[] { "11110101" },
                QueryIntent = QueryIntent.ExplicitPeriod,
                ExplicitPeriod = "2022",
            }).GetAwaiter().GetResult();

            AssertEx.True(result.IsSuccess);
            AssertEx.True(handler.LastQuery!.Contains("stdrYear=2022", StringComparison.Ordinal));
            AssertEx.Equal("2022", result.Data!.Observation!.PeriodId);
            var invalid = source.FetchAsync(new DataQuery
            {
                DistrictCodes = new[] { "11110101" },
                QueryIntent = QueryIntent.ExplicitPeriod,
                ExplicitPeriod = "not-a-year",
            }).GetAwaiter().GetResult();
            AssertEx.False(invalid.IsSuccess);
            AssertEx.Equal(ErrorCodes.LandPriceFailed, invalid.Error!.Code);
        });
    }

    [Test]
    internal static void LegacyPublicMethodAndConstructorSignaturesRemainAvailable()
    {
        AssertEx.True(typeof(URSUSSolver).GetMethod("Run", new[]
        {
            typeof(List<string>), typeof(List<double>), typeof(string), typeof(string), typeof(double?),
        }) != null);
        AssertEx.True(typeof(DataResult<string>).GetMethod("Success", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(string), typeof(DataOrigin), typeof(TimeSpan) }, null) != null);
        foreach (Type type in new[]
        {
            typeof(LandPriceDataSource), typeof(ZoningDataSource),
            typeof(VWorldBoundaryDataSource), typeof(SeoulAvgIncomeDataSource),
            typeof(SeoulResidentPopDataSource), typeof(SeoulTransitDataSource),
            typeof(DataSeoulApiParser), typeof(LandPriceApiParser), typeof(ZoningApiParser),
        })
            AssertEx.True(type.GetConstructor(new[] { type == typeof(DataSeoulApiParser) ||
                type == typeof(LandPriceApiParser) || type == typeof(ZoningApiParser)
                    ? typeof(string) : typeof(URSUS.Config.ApiKeyProvider) }) != null,
                $"Missing legacy constructor: {type.Name}");
        AssertEx.True(typeof(VworldApiParser).GetConstructor(new[] { typeof(string), typeof(string) }) != null);
    }

    private static BoundaryRing Ring(params (double x, double y)[] points)
        => new(points.Select(point => new Coordinate2D(point.x, point.y)).ToArray());

    private static void WithTemporaryDirectory(Action<string> action)
    {
        string path = Path.Combine(Path.GetTempPath(), "ursus-p1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try { action(path); }
        finally { Directory.Delete(path, recursive: true); }
    }

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class ConcurrencyHandler : HttpMessageHandler
    {
        private int _active;
        public int MaxObserved { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int active = Interlocked.Increment(ref _active);
            MaxObserved = Math.Max(MaxObserved, active);
            try
            {
                await Task.Delay(25, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("ok"),
                };
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class StaticHandler : HttpMessageHandler
    {
        private readonly string _body;
        public int RequestCount { get; private set; }
        public StaticHandler(string body) => _body = body;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/xml"),
            });
        }
    }

    private sealed class AdvancingDelay : IAsyncDelay
    {
        private readonly FrozenClock _clock;
        public AdvancingDelay(FrozenClock clock) => _clock = clock;
        public List<TimeSpan> Delays { get; } = new();
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            _clock.UtcNow += delay;
            return Task.CompletedTask;
        }
    }

    private sealed class RetryAfterHandler : HttpMessageHandler
    {
        private readonly TimeSpan _retryAfter;
        private readonly int _succeedOnAttempt;
        public RetryAfterHandler(TimeSpan retryAfter, int succeedOnAttempt)
        {
            _retryAfter = retryAfter;
            _succeedOnAttempt = succeedOnAttempt;
        }
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount >= _succeedOnAttempt)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("ok"),
                });
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(_retryAfter);
            return Task.FromResult(response);
        }
    }

    private sealed class InspectingDelay : IAsyncDelay
    {
        private readonly FrozenClock _clock;
        private readonly Func<bool> _condition;
        public InspectingDelay(FrozenClock clock, Func<bool> condition)
        {
            _clock = clock;
            _condition = condition;
        }
        public bool ConditionObserved { get; private set; }
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConditionObserved = _condition();
            _clock.UtcNow += delay;
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingRetryHandler : HttpMessageHandler
    {
        public TrackingContent? RetryContent { get; private set; }
        private int _requests;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests++;
            if (_requests > 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("ok"),
                });
            RetryContent = new TrackingContent("retry");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = RetryContent,
                Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                    TimeSpan.FromMilliseconds(1)) },
            });
        }
    }

    private sealed class TrackingContent : StringContent
    {
        public TrackingContent(string value) : base(value) { }
        public bool IsDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class LandPriceHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? LastQuery { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            string query = request.RequestUri!.Query;
            LastQuery = query;
            string legal = query.Contains("ldCode=11110", StringComparison.Ordinal)
                ? "1111010100" : "1114010100";
            string body = "{\"referLandPrices\":{\"resultCode\":\"000\",\"totalCount\":1,\"referLandPrice\":[" +
                "{\"ldCode\":\"" + legal + "\",\"pblntfPclnd\":\"100\"}]}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        throw new FileNotFoundException(string.Join('/', parts));
    }
}
