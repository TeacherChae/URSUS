using URSUS.Analysis;
using URSUS.DataSources;
using URSUS.Execution;
using URSUS.Parsers;
using URSUS.Resources;
using URSUS.Config;
using System.Text.Json;
using System.Reflection;

namespace URSUS.Tests;

internal static class Phase0Tests
{
    [Test]
    internal static void Legal8_Legal10_AndPnuCanonicalizeToSameLegalId()
    {
        AssertEx.Equal("11110101", DistrictCode.CanonicalizeLegal("11110101"));
        AssertEx.Equal("11110101", DistrictCode.CanonicalizeLegal("1111010100"));
        AssertEx.Equal("11110101", DistrictCode.CanonicalizeLegal("1111010100101230000"));
    }

    [Test]
    internal static void InvalidLegalCodeFormats_AreRejected()
    {
        AssertEx.Equal(string.Empty, DistrictCode.CanonicalizeLegal("abc1111010100"));
        AssertEx.Equal(string.Empty, DistrictCode.CanonicalizeLegal("1111010100000"));
        AssertEx.Equal(string.Empty, DistrictCode.CanonicalizeLegal("1111010"));
    }

    [Test]
    internal static void LandPriceCache_MatchesRequestedLegal8AgainstLegal10AndPnu()
    {
        WithTemporaryDirectory(directory =>
        {
            File.WriteAllText(
                Path.Combine(directory, "land_price.json"),
                JsonSerializer.Serialize(new Dictionary<string, double>
                {
                    ["1111010100"] = 10,
                    ["1111010100101230000"] = 30,
                }));

            var result = new LandPriceApiParser("test-key")
                .GetLandPriceByLegalDistrict(new List<string> { "11110101" }, directory);

            AssertEx.Equal(1, result.Count);
            AssertEx.Near(20, result["11110101"]);
        });
    }

    [Test]
    internal static void ZoningCache_MatchesRequestedLegal8AgainstLegal10AndPnu()
    {
        WithTemporaryDirectory(directory =>
        {
            File.WriteAllText(
                Path.Combine(directory, "zoning_score.json"),
                JsonSerializer.Serialize(new Dictionary<string, double>
                {
                    ["1111010100"] = 2,
                    ["1111010100101230000"] = 4,
                }));

            var result = new ZoningApiParser("test-key")
                .GetZoningScoreByDistrict(new List<string> { "11110101" }, directory);

            AssertEx.Equal(1, result.Count);
            AssertEx.Near(3, result["11110101"]);
        });
    }

    [Test]
    internal static void AdministrativeCode_IsMappedRatherThanTreatedAsLegalPrefix()
    {
        var mapping = MappingLoader.Load();
        AssertEx.True(mapping.TryGetValue("11110515", out var legalIds));
        AssertEx.True(legalIds!.Contains("11110101"));
        AssertEx.False(legalIds.Contains("11110515"));
    }

    [Test]
    internal static void PartialCoverage_PreservesMissingAndRenormalizesPerDistrict()
    {
        var result = OverlayCalculator.Compute(
            new[] { "A", "B", "C" },
            new[]
            {
                new OverlayLayer("partial", 0.6, new Dictionary<string, double>
                {
                    ["A"] = 10,
                    ["C"] = 30,
                }),
                new OverlayLayer("complete", 0.4, new Dictionary<string, double>
                {
                    ["A"] = 100,
                    ["B"] = 200,
                    ["C"] = 300,
                }),
            });

        AssertEx.Near(0.0, result.Values[0]);
        AssertEx.Near(0.5, result.Values[1]);
        AssertEx.Near(1.0, result.Values[2]);
        AssertEx.Near(2.0 / 3.0, result.Layers[0].Coverage);
        AssertEx.Equal(LayerAvailability.Partial, result.Layers[0].Availability);
    }

    [Test]
    internal static void OverlayIngress_CanonicalizesLegal10SourceKeys()
    {
        var result = OverlayCalculator.Compute(
            new[] { "11110101" },
            new[]
            {
                new OverlayLayer("custom", 1.0, new Dictionary<string, double>
                {
                    ["1111010100"] = 42,
                }),
            });

        AssertEx.Equal(LayerAvailability.Complete, result.Layers.Single().Availability);
        AssertEx.Near(0.5, result.Values.Single());
    }

    [Test]
    internal static void ZeroCoverage_RejectsLayerAndReportsMissing()
    {
        var result = OverlayCalculator.Compute(
            new[] { "A", "B" },
            new[] { new OverlayLayer("missing", 1.0, new Dictionary<string, double>()) });

        AssertEx.True(result.Values.All(double.IsNaN));
        AssertEx.Equal(LayerAvailability.Unavailable, result.Layers.Single().Availability);
        AssertEx.True(result.MissingLayers.Contains("missing"));
    }

    [Test]
    internal static void RequestedLayerAbsentFromCandidates_IsReportedMissing()
    {
        var missing = OverlayCalculator.FindMissingLayers(
            new[] { "income", "population" },
            new[]
            {
                new LayerCoverage("income", 1, 1, 1, LayerAvailability.Complete),
            });

        AssertEx.True(missing.SequenceEqual(new[] { "population" }));
    }

    [Test]
    internal static void EqualValues_NormalizeToNeutral()
    {
        var result = OverlayCalculator.Compute(
            new[] { "A", "B" },
            new[]
            {
                new OverlayLayer("equal", 1.0, new Dictionary<string, double>
                {
                    ["A"] = 5,
                    ["B"] = 5,
                }),
            });

        AssertEx.Near(0.5, result.Values[0]);
        AssertEx.Near(0.5, result.Values[1]);
    }

    [Test]
    internal static void NonSeoulRequest_IsRejectedBeforeFetch()
    {
        AssertEx.True(SeoulCoveragePolicy.Supports(new[] { "11110101", "11740110" }));
        AssertEx.False(SeoulCoveragePolicy.Supports(new[] { "26110101" }));
        AssertEx.False(SeoulCoveragePolicy.Supports(Array.Empty<string>()));
    }

    [Test]
    internal static void NonSeoulFetchAsync_DoesNotInvokeRawFetcher()
    {
        var provider = new ApiKeyProvider(new Dictionary<string, string>
        {
            [ApiKeyProvider.KEY_SEOUL] = "test-seoul-key",
        });
        var source = new CountingSeoulSource(provider);

        var result = source.FetchAsync(new DataQuery
        {
            DistrictCodes = new[] { "26110101" },
        }).GetAwaiter().GetResult();

        AssertEx.False(result.IsSuccess);
        AssertEx.Equal(ErrorCodes.UnsupportedCoverage, result.Error!.Code);
        AssertEx.Equal(0, source.FetchCount);
    }

    [Test]
    internal static void RunGate_DefaultsIdleAndRequiresFalseToTrueEdge()
    {
        var gate = new RisingEdgeGate();
        AssertEx.False(gate.Observe(false));
        AssertEx.True(gate.Observe(true));
        AssertEx.False(gate.Observe(true));
        AssertEx.False(gate.Observe(false));
        AssertEx.True(gate.Observe(true));
    }

    [Test]
    internal static void GrasshopperComponent_AppendsRunPortAndUsesGate()
    {
        string source = File.ReadAllText(FindRepositoryFile(
            "src", "URSUS.GH", "URSUSSolverComponent.cs"));

        AssertEx.True(source.Contains("new Guid(\"794d034a-069d-4790-a220-1293dd3328cf\")",
            StringComparison.Ordinal));
        AssertEx.True(source.Contains("private const int IN_RUN          = 10;",
            StringComparison.Ordinal));
        AssertEx.True(source.Contains("pManager.AddBooleanParameter(\"Run\", \"R\"",
            StringComparison.Ordinal));

        AssertEx.True(source.Contains("GH_TaskCapableComponent<SolverTaskOutput>", StringComparison.Ordinal));
        AssertEx.True(source.Contains("new RunCoordinator", StringComparison.Ordinal));
        AssertEx.True(source.Contains("if (InPreSolve)", StringComparison.Ordinal));
        AssertEx.True(source.Contains("TaskList.Add(CompleteGenerationAsync", StringComparison.Ordinal));
        int gatePosition = source.IndexOf("if (!run)", StringComparison.Ordinal);
        int cachedOutputPosition = source.IndexOf("WriteCachedOutputs(DA, _lastOutput)", StringComparison.Ordinal);
        int keyProviderPosition = source.IndexOf("var keyProvider = new ApiKeyProvider", StringComparison.Ordinal);
        int solverPosition = source.IndexOf("_solverForStart = new URSUSSolver", StringComparison.Ordinal);
        AssertEx.True(gatePosition >= 0 && gatePosition < keyProviderPosition && gatePosition < solverPosition,
            "Run gate must execute before key/config I/O and solver construction.");
        AssertEx.True(cachedOutputPosition > gatePosition && cachedOutputPosition < keyProviderPosition,
            "Idle solves must restore the last completed output without rebuilding the solver.");
    }

    [Test]
    internal static void ErrorGuide_UsesCurrentRepository()
    {
        string url = ErrorGuideMap.GetGuideUrl(ErrorCodes.VWorldKeyMissing);
        AssertEx.True(url.Contains("TeacherChae/URSUS", StringComparison.Ordinal));
        AssertEx.False(url.Contains("DaeguURSUS", StringComparison.Ordinal));
    }

    [Test]
    internal static void SourceErrorCode_IsStableAndDeclared()
    {
        AssertEx.Equal(ErrorCodes.SeoulNoData,
            SeoulOpenDataSourceBase.GetNoDataErrorCode("avg_income"));
        AssertEx.Equal(ErrorCodes.SeoulNoData,
            SeoulOpenDataSourceBase.GetNoDataErrorCode("transit"));
    }

    [Test]
    internal static void DeclaredErrorCodes_AreUnique()
    {
        var duplicates = typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (field.Name, Value: (string)field.GetRawConstantValue()!))
            .GroupBy(item => item.Value)
            .Where(group => group.Count() > 1)
            .ToList();

        AssertEx.Equal(0, duplicates.Count,
            string.Join(", ", duplicates.Select(group =>
                $"{group.Key}=[{string.Join("/", group.Select(item => item.Name))}]")));
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ursus-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            action(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Repository file not found: {Path.Combine(parts)}");
    }

    private sealed class CountingSeoulSource : SeoulOpenDataSourceBase
    {
        internal CountingSeoulSource(ApiKeyProvider provider) : base(provider)
        {
        }

        internal int FetchCount { get; private set; }

        public override DataSourceMetadata Metadata { get; } = new()
        {
            Id = "test-seoul",
            DisplayName = "테스트 서울 데이터",
            Description = "preflight test",
            Category = DataCategory.Other,
            Provider = "test",
            CoverageArea = "서울특별시",
            RequiredApiKeys = new[] { ApiKeyProvider.KEY_SEOUL },
        };

        protected override string ValueUnit => "test";

        protected override Dictionary<string, double> FetchRawData(
            DataSeoulApiParser parser,
            string? cacheDir)
        {
            FetchCount++;
            return new Dictionary<string, double>();
        }
    }
}
