using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;

namespace URSUS.Parsers
{
    /// <summary>
    /// 서울 열린데이터 광장 XML API → 레코드 목록.
    /// data_seoul_api_parser.py (DataSeoulOpenAPIParser) 포팅.
    ///
    /// - 자동 페이지네이션 (list_total_count 기반)
    /// - avg_income.json 캐시 (TTL 30일)
    /// </summary>
    public class DataSeoulApiParser
    {
        private const int    CACHE_TTL_DAYS = 30;
        private const string BASE_URL       = "http://openapi.seoul.go.kr:8088";
        private const string SERVICE_NAME   = "VwsmAdstrdNcmCnsmpW";

        private readonly string     _apiKey;
        private readonly HttpClient _http;

        public DataSeoulApiParser(string apiKey)
        {
            _apiKey = apiKey;
            _http   = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        // ─────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 행정동 기준 월 평균 소득 맵을 반환한다 (캐시 적용).
        /// key = adstrd_cd (행정동코드 끝2자리 제거된 값), value = mt_avrg_income_amt
        /// </summary>
        public Dictionary<string, double> GetAvgIncomeByAdstrd(string? cacheDir = null)
        {
            string? cachePath = cacheDir != null
                ? Path.Combine(cacheDir, "avg_income.json")
                : null;

            if (cachePath != null && IsCacheValid(cachePath))
            {
                double remaining = CACHE_TTL_DAYS
                    - (DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath)).TotalDays;
                Console.WriteLine($"[CACHE] avg_income 캐시 사용 (만료까지 {remaining:F1}일)");
                return LoadCache(cachePath);
            }

            Console.WriteLine("[CACHE] avg_income API 호출 중...");
            var records = FetchAllRecords(SERVICE_NAME);
            var grouped = AggregateByAdstrd(records);

            if (cachePath != null)
                SaveCache(grouped, cachePath);

            return grouped;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Fetch + Pagination
        // ─────────────────────────────────────────────────────────────────

        private List<Dictionary<string, string>> FetchAllRecords(string serviceName)
        {
            const int pageSize = 1000;

            // 1페이지로 total_count 파악
            string firstUrl  = BuildUrl(serviceName, 1, pageSize);
            var    firstRoot = FetchXml(firstUrl);
            int?   total     = GetListTotalCount(firstRoot);
            var    all       = XmlToRecords(firstRoot);

            if (total.HasValue)
                Console.WriteLine($"[INFO] list_total_count = {total.Value}");

            int fetched   = all.Count;
            int nextStart = pageSize + 1;
            int target    = total ?? int.MaxValue;

            while (fetched < target)
            {
                int nextEnd = nextStart + pageSize - 1;
                string url  = BuildUrl(serviceName, nextStart, nextEnd);
                var    root = FetchXml(url);
                var    batch = XmlToRecords(root);

                if (batch.Count == 0) break;
                all.AddRange(batch);
                fetched   += batch.Count;
                nextStart  = nextEnd + 1;
            }

            return all;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Aggregate: 행정동 코드 기준 평균 소득 계산
        // ─────────────────────────────────────────────────────────────────

        private static Dictionary<string, double> AggregateByAdstrd(
            List<Dictionary<string, string>> records)
        {
            // adstrd_cd → (sum, count)
            var acc = new Dictionary<string, (double sum, int count)>();

            foreach (var row in records)
            {
                if (!row.TryGetValue("ADSTRD_CD", out string? cd)
                    || !row.TryGetValue("MT_AVRG_INCOME_AMT", out string? amtStr))
                    continue;

                if (!double.TryParse(amtStr, out double amt)) continue;

                if (acc.TryGetValue(cd, out var prev))
                    acc[cd] = (prev.sum + amt, prev.count + 1);
                else
                    acc[cd] = (amt, 1);
            }

            var result = new Dictionary<string, double>();
            foreach (var (k, (sum, cnt)) in acc)
                result[k] = sum / cnt;
            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        //  XML helpers
        // ─────────────────────────────────────────────────────────────────

        private XElement FetchXml(string url)
        {
            string body = _http.GetStringAsync(url).GetAwaiter().GetResult();
            var root = XElement.Parse(body);

            // API 에러 코드 확인
            var result = root.Descendants("RESULT").FirstOrDefault() ??
                         root.Descendants("result").FirstOrDefault();
            if (result != null)
            {
                string code = result.Element("CODE")?.Value.Trim()
                           ?? result.Element("code")?.Value.Trim() ?? "";
                string msg  = result.Element("MESSAGE")?.Value.Trim()
                           ?? result.Element("message")?.Value.Trim() ?? "";

                if (!string.IsNullOrEmpty(code)
                    && code != "INFO-000"
                    && !msg.Contains("정상"))
                    throw new InvalidOperationException($"API 오류: {code} - {msg}");
            }

            return root;
        }

        private static List<Dictionary<string, string>> XmlToRecords(XElement root)
        {
            var records = new List<Dictionary<string, string>>();
            foreach (var row in root.Descendants("row"))
            {
                var rec = new Dictionary<string, string>();
                foreach (var child in row.Elements())
                    rec[child.Name.LocalName] = child.Value.Trim();
                records.Add(rec);
            }
            return records;
        }

        private static int? GetListTotalCount(XElement root)
        {
            var node = root.Descendants("list_total_count").FirstOrDefault()
                    ?? root.Descendants("LIST_TOTAL_COUNT").FirstOrDefault();
            if (node == null || string.IsNullOrWhiteSpace(node.Value)) return null;
            return int.TryParse(node.Value.Trim(), out int n) ? n : null;
        }

        private string BuildUrl(string serviceName, int start, int end)
            => $"{BASE_URL}/{_apiKey}/xml/{serviceName}/{start}/{end}/";

        // ─────────────────────────────────────────────────────────────────
        //  Cache
        // ─────────────────────────────────────────────────────────────────

        private static bool IsCacheValid(string path)
        {
            if (!File.Exists(path)) return false;
            return (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalDays < CACHE_TTL_DAYS;
        }

        private static void SaveCache(Dictionary<string, double> data, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,
                JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false }),
                System.Text.Encoding.UTF8);
            Console.WriteLine($"[CACHE] avg_income 저장 완료 ({data.Count}건)");
        }

        private static Dictionary<string, double> LoadCache(string path)
        {
            string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            return JsonSerializer.Deserialize<Dictionary<string, double>>(json)
                   ?? new Dictionary<string, double>();
        }
    }
}
