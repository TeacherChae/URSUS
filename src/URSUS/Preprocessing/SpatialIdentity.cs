using System.Globalization;

namespace URSUS.Preprocessing;

public enum SpatialUnitKind
{
    LegalDistrict,
    AdministrativeDistrict,
    StatisticalArea,
    StandardGrid,
    Parcel,
    Building,
    Custom,
}

/// <summary>
/// Provider-independent identity of a spatial partition.
/// Display labels and source column names are deliberately excluded.
/// </summary>
public sealed record SpatialUnitSchema
{
    public SpatialUnitKind Kind { get; }
    public string Authority { get; }
    public string Namespace { get; }
    public string Version { get; }
    public string Level { get; }
    public double? ResolutionMeters { get; }
    public string Identity { get; }

    public SpatialUnitSchema(
        SpatialUnitKind kind,
        string authority,
        string codeNamespace,
        string version,
        string level,
        double? resolutionMeters = null)
    {
        Kind = kind;
        Authority = Required(authority, nameof(authority)).ToUpperInvariant();
        Namespace = Required(codeNamespace, nameof(codeNamespace)).ToUpperInvariant();
        Version = Required(version, nameof(version));
        Level = Required(level, nameof(level));

        if (resolutionMeters.HasValue &&
            (!double.IsFinite(resolutionMeters.Value) || resolutionMeters.Value <= 0))
            throw new ArgumentOutOfRangeException(nameof(resolutionMeters));
        if (kind == SpatialUnitKind.StandardGrid && !resolutionMeters.HasValue)
            throw new ArgumentException(
                "표준 격자 schema에는 resolutionMeters가 필요합니다.",
                nameof(resolutionMeters));

        ResolutionMeters = resolutionMeters;
        Identity = string.Join("|",
            Kind,
            Authority,
            Namespace,
            Version,
            Level,
            ResolutionMeters?.ToString("R", CultureInfo.InvariantCulture) ?? "-");
    }

    public bool IsExactlyCompatibleWith(SpatialUnitSchema? other) =>
        other != null && Equals(other);

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("값은 비어 있을 수 없습니다.", parameterName);
        return value.Trim();
    }
}

/// <summary>A canonical unit ID is meaningful only within its complete schema.</summary>
public sealed record SpatialUnitId
{
    public SpatialUnitSchema Schema { get; }
    public string Value { get; }

    public SpatialUnitId(SpatialUnitSchema schema, string value)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("공간 단위 ID는 비어 있을 수 없습니다.", nameof(value));
        Value = value.Trim();
    }

    public override string ToString() => $"{Schema.Identity}:{Value}";
}

/// <summary>Versioned provenance for one provider dataset contract.</summary>
public sealed record ProviderDatasetIdentity
{
    public string ProviderId { get; }
    public string DatasetId { get; }
    public string SchemaVersion { get; }
    public string EvidenceReference { get; }

    public ProviderDatasetIdentity(
        string providerId,
        string datasetId,
        string schemaVersion,
        string evidenceReference)
    {
        ProviderId = Required(providerId, nameof(providerId));
        DatasetId = Required(datasetId, nameof(datasetId));
        SchemaVersion = Required(schemaVersion, nameof(schemaVersion));
        EvidenceReference = Required(evidenceReference, nameof(evidenceReference));
    }

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("값은 비어 있을 수 없습니다.", parameterName);
        return value.Trim();
    }
}
