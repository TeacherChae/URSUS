using System.Globalization;
using System.Text;
using URSUS.DataSources;
using URSUS.Net;
using URSUS.Caching;

namespace URSUS.Parsers;

public enum SeoulMetricKind { AverageIncome, ResidentPopulation, TransitBoarding }

public sealed record SeoulAggregate(
    IReadOnlyDictionary<string, double> Values,
    ObservationWindow Observation,
    int RawRecordCount,
    bool PaginationComplete,
    IReadOnlyList<string> Warnings);

/// <summary>
/// 서울 열린데이터 XML API의 bounded-page, true-async parser.
/// 한 page의 projected fields만 메모리에 두고 기간/행정동 accumulator로 즉시 축약한다.
/// </summary>
public sealed class DataSeoulApiParser
{
    private const string BaseUrl = "http://openapi.seoul.go.kr:8088";
    private const int PageSize = 1000;
    private const int MaxPages = 1000;
    private readonly string _apiKey;
    private readonly HttpPipeline _http;
    private readonly IClock _clock;

    public DataSeoulApiParser(string apiKey)
        : this(apiKey, null, null) { }

    public DataSeoulApiParser(string apiKey, HttpPipeline? http, IClock? clock = null)
    {
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("서울 API 키가 필요합니다.", nameof(apiKey))
            : apiKey;
        _http = http ?? new HttpPipeline(HttpClientLifetime.Shared, maxConcurrency: 8);
        _clock = clock ?? SystemClock.Instance;
    }

    public Task<SeoulAggregate> GetAvgIncomeByAdstrdAsync(
        DataQuery query, CancellationToken cancellationToken = default)
        => FetchAsync(SeoulMetricKind.AverageIncome, "VwsmAdstrdNcmCnsmpW",
            "ADSTRD_CD", "MT_AVRG_INCOME_AMT", "STDR_YYQU_CD", query, cancellationToken);

    public Task<SeoulAggregate> GetResidentPopByAdstrdAsync(
        DataQuery query, CancellationToken cancellationToken = default)
        => FetchAsync(SeoulMetricKind.ResidentPopulation, "VwsmAdstrdRepopW",
            "ADSTRD_CD", "TOT_REPOP_CO", "STDR_YYQU_CD", query, cancellationToken);

    public Task<SeoulAggregate> GetTransitBoardingByAdstrdAsync(
        DataQuery query, CancellationToken cancellationToken = default)
        => FetchAsync(SeoulMetricKind.TransitBoarding, "tpssPassengerCnt",
            "DONG_ID", "PSNG_NO", "CRTR_DD", query, cancellationToken);

    [Obsolete("DataQuery와 CancellationToken을 받는 async API를 사용하세요.")]
    public Dictionary<string, double> GetAvgIncomeByAdstrd(string? cacheDir = null)
        => new(GetAvgIncomeByAdstrdAsync(LegacyQuery(cacheDir)).GetAwaiter().GetResult().Values);

    [Obsolete("DataQuery와 CancellationToken을 받는 async API를 사용하세요.")]
    public Dictionary<string, double> GetResidentPopByAdstrd(string? cacheDir = null)
        => new(GetResidentPopByAdstrdAsync(LegacyQuery(cacheDir)).GetAwaiter().GetResult().Values);

    [Obsolete("DataQuery와 CancellationToken을 받는 async API를 사용하세요.")]
    public Dictionary<string, double> GetTransitBoardingByAdstrd(string? cacheDir = null)
        => new(GetTransitBoardingByAdstrdAsync(LegacyQuery(cacheDir)).GetAwaiter().GetResult().Values);

    [Obsolete("대용량 생활인구 source는 제품 계약이 확정될 때까지 비활성입니다.")]
    public Dictionary<string, double> GetLivingPopByAdstrd(string? cacheDir = null)
        => throw new NotSupportedException("생활인구 source는 현재 비활성입니다.");

    private async Task<SeoulAggregate> FetchAsync(
        SeoulMetricKind kind,
        string service,
        string keyField,
        string valueField,
        string periodField,
        DataQuery query,
        CancellationToken cancellationToken)
    {
        var policy = query.TransportPolicy ?? TransportPolicy.Default;
        var fields = new[] { keyField, valueField, periodField };
        var aggregate = new Dictionary<(string period, string district), (double sum, int count)>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        int received = 0;
        int total = -1;
        bool duplicate = false;

        for (int page = 0; page < MaxPages; page++)
        {
            int start = page * PageSize + 1;
            int end = start + PageSize - 1;
            var uri = new Uri($"{BaseUrl}/{_apiKey}/xml/{service}/{start}/{end}/");
            policy.EnsureAllowed(uri, ProviderKind.Seoul);
            string xml = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml), writable: false);
            var parsed = SeoulXmlStreamParser.Parse(stream, fields,
                row => StableIdentity(row, keyField, valueField, periodField));
            if (total < 0) total = parsed.TotalCount;
            if (parsed.TotalCount != total)
                warnings.Add($"pagination total changed: {total}->{parsed.TotalCount}");

            foreach (var row in parsed.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string identity = StableIdentity(row, keyField, valueField, periodField);
                if (!identities.Add(identity)) { duplicate = true; continue; }
                if (!row.TryGetValue(keyField, out string? district) ||
                    !row.TryGetValue(periodField, out string? rawPeriod) ||
                    !row.TryGetValue(valueField, out string? rawValue) ||
                    !double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
                    continue;
                string period = NormalizePeriod(kind, rawPeriod);
                if (period.Length == 0) continue;
                var key = (period, district.Trim());
                aggregate.TryGetValue(key, out var previous);
                aggregate[key] = (previous.sum + value, previous.count + 1);
            }

            received += parsed.Rows.Count;
            if (parsed.Rows.Count == 0 || (total >= 0 && received >= total)) break;
        }

        bool paginationComplete = total >= 0 && received == total && !duplicate;
        if (!paginationComplete)
            warnings.Add($"pagination incomplete: received={received}, expected={total}, duplicate={duplicate}");

        string selectedPeriod = SelectPeriod(aggregate.Keys.Select(key => key.period).Distinct(),
            kind, query, _clock.UtcNow);
        var selected = aggregate
            .Where(pair => kind == SeoulMetricKind.TransitBoarding
                ? pair.Key.period.StartsWith(selectedPeriod, StringComparison.Ordinal)
                : pair.Key.period == selectedPeriod)
            .GroupBy(pair => pair.Key.district, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => kind == SeoulMetricKind.ResidentPopulation
                    ? group.Sum(pair => pair.Value.sum)
                    : kind == SeoulMetricKind.TransitBoarding
                        ? group.Average(pair => pair.Value.sum)
                        : group.Sum(pair => pair.Value.sum) / group.Sum(pair => pair.Value.count),
                StringComparer.Ordinal);
        int observed = selected.Keys.Count(SeoulExpectedDistricts.Ids.Contains);
        int expected = SeoulExpectedDistricts.Ids.Count;
        bool temporalComplete = true;
        if (kind == SeoulMetricKind.TransitBoarding)
        {
            var days = aggregate.Keys
                .Where(key => key.period.StartsWith(selectedPeriod, StringComparison.Ordinal))
                .GroupBy(key => key.period, StringComparer.Ordinal)
                .ToList();
            temporalComplete = days.Count >= 28 && days.All(day =>
                day.Select(item => item.district).Distinct(StringComparer.Ordinal)
                    .Count(SeoulExpectedDistricts.Ids.Contains) >= Math.Ceiling(expected * 0.95));
        }
        bool complete = paginationComplete && observed == expected && temporalComplete;
        return new SeoulAggregate(selected,
            new ObservationWindow(selectedPeriod, complete, observed, expected)
            {
                MissingIds = Array.AsReadOnly(SeoulExpectedDistricts.Ids.Except(selected.Keys,
                        StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal).ToArray()),
            },
            received, paginationComplete, warnings);
    }

    private static string StableIdentity(
        IReadOnlyDictionary<string, string> row,
        string keyField,
        string valueField,
        string periodField)
        => $"{row.GetValueOrDefault(keyField)}|{row.GetValueOrDefault(periodField)}|{row.GetValueOrDefault(valueField)}";

    private static string NormalizePeriod(SeoulMetricKind kind, string raw)
    {
        string digits = new(raw.Where(char.IsDigit).ToArray());
        if (kind == SeoulMetricKind.TransitBoarding)
            return digits.Length >= 8 ? digits[..8] : string.Empty;
        if (raw.Length == 6 && raw[4] == 'Q') return raw.ToUpperInvariant();
        if (digits.Length >= 5 && int.TryParse(digits[..4], out int year) &&
            int.TryParse(digits[4..], out int quarter) && quarter is >= 1 and <= 4)
            return $"{year}Q{quarter}";
        return string.Empty;
    }

    private static string SelectPeriod(
        IEnumerable<string> candidates,
        SeoulMetricKind kind,
        DataQuery query,
        DateTimeOffset now)
    {
        var list = candidates.ToList();
        if (query.QueryIntent == QueryIntent.ExplicitPeriod)
        {
            string explicitPeriod = query.ExplicitPeriod?.Trim() ?? string.Empty;
            if (!list.Contains(explicitPeriod, StringComparer.Ordinal))
                throw new InvalidOperationException($"요청 관측기간이 없습니다: {explicitPeriod}");
            return explicitPeriod;
        }
        if (kind == SeoulMetricKind.TransitBoarding)
        {
            string currentMonth = now.ToString("yyyyMM", CultureInfo.InvariantCulture);
            return list.Select(period => period.Length >= 6 ? period[..6] : string.Empty)
                       .Where(period => period.Length == 6 && string.CompareOrdinal(period, currentMonth) < 0)
                       .Distinct(StringComparer.Ordinal)
                       .OrderByDescending(period => period, StringComparer.Ordinal).FirstOrDefault()
                   ?? throw new InvalidOperationException("닫힌 관측 월이 없습니다.");
        }
        string currentQuarter = $"{now.Year}Q{((now.Month - 1) / 3) + 1}";
        return list.Where(period => QuarterOrder(period) < QuarterOrder(currentQuarter))
                   .OrderByDescending(QuarterOrder).FirstOrDefault()
               ?? throw new InvalidOperationException("닫힌 관측 분기가 없습니다.");
    }

    private static int QuarterOrder(string period)
        => period.Length == 6 && int.TryParse(period[..4], out int year) &&
           int.TryParse(period[5..], out int quarter)
            ? year * 4 + quarter - 1
            : int.MinValue;

    private static DataQuery LegacyQuery(string? cacheDir) => new()
    {
        CacheDirectory = cacheDir,
        TransportPolicy = new TransportPolicy(AllowInsecureSeoulHttp: true),
    };
}

internal static class ReadOnlyDictionaryExtensions
{
    public static string GetValueOrDefault(
        this IReadOnlyDictionary<string, string> dictionary, string key)
        => dictionary.TryGetValue(key, out string? value) ? value : string.Empty;
}
