using URSUS.Parsers;

namespace URSUS.DataSources;

public enum QueryIntent { Latest, ExplicitPeriod }

public sealed record PeriodCoverage(string PeriodId, int ObservedCount, int ExpectedCount)
{
    public bool IsComplete => ExpectedCount > 0 && ObservedCount == ExpectedCount;
}

public sealed record ObservationWindow(
    string PeriodId,
    bool IsComplete,
    int ObservedCount,
    int ExpectedCount)
{
    public IReadOnlyList<string> MissingIds { get; init; } = Array.Empty<string>();
}

public static class ClosedPeriodSelector
{
    public static ObservationWindow SelectLatestQuarter(
        IEnumerable<PeriodCoverage> periods,
        DateTimeOffset utcNow)
    {
        string current = $"{utcNow.Year}Q{((utcNow.Month - 1) / 3) + 1}";
        var selected = periods
            .Where(period => TryQuarterOrder(period.PeriodId, out int order) &&
                             TryQuarterOrder(current, out int currentOrder) && order < currentOrder)
            .OrderByDescending(period => QuarterOrder(period.PeriodId))
            .FirstOrDefault()
            ?? throw new InvalidOperationException("닫힌 관측 분기가 없습니다.");
        return new ObservationWindow(selected.PeriodId, selected.IsComplete,
            selected.ObservedCount, selected.ExpectedCount);
    }

    private static bool TryQuarterOrder(string value, out int order)
    {
        order = 0;
        if (value.Length != 6 || value[4] != 'Q' ||
            !int.TryParse(value[..4], out int year) ||
            !int.TryParse(value[5..], out int quarter) || quarter is < 1 or > 4)
            return false;
        order = year * 4 + quarter - 1;
        return true;
    }

    private static int QuarterOrder(string value)
    {
        TryQuarterOrder(value, out int order);
        return order;
    }
}

public static class SeoulExpectedDistricts
{
    public const string Version = "embedded-adstrd-legald-v1";
    private static readonly Lazy<IReadOnlySet<string>> LazyIds = new(() =>
        MappingLoader.Load().Keys
            .Where(id => id.StartsWith("11", StringComparison.Ordinal) && !id.EndsWith("000", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal));

    public static IReadOnlySet<string> Ids => LazyIds.Value;
}

public enum CoordinateReferenceSystem { Epsg5179, Wgs84 }

public sealed record SpatialBounds(
    double MinX, double MinY, double MaxX, double MaxY,
    CoordinateReferenceSystem Crs)
{
    public SpatialBounds Normalize() => new(
        Math.Min(MinX, MaxX), Math.Min(MinY, MaxY),
        Math.Max(MinX, MaxX), Math.Max(MinY, MaxY), Crs);

    public SpatialBounds ToWgs84()
    {
        var normalized = Normalize();
        if (Crs == CoordinateReferenceSystem.Wgs84) return normalized;
        var min = Utils.Epsg5179.ToWgs84(normalized.MinX, normalized.MinY);
        var max = Utils.Epsg5179.ToWgs84(normalized.MaxX, normalized.MaxY);
        return new SpatialBounds(min.Longitude, min.Latitude, max.Longitude, max.Latitude,
            CoordinateReferenceSystem.Wgs84).Normalize();
    }
}

public sealed record TransportPolicy(bool AllowInsecureSeoulHttp = false)
{
    public static TransportPolicy Default { get; } = new();

    public static TransportPolicy FromEnvironment()
    {
        string? value = Environment.GetEnvironmentVariable("URSUS_ALLOW_INSECURE_SEOUL_HTTP");
        return new TransportPolicy(value != null &&
            (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("yes", StringComparison.OrdinalIgnoreCase)));
    }

    public void EnsureAllowed(Uri uri, Net.ProviderKind provider)
    {
        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return;
        if (provider == Net.ProviderKind.Seoul && AllowInsecureSeoulHttp &&
            uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) return;
        throw new InvalidOperationException($"허용되지 않은 전송 방식: {Net.SecretRedactor.Redact(uri.ToString())}");
    }
}
