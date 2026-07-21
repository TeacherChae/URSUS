using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using URSUS.DataSources;

namespace URSUS.Analysis;

public sealed class AnalysisRequest
{
    public IReadOnlyList<string> DataSets { get; }
    public IReadOnlyList<double>? Weights { get; }
    public string? Address1 { get; }
    public string? Address2 { get; }
    public double? RadiusKm { get; }
    public TransportPolicy TransportPolicy { get; }
    public bool ForceRefresh { get; }
    public QueryIntent QueryIntent { get; }
    public string? ExplicitPeriod { get; }
    public SpatialBounds? Bounds { get; }
    public string KeyFingerprint { get; }
    public string QueryFingerprint { get; }

    public AnalysisRequest(
        IEnumerable<string> dataSets,
        IReadOnlyList<double>? weights = null,
        string? address1 = null,
        string? address2 = null,
        double? radiusKm = null,
        TransportPolicy? transportPolicy = null,
        bool forceRefresh = false,
        QueryIntent queryIntent = QueryIntent.Latest,
        string? explicitPeriod = null,
        SpatialBounds? bounds = null,
        string? keyFingerprint = null)
    {
        string[] copiedDataSets = dataSets?.ToArray() ?? throw new ArgumentNullException(nameof(dataSets));
        if (copiedDataSets.Distinct(StringComparer.Ordinal).Count() != copiedDataSets.Length)
            throw new ArgumentException("DataSets에는 중복 항목을 지정할 수 없습니다.", nameof(dataSets));
        DataSets = Array.AsReadOnly(copiedDataSets);
        Weights = weights == null ? null : Array.AsReadOnly(weights.ToArray());
        Address1 = address1?.Trim();
        Address2 = address2?.Trim();
        RadiusKm = radiusKm;
        TransportPolicy = transportPolicy ?? TransportPolicy.FromEnvironment();
        ForceRefresh = forceRefresh;
        QueryIntent = queryIntent;
        ExplicitPeriod = explicitPeriod?.Trim();
        Bounds = bounds?.Normalize();
        KeyFingerprint = keyFingerprint?.Trim() ?? string.Empty;
        QueryFingerprint = ComputeFingerprint();
    }

    private string ComputeFingerprint()
    {
        var text = new StringBuilder("analysis-v1")
            .Append("|transport:").Append(TransportPolicy.AllowInsecureSeoulHttp)
            .Append("|force:").Append(ForceRefresh)
            .Append("|intent:").Append(QueryIntent)
            .Append("|period:").Append(ExplicitPeriod ?? string.Empty)
            .Append("|a1:").Append(Address1 ?? string.Empty)
            .Append("|a2:").Append(Address2 ?? string.Empty)
            .Append("|radius:").Append(RadiusKm?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty)
            .Append("|keys:").Append(KeyFingerprint);
        foreach (string dataSet in DataSets.OrderBy(x => x, StringComparer.Ordinal))
            text.Append("|ds:").Append(dataSet);
        if (Bounds is SpatialBounds bounds)
            text.Append("|bounds:").Append(bounds.Crs).Append(':')
                .Append(bounds.MinX.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(bounds.MinY.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(bounds.MaxX.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(bounds.MaxY.ToString("R", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))
            .ToLowerInvariant();
    }
}

public sealed record AnalysisProgress(double Fraction, string Stage, string? Message = null)
{
    public double ClampedFraction => Math.Clamp(Fraction, 0, 1);
}
