using URSUS.DataSources;
using URSUS.Geometry;

namespace URSUS.Preprocessing;

public enum SpatialJoinStatus
{
    Exact,
    EmptyInput,
    SchemaMismatch,
    IdSetMismatch,
}

public sealed class SpatialJoinException : InvalidOperationException
{
    public SpatialJoinStatus Status { get; }
    public IReadOnlyList<string> MissingGeometryIds { get; }
    public IReadOnlyList<string> MissingStatisticIds { get; }

    internal SpatialJoinException(SpatialJoinResult result)
        : base(result.Status switch
        {
            SpatialJoinStatus.EmptyInput =>
                "빈 통계와 Geometry layer는 exact evidence가 아닙니다.",
            SpatialJoinStatus.SchemaMismatch =>
                "통계와 Geometry의 spatial schema가 정확히 일치하지 않습니다.",
            _ => "통계와 Geometry의 canonical ID 집합이 정확히 일치하지 않습니다.",
        })
    {
        Status = result.Status;
        MissingGeometryIds = result.MissingGeometryIds;
        MissingStatisticIds = result.MissingStatisticIds;
    }
}

public sealed record ExactSpatialFeature(
    SpatialUnitId UnitId,
    double Value,
    BoundaryTopology Geometry);

public sealed class ExactSpatialLayerBinding
{
    public string LayerId { get; }
    public string? Unit { get; }
    public SpatialUnitSchema Schema { get; }
    public ProviderDatasetIdentity StatisticSource { get; }
    public ProviderDatasetIdentity GeometrySource { get; }
    public ExactSpatialIdProjection StatisticProjection { get; }
    public ExactSpatialIdProjection GeometryProjection { get; }
    public CoordinateReferenceSystem Crs { get; }
    public IReadOnlyList<ExactSpatialFeature> Features { get; }

    internal ExactSpatialLayerBinding(
        string layerId,
        string? unit,
        CanonicalStatisticalLayer statistics,
        CanonicalGeometryLayer geometry,
        IReadOnlyList<ExactSpatialFeature> features)
    {
        if (string.IsNullOrWhiteSpace(layerId))
            throw new ArgumentException("layer ID는 비어 있을 수 없습니다.", nameof(layerId));
        LayerId = layerId.Trim();
        Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
        Schema = statistics.Schema;
        StatisticSource = statistics.Source;
        GeometrySource = geometry.Source;
        StatisticProjection = statistics.Projection;
        GeometryProjection = geometry.Projection;
        Crs = geometry.Crs;
        Features = Array.AsReadOnly(features.ToArray());
    }
}

public sealed class SpatialJoinResult
{
    private readonly CanonicalStatisticalLayer _statistics;
    private readonly CanonicalGeometryLayer _geometry;

    public SpatialJoinStatus Status { get; }
    public bool IsExact => Status == SpatialJoinStatus.Exact;
    public IReadOnlyList<string> MissingGeometryIds { get; }
    public IReadOnlyList<string> MissingStatisticIds { get; }
    public IReadOnlyList<ExactSpatialFeature> JoinedFeatures { get; }

    internal SpatialJoinResult(
        CanonicalStatisticalLayer statistics,
        CanonicalGeometryLayer geometry,
        SpatialJoinStatus status,
        IEnumerable<string> missingGeometryIds,
        IEnumerable<string> missingStatisticIds,
        IEnumerable<ExactSpatialFeature> joinedFeatures)
    {
        _statistics = statistics;
        _geometry = geometry;
        Status = status;
        MissingGeometryIds = Sorted(missingGeometryIds);
        MissingStatisticIds = Sorted(missingStatisticIds);
        JoinedFeatures = Array.AsReadOnly(joinedFeatures
            .OrderBy(feature => feature.UnitId.Value, StringComparer.Ordinal)
            .ToArray());
    }

    public ExactSpatialLayerBinding CreateBinding(string layerId, string? unit = null)
    {
        if (!IsExact) throw new SpatialJoinException(this);
        return new ExactSpatialLayerBinding(layerId, unit, _statistics, _geometry, JoinedFeatures);
    }

    private static IReadOnlyList<string> Sorted(IEnumerable<string> values) =>
        Array.AsReadOnly(values.Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray());
}

public static class ExactSpatialJoiner
{
    public static SpatialJoinResult Join(
        CanonicalStatisticalLayer statistics,
        CanonicalGeometryLayer geometry)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(geometry);

        if (!statistics.Schema.IsExactlyCompatibleWith(geometry.Schema))
            return new SpatialJoinResult(
                statistics,
                geometry,
                SpatialJoinStatus.SchemaMismatch,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<ExactSpatialFeature>());
        if (statistics.Records.Count == 0 && geometry.Records.Count == 0)
            return new SpatialJoinResult(
                statistics,
                geometry,
                SpatialJoinStatus.EmptyInput,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<ExactSpatialFeature>());

        var statisticIds = statistics.Records.Keys.ToHashSet(StringComparer.Ordinal);
        var geometryIds = geometry.Records.Keys.ToHashSet(StringComparer.Ordinal);
        string[] missingGeometry = statisticIds.Except(geometryIds, StringComparer.Ordinal).ToArray();
        string[] missingStatistic = geometryIds.Except(statisticIds, StringComparer.Ordinal).ToArray();
        var features = statisticIds.Intersect(geometryIds, StringComparer.Ordinal)
            .Select(id =>
            {
                CanonicalStatisticalRecord statistic = statistics.Records[id];
                CanonicalGeometryRecord shape = geometry.Records[id];
                return new ExactSpatialFeature(statistic.UnitId, statistic.Value, shape.Geometry);
            })
            .ToArray();
        SpatialJoinStatus status = missingGeometry.Length == 0 && missingStatistic.Length == 0
            ? SpatialJoinStatus.Exact
            : SpatialJoinStatus.IdSetMismatch;
        return new SpatialJoinResult(
            statistics,
            geometry,
            status,
            missingGeometry,
            missingStatistic,
            features);
    }
}
