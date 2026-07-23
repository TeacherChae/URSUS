namespace URSUS.Preprocessing;

public enum SpatialAssetRole
{
    Statistic,
    Geometry,
}

public static class SpatialSemantics
{
    public const string UnitBoundary = "unit_boundary";
}

/// <summary>
/// Declares what one concrete provider dataset can produce after validated preprocessing.
/// PreferenceRank is deterministic ordering only; it never relaxes compatibility.
/// </summary>
public sealed record SpatialAssetDescriptor
{
    public ProviderDatasetIdentity Dataset { get; }
    public SpatialAssetRole Role { get; }
    public SpatialUnitSchema OutputSchema { get; }
    public string SemanticsId { get; }
    public string CoverageId { get; }
    public int PreferenceRank { get; }
    public string AssetKey { get; }

    public SpatialAssetDescriptor(
        ProviderDatasetIdentity dataset,
        SpatialAssetRole role,
        SpatialUnitSchema outputSchema,
        string semanticsId,
        string coverageId,
        int preferenceRank)
    {
        Dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        Role = role;
        OutputSchema = outputSchema ?? throw new ArgumentNullException(nameof(outputSchema));
        SemanticsId = Required(semanticsId, nameof(semanticsId)).ToLowerInvariant();
        CoverageId = Required(coverageId, nameof(coverageId));
        if (preferenceRank < 0)
            throw new ArgumentOutOfRangeException(nameof(preferenceRank));
        PreferenceRank = preferenceRank;
        AssetKey = string.Join("|",
            Role,
            Dataset.ProviderId,
            Dataset.DatasetId,
            Dataset.SchemaVersion,
            OutputSchema.Identity,
            SemanticsId,
            CoverageId);
    }

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("값은 비어 있을 수 없습니다.", parameterName);
        return value.Trim();
    }
}

/// <summary>
/// Multi-provider catalog. It returns every exact candidate in deterministic order,
/// so a caller can continue after one provider fails without changing semantics.
/// </summary>
public sealed class SpatialCapabilityRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<string, SpatialAssetDescriptor> _assets =
        new(StringComparer.Ordinal);

    public void Register(SpatialAssetDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (_lock)
        {
            if (!_assets.TryAdd(descriptor.AssetKey, descriptor))
                throw new ArgumentException(
                    $"동일 spatial asset가 이미 등록되었습니다: {descriptor.AssetKey}",
                    nameof(descriptor));
        }
    }

    public IReadOnlyList<SpatialAssetDescriptor> FindExactCandidates(
        SpatialAssetRole role,
        SpatialUnitSchema schema,
        string semanticsId,
        string coverageId)
    {
        ArgumentNullException.ThrowIfNull(schema);
        string semantics = Required(semanticsId, nameof(semanticsId)).ToLowerInvariant();
        string coverage = Required(coverageId, nameof(coverageId));
        lock (_lock)
        {
            return Array.AsReadOnly(_assets.Values
                .Where(asset =>
                    asset.Role == role &&
                    asset.OutputSchema.IsExactlyCompatibleWith(schema) &&
                    asset.SemanticsId.Equals(semantics, StringComparison.Ordinal) &&
                    asset.CoverageId.Equals(coverage, StringComparison.Ordinal))
                .OrderBy(asset => asset.PreferenceRank)
                .ThenBy(asset => asset.Dataset.ProviderId, StringComparer.Ordinal)
                .ThenBy(asset => asset.Dataset.DatasetId, StringComparer.Ordinal)
                .ToArray());
        }
    }

    public IReadOnlyList<SpatialAssetDescriptor> GetAll()
    {
        lock (_lock)
        {
            return Array.AsReadOnly(_assets.Values
                .OrderBy(asset => asset.AssetKey, StringComparer.Ordinal)
                .ToArray());
        }
    }

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("값은 비어 있을 수 없습니다.", parameterName);
        return value.Trim();
    }
}
