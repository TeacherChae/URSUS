using System.Net;
using System.Text;
using System.Text.Json;
using URSUS.Analysis;
using URSUS.Caching;
using URSUS.Config;
using URSUS.DataSources;
using URSUS.Net;
using URSUS.Parsers;
using URSUS.Resources;

namespace URSUS.Tests;

internal static class LandPriceProvenanceTests
{
    [Test]
    internal static void TypedParser_ComputesMeanAndCount_AndLegacyApiKeepsNumericContract()
    {
        var handler = new LandRowsHandler();
        var parser = Parser(handler);

        var typed = parser.GetLandPriceAggregatesByLegalDistrict(
            new List<string> { "11110101", "11110102" });
        AssertEx.Near(200, typed["11110101"].Mean);
        AssertEx.Equal(2, typed["11110101"].SampleCount);
        AssertEx.Near(500, typed["11110102"].Mean);
        AssertEx.Equal(1, typed["11110102"].SampleCount);

        var legacy = parser.GetLandPriceByLegalDistrict(
            new List<string> { "11110101", "11110102" });
        AssertEx.Near(200, legacy["11110101"]);
        AssertEx.Near(500, legacy["11110102"]);
        AssertEx.Equal(typeof(Dictionary<string, double>), legacy.GetType());
    }

    [Test]
    internal static void TypedParser_CacheRoundTripPreservesSampleCountsAndUsesNewSchemaFile()
    {
        WithTemporaryDirectory(directory =>
        {
            var online = new LandRowsHandler();
            var first = Parser(online).GetLandPriceAggregatesByLegalDistrict(
                new List<string> { "11110101" }, directory);
            AssertEx.Equal(2, first["11110101"].SampleCount);
            string cachePath = Directory.GetFiles(directory, "land_price.v2.*.json").Single();

            var offline = new ThrowingHandler();
            var second = Parser(offline).GetLandPriceAggregatesByLegalDistrict(
                new List<string> { "11110101" }, directory);
            AssertEx.Equal(2, second["11110101"].SampleCount);
            AssertEx.Near(200, second["11110101"].Mean);
            AssertEx.Equal(0, offline.RequestCount);

            File.WriteAllText(cachePath,
                "{\"11110101\":{\"Mean\":200,\"SampleCount\":0}}");
            var refetch = new LandRowsHandler();
            var repaired = Parser(refetch).GetLandPriceAggregatesByLegalDistrict(
                new List<string> { "11110101" }, directory);
            AssertEx.Equal(2, repaired["11110101"].SampleCount);
            AssertEx.Equal(1, refetch.RequestCount);

            File.WriteAllText(cachePath, "{\"11110101\":null}");
            var nullRepair = new LandRowsHandler();
            var repairedNull = Parser(nullRepair).GetLandPriceAggregatesByLegalDistrict(
                new List<string> { "11110101" }, directory);
            AssertEx.Equal(2, repairedNull["11110101"].SampleCount);
            AssertEx.Equal(1, nullRepair.RequestCount);
        });
    }

    [Test]
    internal static void TypedParser_CacheIsSeparatedByYearAndRequestedDistrictSet()
    {
        WithTemporaryDirectory(directory =>
        {
            var handler = new LandRowsHandler();
            var parser = Parser(handler);
            parser.GetLandPriceAggregatesByLegalDistrictAsync(
                new List<string> { "11110101" }, directory, standardYear: 2024)
                .GetAwaiter().GetResult();
            parser.GetLandPriceAggregatesByLegalDistrictAsync(
                new List<string> { "11110101" }, directory, standardYear: 2024)
                .GetAwaiter().GetResult();
            parser.GetLandPriceAggregatesByLegalDistrictAsync(
                new List<string> { "11110101" }, directory, standardYear: 2023)
                .GetAwaiter().GetResult();
            parser.GetLandPriceAggregatesByLegalDistrictAsync(
                new List<string> { "11110102" }, directory, standardYear: 2024)
                .GetAwaiter().GetResult();

            AssertEx.Equal(3, handler.RequestCount);
            AssertEx.Equal(3, Directory.GetFiles(directory, "land_price.v2.*.json").Length);
        });
    }

    [Test]
    internal static void LandSource_ExposesPerDistrictCountsAndRawSourceSampleTotal()
    {
        WithTemporaryDirectory(directory =>
        {
            var oldKey = PersistentCacheKey.Create("land_price", 2, QueryIntent.Latest,
                new Dictionary<string, string> { ["stdrYear"] = "2025" },
                new[] { "11110101", "11110102" },
                CoordinateReferenceSystem.Epsg5179);
            File.WriteAllText(Path.Combine(directory, oldKey.Value + ".json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    retrievedAt = "2026-07-01T00:00:00+00:00",
                    acquisitionOrigin = 0,
                    value = new Dictionary<string, double>
                    {
                        ["11110101"] = 999,
                        ["11110102"] = 999,
                    },
                }));
            var currentKey = PersistentCacheKey.Create("land_price",
                LandPriceDataSource.CacheSchemaVersion, QueryIntent.Latest,
                new Dictionary<string, string> { ["stdrYear"] = "2025" },
                new[] { "11110101", "11110102" },
                CoordinateReferenceSystem.Epsg5179);
            File.WriteAllText(Path.Combine(directory, currentKey.Value + ".json"), """
                {"schemaVersion":1,"retrievedAt":"2026-07-01T00:00:00+00:00",
                 "acquisitionOrigin":0,"value":{
                   "11110101":{"mean":999,"sampleCount":0},
                   "11110102":{"mean":999,"sampleCount":-1}}}
                """);

            var handler = new LandRowsHandler();
            var source = new LandPriceDataSource(Keys(),
                new HttpPipeline(new HttpClient(handler), maxRetries: 0),
                new FrozenClock(), new AtomicCacheStore(directory, new FrozenClock()));
            var result = source.FetchAsync(new DataQuery
            {
                DistrictCodes = new[] { "11110101", "11110102" },
            }).GetAwaiter().GetResult();

            AssertEx.True(result.IsSuccess);
            AssertEx.Equal(DeliveryOrigin.Network, result.DeliveryOrigin);
            AssertEx.Equal(3, result.Data!.RawRecordCount);
            AssertEx.Equal(2, result.Data.Records["11110101"].SampleCount);
            AssertEx.Equal(1, result.Data.SampleCounts["11110102"]);
            AssertEx.Near(200, result.Data.ToDictionary()["11110101"]);
            AssertEx.Equal(3, LandPriceDataSource.CacheSchemaVersion);

            var cached = source.FetchAsync(new DataQuery
            {
                DistrictCodes = new[] { "11110101", "11110102" },
            }).GetAwaiter().GetResult();
            AssertEx.Equal(DeliveryOrigin.Cache, cached.DeliveryOrigin);
            AssertEx.Equal(2, cached.Data!.SampleCounts["11110101"]);
            AssertEx.Equal(1, handler.RequestCount);
        });
    }

    [Test]
    internal static void LandSource_ZeroProviderRowsPreservesTypedNoDataFailureAndCachesResponse()
    {
        WithTemporaryDirectory(directory =>
        {
            var handler = new EmptyLandHandler();
            var source = new LandPriceDataSource(Keys(),
                new HttpPipeline(new HttpClient(handler), maxRetries: 0),
                new FrozenClock(), new AtomicCacheStore(directory, new FrozenClock()));
            var query = new DataQuery { DistrictCodes = new[] { "11110101" } };

            var first = source.FetchAsync(query).GetAwaiter().GetResult();
            AssertEx.False(first.IsSuccess);
            AssertEx.Equal(ErrorCodes.LandPriceNoData, first.Error!.Code);
            var second = source.FetchAsync(query).GetAwaiter().GetResult();
            AssertEx.False(second.IsSuccess);
            AssertEx.Equal(ErrorCodes.LandPriceNoData, second.Error!.Code);
            AssertEx.Equal(1, handler.RequestCount);
        });
    }

    [Test]
    internal static void Snapshot_DefensivelyCopiesSampleCounts()
    {
        var counts = new Dictionary<string, int> { ["11110101"] = 2 };
        var values = new Dictionary<string, double> { ["11110101"] = 200 };
        var snapshot = new AnalysisSnapshot(new[] { "11110101" }, new[]
        {
            new SnapshotLayer("land_price", "원/㎡", values, null,
                DateTimeOffset.UtcNow, AcquisitionOrigin.Network,
                DeliveryOrigin.Network, 1.0, SampleCounts: counts),
        });

        counts["11110101"] = 999;
        values["11110101"] = 999;
        AssertEx.Equal(2, snapshot.Layers["land_price"].SampleCounts!["11110101"]);
        AssertEx.Near(200, snapshot.Layers["land_price"].RawValues["11110101"]);
        AssertEx.Throws<NotSupportedException>(() =>
            ((IDictionary<string, int>)snapshot.Layers["land_price"].SampleCounts!)["x"] = 1);
    }

    private static LandPriceApiParser Parser(HttpMessageHandler handler)
        => new("secret", new HttpPipeline(new HttpClient(handler), maxRetries: 0),
            new FrozenClock());

    private static ApiKeyProvider Keys() => new(new Dictionary<string, string>
    {
        [ApiKeyProvider.LegacyDataGoKrKeyName] = "secret",
    });

    private static void WithTemporaryDirectory(Action<string> action)
    {
        string path = Path.Combine(Path.GetTempPath(), "ursus-land-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try { action(path); }
        finally { Directory.Delete(path, recursive: true); }
    }

    private sealed class FrozenClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class LandRowsHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            const string body = """
            {"referLandPrices":{"resultCode":"000","totalCount":3,"referLandPrice":[
              {"ldCode":"1111010100","pblntfPclnd":"100"},
              {"ldCode":"1111010100123456789","pblntfPclnd":"300"},
              {"ldCode":"1111010200","pblntfPclnd":"500"}
            ]}}
            """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new InvalidOperationException("network should not be used");
        }
    }

    private sealed class EmptyLandHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"referLandPrices\":{\"resultCode\":\"000\",\"totalCount\":0}}",
                    Encoding.UTF8, "application/json"),
            });
        }
    }
}
