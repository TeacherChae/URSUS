using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace URSUS.Parsers
{
    /// <summary>
    /// 공공데이터포털(data.go.kr) 표준지공시지가 API → 법정동별 평균 공시지가.
    ///
    /// - 시군구 단위(5자리)로 일괄 조회하여 API 호출 횟수를 최소화
    /// - 법정동코드(10자리) 기준으로 평균 집계
    /// - 캐시 적용 (TTL 30일)
    ///
    /// API 발급: https://www.data.go.kr/data/15058747/openapi.do (무료, 즉시 발급)
    /// </summary>
    public class LandPriceApiParser
    {
        private const int    CACHE_TTL_DAYS = 30;
        private const string BASE_URL =
            "http://apis.data.go.kr/1611000/nsdi/ReferLandPriceService/attr/getReferLandPriceAttr";

        /// <summary>공시지가 값 필드명 (원/㎡)</summary>
        private const string PRICE_FIELD = "pblntfPclnd";

        /// <summary>법정동코드 필드명</summary>
        private const string LD_CODE_FIELD = "ldCode";

        private readonly string     _apiKey;
        private readonly HttpClient _http;

        public LandPriceApiParser(string apiKey)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _http   = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        // ─────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 법정동 코드 목록에 대한 평균 공시지가를 반환한다 (캐시 적용).
        /// key = 법정동코드(10자리), value = 평균 공시지가 (원/㎡)
        /// </summary>
        /// <param name="legalDistrictCodes">법정동 코드 목록 (VWorld에서 수집한 emd_cd)</param>
        /// <param name="cacheDir">캐시 디렉터리 경로 (null이면 캐시 비활성)</param>
        public Dictionary<string, double> GetLandPriceByLegalDistrict(
            List<string> legalDistrictCodes, string? cacheDir = null)
        {
            string? cachePath = cacheDir != null
                ? Path.Combine(cacheDir, "land_price.json")
                : null;

            if (cachePath != null && IsCacheValid(cachePath))
            {
                double remaining = CACHE_TTL_DAYS
                    - (DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath)).TotalDays;
                Console.WriteLine(
                    $"[CACHE] land_price 캐시 사용 (만료까지 {remaining:F1}일)");
                return FilterByRequestedCodes(LoadCache(cachePath), legalDistrictCodes);
            }

            Console.WriteLine("[CACHE] land_price API 호출 중...");
            var result = FetchLandPrices(legalDistrictCodes);

            if (cachePath != null && result.Count > 0)
                SaveCache(result, cachePath);

            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Fetch — 시군구 단위 일괄 조회
        // ─────────────────────────────────────────────────────────────────

        private Dictionary<string, double> FetchLandPrices(List<string> codes)
        {
            // 기준연도: 공시지가는 매년 1월 1일 기준으로 공시 → 전년도 사용
            int currentYear = DateTime.Now.Year;
            string stdrYear = (currentYear - 1).ToString();

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
                try
                {
                    var records = FetchForSig(sigCode, stdrYear);
                    foreach (var (ldCode, price) in records)
                    {
                        // ldCode → 법정동코드 10자리로 정규화
                        string key = NormalizeLdCode(ldCode);
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
                catch (Exception ex)
                {
                    completed++;
                    Console.WriteLine(
                        $"[WARN] 공시지가 조회 실패 (시군구 {sigCode}): {ex.Message}");
                }
            }

            // 평균 계산 + 요청된 코드만 필터
            var codesSet = new HashSet<string>(codes);
            var result = new Dictionary<string, double>();
            foreach (var (key, (sum, cnt)) in acc)
            {
                if (codesSet.Contains(key))
                    result[key] = sum / cnt;
            }

            Console.WriteLine(
                $"[INFO] 공시지가 수집 완료 ({result.Count}/{codes.Count} 법정동)");
            return result;
        }

        /// <summary>
        /// 시군구 코드(5자리) 기준으로 표준지공시지가 전체를 페이지네이션하여 수집.
        /// </summary>
        private List<(string ldCode, double price)> FetchForSig(
            string sigCode, string stdrYear)
        {
            var results = new List<(string, double)>();
            int pageNo = 1;
            const int numOfRows = 1000;
            const int maxPages  = 50; // 안전 제한

            while (pageNo <= maxPages)
            {
                string url = BuildUrl(sigCode, stdrYear, pageNo, numOfRows);
                string body = _http.GetStringAsync(url).GetAwaiter().GetResult();

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // 응답 구조: { "referLandPrices": { "referLandPrice": [...] } }
                if (!root.TryGetProperty("referLandPrices", out var container))
                    break;

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

                // referLandPrice 배열이 없으면 데이터 없음
                if (!container.TryGetProperty("referLandPrice", out var items))
                    break;

                int count = 0;
                foreach (var item in items.EnumerateArray())
                {
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

                // 다음 페이지 여부 확인
                if (count < numOfRows) break;
                pageNo++;
            }

            return results;
        }

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
        /// 법정동코드를 10자리로 정규화.
        /// API 응답의 ldCode는 PNU(19자리)일 수 있으므로 앞 10자리만 사용.
        /// </summary>
        private static string NormalizeLdCode(string ldCode)
        {
            if (string.IsNullOrEmpty(ldCode)) return "";
            // 10자리 이상이면 앞 10자리 = 법정동코드
            if (ldCode.Length >= 10) return ldCode.Substring(0, 10);
            // 10자리 미만이면 오른쪽 0 패딩
            return ldCode.PadRight(10, '0');
        }

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
            var codesSet = new HashSet<string>(codes);
            return cached
                .Where(kv => codesSet.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
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
            Console.WriteLine($"[CACHE] land_price 저장 완료 ({data.Count}건)");
        }

        private static Dictionary<string, double> LoadCache(string path)
        {
            string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            return JsonSerializer.Deserialize<Dictionary<string, double>>(json)
                   ?? new Dictionary<string, double>();
        }
    }
}
