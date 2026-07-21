using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
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

internal sealed class SeoulPaginationException : Exception
{
    public SeoulPaginationException(string message) : base(message) { }
}

/// <summary>
/// 서울 열린데이터 XML API의 bounded-page, true-async parser.
/// 한 page의 projected fields만 메모리에 두고 기간/행정동 accumulator로 즉시 축약한다.
/// </summary>
public sealed class DataSeoulApiParser
{
    private const string BaseUrl = "http://openapi.seoul.go.kr:8088";
    private const int PageSize = 1000;
    private const int MaxPages = 1000;
    internal const int MaximumRows = PageSize * MaxPages;
    internal const int MaximumRetainedAggregateEntries = 20_000;
    private readonly string _apiKey;
    private readonly HttpPipeline _http;
    private readonly IClock _clock;

    internal int PeakRetainedAggregateEntries { get; private set; }

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
        var accumulator = new SelectedPeriodAccumulator(kind, query, _clock.UtcNow);
        var identities = new HashSet<StableRowFingerprint>();
        int received = 0;
        int total = -1;
        PeakRetainedAggregateEntries = 0;

        for (int page = 0; page < MaxPages; page++)
        {
            int start = page * PageSize + 1;
            int end = start + PageSize - 1;
            var uri = new Uri($"{BaseUrl}/{_apiKey}/xml/{service}/{start}/{end}/");
            policy.EnsureAllowed(uri, ProviderKind.Seoul);
            int pageRows = 0;
            SeoulXmlPageSummary parsed = await _http.ProcessStreamAsync(uri, (stream, token) =>
                SeoulXmlStreamParser.ParseRowsAsync(stream, fields, (row, rowToken) =>
                {
                    rowToken.ThrowIfCancellationRequested();
                    pageRows++;
                    if (pageRows > PageSize)
                        throw new SeoulPaginationException(
                            $"서울 API pagination page 크기 상한을 초과했습니다 ({pageRows}/{PageSize}).");
                    if (received + pageRows > MaximumRows)
                        throw new SeoulPaginationException(
                            $"서울 API pagination row 안전 상한을 초과했습니다 ({received + pageRows}/{MaximumRows}).");

                    StableRowFingerprint identity = StableIdentity(
                        row, keyField, valueField, periodField);
                    if (!identities.Add(identity))
                        throw new SeoulPaginationException(
                            "서울 API pagination duplicate row가 감지되었습니다.");

                    if (!row.TryGetValue(keyField, out string? district) ||
                        !row.TryGetValue(periodField, out string? rawPeriod) ||
                        !row.TryGetValue(valueField, out string? rawValue) ||
                        !double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
                        return ValueTask.CompletedTask;
                    string period = NormalizePeriod(kind, rawPeriod);
                    if (period.Length == 0) return ValueTask.CompletedTask;
                    accumulator.Add(period, district.Trim(), value);
                    PeakRetainedAggregateEntries = Math.Max(
                        PeakRetainedAggregateEntries, accumulator.RetainedEntryCount);
                    return ValueTask.CompletedTask;
                }, token), cancellationToken).ConfigureAwait(false);
            if (parsed.TotalCount < 0)
                throw new SeoulPaginationException(
                    "서울 API pagination total count가 없습니다.");
            if (total < 0)
            {
                total = parsed.TotalCount;
                if (total > MaximumRows)
                    throw new SeoulPaginationException(
                        $"서울 API pagination 안전 상한을 초과했습니다 ({total}/{MaximumRows}).");
            }
            if (parsed.TotalCount != total)
                throw new SeoulPaginationException(
                    $"서울 API pagination total count가 변경되었습니다 ({total}->{parsed.TotalCount}).");

            received += parsed.RowCount;
            if (parsed.RowCount == 0 && received < total)
                throw new SeoulPaginationException(
                    $"서울 API pagination이 조기 종료되었습니다 ({received}/{total}).");
            if (received > total)
                throw new SeoulPaginationException(
                    $"서울 API pagination row count가 일치하지 않습니다 ({received}/{total}).");
            if (received == total) break;
            if (page == MaxPages - 1)
                throw new SeoulPaginationException(
                    $"서울 API pagination 안전 상한에서 잘렸습니다 ({received}/{total}).");
        }

        bool paginationComplete = total >= 0 && received == total;
        if (!paginationComplete)
            throw new SeoulPaginationException(
                $"서울 API pagination이 완결되지 않았습니다 ({received}/{total}).");

        string selectedPeriod = accumulator.GetSelectedPeriod();
        IReadOnlyDictionary<(string period, string district), (double sum, int count)> aggregate =
            accumulator.Entries;
        var selected = aggregate
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
            received, paginationComplete, Array.Empty<string>());
    }

    private static StableRowFingerprint StableIdentity(
        IReadOnlyDictionary<string, string> row,
        string keyField,
        string valueField,
        string periodField)
    {
        string key = row.GetValueOrDefault(keyField);
        string period = row.GetValueOrDefault(periodField);
        string value = row.GetValueOrDefault(valueField);
        int byteCount = checked(
            12 + Encoding.UTF8.GetByteCount(key) +
            Encoding.UTF8.GetByteCount(period) + Encoding.UTF8.GetByteCount(value));
        byte[]? rented = null;
        Span<byte> input = byteCount <= 256
            ? stackalloc byte[byteCount]
            : (rented = ArrayPool<byte>.Shared.Rent(byteCount)).AsSpan(0, byteCount);
        try
        {
            int offset = 0;
            WriteFramedUtf8(key, input, ref offset);
            WriteFramedUtf8(period, input, ref offset);
            WriteFramedUtf8(value, input, ref offset);
            Span<byte> digest = stackalloc byte[32];
            SHA256.HashData(input, digest);
            return new StableRowFingerprint(
                BinaryPrimitives.ReadUInt64LittleEndian(digest),
                BinaryPrimitives.ReadUInt64LittleEndian(digest[8..]),
                BinaryPrimitives.ReadUInt64LittleEndian(digest[16..]),
                BinaryPrimitives.ReadUInt64LittleEndian(digest[24..]));
        }
        finally
        {
            if (rented != null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void WriteFramedUtf8(string value, Span<byte> destination, ref int offset)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], byteCount);
        offset += sizeof(int);
        offset += Encoding.UTF8.GetBytes(value, destination[offset..]);
    }

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

    private readonly record struct StableRowFingerprint(ulong A, ulong B, ulong C, ulong D);

    private sealed class SelectedPeriodAccumulator
    {
        private readonly SeoulMetricKind _kind;
        private readonly QueryIntent _intent;
        private readonly string _explicitPeriod;
        private readonly string _currentMonth;
        private readonly int _currentQuarterOrder;
        private readonly Dictionary<(string period, string district), (double sum, int count)> _entries = new();
        private string? _selectedPeriod;

        public SelectedPeriodAccumulator(SeoulMetricKind kind, DataQuery query, DateTimeOffset now)
        {
            _kind = kind;
            _intent = query.QueryIntent;
            _explicitPeriod = query.ExplicitPeriod?.Trim() ?? string.Empty;
            _currentMonth = now.ToString("yyyyMM", CultureInfo.InvariantCulture);
            _currentQuarterOrder = QuarterOrder($"{now.Year}Q{((now.Month - 1) / 3) + 1}");
        }

        public int RetainedEntryCount => _entries.Count;

        public IReadOnlyDictionary<(string period, string district), (double sum, int count)> Entries
            => _entries;

        public void Add(string period, string district, double value)
        {
            string candidate = _kind == SeoulMetricKind.TransitBoarding
                ? period[..6]
                : period;
            if (_intent == QueryIntent.ExplicitPeriod)
            {
                if (_explicitPeriod.Length == 0 ||
                    (_kind == SeoulMetricKind.TransitBoarding
                        ? !period.StartsWith(_explicitPeriod, StringComparison.Ordinal)
                        : !period.Equals(_explicitPeriod, StringComparison.Ordinal)))
                    return;
                _selectedPeriod = _explicitPeriod;
            }
            else
            {
                bool closed = _kind == SeoulMetricKind.TransitBoarding
                    ? string.CompareOrdinal(candidate, _currentMonth) < 0
                    : QuarterOrder(candidate) < _currentQuarterOrder;
                if (!closed) return;
                if (_selectedPeriod == null || IsAfter(candidate, _selectedPeriod))
                {
                    _entries.Clear();
                    _selectedPeriod = candidate;
                }
                else if (!candidate.Equals(_selectedPeriod, StringComparison.Ordinal))
                {
                    return;
                }
            }

            var key = (period, district);
            if (!_entries.ContainsKey(key) && _entries.Count >= MaximumRetainedAggregateEntries)
                throw new SeoulPaginationException(
                    $"서울 API aggregation 안전 상한을 초과했습니다 ({_entries.Count + 1}/{MaximumRetainedAggregateEntries}).");
            _entries.TryGetValue(key, out var previous);
            _entries[key] = (previous.sum + value, previous.count + 1);
        }

        public string GetSelectedPeriod()
            => _selectedPeriod ?? throw new InvalidOperationException(
                _intent == QueryIntent.ExplicitPeriod
                    ? $"요청 관측기간이 없습니다: {_explicitPeriod}"
                    : _kind == SeoulMetricKind.TransitBoarding
                        ? "닫힌 관측 월이 없습니다."
                        : "닫힌 관측 분기가 없습니다.");

        private bool IsAfter(string candidate, string selected)
            => _kind == SeoulMetricKind.TransitBoarding
                ? string.CompareOrdinal(candidate, selected) > 0
                : QuarterOrder(candidate) > QuarterOrder(selected);
    }
}

internal static class ReadOnlyDictionaryExtensions
{
    public static string GetValueOrDefault(
        this IReadOnlyDictionary<string, string> dictionary, string key)
        => dictionary.TryGetValue(key, out string? value) ? value : string.Empty;
}
