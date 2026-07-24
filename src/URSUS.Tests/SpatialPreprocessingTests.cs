using URSUS.DataSources;
using URSUS.Geometry;
using URSUS.Preprocessing;
using System.Text.Json.Nodes;

namespace URSUS.Tests;

internal static class SpatialPreprocessingTests
{
    [Test]
    private static void Seoul250mAcquisitionGate_RemainsClosedOnObservedGrainAndIdDrift()
    {
        JsonObject fixture = JsonNode.Parse(File.ReadAllText(FindRepositoryFile(
            "docs", "fixtures", "seoul-250m-acquisition-gate-1.json")))!.AsObject();

        AssertEx.Equal("HOLD", fixture["verdict"]!.GetValue<string>());
        AssertEx.Equal(
            0,
            fixture["statisticArtifact"]!["rawPrimaryKeyDuplicateCount"]!.GetValue<int>());
        AssertEx.True(
            fixture["statisticArtifact"]!["dateHourGridDuplicateExtraRowCount"]!.GetValue<int>() > 0);
        AssertEx.Equal(
            1,
            fixture["idAlignment"]!["statisticOnlyIdCount"]!.GetValue<int>());
        AssertEx.Equal(
            0,
            fixture["idAlignment"]!["statisticOnlyIdNumericRows"]!.GetValue<int>());
        AssertEx.Equal(
            10125,
            fixture["geometryArtifact"]!["validGeometryCount"]!.GetValue<int>());
        AssertEx.False(
            fixture["providerManual"]!["definesCrossAdministrativeGridAggregation"]!
                .GetValue<bool>());

        var blockingCodes = fixture["blockingReasons"]!.AsArray()
            .Select(reason => reason!["code"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);
        AssertEx.True(blockingCodes.Contains("RAW_GRAIN_NOT_GRID_UNIQUE"));
        AssertEx.True(blockingCodes.Contains("AGGREGATION_SEMANTICS_UNPROVEN"));
        AssertEx.True(blockingCodes.Contains("MASKED_PART_AGGREGATION_UNDEFINED"));
        AssertEx.True(blockingCodes.Contains("ARTIFACT_ID_SET_MISMATCH"));
    }

    [Test]
    private static void SpatialSchema_UsesAuthorityNamespaceVersionAndLevelAsIdentity()
    {
        var ngii2022 = Grid250("2022");
        var same = new SpatialUnitSchema(
            SpatialUnitKind.StandardGrid, " NGII ", " NATIONAL_GRID ", "2022", "250m", 250);
        var otherAuthority = new SpatialUnitSchema(
            SpatialUnitKind.StandardGrid, "SEOUL", "NATIONAL_GRID", "2022", "250m", 250);
        var otherVersion = Grid250("2023");

        AssertEx.Equal(ngii2022, same);
        AssertEx.True(ngii2022.IsExactlyCompatibleWith(same));
        AssertEx.False(ngii2022.IsExactlyCompatibleWith(otherAuthority));
        AssertEx.False(ngii2022.IsExactlyCompatibleWith(otherVersion));
        AssertEx.True(ngii2022.Identity.Contains("NGII", StringComparison.Ordinal));
    }

    [Test]
    private static void SpatialSchema_RejectsIncompleteOrInvalidGridContracts()
    {
        AssertEx.Throws<ArgumentException>(() => new SpatialUnitSchema(
            SpatialUnitKind.StandardGrid, "", "NATIONAL_GRID", "2022", "250m", 250));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new SpatialUnitSchema(
            SpatialUnitKind.StandardGrid, "NGII", "NATIONAL_GRID", "2022", "250m", 0));
        AssertEx.Throws<ArgumentException>(() => new SpatialUnitSchema(
            SpatialUnitKind.StandardGrid, "NGII", "NATIONAL_GRID", "2022", "250m"));
    }

    [Test]
    private static void SpatialUnitId_RequiresCanonicalValueAndCarriesSchema()
    {
        var schema = Grid250("2022");
        var a = new SpatialUnitId(schema, " cell-001 ");
        var same = new SpatialUnitId(Grid250("2022"), "cell-001");
        var otherVersion = new SpatialUnitId(Grid250("2023"), "cell-001");

        AssertEx.Equal("cell-001", a.Value);
        AssertEx.Equal(a, same);
        AssertEx.False(a.Equals(otherVersion));
        AssertEx.Throws<ArgumentException>(() => new SpatialUnitId(schema, " "));
    }

    [Test]
    private static void ProviderDatasetIdentity_IsNormalizedAndEvidenceIsRequired()
    {
        var identity = new ProviderDatasetIdentity(
            " Seoul ", " living-population-250m ", " schema-v1 ", "docs/fixtures/seoul-grid-v1.json");

        AssertEx.Equal("Seoul", identity.ProviderId);
        AssertEx.Equal("living-population-250m", identity.DatasetId);
        AssertEx.Throws<ArgumentException>(() => new ProviderDatasetIdentity(
            "Seoul", "living-population-250m", "schema-v1", ""));
    }

    [Test]
    private static void IdentityProjection_JoinsDifferentSourceColumnNamesIntoSameCanonicalIds()
    {
        var schema = Grid250("2022");
        var statistic = new RawStatisticalLayer(
            new ProviderDatasetIdentity("Seoul", "living-population", "v1", "stat-fixture"),
            schema,
            "CELL_KEY",
            new[] { new RawStatisticalRecord("cell-001", 125) });
        var geometry = new RawGeometryLayer(
            new ProviderDatasetIdentity("Seoul", "living-population-grid", "v1", "geometry-fixture"),
            schema,
            "GRID_ID",
            CoordinateReferenceSystem.Epsg5179,
            new[] { new RawGeometryRecord("cell-001", Square(0, 0, 250)) });
        var projection = ExactSpatialIdProjection.SharedNamespace(
            schema, new[] { "cell-001" }, "seoul-grid-contract-v1", "paired-provider-fixture");

        var projectedStatistic = projection.Project(statistic);
        var projectedGeometry = projection.Project(geometry);

        AssertEx.Equal("CELL_KEY", statistic.SourceIdField);
        AssertEx.Equal("GRID_ID", geometry.SourceIdField);
        AssertEx.True(projectedStatistic.Records.ContainsKey("cell-001"));
        AssertEx.True(projectedGeometry.Records.ContainsKey("cell-001"));
    }

    [Test]
    private static void OfficialCrosswalk_ProjectsDifferentNamespacesOnlyWhenOneToOne()
    {
        var providerSchema = new SpatialUnitSchema(
            SpatialUnitKind.StandardGrid, "SEOUL", "LIVING_GRID", "v1", "250m", 250);
        var canonicalSchema = Grid250("2022");
        var projection = ExactSpatialIdProjection.OfficialCrosswalk(
            providerSchema,
            canonicalSchema,
            new Dictionary<string, string>
            {
                ["provider-17"] = "ngii-001",
                ["provider-18"] = "ngii-002",
            },
            "seoul-to-ngii-v1",
            "docs/fixtures/seoul-ngii-crosswalk-v1.json");
        var layer = new RawStatisticalLayer(
            new ProviderDatasetIdentity("Seoul", "living-population", "v1", "stat-fixture"),
            providerSchema,
            "cell_id",
            new[]
            {
                new RawStatisticalRecord("provider-17", 10),
                new RawStatisticalRecord("provider-18", 20),
            });

        var projected = projection.Project(layer);

        AssertEx.Equal(2, projected.Records.Count);
        AssertEx.Equal("provider-17", projected.Records["ngii-001"].SourceUnitId);
        AssertEx.Equal(10d, projected.Records["ngii-001"].Value);
    }

    [Test]
    private static void ExactProjection_RejectsDuplicateUnmappedAndSchemaMismatch()
    {
        var schema = Grid250("2022");
        AssertEx.Throws<SpatialProjectionException>(() => new RawStatisticalLayer(
            new ProviderDatasetIdentity("Seoul", "stats", "v1", "fixture"),
            schema,
            "grid_id",
            new[]
            {
                new RawStatisticalRecord("a", 1),
                new RawStatisticalRecord("a", 2),
            }));
        AssertEx.Throws<SpatialProjectionException>(() =>
            ExactSpatialIdProjection.OfficialCrosswalk(
                schema,
                schema,
                new Dictionary<string, string> { ["a"] = "same", ["b"] = "same" },
                "bad-crosswalk",
                "fixture"));

        var layer = new RawStatisticalLayer(
            new ProviderDatasetIdentity("Seoul", "stats", "v1", "fixture"),
            schema,
            "grid_id",
            new[] { new RawStatisticalRecord("missing", 1) });
        var projection = ExactSpatialIdProjection.SharedNamespace(
            schema, new[] { "known" }, "identity-v1", "fixture");
        AssertEx.Throws<SpatialProjectionException>(() => projection.Project(layer));

        var otherSchemaLayer = new RawStatisticalLayer(
            new ProviderDatasetIdentity("Seoul", "stats", "v1", "fixture"),
            Grid250("2023"),
            "grid_id",
            new[] { new RawStatisticalRecord("known", 1) });
        AssertEx.Throws<SpatialProjectionException>(() => projection.Project(otherSchemaLayer));
    }

    [Test]
    private static void ExactJoin_CreatesOneFeaturePerCanonicalUnitWithBothProvenances()
    {
        var schema = Grid250("2022");
        var statistic = ProjectStatistics(schema, ("b", 20), ("a", 10));
        var geometry = ProjectGeometry(schema, ("a", Square(0, 0, 250)), ("b", Square(250, 0, 250)));

        SpatialJoinResult result = ExactSpatialJoiner.Join(statistic, geometry);
        ExactSpatialLayerBinding binding = result.CreateBinding("living_population");

        AssertEx.True(result.IsExact);
        AssertEx.Equal(2, binding.Features.Count);
        AssertEx.Equal("a", binding.Features[0].UnitId.Value);
        AssertEx.Equal(10d, binding.Features[0].Value);
        AssertEx.Equal("Seoul", binding.StatisticSource.ProviderId);
        AssertEx.Equal("NGII", binding.GeometrySource.ProviderId);
        AssertEx.Equal(CoordinateReferenceSystem.Epsg5179, binding.Crs);
    }

    [Test]
    private static void ExactJoin_FailsClosedAndReportsBothSidesOfIdSetMismatch()
    {
        var schema = Grid250("2022");
        var statistic = ProjectStatistics(schema, ("a", 10), ("stat-only", 20));
        var geometry = ProjectGeometry(
            schema,
            ("a", Square(0, 0, 250)),
            ("geometry-only", Square(250, 0, 250)));

        SpatialJoinResult result = ExactSpatialJoiner.Join(statistic, geometry);

        AssertEx.False(result.IsExact);
        AssertEx.Equal(SpatialJoinStatus.IdSetMismatch, result.Status);
        AssertEx.Equal("stat-only", result.MissingGeometryIds.Single());
        AssertEx.Equal("geometry-only", result.MissingStatisticIds.Single());
        AssertEx.Throws<SpatialJoinException>(() => result.CreateBinding("living_population"));
    }

    [Test]
    private static void ExactJoin_RejectsSameResolutionWithDifferentSchemaVersion()
    {
        var statistic = ProjectStatistics(Grid250("2022"), ("a", 10));
        var geometry = ProjectGeometry(Grid250("2023"), ("a", Square(0, 0, 250)));

        SpatialJoinResult result = ExactSpatialJoiner.Join(statistic, geometry);

        AssertEx.Equal(SpatialJoinStatus.SchemaMismatch, result.Status);
        AssertEx.False(result.IsExact);
        AssertEx.Equal(0, result.JoinedFeatures.Count);
        AssertEx.Throws<SpatialJoinException>(() => result.CreateBinding("living_population"));
    }

    [Test]
    private static void ExactJoin_DoesNotTreatTwoEmptyLayersAsSuccessfulEvidence()
    {
        var schema = Grid250("2022");

        SpatialJoinResult result = ExactSpatialJoiner.Join(
            ProjectStatistics(schema),
            ProjectGeometry(schema));

        AssertEx.Equal(SpatialJoinStatus.EmptyInput, result.Status);
        AssertEx.False(result.IsExact);
        AssertEx.Throws<SpatialJoinException>(() => result.CreateBinding("empty"));
    }

    [Test]
    private static void CapabilityRegistry_ContinuesAcrossProvidersAndExcludesIncompatibleUnits()
    {
        var registry = new SpatialCapabilityRegistry();
        var grid250 = Grid250("2022");
        var grid500 = new SpatialUnitSchema(
            SpatialUnitKind.StandardGrid, "NGII", "NATIONAL_GRID", "2022", "500m", 500);
        registry.Register(Asset(
            "VWorld", "legal-boundary", SpatialAssetRole.Geometry,
            new SpatialUnitSchema(
                SpatialUnitKind.LegalDistrict, "MOLIT", "LEGAL_DONG", "2026-07", "emd"),
            SpatialSemantics.UnitBoundary, 0));
        registry.Register(Asset(
            "VWorld", "wrong-grid", SpatialAssetRole.Geometry,
            grid500, SpatialSemantics.UnitBoundary, 0));
        registry.Register(Asset(
            "NGII", "national-grid-250", SpatialAssetRole.Geometry,
            grid250, SpatialSemantics.UnitBoundary, 10));
        registry.Register(Asset(
            "SGIS", "national-grid-250", SpatialAssetRole.Geometry,
            grid250, SpatialSemantics.UnitBoundary, 20));
        registry.Register(Asset(
            "Seoul", "living-population", SpatialAssetRole.Statistic,
            grid250, "living_population", 0));

        IReadOnlyList<SpatialAssetDescriptor> candidates = registry.FindExactCandidates(
            SpatialAssetRole.Geometry,
            grid250,
            SpatialSemantics.UnitBoundary,
            "Seoul");

        AssertEx.Equal(2, candidates.Count);
        AssertEx.Equal("NGII", candidates[0].Dataset.ProviderId);
        AssertEx.Equal("SGIS", candidates[1].Dataset.ProviderId);
        AssertEx.False(candidates.Any(candidate =>
            candidate.Dataset.ProviderId.Equals("VWorld", StringComparison.Ordinal)));
    }

    [Test]
    private static void CapabilityRegistry_RequiresSameSemanticsCoverageAndSchema()
    {
        var registry = new SpatialCapabilityRegistry();
        var schema = Grid250("2022");
        registry.Register(Asset(
            "Seoul", "living-population", SpatialAssetRole.Statistic,
            schema, "living_population", 0, "Seoul"));
        registry.Register(Asset(
            "Seoul", "consumer-spend", SpatialAssetRole.Statistic,
            schema, "consumer_spend", 1, "Seoul"));
        registry.Register(Asset(
            "National", "living-population", SpatialAssetRole.Statistic,
            schema, "living_population", 2, "Nationwide"));

        var candidates = registry.FindExactCandidates(
            SpatialAssetRole.Statistic, schema, "living_population", "Seoul");

        AssertEx.Equal(1, candidates.Count);
        AssertEx.Equal("Seoul", candidates[0].Dataset.ProviderId);
        AssertEx.Throws<ArgumentException>(() => registry.Register(candidates[0]));
    }

    [Test]
    private static void SnapshotAndChoropleth_UseTheRequestedLayersExactSpatialBinding()
    {
        var schema = Grid250("2022");
        var binding = ExactSpatialJoiner.Join(
                ProjectStatistics(schema, ("b", 20), ("a", 10)),
                ProjectGeometry(
                    schema,
                    ("a", Square(0, 0, 250)),
                    ("b", Square(250, 0, 250))))
            .CreateBinding("living_population", "명");
        var snapshot = new URSUS.Analysis.AnalysisSnapshot(
            Array.Empty<string>(),
            Array.Empty<URSUS.Analysis.SnapshotLayer>(),
            spatialLayers: new[] { binding });

        var plan = URSUS.Visualization.ChoroplethPlanner.Create(
            snapshot,
            "living_population",
            URSUS.Visualization.VisualizationMode.Choropleth,
            0);

        AssertEx.Equal(2, plan.Districts.Count);
        AssertEx.Equal("a", plan.Districts[0].Code);
        AssertEx.Equal(10d, plan.Districts[0].RawValue);
        AssertEx.Equal(62500d, plan.Districts[0].Topology.Area);
        AssertEx.Equal("명", plan.Unit);
        AssertEx.Equal(0, snapshot.DistrictIndex.Count);
        AssertEx.Equal(1, snapshot.SpatialLayers.Count);
    }

    [Test]
    private static void Snapshot_RejectsDuplicateBindingsAndImplicitCrossUnitOverlay()
    {
        var schema = Grid250("2022");
        var binding = ExactSpatialJoiner.Join(
                ProjectStatistics(schema, ("a", 10)),
                ProjectGeometry(schema, ("a", Square(0, 0, 250))))
            .CreateBinding("living_population");
        AssertEx.Throws<ArgumentException>(() => new URSUS.Analysis.AnalysisSnapshot(
            Array.Empty<string>(),
            Array.Empty<URSUS.Analysis.SnapshotLayer>(),
            spatialLayers: new[] { binding, binding }));

        var snapshot = new URSUS.Analysis.AnalysisSnapshot(
            Array.Empty<string>(),
            Array.Empty<URSUS.Analysis.SnapshotLayer>(),
            spatialLayers: new[] { binding });
        AssertEx.Throws<InvalidOperationException>(() =>
            URSUS.Visualization.ChoroplethPlanner.Create(
                snapshot,
                null,
                URSUS.Visualization.VisualizationMode.Choropleth,
                0,
                Array.Empty<double>()));
    }

    private static SpatialUnitSchema Grid250(string version) => new(
        SpatialUnitKind.StandardGrid,
        "NGII",
        "NATIONAL_GRID",
        version,
        "250m",
        250);

    private static BoundaryTopology Square(double x, double y, double size)
    {
        var ring = new BoundaryRing(new[]
        {
            new Coordinate2D(x, y),
            new Coordinate2D(x + size, y),
            new Coordinate2D(x + size, y + size),
            new Coordinate2D(x, y + size),
            new Coordinate2D(x, y),
        });
        return BoundaryTopology.Create(new[]
        {
            new BoundaryPart(ring, Array.Empty<BoundaryRing>()),
        });
    }

    private static CanonicalStatisticalLayer ProjectStatistics(
        SpatialUnitSchema schema,
        params (string id, double value)[] records)
    {
        var raw = new RawStatisticalLayer(
            new ProviderDatasetIdentity("Seoul", "living-population", "v1", "stat-fixture"),
            schema,
            "grid_id",
            records.Select(record => new RawStatisticalRecord(record.id, record.value)));
        var projection = ExactSpatialIdProjection.SharedNamespace(
            schema, records.Select(record => record.id), "stat-identity", "stat-fixture");
        return projection.Project(raw);
    }

    private static CanonicalGeometryLayer ProjectGeometry(
        SpatialUnitSchema schema,
        params (string id, BoundaryTopology geometry)[] records)
    {
        var raw = new RawGeometryLayer(
            new ProviderDatasetIdentity("NGII", "national-grid", "v1", "geometry-fixture"),
            schema,
            "grid_id",
            CoordinateReferenceSystem.Epsg5179,
            records.Select(record => new RawGeometryRecord(record.id, record.geometry)));
        var projection = ExactSpatialIdProjection.SharedNamespace(
            schema, records.Select(record => record.id), "geometry-identity", "geometry-fixture");
        return projection.Project(raw);
    }

    private static SpatialAssetDescriptor Asset(
        string provider,
        string dataset,
        SpatialAssetRole role,
        SpatialUnitSchema schema,
        string semantics,
        int preferenceRank,
        string coverage = "Seoul") =>
        new(
            new ProviderDatasetIdentity(provider, dataset, "v1", $"{provider}-{dataset}-fixture"),
            role,
            schema,
            semantics,
            coverage,
            preferenceRank);

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
}
