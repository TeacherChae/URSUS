using System.Collections.ObjectModel;

namespace URSUS.Preprocessing;

public enum SpatialIdProjectionKind
{
    SharedNamespace,
    OfficialCrosswalk,
}

/// <summary>
/// A versioned, evidence-backed one-to-one map from provider IDs to canonical IDs.
/// It deliberately cannot express aggregation or disaggregation.
/// </summary>
public sealed class ExactSpatialIdProjection
{
    public SpatialIdProjectionKind Kind { get; }
    public SpatialUnitSchema SourceSchema { get; }
    public SpatialUnitSchema TargetSchema { get; }
    public string ProjectionId { get; }
    public string EvidenceReference { get; }
    public IReadOnlyDictionary<string, string> IdMap { get; }

    private ExactSpatialIdProjection(
        SpatialIdProjectionKind kind,
        SpatialUnitSchema sourceSchema,
        SpatialUnitSchema targetSchema,
        IReadOnlyDictionary<string, string> idMap,
        string projectionId,
        string evidenceReference)
    {
        Kind = kind;
        SourceSchema = sourceSchema ?? throw new ArgumentNullException(nameof(sourceSchema));
        TargetSchema = targetSchema ?? throw new ArgumentNullException(nameof(targetSchema));
        ProjectionId = Required(projectionId, nameof(projectionId));
        EvidenceReference = Required(evidenceReference, nameof(evidenceReference));
        ArgumentNullException.ThrowIfNull(idMap);

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string source, string target) in idMap)
        {
            string normalizedSource = Required(source, nameof(idMap));
            string normalizedTarget = Required(target, nameof(idMap));
            if (!normalized.TryAdd(normalizedSource, normalizedTarget))
                throw new SpatialProjectionException(
                    SpatialProjectionFailureReason.DuplicateSourceId,
                    "projection source ID가 중복되었습니다.",
                    new[] { normalizedSource });
        }

        string[] duplicateTargets = normalized
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateTargets.Length > 0)
            throw new SpatialProjectionException(
                SpatialProjectionFailureReason.DuplicateCanonicalId,
                "exact projection은 여러 source ID를 하나의 canonical ID로 투영할 수 없습니다.",
                duplicateTargets);

        if (kind == SpatialIdProjectionKind.SharedNamespace)
        {
            if (!sourceSchema.IsExactlyCompatibleWith(targetSchema))
                throw new SpatialProjectionException(
                    SpatialProjectionFailureReason.SourceSchemaMismatch,
                    "shared namespace projection은 동일 spatial schema를 요구합니다.");
            string[] changed = normalized.Where(pair =>
                    !pair.Key.Equals(pair.Value, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .ToArray();
            if (changed.Length > 0)
                throw new SpatialProjectionException(
                    SpatialProjectionFailureReason.SourceSchemaMismatch,
                    "shared namespace projection은 ID 값을 변경할 수 없습니다.",
                    changed);
        }

        IdMap = new ReadOnlyDictionary<string, string>(normalized);
    }

    public static ExactSpatialIdProjection SharedNamespace(
        SpatialUnitSchema schema,
        IEnumerable<string> ids,
        string projectionId,
        string evidenceReference)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var map = ids.ToDictionary(
            id => Required(id, nameof(ids)),
            id => Required(id, nameof(ids)),
            StringComparer.Ordinal);
        return new ExactSpatialIdProjection(
            SpatialIdProjectionKind.SharedNamespace,
            schema,
            schema,
            map,
            projectionId,
            evidenceReference);
    }

    public static ExactSpatialIdProjection OfficialCrosswalk(
        SpatialUnitSchema sourceSchema,
        SpatialUnitSchema targetSchema,
        IReadOnlyDictionary<string, string> idMap,
        string projectionId,
        string evidenceReference) =>
        new(
            SpatialIdProjectionKind.OfficialCrosswalk,
            sourceSchema,
            targetSchema,
            idMap,
            projectionId,
            evidenceReference);

    public CanonicalStatisticalLayer Project(RawStatisticalLayer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureSourceSchema(source.SourceSchema);
        var projected = source.Records.Select(record =>
        {
            string target = TargetFor(record.SourceUnitId);
            return new CanonicalStatisticalRecord(
                record.SourceUnitId,
                new SpatialUnitId(TargetSchema, target),
                record.Value);
        }).ToArray();
        return new CanonicalStatisticalLayer(source.Source, TargetSchema, this, projected);
    }

    public CanonicalGeometryLayer Project(RawGeometryLayer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureSourceSchema(source.SourceSchema);
        var projected = source.Records.Select(record =>
        {
            string target = TargetFor(record.SourceUnitId);
            return new CanonicalGeometryRecord(
                record.SourceUnitId,
                new SpatialUnitId(TargetSchema, target),
                record.Geometry);
        }).ToArray();
        return new CanonicalGeometryLayer(
            source.Source, TargetSchema, this, source.Crs, projected);
    }

    private void EnsureSourceSchema(SpatialUnitSchema schema)
    {
        if (!SourceSchema.IsExactlyCompatibleWith(schema))
            throw new SpatialProjectionException(
                SpatialProjectionFailureReason.SourceSchemaMismatch,
                $"projection source schema 불일치: expected {SourceSchema.Identity}, actual {schema.Identity}");
    }

    private string TargetFor(string sourceId)
    {
        if (IdMap.TryGetValue(sourceId, out string? target)) return target;
        throw new SpatialProjectionException(
            SpatialProjectionFailureReason.UnmappedSourceId,
            "공식 exact projection에 없는 source unit ID입니다.",
            new[] { sourceId });
    }

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("값은 비어 있을 수 없습니다.", parameterName);
        return value.Trim();
    }
}
