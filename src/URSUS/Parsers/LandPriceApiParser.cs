using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using URSUS.DataSources;
using URSUS.Net;
using URSUS.Caching;

namespace URSUS.Parsers
{
    /// <summary>
    /// A legal district's land-price mean together with the number of source
    /// observations that contributed to it.
    /// </summary>
    public sealed record LandPriceAggregate(double Mean, int SampleCount);

    /// <summary>
    /// 공공데이터포털(data.go.kr) 표준지공시지가 API → 법정동별 평균 공시지가.
    ///
    /// - 시군구 단위(5자리)로 일괄 조회하여 API 호출 횟수를 최소화
    /// - 응답의 법정동코드(10자리/PNU)를 canonical 8자리 ID로 변환해 평균 집계
    /// - 캐시 적용 (TTL 30일)
    ///
    /// API 발급: https://www.data.go.kr/data/15058747/openapi.do (무료, 즉시 발급)
    /// </summary>
    public class LandPriceApiParser
    {
        private const int    CACHE_TTL_DAYS = 30;
        private const string BASE_URL =
            "https://apis.data.go.kr/1611000/nsdi/ReferLandPriceService/attr/getReferLandPriceAttr";

        /// <summary>공시지가 값 필드명 (원/㎡)</summary>
        private const string PRICE_FIELD = "pblntfPclnd";

        /// <summary>법정동코드 필드명</summary>
        private const string LD_CODE_FIELD = "ldCode";

        private readonly string     _apiKey;
        private readonly HttpPipeline _http;
        private readonly IClock _clock;

        public LandPriceApiParser(string apiKey)
            : this(apiKey, null, null) { }

        public LandPriceApiParser(string apiKey, HttpPipeline? http, IClock? clock = null)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _http   = http ?? new HttpPipeline(HttpClientLifetime.Shared, maxConcurrency: 8);
            _clock = clock ?? SystemClock.Instance;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 법정동 코드 목록에 대한 평균 공시지가를 반환한다 (캐시 적용).
        /// key = canonical 법정동 ID(8자리), value = 평균 공시지가 (원/㎡)
        /// </summary>
        /// <param name="legalDistrictCodes">법정동 코드 목록 (VWorld에서 수집한 emd_cd)</param>
        /// <param name="cacheDir">캐시 디렉터리 경로 (null이면 캐시 비활성)</param>
        public Dictionary<string, double> GetLandPriceByLegalDistrict(
            List<string> legalDistrictCodes, string? cacheDir = null)
            => GetLandPriceByLegalDistrictAsync(legalDistrictCodes, cacheDir)
                .GetAwaiter().GetResult();

        public async Task<Dictionary<string, double>> GetLandPriceByLegalDistrictAsync(
            List<string> legalDistrictCodes, string? cacheDir = null,
            CancellationToken cancellationToken = default, int? standardYear = null)
        {
            // Retain read compatibility with the pre-provenance value-only cache
            // for callers of this legacy API. Typed consumers never read this file.
            string? legacyCachePath = cacheDir != null
                ? Path.Combine(cacheDir, "land_price.json")
                : null;
            if (legacyCachePath != null && IsCacheValid(legacyCachePath))
            {
                double remaining = CACHE_TTL_DAYS
                    - (DateTime.UtcNow - File.GetLastWriteTimeUtc(legacyCachePath)).TotalDays;
                Console.WriteLine(
                    $"[CACHE] land_price 캐시 사용 (만료까지 {remaining:F1}일)");
                return FilterByRequestedCodes(LoadLegacyCache(legacyCachePath), legalDistrictCodes);
            }

            var aggregates = await GetLandPriceAggregatesByLegalDistrictAsync(
                    legalDistrictCodes, cacheDir, cancellationToken, standardYear)
                .ConfigureAwait(false);
            return aggregates.ToDictionary(pair => pair.Key, pair => pair.Value.Mean,
                StringComparer.Ordinal);
        }

        /// <summary>
        /// Returns the mean land price and its contributing sample count per
        /// canonical legal-district ID. Its cache schema is intentionally
        /// separate from the legacy value-only cache.
        /// </summary>
        public Dictionary<string, LandPriceAggregate> GetLandPriceAggregatesByLegalDistrict(
            List<string> legalDistrictCodes, string? cacheDir = null)
            => GetLandPriceAggregatesByLegalDistrictAsync(legalDistrictCodes, cacheDir)
                .GetAwaiter().GetResult();

        public async Task<Dictionary<string, LandPriceAggregate>>
            GetLandPriceAggregatesByLegalDistrictAsync(
                List<string> legalDistrictCodes, string? cacheDir = null,
                CancellationToken cancellationToken = default, int? standardYear = null)
        {
            int resolvedStandardYear = standardYear ?? (_clock.UtcNow.Year - 1);
            string? cachePath = cacheDir != null
                ? BuildAggregateCachePath(
                    cacheDir, legalDistrictCodes, resolvedStandardYear, standardYear.HasValue)
                : null;

            if (cachePath != null && IsCacheValid(cachePath) &&
                TryLoadAggregateCache(cachePath, out var cached))
            {
                double remaining = CACHE_TTL_DAYS
                    - (DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath)).TotalDays;
                Console.WriteLine(
                    $"[CACHE] land_price provenance 캐시 사용 (만료까지 {remaining:F1}일)");
                return FilterAggregatesByRequestedCodes(
                    cached, legalDistrictCodes);
            }

            Console.WriteLine("[CACHE] land_price API 호출 중...");
            var result = await FetchLandPricesAsync(
                    legalDistrictCodes, resolvedStandardYear, cancellationToken)
                .ConfigureAwait(false);

            if (cachePath != null && result.Count > 0)
                SaveCache(result, cachePath);

            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Fetch — 시군구 단위 일괄 조회
        // ─────────────────────────────────────────────────────────────────

        private async Task<Dictionary<string, LandPriceAggregate>> FetchLandPricesAsync(
            List<string> codes, int? standardYear, CancellationToken cancellationToken)
        {
            // 기준연도: 공시지가는 매년 1월 1일 기준으로 공시 → 전년도 사용
            string stdrYear = (standardYear ?? (_clock.UtcNow.Year - 1)).ToString(
                System.Globalization.CultureInfo.InvariantCulture);

            // 시군구(5자리) 단위로 그룹핑하여 API 호출 횟수 최소화
            var sigCodes = codes
                .Where(c => c.Length >= 5)
                .Select(c => c.Substring(0, 5))
                .Distinct()
                .ToList();

            // 법정동별 가격 누적
            var acc = new Dictionary<string, (double sum, int count)>();

            int completed = 0;
            foreach (string sigCode in sigCodes)
            {
                var records = await FetchForSigAsync(sigCode, stdrYear, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var (ldCode, price) in records)
                {
                    string key = DistrictCode.CanonicalizeLegal(ldCode);
                    if (string.IsNullOrEmpty(key)) continue;

                    if (acc.TryGetValue(key, out var prev))
                        acc[key] = (prev.sum + price, prev.count + 1);
                    else
                        acc[key] = (price, 1);
                }
                completed++;
                Console.WriteLine(
                    $"[INFO] 공시지가 시군구 {completed}/{sigCodes.Count} 완료 ({sigCode})");
            }

            // 평균 계산 + 요청된 코드만 필터
            var codesSet = new HashSet<string>(
                codes.Select(DistrictCode.CanonicalizeLegal)
                    .Where(code => !string.IsNullOrEmpty(code)));
            var result = new Dictionary<string, LandPriceAggregate>();
            foreach (var (key, (sum, cnt)) in acc)
            {
                if (codesSet.Contains(key))
                    result[key] = new LandPriceAggregate(sum / cnt, cnt);
            }

            Console.WriteLine(
                $"[INFO] 공시지가 수집 완료 ({result.Count}/{codes.Count} 법정동)");
            return result;
        }

        /// <summary>
        /// 시군구 코드(5자리) 기준으로 표준지공시지가 전체를 페이지네이션하여 수집.
        /// </summary>
        private async Task<List<(string ldCode, double price)>> FetchForSigAsync(
            string sigCode, string stdrYear, CancellationToken cancellationToken)
        {
            var results = new List<(string, double)>();
            int pageNo = 1;
            const int numOfRows = 1000;
            const int maxPages  = 50; // 안전 제한
            int? expectedTotal = null;
            int received = 0;
            var identities = new HashSet<string>(StringComparer.Ordinal);

            while (pageNo <= maxPages)
            {
                string url = BuildUrl(sigCode, stdrYear, pageNo, numOfRows);
                string body = await _http.GetStringAsync(new Uri(url), cancellationToken)
                    .ConfigureAwait(false);

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // 응답 구조: { "referLandPrices": { "referLandPrice": [...] } }
                if (!root.TryGetProperty("referLandPrices", out var container))
                    throw new InvalidOperationException("공시지가 API 응답에 referLandPrices가 없습니다.");

                // 에러 코드 확인
                if (container.TryGetProperty("resultCode", out var codeElem))
                {
                    string resultCode = codeElem.GetString() ?? "";
                    if (resultCode != "000")
                    {
                        string msg = container.TryGetProperty("resultMsg", out var msgElem)
                            ? msgElem.GetString() ?? ""
                            : "Unknown error";
                        throw new InvalidOperationException(
                            $"공시지가 API 오류: {resultCode} - {msg}");
                    }
                }

                int pageTotal = ReadRequiredTotal(container, "공시지가");
                if (expectedTotal is null) expectedTotal = pageTotal;
                else if (expectedTotal.Value != pageTotal)
                    throw new InvalidOperationException("공시지가 API totalCount가 페이지 사이에 변경되었습니다.");

                if (!container.TryGetProperty("referLandPrice", out var items))
                {
                    if (pageTotal == 0) break;
                    throw new InvalidOperationException("공시지가 API 행이 totalCount보다 먼저 종료되었습니다.");
                }

                int count = 0;
                foreach (var item in items.EnumerateArray())
                {
                    if (!identities.Add(HashIdentity(item.GetRawText())))
                        throw new InvalidOperationException("공시지가 pagination duplicate row가 감지되었습니다.");
                    string ldCode = GetStringProp(item, LD_CODE_FIELD);
                    string priceStr = GetStringProp(item, PRICE_FIELD);

                    if (!string.IsNullOrEmpty(ldCode)
                        && double.TryParse(priceStr, out double price)
                        && price > 0)
                    {
                        results.Add((ldCode, price));
                    }
                    count++;
                }
                received += count;
                if (received > pageTotal)
                    throw new InvalidOperationException("공시지가 수신 행 수가 totalCount를 초과했습니다.");

                if (received == pageTotal) break;
                if (count < numOfRows)
                    throw new InvalidOperationException(
                        $"공시지가 pagination이 조기 종료되었습니다 ({received}/{pageTotal}).");
                pageNo++;
            }

            if (expectedTotal is null || received != expectedTotal.Value)
                throw new InvalidOperationException(
                    $"공시지가 pagination이 완결되지 않았습니다 ({received}/{expectedTotal?.ToString() ?? "?"}).");

            return results;
        }

        private static int ReadRequiredTotal(JsonElement container, string source)
        {
            if (!container.TryGetProperty("totalCount", out var total) ||
                !int.TryParse(total.ToString(), out int value) || value < 0)
                throw new InvalidOperationException($"{source} API totalCount가 없거나 유효하지 않습니다.");
            return value;
        }

        private static string HashIdentity(string value)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        private string BuildUrl(string ldCode, string stdrYear, int pageNo, int numOfRows)
        {
            return $"{BASE_URL}" +
                $"?serviceKey={Uri.EscapeDataString(_apiKey)}" +
                $"&ldCode={ldCode}" +
                $"&stdrYear={stdrYear}" +
                $"&format=json" +
                $"&numOfRows={numOfRows}" +
                $"&pageNo={pageNo}";
        }

        // ─────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 법정동코드를 VWorld와 동일한 8자리 식별자로 정규화.
        /// </summary>
        private static string GetStringProp(JsonElement elem, string propName)
        {
            if (elem.TryGetProperty(propName, out var prop))
            {
                return prop.ValueKind == JsonValueKind.String
                    ? prop.GetString() ?? ""
                    : prop.ToString();
            }
            return "";
        }

        /// <summary>캐시에서 로드한 데이터 중 요청된 코드만 반환</summary>
        private static Dictionary<string, double> FilterByRequestedCodes(
            Dictionary<string, double> cached, List<string> codes)
        {
            var codesSet = new HashSet<string>(
                codes.Select(DistrictCode.CanonicalizeLegal)
                    .Where(code => !string.IsNullOrEmpty(code)));
            return cached
                .Select(kv => new KeyValuePair<string, double>(
                    DistrictCode.CanonicalizeLegal(kv.Key), kv.Value))
                .Where(kv => !string.IsNullOrEmpty(kv.Key) && codesSet.Contains(kv.Key))
                .GroupBy(kv => kv.Key)
                .ToDictionary(group => group.Key, group => group.Average(item => item.Value));
        }

        private static Dictionary<string, LandPriceAggregate> FilterAggregatesByRequestedCodes(
            Dictionary<string, LandPriceAggregate> cached, List<string> codes)
        {
            var codesSet = new HashSet<string>(
                codes.Select(DistrictCode.CanonicalizeLegal)
                    .Where(code => !string.IsNullOrEmpty(code)), StringComparer.Ordinal);
            var accumulator = new Dictionary<string, (double weightedSum, int count)>(
                StringComparer.Ordinal);
            foreach (var (rawCode, aggregate) in cached)
            {
                string code = DistrictCode.CanonicalizeLegal(rawCode);
                if (code.Length == 0 || !codesSet.Contains(code) || aggregate.SampleCount <= 0)
                    continue;
                if (accumulator.TryGetValue(code, out var previous))
                    accumulator[code] = (
                        previous.weightedSum + aggregate.Mean * aggregate.SampleCount,
                        checked(previous.count + aggregate.SampleCount));
                else
                    accumulator[code] = (aggregate.Mean * aggregate.SampleCount,
                        aggregate.SampleCount);
            }
            return accumulator.ToDictionary(
                pair => pair.Key,
                pair => new LandPriceAggregate(
                    pair.Value.weightedSum / pair.Value.count, pair.Value.count),
                StringComparer.Ordinal);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Cache
        // ─────────────────────────────────────────────────────────────────

        private static bool IsCacheValid(string path)
        {
            if (!File.Exists(path)) return false;
            return (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalDays
                   < CACHE_TTL_DAYS;
        }

        private static string BuildAggregateCachePath(
            string cacheDir, IEnumerable<string> codes, int standardYear, bool explicitYear)
        {
            var key = PersistentCacheKey.Create(
                "land_price_parser", 2,
                explicitYear ? QueryIntent.ExplicitPeriod : QueryIntent.Latest,
                new Dictionary<string, string>
                {
                    ["stdrYear"] = standardYear.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                },
                codes,
                CoordinateReferenceSystem.Epsg5179);
            return Path.Combine(cacheDir, $"land_price.v2.{key.Value}.json");
        }

        private static void SaveCache(
            Dictionary<string, LandPriceAggregate> data, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,
                JsonSerializer.Serialize(data,
                    new JsonSerializerOptions { WriteIndented = false }),
                System.Text.Encoding.UTF8);
            Console.WriteLine($"[CACHE] land_price 저장 완료 ({data.Count}건)");
        }

        private static Dictionary<string, double> LoadLegacyCache(string path)
        {
            string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            return JsonSerializer.Deserialize<Dictionary<string, double>>(json)
                   ?? new Dictionary<string, double>();
        }

        private static bool TryLoadAggregateCache(
            string path, out Dictionary<string, LandPriceAggregate> data)
        {
            data = new Dictionary<string, LandPriceAggregate>();
            try
            {
                string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                var candidate =
                    JsonSerializer.Deserialize<Dictionary<string, LandPriceAggregate>>(json);
                if (candidate == null || candidate.Count == 0 || candidate.Any(pair =>
                        DistrictCode.CanonicalizeLegal(pair.Key).Length == 0 ||
                        pair.Value is null ||
                        !double.IsFinite(pair.Value.Mean) || pair.Value.Mean <= 0 ||
                        pair.Value.SampleCount <= 0))
                    return false;
                data = candidate;
                return true;
            }
            catch (JsonException) { return false; }
            catch (IOException) { return false; }
        }
    }
}
