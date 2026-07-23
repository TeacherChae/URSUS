using System.Collections.ObjectModel;
using URSUS.DataSources;
using URSUS.Geometry;

namespace URSUS.Preprocessing;

public enum SpatialProjectionFailureReason
{
    DuplicateSourceId,
    DuplicateCanonicalId,
    UnmappedSourceId,
    SourceSchemaMismatch,
}

public sealed class SpatialProjectionException : InvalidOperationException
{
    public SpatialProjectionFailureReason Reason { get; }
    public IReadOnlyList<string> Ids { get; }

    public SpatialProjectionException(
        SpatialProjectionFailureReason reason,
        string message,
        IEnumerable<string>? ids = null)
        : base(message)
    {
        Reason = reason;
        Ids = Array.AsReadOnly((ids ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray());
    }
}

public sealed record RawStatisticalRecord
{
    public string SourceUnitId { get; }
    public double Value { get; }

    public RawStatisticalRecord(string sourceUnitId, double value)
    {
        SourceUnitId = Required(sourceUnitId, nameof(sourceUnitId));
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    private static string Required(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("source unit ID는 비어 있을 수 없습니다.", name);
        return value.Trim();
    }
}

public sealed record RawGeometryRecord
{
    public string SourceUnitId { get; }
    public BoundaryTopology Geometry { get; }

    public RawGeometryRecord(string sourceUnitId, BoundaryTopology geometry)
    {
        if (string.IsNullOrWhiteSpace(sourceUnitId))
            throw new ArgumentException("source unit ID는 비어 있을 수 없습니다.", nameof(sourceUnitId));
        SourceUnitId = sourceUnitId.Trim();
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
    }
}

public sealed class RawStatisticalLayer
{
    public ProviderDatasetIdentity Source { get; }
    public SpatialUnitSchema SourceSchema { get; }
    public string SourceIdField { get; }
    public IReadOnlyList<RawStatisticalRecord> Records { get; }

    public RawStatisticalLayer(
        ProviderDatasetIdentity source,
        SpatialUnitSchema sourceSchema,
        string sourceIdField,
        IEnumerable<RawStatisticalRecord> records)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        SourceSchema = sourceSchema ?? throw new ArgumentNullException(nameof(sourceSchema));
        SourceIdField = Required(sourceIdField, nameof(sourceIdField));
        Records = Unique(records, record => record.SourceUnitId);
    }

    private static IReadOnlyList<T> Unique<T>(
        IEnumerable<T> records,
        Func<T, string> idSelector)
    {
        ArgumentNullException.ThrowIfNull(records);
        var copy = records.ToArray();
        string[] duplicates = copy.GroupBy(idSelector, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new SpatialProjectionException(
                SpatialProjectionFailureReason.DuplicateSourceId,
                "통계 source unit ID가 중복되었습니다.",
                duplicates);
        return Array.AsReadOnly(copy);
    }

    private static string Required(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("값은 비어 있을 수 없습니다.", name);
        return value.Trim();
    }
}

public sealed class RawGeometryLayer
{
    public ProviderDatasetIdentity Source { get; }
    public SpatialUnitSchema SourceSchema { get; }
    public string SourceIdField { get; }
    public CoordinateReferenceSystem Crs { get; }
    public IReadOnlyList<RawGeometryRecord> Records { get; }

    public RawGeometryLayer(
        ProviderDatasetIdentity source,
        SpatialUnitSchema sourceSchema,
        string sourceIdField,
        CoordinateReferenceSystem crs,
        IEnumerable<RawGeometryRecord> records)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        SourceSchema = sourceSchema ?? throw new ArgumentNullException(nameof(sourceSchema));
        if (string.IsNullOrWhiteSpace(sourceIdField))
            throw new ArgumentException("값은 비어 있을 수 없습니다.", nameof(sourceIdField));
        SourceIdField = sourceIdField.Trim();
        Crs = crs;
        ArgumentNullException.ThrowIfNull(records);
        var copy = records.ToArray();
        string[] duplicates = copy.GroupBy(record => record.SourceUnitId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new SpatialProjectionException(
                SpatialProjectionFailureReason.DuplicateSourceId,
                "Geometry source unit ID가 중복되었습니다.",
                duplicates);
        Records = Array.AsReadOnly(copy);
    }
}

public sealed record CanonicalStatisticalRecord(
    string SourceUnitId,
    SpatialUnitId UnitId,
    double Value);

public sealed record CanonicalGeometryRecord(
    string SourceUnitId,
    SpatialUnitId UnitId,
    BoundaryTopology Geometry);

public sealed class CanonicalStatisticalLayer
{
    public ProviderDatasetIdentity Source { get; }
    public SpatialUnitSchema Schema { get; }
    public ExactSpatialIdProjection Projection { get; }
    public IReadOnlyDictionary<string, CanonicalStatisticalRecord> Records { get; }

    internal CanonicalStatisticalLayer(
        ProviderDatasetIdentity source,
        SpatialUnitSchema schema,
        ExactSpatialIdProjection projection,
        IEnumerable<CanonicalStatisticalRecord> records)
    {
        Source = source;
        Schema = schema;
        Projection = projection;
        Records = new ReadOnlyDictionary<string, CanonicalStatisticalRecord>(
            records.ToDictionary(record => record.UnitId.Value, StringComparer.Ordinal));
    }
}

public sealed class CanonicalGeometryLayer
{
    public ProviderDatasetIdentity Source { get; }
    public SpatialUnitSchema Schema { get; }
    public ExactSpatialIdProjection Projection { get; }
    public CoordinateReferenceSystem Crs { get; }
    public IReadOnlyDictionary<string, CanonicalGeometryRecord> Records { get; }

    internal CanonicalGeometryLayer(
        ProviderDatasetIdentity source,
        SpatialUnitSchema schema,
        ExactSpatialIdProjection projection,
        CoordinateReferenceSystem crs,
        IEnumerable<CanonicalGeometryRecord> records)
    {
        Source = source;
        Schema = schema;
        Projection = projection;
        Crs = crs;
        Records = new ReadOnlyDictionary<string, CanonicalGeometryRecord>(
            records.ToDictionary(record => record.UnitId.Value, StringComparer.Ordinal));
    }
}
