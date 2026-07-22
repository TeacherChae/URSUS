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

    [Test]
    internal static void DataGoKrCredential_IsDeprecatedButLegacyContractRemainsReadable()
    {
        var keyField = typeof(ApiKeyProvider).GetField(
            "KEY_DATA_GO_KR", BindingFlags.Public | BindingFlags.Static);
        var keyProperty = typeof(ApiKeyProvider).GetProperty(
            "DataGoKrKey", BindingFlags.Public | BindingFlags.Instance);

        AssertEx.True(keyField != null);
        AssertEx.True(keyProperty != null);
        AssertEx.True(keyField!.GetCustomAttribute<ObsoleteAttribute>() != null);
        AssertEx.True(keyProperty!.GetCustomAttribute<ObsoleteAttribute>() != null);

        var provider = new ApiKeyProvider(new Dictionary<string, string>
        {
            ["DataGoKrKey"] = "legacy-test-key",
        });
        AssertEx.Equal("legacy-test-key", (string?)keyProperty!.GetValue(provider));
        AssertEx.False(URSUSSolver.DefaultDataSets.Contains(URSUSSolver.DS_LAND_PRICE));
        AssertEx.False(URSUSSolver.DefaultDataSets.Contains(URSUSSolver.DS_ZONING));
    }

    [Test]
    internal static void CurrentKeyUx_DoesNotRequestDataGoKrCredential()
    {
        string ghSource = File.ReadAllText(FindRepositoryFile(
            "src", "URSUS.GH", "ApiKeySettingsComponent.cs"));
        string setupSource = File.ReadAllText(FindRepositoryFile(
            "src", "URSUS.Setup", "SetupForm.cs"));

        AssertEx.True(ghSource.Contains(
            "[Deprecated] Legacy DataGoKr credential status",
            StringComparison.Ordinal));
        AssertEx.False(ghSource.Contains(
            "AddKeyField(\"공공데이터포털 API 키",
            StringComparison.Ordinal));
        AssertEx.False(setupSource.Contains(
            "BuildKeyRow(\n                \"공공데이터포털 API 키",
            StringComparison.Ordinal));
        AssertEx.True(setupSource.Contains(
            "DataGoKrKey is deprecated; preserve legacy config without presenting it in setup.",
            StringComparison.Ordinal));
    }

    [Test]
    internal static void Stage1LiveAcquisitionFixture_ProvesCompletePaginationAndJungGuCohort()
    {
        string serialized = File.ReadAllText(FindRepositoryFile(
            "docs", "fixtures", "vworld-seoul-boundary-live-v1.json"));
        using var document = JsonDocument.Parse(serialized);
        JsonElement root = document.RootElement;
        JsonElement request = root.GetProperty("requestContract");
        JsonElement pagination = root.GetProperty("pagination");
        JsonElement cohort = root.GetProperty("cohortFilter");

        AssertEx.Equal("vworld-seoul-boundary-live/1",
            root.GetProperty("fixtureVersion").GetString());
        AssertEx.False(request.GetProperty("fullRequestUrlStored").GetBoolean());
        AssertEx.False(request.GetProperty("geometryStored").GetBoolean());
        AssertEx.False(request.GetProperty("localPathStored").GetBoolean());
        AssertEx.Equal("<redacted>",
            request.GetProperty("parameters").GetProperty("KEY").GetString());
        AssertEx.False(serialized.Contains("api.vworld.kr/req/wfs?", StringComparison.OrdinalIgnoreCase));
        AssertEx.False(serialized.Contains("\"geometry\":", StringComparison.OrdinalIgnoreCase));
        AssertEx.False(serialized.Contains("\"coordinates\":", StringComparison.OrdinalIgnoreCase));
        AssertEx.False(serialized.Contains("\"features\":", StringComparison.OrdinalIgnoreCase));
        AssertEx.False(serialized.Contains("서울특별시 중구 세종대로 110", StringComparison.Ordinal));
        AssertEx.False(serialized.Contains("서울특별시 중구 태평로1가 31", StringComparison.Ordinal));
        AssertEx.False(serialized.Contains("/home/", StringComparison.OrdinalIgnoreCase));
        AssertEx.False(serialized.Contains("\\Users\\", StringComparison.OrdinalIgnoreCase));

        AssertEx.True(pagination.GetProperty("complete").GetBoolean());
        int expected = pagination.GetProperty("expectedTotal").GetInt32();
        int received = pagination.GetProperty("receivedTotal").GetInt32();
        AssertEx.Equal(1, pagination.GetProperty("pageCount").GetInt32());
        AssertEx.Equal(1, pagination.GetProperty("pages").GetArrayLength());
        AssertEx.Equal(758, expected);
        AssertEx.Equal(758, received);
        AssertEx.Equal(expected, received);
        AssertEx.Equal(received, pagination.GetProperty("pages").EnumerateArray()
            .Sum(page => page.GetProperty("numberReturned").GetInt32()));
        AssertEx.Equal(0, pagination.GetProperty("duplicateFeatureIdentityCount").GetInt32());
        AssertEx.Equal(0, pagination.GetProperty("duplicateCanonicalIdCount").GetInt32());

        JsonElement page = pagination.GetProperty("pages")[0];
        AssertEx.Equal(0, page.GetProperty("startIndex").GetInt32());
        AssertEx.Equal(758, page.GetProperty("numberMatched").GetInt32());
        AssertEx.Equal(758, page.GetProperty("numberReturned").GetInt32());
        AssertEx.Equal(758, page.GetProperty("featureCount").GetInt32());
        string responseSha = page.GetProperty("responseSha256").GetString()!;
        AssertEx.Equal(64, responseSha.Length);
        AssertEx.True(responseSha.All(Uri.IsHexDigit));

        string[] providerIds = root.GetProperty("providerCanonicalSummary")
            .GetProperty("canonicalIds").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
        AssertEx.Equal(expected, providerIds.Length);
        AssertEx.Equal(expected, providerIds.Distinct(StringComparer.Ordinal).Count());
        AssertEx.True(providerIds.All(id => id.Length == 8 && id.All(char.IsDigit)));
        AssertEx.True(providerIds.SequenceEqual(
            providerIds.OrderBy(id => id, StringComparer.Ordinal)));
        string[] pageIds = page.GetProperty("canonicalIds").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
        AssertEx.True(pageIds.SequenceEqual(providerIds));

        string[] seoulCatalog = MappingLoader.Load().Values.SelectMany(ids => ids)
            .Select(DistrictCode.CanonicalizeLegal)
            .Where(id => id.Length == 8 && id.StartsWith("11", StringComparison.Ordinal) &&
                !id.EndsWith("000", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        string[] jungGuCatalog = seoulCatalog
            .Where(id => id.StartsWith("11140", StringComparison.Ordinal))
            .ToArray();
        AssertEx.Equal(467, seoulCatalog.Length);
        AssertEx.Equal(74, jungGuCatalog.Length);

        AssertEx.True(cohort.GetProperty("complete").GetBoolean());
        AssertEx.Equal(74, cohort.GetProperty("requiredCount").GetInt32());
        AssertEx.Equal(74, cohort.GetProperty("matchedCount").GetInt32());
        AssertEx.Equal(0, cohort.GetProperty("missingDistrictIds").GetArrayLength());
        string[] requiredIds = cohort.GetProperty("requiredDistrictIds").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
        string[] matchedIds = cohort.GetProperty("matchedDistrictIds").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
        AssertEx.True(requiredIds.SequenceEqual(jungGuCatalog));
        AssertEx.True(matchedIds.SequenceEqual(jungGuCatalog));
        AssertEx.True(matchedIds.Contains("11140103", StringComparer.Ordinal));
        AssertEx.Equal(684, cohort.GetProperty("providerExtraCount").GetInt32());
        AssertEx.Equal(684, providerIds.Except(requiredIds, StringComparer.Ordinal).Count());
    }

    [Test]
    internal static void InnoPascalCharacterLiteral_DoesNotStartAContinuationLine()
    {
        string[] lines = File.ReadAllLines(FindRepositoryFile("installer", "URSUS.iss"));
        string[] invalid = lines.Where(line =>
        {
            string trimmed = line.TrimStart();
            return trimmed.Length > 1 && trimmed[0] == '#' && char.IsDigit(trimmed[1]);
        }).ToArray();

        AssertEx.Equal(0, invalid.Length,
            "Inno Setup treats a line-leading #13/#10 Pascal literal as a preprocessor directive.");
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
