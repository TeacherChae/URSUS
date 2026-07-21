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
using URSUS.Analysis;

namespace URSUS.Parsers
{
    /// <summary>
    /// 공공데이터포털(data.go.kr) 토지이용규제정보서비스 → 법정동별 용도지역 개발밀도 점수.
    ///
    /// - 국토교통부 토지이용규제정보서비스 API를 활용
    /// - 각 필지의 용도지역 유형을 수치 점수(1~5)로 변환하여 법정동별 평균 산출
    /// - 점수가 높을수록 개발밀도/상업화 정도가 높음
    ///
    /// 점수 기준:
    ///   5: 중심상업지역 / 일반상업지역
    ///   4: 근린상업지역 / 유통상업지역
    ///   3.5: 준주거지역
    ///   3: 제2종/제3종 일반주거지역
    ///   2.5: 제1종 일반주거지역 / 준공업지역
    ///   2: 전용주거지역 / 일반공업지역
    ///   1.5: 전용공업지역
    ///   1: 녹지지역 (보전/생산/자연)
    ///
    /// API 발급: https://www.data.go.kr/data/15056930/openapi.do (무료, 즉시 발급)
    /// </summary>
    public class ZoningApiParser
    {
        private const int    CACHE_TTL_DAYS = 30;
        private const string BASE_URL =
            "https://apis.data.go.kr/1611000/nsdi/LandUseService/attr/getLandUseAttr";

        /// <summary>용도지역명 필드</summary>
        private const string ZONE_NAME_FIELD = "prposAreaDstrcCodeNm";

        /// <summary>법정동코드 필드</summary>
        private const string LD_CODE_FIELD = "ldCode";

        private readonly string     _apiKey;
        private readonly HttpPipeline _http;

        public ZoningApiParser(string apiKey)
            : this(apiKey, null) { }

        public ZoningApiParser(string apiKey, HttpPipeline? http)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _http   = http ?? new HttpPipeline(HttpClientLifetime.Shared, maxConcurrency: 8);
        }

        // ─────────────────────────────────────────────────────────────────
        //  용도지역 유형 → 개발밀도 점수 매핑
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 용도지역명에서 개발밀도 점수를 반환한다.
        /// 높을수록 도심/상업 성격이 강함.
        /// </summary>
        internal static double GetZoningScore(string zoneName)
        {
            if (string.IsNullOrEmpty(zoneName)) return 0;

            // 상업지역
            if (zoneName.Contains("중심상업")) return 5.0;
            if (zoneName.Contains("일반상업")) return 5.0;
            if (zoneName.Contains("근린상업")) return 4.0;
            if (zoneName.Contains("유통상업")) return 4.0;

            // 주거지역
            if (zoneName.Contains("준주거"))   return 3.5;
            if (zoneName.Contains("제3종") && zoneName.Contains("일반주거")) return 3.0;
            if (zoneName.Contains("제2종") && zoneName.Contains("일반주거")) return 3.0;
            if (zoneName.Contains("제1종") && zoneName.Contains("일반주거")) return 2.5;
            if (zoneName.Contains("일반주거")) return 3.0;  // fallback
            if (zoneName.Contains("전용주거")) return 2.0;

            // 공업지역
            if (zoneName.Contains("준공업"))   return 2.5;
            if (zoneName.Contains("일반공업")) return 2.0;
            if (zoneName.Contains("전용공업")) return 1.5;

            // 녹지지역
            if (zoneName.Contains("녹지"))     return 1.0;
            if (zoneName.Contains("보전"))     return 1.0;

            // 관리지역
            if (zoneName.Contains("계획관리")) return 2.0;
            if (zoneName.Contains("생산관리")) return 1.5;
            if (zoneName.Contains("보전관리")) return 1.0;
            if (zoneName.Contains("관리"))     return 1.5;

            // 농림/자연환경보전
            if (zoneName.Contains("농림"))     return 1.0;
            if (zoneName.Contains("자연환경")) return 1.0;

            // 알 수 없는 유형은 임의 숫자화하지 않고 결측 처리
            return 0.0;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 법정동 코드 목록에 대한 평균 용도지역 개발밀도 점수를 반환한다 (캐시 적용).
        /// key = canonical 법정동 ID(8자리), value = 평균 개발밀도 점수 (1~5)
        /// </summary>
        /// <param name="legalDistrictCodes">법정동 코드 목록</param>
        /// <param name="cacheDir">캐시 디렉터리 경로 (null이면 캐시 비활성)</param>
        public Dictionary<string, double> GetZoningScoreByDistrict(
            List<string> legalDistrictCodes, string? cacheDir = null)
            => GetZoningScoreByDistrictAsync(legalDistrictCodes, cacheDir)
                .GetAwaiter().GetResult();

        public async Task<Dictionary<string, double>> GetZoningScoreByDistrictAsync(
            List<string> legalDistrictCodes, string? cacheDir = null,
            CancellationToken cancellationToken = default)
        {
            string? cachePath = cacheDir != null
                ? Path.Combine(cacheDir, "zoning_score.json")
                : null;

            if (cachePath != null && IsCacheValid(cachePath))
            {
                double remaining = CACHE_TTL_DAYS
                    - (DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath)).TotalDays;
                Console.WriteLine(
                    $"[CACHE] zoning_score 캐시 사용 (만료까지 {remaining:F1}일)");
                return FilterByRequestedCodes(LoadCache(cachePath), legalDistrictCodes);
            }

            Console.WriteLine("[CACHE] zoning_score API 호출 중...");
            var result = await FetchZoningScoresAsync(legalDistrictCodes, cancellationToken)
                .ConfigureAwait(false);

            if (cachePath != null && result.Count > 0)
                SaveCache(result, cachePath);

            return result;
        }

        public async Task<Dictionary<string, ZoningCategoryHistogram>> GetZoningHistogramByDistrictAsync(
            List<string> legalDistrictCodes, CancellationToken cancellationToken = default)
        {
            var requested = legalDistrictCodes.Select(DistrictCode.CanonicalizeLegal)
                .Where(code => code.Length > 0).ToHashSet(StringComparer.Ordinal);
            var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            foreach (string sigCode in requested.Select(code => code[..5]).Distinct(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var records = await FetchCategoriesForSigAsync(sigCode, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var (rawCode, category) in records)
                {
                    string code = DistrictCode.CanonicalizeLegal(rawCode);
                    if (!requested.Contains(code) || string.IsNullOrWhiteSpace(category)) continue;
                    if (!counts.TryGetValue(code, out var histogram))
                        counts[code] = histogram = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    histogram[category] = histogram.GetValueOrDefault(category) + 1;
                }
            }
            return counts.ToDictionary(pair => pair.Key,
                pair => new ZoningCategoryHistogram(pair.Value), StringComparer.Ordinal);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Fetch — 시군구 단위 일괄 조회
        // ─────────────────────────────────────────────────────────────────

        private async Task<Dictionary<string, double>> FetchZoningScoresAsync(
            List<string> codes, CancellationToken cancellationToken)
        {
            // 시군구(5자리) 단위로 그룹핑하여 API 호출 횟수 최소화
            var sigCodes = codes
                .Where(c => c.Length >= 5)
                .Select(c => c.Substring(0, 5))
                .Distinct()
                .ToList();

            // 법정동별 점수 누적
            var acc = new Dictionary<string, (double sum, int count)>();

            int completed = 0;
            foreach (string sigCode in sigCodes)
            {
                var records = await FetchForSigAsync(sigCode, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var (ldCode, score) in records)
                {
                    string key = DistrictCode.CanonicalizeLegal(ldCode);
                    if (string.IsNullOrEmpty(key)) continue;

                    if (acc.TryGetValue(key, out var prev))
                        acc[key] = (prev.sum + score, prev.count + 1);
                    else
                        acc[key] = (score, 1);
                }
                completed++;
                Console.WriteLine(
                    $"[INFO] 용도지역 시군구 {completed}/{sigCodes.Count} 완료 ({sigCode})");
            }

            // 평균 계산 + 요청된 코드만 필터
            var codesSet = new HashSet<string>(
                codes.Select(DistrictCode.CanonicalizeLegal)
                    .Where(code => !string.IsNullOrEmpty(code)));
            var result = new Dictionary<string, double>();
            foreach (var (key, (sum, cnt)) in acc)
            {
                if (codesSet.Contains(key))
                    result[key] = Math.Round(sum / cnt, 2);
            }

            Console.WriteLine(
                $"[INFO] 용도지역 수집 완료 ({result.Count}/{codes.Count} 법정동)");
            return result;
        }

        /// <summary>
        /// 시군구 코드(5자리) 기준으로 토지이용규제정보를 페이지네이션하여 수집.
        /// </summary>
        private async Task<List<(string ldCode, double score)>> FetchForSigAsync(
            string sigCode, CancellationToken cancellationToken)
        {
            var categories = await FetchCategoriesForSigAsync(sigCode, cancellationToken)
                .ConfigureAwait(false);
            return categories.Select(row => (row.ldCode, score: GetZoningScore(row.category)))
                .Where(row => row.score > 0).ToList();
        }

        private async Task<List<(string ldCode, string category)>> FetchCategoriesForSigAsync(
            string sigCode, CancellationToken cancellationToken)
        {
            var results = new List<(string, string)>();
            const int numOfRows = 1000;
            const int maxPages = 30;
            int? expectedTotal = null;
            int received = 0;
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (int pageNo = 1; pageNo <= maxPages; pageNo++)
            {
                string body = await _http.GetStringAsync(
                    new Uri(BuildUrl(sigCode, pageNo, numOfRows)), cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("landUses", out var container))
                    throw new InvalidOperationException("용도지역 API 응답에 landUses가 없습니다.");
                if (container.TryGetProperty("resultCode", out var codeElem) &&
                    (codeElem.GetString() ?? "") != "000")
                    throw new InvalidOperationException(
                        $"용도지역 API 오류: {codeElem} - " +
                        (container.TryGetProperty("resultMsg", out var msg) ? msg.ToString() : "Unknown error"));
                int pageTotal = ReadRequiredTotal(container);
                if (expectedTotal is null) expectedTotal = pageTotal;
                else if (expectedTotal.Value != pageTotal)
                    throw new InvalidOperationException("용도지역 API totalCount가 페이지 사이에 변경되었습니다.");
                if (!container.TryGetProperty("landUse", out var items))
                {
                    if (pageTotal == 0) break;
                    throw new InvalidOperationException("용도지역 API 행이 totalCount보다 먼저 종료되었습니다.");
                }
                int count = 0;
                foreach (var item in items.EnumerateArray())
                {
                    if (!identities.Add(HashIdentity(item.GetRawText())))
                        throw new InvalidOperationException("용도지역 pagination duplicate row가 감지되었습니다.");
                    string code = GetStringProp(item, LD_CODE_FIELD);
                    string category = GetStringProp(item, ZONE_NAME_FIELD);
                    if (code.Length > 0 && category.Length > 0) results.Add((code, category));
                    count++;
                }
                received += count;
                if (received > pageTotal)
                    throw new InvalidOperationException("용도지역 수신 행 수가 totalCount를 초과했습니다.");
                if (received == pageTotal) break;
                if (count < numOfRows)
                    throw new InvalidOperationException(
                        $"용도지역 pagination이 조기 종료되었습니다 ({received}/{pageTotal}).");
                if (pageNo == maxPages)
                    throw new InvalidOperationException(
                        $"용도지역 pagination 안전 상한을 초과했습니다 ({received}/{pageTotal}).");
            }
            if (expectedTotal is null || received != expectedTotal.Value)
                throw new InvalidOperationException(
                    $"용도지역 pagination이 완결되지 않았습니다 ({received}/{expectedTotal?.ToString() ?? "?"}).");
            return results;
        }

        private static int ReadRequiredTotal(JsonElement container)
        {
            if (!container.TryGetProperty("totalCount", out var total) ||
                !int.TryParse(total.ToString(), out int value) || value < 0)
                throw new InvalidOperationException("용도지역 API totalCount가 없거나 유효하지 않습니다.");
            return value;
        }

        private static string HashIdentity(string value)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        private string BuildUrl(string ldCode, int pageNo, int numOfRows)
        {
            return $"{BASE_URL}" +
                $"?serviceKey={Uri.EscapeDataString(_apiKey)}" +
                $"&ldCode={ldCode}" +
                $"&format=json" +
                $"&numOfRows={numOfRows}" +
                $"&pageNo={pageNo}";
        }

        // ─────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────

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

        // ─────────────────────────────────────────────────────────────────
        //  Cache
        // ─────────────────────────────────────────────────────────────────

        private static bool IsCacheValid(string path)
        {
            if (!File.Exists(path)) return false;
            return (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalDays
                   < CACHE_TTL_DAYS;
        }

        private static void SaveCache(Dictionary<string, double> data, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,
                JsonSerializer.Serialize(data,
                    new JsonSerializerOptions { WriteIndented = false }),
                System.Text.Encoding.UTF8);
            Console.WriteLine($"[CACHE] zoning_score 저장 완료 ({data.Count}건)");
        }

        private static Dictionary<string, double> LoadCache(string path)
        {
            string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            return JsonSerializer.Deserialize<Dictionary<string, double>>(json)
                   ?? new Dictionary<string, double>();
        }
    }
}
