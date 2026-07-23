using System.Net;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using URSUS.Caching;
using URSUS.Net;
using URSUS.SiteBriefing;

namespace URSUS.Tests;

internal static class Stage1ProviderTests
{
    [Test]
    internal static void AddressProvider_ProjectsRoadFixtureAndUsesHashedCacheIdentity()
    {
        WithTemporaryDirectory(directory =>
        {
            JsonObject capture = ReadFixture("vworld-address-road-sejongdaero-110.json");
            string response = capture["response"]!.ToJsonString();
            var clock = new MutableClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z"));
            var handler = new RecordingHandler((_, _) => response);
            VWorldResolutionProvider provider = Provider(handler, directory, clock);

            VWorldAddressLookup first = provider.GetAddressAsync(
                "서울특별시 중구 세종대로 110", AddressCandidateKind.Road)
                .GetAwaiter().GetResult();
            clock.UtcNow = clock.UtcNow.AddHours(2);
            VWorldAddressLookup cached = provider.GetAddressAsync(
                "\t서울특별시  중구　세종대로 110\n", AddressCandidateKind.Road)
                .GetAwaiter().GetResult();

            AssertEx.Equal(AddressLookupStatus.Success, first.Status);
            AssertEx.Equal(AddressCandidateKind.Road, first.Candidate!.Kind);
            AssertEx.Equal("서울특별시 중구 세종대로 110 (태평로1가)",
                first.Candidate.RefinedText);
            AssertEx.Near(126.97834678005411, first.Candidate.Wgs84.Longitude);
            AssertEx.Near(37.56670100709671, first.Candidate.Wgs84.Latitude);
            AssertEx.Equal(
                "702f1e6f788434921910ab4ce1ec479cd18c096a1d20a22acb1402194cbcb13f",
                first.Candidate.ResponseFingerprint);
            AssertEx.Equal(DeliveryOrigin.Network, first.DeliveryOrigin);
            AssertEx.Equal(DeliveryOrigin.Cache, cached.DeliveryOrigin);
            AssertEx.Equal("\t서울특별시  중구　세종대로 110\n",
                cached.Candidate!.InputAddress);
            AssertEx.Equal(first.CapturedAtUtc, cached.CapturedAtUtc);
            AssertEx.Equal(1, handler.Requests.Count);
            AssertEx.True(File.Exists(Path.Combine(directory,
                "a695286bce728a878a5771ef280ec3ff7551a425d8226bdb198b42281b7d1f9b.json")));
        });
    }

    [Test]
    internal static void AddressProvider_ProjectsParcelFixture()
    {
        WithTemporaryDirectory(directory =>
        {
            string response = ReadFixture("vworld-address-parcel-taepyeongno-31.json")
                ["response"]!.ToJsonString();
            var handler = new RecordingHandler((_, _) => response);
            VWorldResolutionProvider provider = Provider(handler, directory,
                new MutableClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z")));

            VWorldAddressLookup lookup = provider.GetAddressAsync(
                "서울특별시 중구 태평로1가 31", AddressCandidateKind.Parcel)
                .GetAwaiter().GetResult();

            AssertEx.Equal(AddressLookupStatus.Success, lookup.Status);
            AssertEx.Equal(AddressCandidateKind.Parcel, lookup.Candidate!.Kind);
            AssertEx.Equal("서울특별시 중구 태평로1가 31", lookup.Candidate.RefinedText);
            AssertEx.Equal("태평로1가", lookup.Candidate.RefinedStructure.Level4L);
            AssertEx.Near(126.9782290751147, lookup.Candidate.Wgs84.Longitude);
            AssertEx.Near(37.56657117348658, lookup.Candidate.Wgs84.Latitude);
            AssertEx.Equal(1, handler.Requests.Count);
        });
    }

    [Test]
    internal static void AddressProvider_UsesShortNotFoundTtlAndForceRefresh()
    {
        WithTemporaryDirectory(directory =>
        {
            JsonObject capture = ReadFixture("vworld-address-road-sejongdaero-110.json");
            string notFound = capture["alternateModeCapture"]!["response"]!.ToJsonString();
            var clock = new MutableClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z"));
            var handler = new RecordingHandler((_, _) => notFound);
            VWorldResolutionProvider provider = Provider(handler, directory, clock);

            AssertEx.Equal(AddressLookupStatus.NotFound, provider.GetAddressAsync(
                "서울특별시 중구 세종대로 110", AddressCandidateKind.Parcel)
                .GetAwaiter().GetResult().Status);
            clock.UtcNow = clock.UtcNow.AddHours(23);
            AssertEx.Equal(DeliveryOrigin.Cache, provider.GetAddressAsync(
                "서울특별시 중구 세종대로 110", AddressCandidateKind.Parcel)
                .GetAwaiter().GetResult().DeliveryOrigin);
            clock.UtcNow = clock.UtcNow.AddHours(2);
            AssertEx.Equal(DeliveryOrigin.Network, provider.GetAddressAsync(
                "서울특별시 중구 세종대로 110", AddressCandidateKind.Parcel)
                .GetAwaiter().GetResult().DeliveryOrigin);
            AssertEx.Equal(2, handler.Requests.Count);

            _ = provider.GetAddressAsync("서울특별시 중구 세종대로 110",
                AddressCandidateKind.Parcel, forceRefresh: true).GetAwaiter().GetResult();
            AssertEx.Equal(3, handler.Requests.Count);
        });
    }

    [Test]
    internal static void AddressProvider_UsesThirtyDaySuccessTtlAndRepairsCorruptEntries()
    {
        WithTemporaryDirectory(directory =>
        {
            string response = ReadFixture("vworld-address-road-sejongdaero-110.json")
                ["response"]!.ToJsonString();
            var clock = new MutableClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z"));
            var handler = new RecordingHandler((_, _) => response);
            VWorldResolutionProvider provider = Provider(handler, directory, clock);
            _ = provider.GetAddressAsync("서울특별시 중구 세종대로 110",
                AddressCandidateKind.Road).GetAwaiter().GetResult();
            clock.UtcNow = clock.UtcNow.AddDays(29);
            AssertEx.Equal(DeliveryOrigin.Cache, provider.GetAddressAsync(
                "서울특별시 중구 세종대로 110", AddressCandidateKind.Road)
                .GetAwaiter().GetResult().DeliveryOrigin);
            clock.UtcNow = clock.UtcNow.AddDays(2);
            AssertEx.Equal(DeliveryOrigin.Network, provider.GetAddressAsync(
                "서울특별시 중구 세종대로 110", AddressCandidateKind.Road)
                .GetAwaiter().GetResult().DeliveryOrigin);
            AssertEx.Equal(2, handler.Requests.Count);

            string cachePath = Directory.EnumerateFiles(directory).Single();
            File.WriteAllText(cachePath,
                "{\"schemaVersion\":1,\"retrievedAt\":\"2026-07-23T00:00:00Z\",\"acquisitionOrigin\":0,\"value\":null}");
            AssertEx.Equal(DeliveryOrigin.Network, provider.GetAddressAsync(
                "서울특별시 중구 세종대로 110", AddressCandidateKind.Road)
                .GetAwaiter().GetResult().DeliveryOrigin);
            AssertEx.Equal(3, handler.Requests.Count);
        });
    }

    [Test]
    internal static void AddressProvider_MapsReportedAndSchemaFailuresWithoutCachingSecrets()
    {
        WithTemporaryDirectory(directory =>
        {
            JsonObject fixture = ReadFixture("vworld-address-error-captures.json");
            string error = fixture["captures"]![1]!["response"]!.ToJsonString();
            var handler = new RecordingHandler((_, _) => error);
            VWorldResolutionProvider provider = Provider(handler, directory,
                new MutableClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z")),
                apiKey: "top-secret-test-key");

            for (int i = 0; i < 2; i++)
            {
                try
                {
                    _ = provider.GetAddressAsync("서울특별시 중구 세종대로 110",
                        AddressCandidateKind.Road).GetAwaiter().GetResult();
                    throw new InvalidOperationException("Expected provider failure.");
                }
                catch (VWorldProviderException ex)
                {
                    AssertEx.Equal(AddressResolutionReason.ProviderReportedError, ex.ReasonCode);
                    AssertEx.Equal("PROVIDER_REPORTED_ERROR", ex.Failure.CanonicalCode);
                    AssertEx.False(ex.Message.Contains("top-secret-test-key", StringComparison.Ordinal));
                    AssertEx.False(ex.Message.Contains("세종대로", StringComparison.Ordinal));
                }
            }
            AssertEx.Equal(2, handler.Requests.Count);
            AssertEx.Equal(0, Directory.EnumerateFiles(directory).Count());
        });

        WithTemporaryDirectory(directory =>
        {
            const string malformed = "{\"status\":\"OK\",\"service\":{\"version\":\"2.0\"}}";
            var handler = new RecordingHandler((_, _) => malformed);
            VWorldResolutionProvider provider = Provider(handler, directory,
                new MutableClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z")));
            AssertEx.Throws<VWorldProviderException>(() => provider.GetAddressAsync(
                "서울특별시 중구 세종대로 110", AddressCandidateKind.Road)
                .GetAwaiter().GetResult());
            AssertEx.Equal(0, Directory.EnumerateFiles(directory).Count());
        });
    }

    [Test]
    internal static void AddressProvider_RejectsMalformedSuccessAndErrorContracts()
    {
        string valid = ReadFixture("vworld-address-road-sejongdaero-110.json")
            ["response"]!.ToJsonString();
        var malformed = new List<string>();
        foreach (Action<JsonObject> mutation in new Action<JsonObject>[]
        {
            response => response["service"]!["operation"] = "search",
            response => response["result"]!["crs"] = "EPSG:5179",
            response => response["input"]!["type"] = "parcel",
            response => response["input"]!["address"] = "서울특별시 종로구 세종대로 1",
        })
        {
            JsonObject response = JsonNode.Parse(valid)!.AsObject();
            mutation(response);
            malformed.Add(response.ToJsonString());
        }
        malformed.Add(new JsonObject
        {
            ["service"] = new JsonObject
            {
                ["name"] = "address",
                ["version"] = "2.0",
                ["operation"] = "getcoord",
            },
            ["status"] = "ERROR",
            ["error"] = new JsonObject { ["text"] = "missing code" },
        }.ToJsonString());

        foreach (string body in malformed)
        {
            WithTemporaryDirectory(directory =>
            {
                var handler = new RecordingHandler((_, _) => body);
                VWorldResolutionProvider provider = Provider(handler, directory,
                    new MutableClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z")));
                VWorldProviderException ex = CaptureFailure(() => provider.GetAddressAsync(
                    "서울특별시 중구 세종대로 110", AddressCandidateKind.Road)
                    .GetAwaiter().GetResult());
                AssertEx.Equal(AddressResolutionReason.ProviderSchemaInvalid, ex.ReasonCode);
                AssertEx.Equal(0, Directory.EnumerateFiles(directory).Count());
            });
        }
    }

    [Test]
    internal static void CohortBoundaryProvider_FiltersExactMembershipAndRoundTripsCache()
    {
        WithTemporaryDirectory(directory =>
        {
            JsonObject oracle = ReadFixture("vworld-cohort-boundary-cases-v1.json");
            JsonObject scenario = oracle["scenarios"]!.AsArray().Select(node => node!.AsObject())
                .Single(item => item["caseId"]!.GetValue<string>() ==
                    "complete-membership-with-provider-extras");
            string[] providerIds = scenario["pages"]![0]!["featureCanonicalIds"]!.AsArray()
                .Select(node => node!.GetValue<string>()).ToArray();
            string[] expectedIds = scenario["expected"]!["filteredDistrictIds"]!.AsArray()
                .Select(node => node!.GetValue<string>()).ToArray();
            ReferenceCohort cohort = SeoulLegalDistrictCatalog.CreateCohort(
                "11140103", "서울특별시 중구");
            string response = WfsResponse(providerIds, providerIds.Length, providerIds.Length);
            var clock = new MutableClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z"));
            var handler = new RecordingHandler((uri, _) =>
            {
                AssertEx.True(uri.Query.Contains("BBOX=126.7,37.4,127.3,37.72",
                    StringComparison.Ordinal));
                AssertEx.True(uri.Query.Contains("COUNT=1000", StringComparison.Ordinal));
                AssertEx.True(uri.Query.Contains("STARTINDEX=0", StringComparison.Ordinal));
                AssertEx.False(uri.Query.Contains("radius", StringComparison.OrdinalIgnoreCase));
                return response;
            });
            VWorldResolutionProvider provider = Provider(handler, directory, clock);

            CohortBoundaryLookup first = provider.GetLegalDistrictsForCohortAsync(cohort)
                .GetAwaiter().GetResult();
            clock.UtcNow = clock.UtcNow.AddDays(29);
            CohortBoundaryLookup cached = provider.GetLegalDistrictsForCohortAsync(cohort)
                .GetAwaiter().GetResult();

            AssertEx.Equal(74, first.Features.Count);
            AssertEx.Equal(2, first.ProviderExtraCount);
            AssertEx.True(first.Features.Select(feature => feature.CanonicalCode)
                .SequenceEqual(expectedIds));
            AssertEx.True(first.Features.All(feature => feature.Topology.Area > 0));
            AssertEx.True(first.Features[0].Topology.Centroid.X > 900_000);
            AssertEx.Equal($"서울특별시 테스트구 법정동{expectedIds[0]}",
                first.Features[0].FullName);
            AssertEx.Equal($"법정동{expectedIds[0]}", first.Features[0].DisplayName);
            AssertEx.Equal(DeliveryOrigin.Network, first.DeliveryOrigin);
            AssertEx.Equal(DeliveryOrigin.Cache, cached.DeliveryOrigin);
            AssertEx.Equal(first.Features[0].ProviderCode, cached.Features[0].ProviderCode);
            AssertEx.Equal(first.Features[0].FullName, cached.Features[0].FullName);
            AssertEx.Equal(first.Features[0].DisplayName, cached.Features[0].DisplayName);
            AssertEx.Equal(1, handler.Requests.Count);
            AssertEx.Equal(
                oracle["cacheIdentity"]!["sha256"]!.GetValue<string>(),
                first.CacheId);
            AssertEx.True(File.Exists(Path.Combine(directory, first.CacheId + ".json")));

            clock.UtcNow = clock.UtcNow.AddDays(2);
            AssertEx.Equal(DeliveryOrigin.Network, provider.GetLegalDistrictsForCohortAsync(cohort)
                .GetAwaiter().GetResult().DeliveryOrigin);
            AssertEx.Equal(2, handler.Requests.Count);
            _ = provider.GetLegalDistrictsForCohortAsync(cohort, forceRefresh: true)
                .GetAwaiter().GetResult();
            AssertEx.Equal(3, handler.Requests.Count);

            File.WriteAllText(Path.Combine(directory, first.CacheId + ".json"),
                "{\"schemaVersion\":1,\"retrievedAt\":\"2026-07-23T00:00:00Z\",\"acquisitionOrigin\":0," +
                "\"value\":{\"schemaVersion\":0}}" );
            AssertEx.Equal(DeliveryOrigin.Network, provider.GetLegalDistrictsForCohortAsync(cohort)
                .GetAwaiter().GetResult().DeliveryOrigin);
            AssertEx.Equal(4, handler.Requests.Count);
        });
    }

    [Test]
    internal static void CohortBoundaryProvider_RejectsLossyTopologyWithoutCaching()
    {
        ReferenceCohort cohort = SeoulLegalDistrictCatalog.CreateCohort(
            "11140103", "서울특별시 중구");
        string[] ids = cohort.DistrictIds.ToArray();
        foreach (Action<JsonObject> mutation in new Action<JsonObject>[]
        {
            response => response["features"]![0]!["geometry"]!["coordinates"]![0]!
                .AsArray().RemoveAt(4),
            response => response["features"]![0]!["geometry"]!["coordinates"]!
                .AsArray().Add(new JsonArray(
                    new JsonArray(126.9702, 37.5602),
                    new JsonArray(126.9703, 37.5602),
                    new JsonArray(126.9702, 37.5602))),
            response => response["features"]![0]!["geometry"] = new JsonObject
            {
                ["type"] = "MultiPolygon",
                ["coordinates"] = new JsonArray(
                    response["features"]![0]!["geometry"]!["coordinates"]!.DeepClone(),
                    new JsonArray()),
            },
        })
        {
            WithTemporaryDirectory(directory =>
            {
                JsonObject response = JsonNode.Parse(WfsResponse(ids, ids.Length, ids.Length))!
                    .AsObject();
                mutation(response);
                var handler = new RecordingHandler((_, _) => response.ToJsonString());
                VWorldResolutionProvider provider = Provider(handler, directory,
                    new MutableClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z")));
                VWorldProviderException ex = CaptureFailure(() =>
                    provider.GetLegalDistrictsForCohortAsync(cohort).GetAwaiter().GetResult());
                AssertEx.Equal(AddressResolutionReason.ProviderSchemaInvalid, ex.ReasonCode);
                AssertEx.Equal(0, Directory.EnumerateFiles(directory).Count());
            });
        }
    }

    [Test]
    internal static void Provider_PropagatesCallerCancellationWithoutCaching()
    {
        WithTemporaryDirectory(directory =>
        {
            var handler = new CancellationHandler();
            VWorldResolutionProvider provider = Provider(handler, directory,
                new MutableClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z")));
            using var cancellation = new CancellationTokenSource();
            Task<VWorldAddressLookup> pending = provider.GetAddressAsync(
                "서울특별시 중구 세종대로 110", AddressCandidateKind.Road,
                cancellationToken: cancellation.Token);
            AssertEx.True(handler.Started.Task.Wait(TimeSpan.FromSeconds(2)));
            cancellation.Cancel();
            AssertEx.Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
            AssertEx.True(handler.Cancelled.Task.Wait(TimeSpan.FromSeconds(2)));
            AssertEx.Equal(0, Directory.EnumerateFiles(directory).Count());
        });
    }

    [Test]
    internal static void CohortBoundaryProvider_FailsClosedForMissingDuplicateAndEarlyTermination()
    {
        ReferenceCohort cohort = SeoulLegalDistrictCatalog.CreateCohort(
            "11140103", "서울특별시 중구");

        WithTemporaryDirectory(directory =>
        {
            string[] ids = cohort.DistrictIds.Take(73).Concat(new[] { "11140175" }).ToArray();
            var handler = new RecordingHandler((_, _) => WfsResponse(ids, ids.Length, ids.Length));
            VWorldResolutionProvider provider = Provider(handler, directory,
                new MutableClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z")));
            VWorldProviderException ex = CaptureFailure(() =>
                provider.GetLegalDistrictsForCohortAsync(cohort).GetAwaiter().GetResult());
            AssertEx.Equal(AddressResolutionReason.CohortBoundaryIncomplete, ex.ReasonCode);
            AssertEx.Equal("COHORT_BOUNDARY_INCOMPLETE", ex.Failure.CanonicalCode);
            AssertEx.Equal(0, Directory.EnumerateFiles(directory).Count());
        });

        WithTemporaryDirectory(directory =>
        {
            string[] ids = cohort.DistrictIds.Concat(new[] { cohort.DistrictIds[0] }).ToArray();
            var handler = new RecordingHandler((_, _) => WfsResponse(ids, ids.Length, ids.Length));
            VWorldResolutionProvider provider = Provider(handler, directory,
                new MutableClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z")));
            VWorldProviderException ex = CaptureFailure(() =>
                provider.GetLegalDistrictsForCohortAsync(cohort).GetAwaiter().GetResult());
            AssertEx.Equal(AddressResolutionReason.ProviderSchemaInvalid, ex.ReasonCode);
            AssertEx.Equal(0, Directory.EnumerateFiles(directory).Count());
        });

        WithTemporaryDirectory(directory =>
        {
            string[] firstPage = Enumerable.Range(0, 1000)
                .Select(index => (11_200_000 + index).ToString(CultureInfo.InvariantCulture))
                .ToArray();
            var handler = new RecordingHandler((uri, _) =>
            {
                bool second = uri.Query.Contains("STARTINDEX=1000", StringComparison.Ordinal);
                return second ? WfsResponse([], 1200, 0) : WfsResponse(firstPage, 1200, 1000);
            });
            VWorldResolutionProvider provider = Provider(handler, directory,
                new MutableClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z")));
            VWorldProviderException ex = CaptureFailure(() =>
                provider.GetLegalDistrictsForCohortAsync(cohort).GetAwaiter().GetResult());
            AssertEx.Equal(AddressResolutionReason.ProviderSchemaInvalid, ex.ReasonCode);
            AssertEx.Equal(2, handler.Requests.Count);
            AssertEx.Equal(0, Directory.EnumerateFiles(directory).Count());
        });

        WithTemporaryDirectory(directory =>
        {
            JsonObject response = JsonNode.Parse(WfsResponse(
                cohort.DistrictIds, cohort.DistrictIds.Count, cohort.DistrictIds.Count))!
                .AsObject();
            response["features"]!.AsArray()[0] = "not-a-feature-object";
            var handler = new RecordingHandler((_, _) => response.ToJsonString());
            VWorldResolutionProvider provider = Provider(handler, directory,
                new MutableClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z")));
            VWorldProviderException ex = CaptureFailure(() =>
                provider.GetLegalDistrictsForCohortAsync(cohort).GetAwaiter().GetResult());
            AssertEx.Equal(AddressResolutionReason.ProviderSchemaInvalid, ex.ReasonCode);
            AssertEx.Equal(0, Directory.EnumerateFiles(directory).Count());
        });
    }

    [Test]
    internal static void CohortBoundaryProvider_MapsProviderAndTransportErrorsToSafeNonCachedFailures()
    {
        ReferenceCohort cohort = SeoulLegalDistrictCatalog.CreateCohort(
            "11140103", "서울특별시 중구");
        WithTemporaryDirectory(directory =>
        {
            const string exceptionReport =
                "<ows:ExceptionReport><ows:ExceptionText>INVALID_KEY top-secret-test-key</ows:ExceptionText></ows:ExceptionReport>";
            var handler = new RecordingHandler((_, _) => exceptionReport);
            VWorldResolutionProvider provider = Provider(handler, directory,
                new MutableClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z")),
                "top-secret-test-key");
            VWorldProviderException ex = CaptureFailure(() =>
                provider.GetLegalDistrictsForCohortAsync(cohort).GetAwaiter().GetResult());
            AssertEx.Equal(AddressResolutionReason.ProviderReportedError, ex.ReasonCode);
            AssertEx.False(ex.ToString().Contains("top-secret-test-key", StringComparison.Ordinal));
            AssertEx.Equal(0, Directory.EnumerateFiles(directory).Count());
        });

        WithTemporaryDirectory(directory =>
        {
            var handler = new RecordingHandler((uri, _) => throw new HttpRequestException(
                $"failed {uri}"));
            VWorldResolutionProvider provider = Provider(handler, directory,
                new MutableClock(DateTimeOffset.Parse("2026-07-23T00:00:00Z")),
                "top-secret-test-key");
            VWorldProviderException ex = CaptureFailure(() =>
                provider.GetLegalDistrictsForCohortAsync(cohort).GetAwaiter().GetResult());
            AssertEx.Equal(AddressResolutionReason.ProviderTransportFailure, ex.ReasonCode);
            AssertEx.False(ex.ToString().Contains("top-secret-test-key", StringComparison.Ordinal));
            AssertEx.Equal(0, Directory.EnumerateFiles(directory).Count());
        });
    }

    private static VWorldResolutionProvider Provider(
        HttpMessageHandler handler,
        string cacheDirectory,
        MutableClock clock,
        string apiKey = "test-key-not-a-secret")
    {
        var pipeline = new HttpPipeline(new HttpClient(handler), maxConcurrency: 2,
            maxRetries: 0, requestTimeout: TimeSpan.FromSeconds(5));
        return new VWorldResolutionProvider(apiKey, pipeline,
            new AtomicCacheStore(cacheDirectory, clock), clock);
    }

    private static JsonObject ReadFixture(string name)
        => JsonNode.Parse(File.ReadAllText(FindRepositoryFile("docs", "fixtures", name)))!
            .AsObject();

    private static string WfsResponse(
        IReadOnlyList<string> ids,
        int numberMatched,
        int numberReturned)
    {
        var features = new JsonArray();
        for (int index = 0; index < ids.Count; index++)
        {
            string id = ids[index];
            double offset = index * 0.000001;
            var ring = new JsonArray
            {
                new JsonArray(126.97 + offset, 37.56),
                new JsonArray(126.971 + offset, 37.56),
                new JsonArray(126.971 + offset, 37.561),
                new JsonArray(126.97 + offset, 37.561),
                new JsonArray(126.97 + offset, 37.56),
            };
            features.Add(new JsonObject
            {
                ["type"] = "Feature",
                ["properties"] = new JsonObject
                {
                    ["emd_cd"] = id,
                    ["full_nm"] = $"서울특별시 테스트구 법정동{id}",
                    ["emd_kor_nm"] = $"법정동{id}",
                },
                ["geometry"] = new JsonObject
                {
                    ["type"] = "Polygon",
                    ["coordinates"] = new JsonArray(ring),
                },
            });
        }
        return new JsonObject
        {
            ["type"] = "FeatureCollection",
            ["numberMatched"] = numberMatched,
            ["numberReturned"] = numberReturned,
            ["features"] = features,
        }.ToJsonString();
    }

    private static VWorldProviderException CaptureFailure(Action action)
    {
        try { action(); }
        catch (VWorldProviderException ex) { return ex; }
        throw new InvalidOperationException("Expected VWorldProviderException.");
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ursus-stage1-{Guid.NewGuid():N}");
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
        throw new FileNotFoundException($"Repository file not found: {Path.Combine(parts)}");
    }

    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<Uri, int, string> _response;
        public RecordingHandler(Func<Uri, int, string> response) => _response = response;
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            Requests.Add(uri);
            string body = _response(uri, Requests.Count);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }
        }
    }
}
