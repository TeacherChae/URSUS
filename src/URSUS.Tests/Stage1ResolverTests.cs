using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using URSUS.Caching;
using URSUS.Geometry;
using URSUS.Net;
using URSUS.SiteBriefing;

namespace URSUS.Tests;

internal static class Stage1ResolverTests
{
    [Test]
    internal static void TopologyMatcher_UsesGlobalEdgeToleranceAndOuterMinusHoles()
    {
        AddressCandidate candidate = Candidate(AddressCandidateKind.Road,
            new Wgs84Coordinate(126.97834678005411, 37.56670100709671), "태평로1가");
        ProjectedCoordinate point = ProjectedCoordinate.From(candidate.Wgs84);

        AssertEx.Equal(CandidateTopologyStatus.Edge, AddressTopologyResolver.Match(candidate,
            new[] { Feature("11140103", Rectangle(point, -0.009, 10, -5, 5)) }).Status);
        AssertEx.Equal(CandidateTopologyStatus.Edge, AddressTopologyResolver.Match(candidate,
            new[] { Feature("11140103", Rectangle(point, -0.01, 10, -5, 5)) }).Status);
        AssertEx.Equal(CandidateTopologyStatus.UniqueStrict, AddressTopologyResolver.Match(candidate,
            new[] { Feature("11140103", Rectangle(point, -0.011, 10, -5, 5)) }).Status);
        AssertEx.Equal(CandidateTopologyStatus.Outside, AddressTopologyResolver.Match(candidate,
            new[] { Feature("11140103", Rectangle(point, 0.011, 10, -5, 5)) }).Status);

        BoundaryRing outer = Rectangle(point, -10, 10, -10, 10);
        BoundaryRing hole = Rectangle(point, -1, 1, -1, 1);
        AssertEx.Equal(CandidateTopologyStatus.Outside, AddressTopologyResolver.Match(candidate,
            new[] { Feature("11140103", new BoundaryPart(outer, new[] { hole })) }).Status);
        BoundaryRing edgeHole = Rectangle(point, 0, 2, -2, 2);
        AssertEx.Equal(CandidateTopologyStatus.Edge, AddressTopologyResolver.Match(candidate,
            new[] { Feature("11140103", new BoundaryPart(outer, new[] { edgeHole })) }).Status);
    }

    [Test]
    internal static void TopologyMatcher_HandlesZeroLengthMultipartAndOverlappingDistricts()
    {
        AddressCandidate candidate = Candidate(AddressCandidateKind.Road,
            new Wgs84Coordinate(126.97834678005411, 37.56670100709671), "태평로1가");
        ProjectedCoordinate point = ProjectedCoordinate.From(candidate.Wgs84);
        var duplicateVertex = new BoundaryRing(new[]
        {
            new Coordinate2D(point.X - 5, point.Y - 5),
            new Coordinate2D(point.X + 5, point.Y - 5),
            new Coordinate2D(point.X + 5, point.Y - 5),
            new Coordinate2D(point.X + 5, point.Y + 5),
            new Coordinate2D(point.X - 5, point.Y + 5),
            new Coordinate2D(point.X - 5, point.Y - 5),
        });
        AssertEx.Equal(CandidateTopologyStatus.UniqueStrict, AddressTopologyResolver.Match(candidate,
            new[] { Feature("11140103", duplicateVertex) }).Status);
        BoundaryRing reversed = new BoundaryRing(
            Rectangle(point, -5, 5, -5, 5).Points.Reverse().ToArray());
        AssertEx.Equal(CandidateTopologyStatus.UniqueStrict, AddressTopologyResolver.Match(candidate,
            new[] { Feature("11140103", reversed) }).Status);
        var vertexRay = new BoundaryRing(new[]
        {
            new Coordinate2D(point.X - 6, point.Y),
            new Coordinate2D(point.X, point.Y - 6),
            new Coordinate2D(point.X + 6, point.Y),
            new Coordinate2D(point.X, point.Y + 6),
            new Coordinate2D(point.X - 6, point.Y),
        });
        AssertEx.Equal(CandidateTopologyStatus.UniqueStrict, AddressTopologyResolver.Match(candidate,
            new[] { Feature("11140103", vertexRay) }).Status);

        var multipart = new[]
        {
            new BoundaryPart(Rectangle(point, 100, 110, 100, 110), Array.Empty<BoundaryRing>()),
            new BoundaryPart(Rectangle(point, -5, 5, -5, 5), Array.Empty<BoundaryRing>()),
        };
        AssertEx.Equal(CandidateTopologyStatus.UniqueStrict, AddressTopologyResolver.Match(candidate,
            new[] { Feature("11140103", multipart) }).Status);
        CandidateTopologyMatch overlap = AddressTopologyResolver.Match(candidate, new[]
        {
            Feature("11140103", Rectangle(point, -5, 5, -5, 5)),
            Feature("11140104", Rectangle(point, -4, 6, -4, 6), "을지로1가"),
        });
        AssertEx.Equal(CandidateTopologyStatus.MultipleStrict, overlap.Status);
        AssertEx.Equal(2, overlap.Alternatives.Count);
        AssertEx.True(overlap.Alternatives.All(value => value.DistanceMeters > 0));

        CandidateTopologyMatch edgeDominatesStrict = AddressTopologyResolver.Match(candidate,
            new[]
            {
                Feature("11140103", Rectangle(point, -5, 5, -5, 5)),
                Feature("11140104", Rectangle(point, 0, 5, -5, 5), "을지로1가"),
            });
        AssertEx.Equal(CandidateTopologyStatus.Edge, edgeDominatesStrict.Status);
        AssertEx.Equal("11140104", edgeDominatesStrict.Alternatives.Single().CanonicalCode);
    }

    [Test]
    internal static void Resolver_ResolvesCapturedRoadParcelAndDualModes()
    {
        RunResolverCase("서울특별시 중구 세종대로 110", uri =>
        {
            if (IsWfs(uri)) return WfsForJungGu();
            JsonObject road = AddressFixture("vworld-address-road-sejongdaero-110.json");
            return AddressMode(uri) == "road" ? road["response"]!.ToJsonString()
                : road["alternateModeCapture"]!["response"]!.ToJsonString();
        }, result =>
        {
            AssertEx.Equal(AddressResolutionStatus.Resolved, result.Status);
            AssertEx.Equal(AddressKind.Road, result.ResolvedSite!.AddressKind);
            AssertEx.Equal("11140103", result.ResolvedSite.LegalDistrict.CanonicalCode);
            AssertEx.True(ReferenceEquals(result.Candidates, result.ResolvedSite.Candidates));
            AssertEx.Equal(2, result.ResolvedSite.ResolverSources.Count);
            AssertEx.Equal("address", result.ResolvedSite.ResolverSources[0].Service);
            AssertEx.Equal("WFS", result.ResolvedSite.ResolverSources[1].Service);
            AssertEx.Equal(DateTimeOffset.Parse("2026-07-23T00:00:00Z"),
                result.ResolvedSite.ResolverSources[0].CapturedAtUtc);
            AssertEx.Equal(DateTimeOffset.Parse("2026-07-23T00:00:00Z"),
                result.ResolvedSite.ResolvedAt);
        });

        RunResolverCase("서울특별시 중구 태평로1가 31", uri =>
        {
            if (IsWfs(uri)) return WfsForJungGu();
            JsonObject parcel = AddressFixture("vworld-address-parcel-taepyeongno-31.json");
            return AddressMode(uri) == "parcel" ? parcel["response"]!.ToJsonString()
                : parcel["alternateModeCapture"]!["response"]!.ToJsonString();
        }, result => AssertEx.Equal(AddressKind.Parcel, result.ResolvedSite!.AddressKind));

        const string dualInput = "합성 dual 입력";
        RunResolverCase(dualInput, uri =>
        {
            if (IsWfs(uri)) return WfsForJungGu();
            string fixture = AddressMode(uri) == "road"
                ? "vworld-address-road-sejongdaero-110.json"
                : "vworld-address-parcel-taepyeongno-31.json";
            JsonObject response = AddressFixture(fixture)["response"]!.AsObject().DeepClone().AsObject();
            response["input"]!["address"] = dualInput;
            return response.ToJsonString();
        }, result =>
        {
            AssertEx.Equal(AddressKind.DualEquivalent, result.ResolvedSite!.AddressKind);
            AssertEx.Equal(2, result.Candidates.Count);
            AssertEx.Equal("11140103", result.ResolvedSite.LegalDistrict.CanonicalCode);
        });
    }

    [Test]
    internal static void Resolver_SkipsBoundaryForDistanceAndBootstrapDisagreement()
    {
        const string input = "합성 scope 입력";
        int wfsCalls = 0;
        WithTemporaryDirectory(directory =>
        {
            var handler = new RoutingHandler(uri =>
            {
                if (IsWfs(uri)) { Interlocked.Increment(ref wfsCalls); return WfsForJungGu(); }
                JsonObject response = AddressFixture(AddressMode(uri) == "road"
                    ? "vworld-address-road-sejongdaero-110.json"
                    : "vworld-address-parcel-taepyeongno-31.json")["response"]!
                    .AsObject().DeepClone().AsObject();
                response["input"]!["address"] = input;
                if (AddressMode(uri) == "parcel")
                    response["result"]!["point"] = new JsonObject
                    {
                        ["x"] = "126.981", ["y"] = "37.569",
                    };
                response["refined"]!["structure"]!["level4AC"] = "";
                return response.ToJsonString();
            });
            AddressResolutionResult result = Resolver(handler, directory).ResolveAsync(input)
                .GetAwaiter().GetResult();
            AssertEx.Equal(AddressResolutionReason.DualModeDistanceExceeded, result.ReasonCode);
            AssertEx.Equal(0, wfsCalls);
        });

        WithTemporaryDirectory(directory =>
        {
            var handler = new RoutingHandler(uri =>
            {
                if (IsWfs(uri)) { Interlocked.Increment(ref wfsCalls); return WfsForJungGu(); }
                JsonObject response = AddressFixture(AddressMode(uri) == "road"
                    ? "vworld-address-road-sejongdaero-110.json"
                    : "vworld-address-parcel-taepyeongno-31.json")["response"]!
                    .AsObject().DeepClone().AsObject();
                response["input"]!["address"] = input;
                if (AddressMode(uri) == "parcel")
                {
                    response["refined"]!["structure"]!["level2"] = "종로구";
                    response["refined"]!["structure"]!["level4AC"] = "1111051500";
                }
                return response.ToJsonString();
            });
            AddressResolutionResult result = Resolver(handler, directory).ResolveAsync(input)
                .GetAwaiter().GetResult();
            AssertEx.Equal(AddressResolutionReason.CohortBootstrapDisagreement, result.ReasonCode);
            AssertEx.Equal(0, wfsCalls);
        });
    }

    [Test]
    internal static void Resolver_PreservesMixedAddressProviderFailuresInModeOrder()
    {
        WithTemporaryDirectory(directory =>
        {
            string error = AddressFixture("vworld-address-error-captures.json")
                ["captures"]![1]!["response"]!.ToJsonString();
            var handler = new RoutingHandler(uri => AddressMode(uri) == "road"
                ? error
                : throw new HttpRequestException("synthetic transport failure"));
            AddressResolutionResult result = Resolver(handler, directory)
                .ResolveAsync("합성 provider 실패 주소").GetAwaiter().GetResult();

            AssertEx.Equal(AddressResolutionReason.ProviderReportedError, result.ReasonCode);
            AssertEx.Equal(2, result.ProviderFailures.Count);
            AssertEx.Equal(ProviderFailureMode.Road, result.ProviderFailures[0].Mode);
            AssertEx.Equal("PROVIDER_REPORTED_ERROR", result.ProviderFailures[0].CanonicalCode);
            AssertEx.Equal(ProviderFailureMode.Parcel, result.ProviderFailures[1].Mode);
            AssertEx.Equal("PROVIDER_TRANSPORT_FAILURE", result.ProviderFailures[1].CanonicalCode);
        });
    }

    [Test]
    internal static void Resolver_ConvertsMalformedBootstrapToModeSchemaFailure()
    {
        int wfsCalls = 0;
        WithTemporaryDirectory(directory =>
        {
            var handler = new RoutingHandler(uri =>
            {
                if (IsWfs(uri)) { Interlocked.Increment(ref wfsCalls); return WfsForJungGu(); }
                JsonObject road = AddressFixture("vworld-address-road-sejongdaero-110.json");
                if (AddressMode(uri) == "parcel")
                    return road["alternateModeCapture"]!["response"]!.ToJsonString();
                JsonObject response = road["response"]!.AsObject().DeepClone().AsObject();
                response["refined"]!["structure"]!["level4AC"] = "";
                return response.ToJsonString();
            });
            AddressResolutionResult result = Resolver(handler, directory)
                .ResolveAsync("서울특별시 중구 세종대로 110").GetAwaiter().GetResult();

            AssertEx.Equal(AddressResolutionReason.ProviderSchemaInvalid, result.ReasonCode);
            AssertEx.Equal(0, result.Candidates.Count);
            AssertEx.Equal(ProviderFailureMode.Road, result.ProviderFailures.Single().Mode);
            AssertEx.Equal(0, wfsCalls);
        });
    }

    [Test]
    internal static void Resolver_MapsTopologyAmbiguitiesAndNameFailuresAtResultLevel()
    {
        AddressCandidate road = Candidate(AddressCandidateKind.Road,
            new Wgs84Coordinate(126.97834678005411, 37.56670100709671), "태평로1가");
        ProjectedCoordinate point = ProjectedCoordinate.From(road.Wgs84);

        AddressResolutionResult edge = ResolveTopology(new[] { road }, "서울특별시 중구",
            new[]
            {
                Feature("11140103", Rectangle(point, -5, 5, -5, 5)),
                Feature("11140104", Rectangle(point, 0, 5, -5, 5), "을지로1가"),
            });
        AssertEx.Equal(AddressResolutionReason.DistrictEdge, edge.ReasonCode);
        AssertEx.Equal(AddressCandidateKind.Road, edge.RepresentativeLocation!.CandidateKind);
        AssertEx.Equal(1, edge.LegalDistrictAlternatives.Count);

        AddressResolutionResult multiple = ResolveTopology(new[] { road }, "서울특별시 중구",
            new[]
            {
                Feature("11140103", Rectangle(point, -5, 5, -5, 5)),
                Feature("11140104", Rectangle(point, -4, 6, -4, 6), "을지로1가"),
            });
        AssertEx.Equal(AddressResolutionReason.MultipleDistrictContainment, multiple.ReasonCode);
        AssertEx.Equal(2, multiple.LegalDistrictAlternatives.Count);

        AddressResolutionResult outside = ResolveTopology(new[] { road }, "서울특별시 중구",
            new[] { Feature("11140103", Rectangle(point, 20, 30, 20, 30)) });
        AssertEx.Equal(AddressResolutionStatus.OutOfCoverage, outside.Status);
        AssertEx.Equal(AddressCandidateKind.Road, outside.RepresentativeLocation!.CandidateKind);

        AddressResolutionResult legalMismatch = ResolveTopology(new[] { road }, "서울특별시 중구",
            new[] { Feature("11140103", Rectangle(point, -5, 5, -5, 5), "을지로1가") });
        AssertEx.Equal(AddressResolutionReason.LegalNameMismatch, legalMismatch.ReasonCode);

        AddressResolutionResult sigunguMismatch = ResolveTopology(new[] { road }, "서울특별시 종로구",
            new[] { Feature("11140103", Rectangle(point, -5, 5, -5, 5)) });
        AssertEx.Equal(AddressResolutionReason.LegalNameMismatch, sigunguMismatch.ReasonCode);
    }

    [Test]
    internal static void Resolver_MapsDistrictDisagreementAndExactCohortMismatch()
    {
        AddressCandidate road = Candidate(AddressCandidateKind.Road,
            new Wgs84Coordinate(126.97834678005411, 37.56670100709671), "태평로1가");
        var parcelPoint = new Wgs84Coordinate(126.9789, 37.5667);
        AddressCandidate parcel = Candidate(AddressCandidateKind.Parcel, parcelPoint, "을지로1가");
        ProjectedCoordinate roadProjected = ProjectedCoordinate.From(road.Wgs84);
        ProjectedCoordinate parcelProjected = ProjectedCoordinate.From(parcel.Wgs84);
        CohortBoundaryFeature first = Feature("11140103",
            Rectangle(roadProjected, -10, 10, -10, 10));
        CohortBoundaryFeature second = Feature("11140104",
            Rectangle(parcelProjected, -10, 10, -10, 10), "을지로1가");

        AddressResolutionResult disagreement = ResolveTopology(new[] { road, parcel },
            "서울특별시 중구", new[] { first, second });
        AssertEx.Equal(AddressResolutionReason.DistrictDisagreement, disagreement.ReasonCode);
        AssertEx.Equal(2, disagreement.LegalDistrictAlternatives.Count);
        AssertEx.True(disagreement.LegalDistrictAlternatives.Select(value => value.CandidateKind)
            .SequenceEqual(new[] { AddressCandidateKind.Road, AddressCandidateKind.Parcel }));

        ReferenceCohort cohort = new("11140", "서울특별시 중구",
            SeoulLegalDistrictCatalog.SourceVersion, new[] { "11140103", "11140104" });
        var reversed = new CohortBoundaryLookup(new[] { second, first }, 0, "cache",
            DateTimeOffset.Parse("2026-07-23T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-23T00:00:00Z"), DeliveryOrigin.Network);
        AddressResolutionResult mismatch = AddressTopologyResolver.Resolve("합성 입력",
            new[] { road }, new[] { DateTimeOffset.Parse("2026-07-23T00:00:00Z") },
            cohort, reversed, DateTimeOffset.Parse("2026-07-23T00:00:00Z"));
        AssertEx.Equal(AddressResolutionReason.ProviderSchemaInvalid, mismatch.ReasonCode);
        AssertEx.Equal(ProviderFailureMode.Boundary, mismatch.ProviderFailures.Single().Mode);
    }

    [Test]
    internal static void Resolver_DualDistanceThresholdIsInclusiveAtOneHundredFiftyMeters()
    {
        var origin = new ProjectedCoordinate(953_000, 1_952_000);
        AssertEx.False(VWorldAddressResolver.ExceedsDualDistance(origin,
            new ProjectedCoordinate(origin.X + 149.999, origin.Y)));
        AssertEx.False(VWorldAddressResolver.ExceedsDualDistance(origin,
            new ProjectedCoordinate(origin.X + 150.0, origin.Y)));
        AssertEx.True(VWorldAddressResolver.ExceedsDualDistance(origin,
            new ProjectedCoordinate(origin.X + 150.001, origin.Y)));
    }

    [Test]
    internal static void Resolver_PropagatesCancellationAndBoundaryProviderFailure()
    {
        WithTemporaryDirectory(directory =>
        {
            var handler = new AddressCancellationHandler();
            using var cancellation = new CancellationTokenSource();
            Task<AddressResolutionResult> pending = Resolver(handler, directory).ResolveAsync(
                "합성 취소 주소", cancellationToken: cancellation.Token);
            AssertEx.True(handler.Started.Task.Wait(TimeSpan.FromSeconds(2)));
            cancellation.Cancel();
            AssertEx.Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
            AssertEx.Equal(0, Directory.EnumerateFiles(directory).Count());
        });

        WithTemporaryDirectory(directory =>
        {
            var handler = new RoutingHandler(uri =>
            {
                if (IsWfs(uri)) return "<ows:ExceptionReport>INVALID_KEY</ows:ExceptionReport>";
                JsonObject road = AddressFixture("vworld-address-road-sejongdaero-110.json");
                return AddressMode(uri) == "road" ? road["response"]!.ToJsonString()
                    : road["alternateModeCapture"]!["response"]!.ToJsonString();
            });
            AddressResolutionResult result = Resolver(handler, directory)
                .ResolveAsync("서울특별시 중구 세종대로 110").GetAwaiter().GetResult();
            AssertEx.Equal(AddressResolutionReason.ProviderReportedError, result.ReasonCode);
            AssertEx.Equal(ProviderFailureMode.Boundary, result.ProviderFailures.Single().Mode);
            AssertEx.Equal(1, result.Candidates.Count);
        });
    }

    [Test]
    internal static void Resolver_ForceRefreshBypassesAddressAndBoundaryCaches()
    {
        WithTemporaryDirectory(directory =>
        {
            var handler = new RoutingHandler(uri =>
            {
                if (IsWfs(uri)) return WfsForJungGu();
                JsonObject road = AddressFixture("vworld-address-road-sejongdaero-110.json");
                return AddressMode(uri) == "road" ? road["response"]!.ToJsonString()
                    : road["alternateModeCapture"]!["response"]!.ToJsonString();
            });
            VWorldAddressResolver resolver = Resolver(handler, directory);
            _ = resolver.ResolveAsync("서울특별시 중구 세종대로 110").GetAwaiter().GetResult();
            AssertEx.Equal(3, handler.Requests.Count);
            _ = resolver.ResolveAsync("서울특별시 중구 세종대로 110").GetAwaiter().GetResult();
            AssertEx.Equal(3, handler.Requests.Count);
            _ = resolver.ResolveAsync("서울특별시 중구 세종대로 110", forceRefresh: true)
                .GetAwaiter().GetResult();
            AssertEx.Equal(6, handler.Requests.Count);
        });
    }

    [Test]
    internal static void Resolver_PropagatesBoundaryCancellationWithoutBoundaryCacheWrite()
    {
        WithTemporaryDirectory(directory =>
        {
            var handler = new BoundaryCancellationHandler();
            using var cancellation = new CancellationTokenSource();
            Task<AddressResolutionResult> pending = Resolver(handler, directory).ResolveAsync(
                "서울특별시 중구 세종대로 110", cancellationToken: cancellation.Token);
            AssertEx.True(handler.BoundaryStarted.Task.Wait(TimeSpan.FromSeconds(2)));
            cancellation.Cancel();
            AssertEx.Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
            ReferenceCohort cohort = SeoulLegalDistrictCatalog.CreateCohortForSigungu(
                "11140", "서울특별시 중구");
            string boundaryPath = Path.Combine(directory,
                CohortBoundaryCacheIdentity.Compute(cohort) + ".json");
            AssertEx.False(File.Exists(boundaryPath));
        });
    }

    [Test]
    internal static void Resolver_MapsBoundarySchemaTransportAndIncompleteFailures()
    {
        AssertBoundaryFailure("{}", AddressResolutionReason.ProviderSchemaInvalid);
        AssertBoundaryFailure(null, AddressResolutionReason.ProviderTransportFailure);

        JsonObject incomplete = JsonNode.Parse(WfsForJungGu())!.AsObject();
        incomplete["features"]!.AsArray().RemoveAt(incomplete["features"]!.AsArray().Count - 1);
        incomplete["numberMatched"] = 73;
        incomplete["numberReturned"] = 73;
        AssertBoundaryFailure(incomplete.ToJsonString(),
            AddressResolutionReason.CohortBoundaryIncomplete);
    }

    private static void RunResolverCase(
        string input,
        Func<Uri, string> route,
        Action<AddressResolutionResult> assert)
    {
        WithTemporaryDirectory(directory =>
        {
            AddressResolutionResult result = Resolver(new RoutingHandler(route), directory)
                .ResolveAsync(input).GetAwaiter().GetResult();
            assert(result);
        });
    }

    private static void AssertBoundaryFailure(string? boundaryBody, AddressResolutionReason reason)
    {
        WithTemporaryDirectory(directory =>
        {
            var handler = new RoutingHandler(uri =>
            {
                if (IsWfs(uri))
                {
                    if (boundaryBody == null) throw new HttpRequestException("synthetic WFS failure");
                    return boundaryBody;
                }
                JsonObject road = AddressFixture("vworld-address-road-sejongdaero-110.json");
                return AddressMode(uri) == "road" ? road["response"]!.ToJsonString()
                    : road["alternateModeCapture"]!["response"]!.ToJsonString();
            });
            AddressResolutionResult result = Resolver(handler, directory)
                .ResolveAsync("서울특별시 중구 세종대로 110").GetAwaiter().GetResult();
            AssertEx.Equal(reason, result.ReasonCode);
            AssertEx.Equal(ProviderFailureMode.Boundary, result.ProviderFailures.Single().Mode);
        });
    }

    private static AddressResolutionResult ResolveTopology(
        IReadOnlyList<AddressCandidate> candidates,
        string sigunguName,
        IReadOnlyList<CohortBoundaryFeature> features)
    {
        string[] ids = features.Select(feature => feature.CanonicalCode)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        ReferenceCohort cohort = new("11140", sigunguName,
            SeoulLegalDistrictCatalog.SourceVersion, ids);
        var lookup = new CohortBoundaryLookup(features.OrderBy(feature => feature.CanonicalCode,
                StringComparer.Ordinal), 0, "cache", DateTimeOffset.Parse("2026-07-23T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-23T00:00:00Z"), DeliveryOrigin.Network);
        return AddressTopologyResolver.Resolve("합성 입력", candidates,
            candidates.Select(_ => DateTimeOffset.Parse("2026-07-23T00:00:00Z")).ToArray(),
            cohort, lookup, DateTimeOffset.Parse("2026-07-23T00:00:00Z"));
    }

    private static VWorldAddressResolver Resolver(HttpMessageHandler handler, string directory)
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z"));
        var pipeline = new HttpPipeline(new HttpClient(handler), 2, 0,
            TimeSpan.FromSeconds(2));
        var provider = new VWorldResolutionProvider("test-key", pipeline,
            new AtomicCacheStore(directory, clock), clock);
        return new VWorldAddressResolver(provider, clock);
    }

    private static AddressCandidate Candidate(
        AddressCandidateKind kind,
        Wgs84Coordinate point,
        string legalName)
        => new(kind, "합성 입력", "합성 정제 주소",
            kind == AddressCandidateKind.Road
                ? new RefinedAddressStructure("대한민국", "서울특별시", "중구", legalName,
                    "합성로", "", "명동", "1114055000")
                : new RefinedAddressStructure("대한민국", "서울특별시", "중구", "",
                    legalName, "1114010300100010000", "명동", "1114055000"),
            point, "VWorld address/2.0",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

    private static CohortBoundaryFeature Feature(
        string code,
        BoundaryRing ring,
        string displayName = "태평로1가")
        => Feature(code, new[] { new BoundaryPart(ring, Array.Empty<BoundaryRing>()) }, displayName);

    private static CohortBoundaryFeature Feature(
        string code,
        BoundaryPart part,
        string displayName = "태평로1가")
        => Feature(code, new[] { part }, displayName);

    private static CohortBoundaryFeature Feature(
        string code,
        IEnumerable<BoundaryPart> parts,
        string displayName = "태평로1가")
        => new(code, code, $"서울특별시 중구 {displayName}", displayName,
            BoundaryTopology.Create(parts));

    private static BoundaryRing Rectangle(
        ProjectedCoordinate origin,
        double minX,
        double maxX,
        double minY,
        double maxY)
        => new(new[]
        {
            new Coordinate2D(origin.X + minX, origin.Y + minY),
            new Coordinate2D(origin.X + maxX, origin.Y + minY),
            new Coordinate2D(origin.X + maxX, origin.Y + maxY),
            new Coordinate2D(origin.X + minX, origin.Y + maxY),
            new Coordinate2D(origin.X + minX, origin.Y + minY),
        });

    private static JsonObject AddressFixture(string name)
        => JsonNode.Parse(File.ReadAllText(FindRepositoryFile("docs", "fixtures", name)))!
            .AsObject();

    private static string WfsForJungGu()
    {
        ReferenceCohort cohort = SeoulLegalDistrictCatalog.CreateCohortForSigungu(
            "11140", "서울특별시 중구");
        var features = new JsonArray();
        for (int index = 0; index < cohort.DistrictIds.Count; index++)
        {
            string id = cohort.DistrictIds[index];
            bool target = id == "11140103";
            double minX = target ? 126.977 : 127.1 + index * 0.00001;
            double minY = target ? 37.565 : 37.7;
            string name = target ? "태평로1가" : $"법정동{id}";
            features.Add(new JsonObject
            {
                ["type"] = "Feature",
                ["properties"] = new JsonObject
                {
                    ["emd_cd"] = id,
                    ["full_nm"] = $"서울특별시 중구 {name}",
                    ["emd_kor_nm"] = name,
                },
                ["geometry"] = new JsonObject
                {
                    ["type"] = "Polygon",
                    ["coordinates"] = new JsonArray(new JsonArray
                    {
                        new JsonArray(minX, minY),
                        new JsonArray(minX + 0.002, minY),
                        new JsonArray(minX + 0.002, minY + 0.004),
                        new JsonArray(minX, minY + 0.004),
                        new JsonArray(minX, minY),
                    }),
                },
            });
        }
        return new JsonObject
        {
            ["type"] = "FeatureCollection",
            ["numberMatched"] = features.Count,
            ["numberReturned"] = features.Count,
            ["features"] = features,
        }.ToJsonString();
    }

    private static bool IsWfs(Uri uri) => uri.AbsolutePath.EndsWith("/wfs", StringComparison.Ordinal);

    private static string AddressMode(Uri uri)
    {
        foreach (string pair in uri.Query.TrimStart('?').Split('&'))
        {
            string[] parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == "type") return Uri.UnescapeDataString(parts[1]);
        }
        return string.Empty;
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ursus-resolver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join('/', parts));
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<Uri, string> _route;
        public RoutingHandler(Func<Uri, string> route) => _route = route;
        public ConcurrentBag<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            string body = _route(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class BoundaryCancellationHandler : HttpMessageHandler
    {
        public TaskCompletionSource BoundaryStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            if (IsWfs(uri))
            {
                BoundaryStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            JsonObject road = AddressFixture("vworld-address-road-sejongdaero-110.json");
            string body = AddressMode(uri) == "road" ? road["response"]!.ToJsonString()
                : road["alternateModeCapture"]!["response"]!.ToJsonString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class AddressCancellationHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
