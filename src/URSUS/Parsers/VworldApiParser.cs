using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rhino.Geometry;
using URSUS.Utils;

namespace URSUS.Parsers
{
    /// <summary>
    /// VWorld WFS API → 법정동 경계 레코드 목록.
    /// vworld_api_parser.py (VworldOpenAPIParser) 포팅.
    ///
    /// - GeoJSON 캐시 (TTL 30일)
    /// - GPS → UTM 변환 (GpsToUtm)
    /// - Rhino Geometry (PolylineCurve, Point3d, AreaMassProperties) 내부 생성
    /// </summary>
    public class VworldApiParser
    {
        private const int    CACHE_TTL_DAYS = 30;
        private const string WFS_URL        = "https://api.vworld.kr/req/wfs";
        private const string GEOCODER_URL   = "https://api.vworld.kr/req/address";

        private readonly string      _apiKey;
        private readonly string?     _cacheDir;
        private readonly HttpClient  _http;

        public VworldApiParser(string apiKey, string? cacheDir = null)
        {
            _apiKey   = apiKey;
            _cacheDir = cacheDir;
            _http     = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        // ─────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 두 주소의 BBOX로 법정동 경계를 수집한다 (캐시 적용).
        /// </summary>
        public List<LegalDistrictRecord> GetLegalDistricts(string address1, string address2)
        {
            string? cachePath = _cacheDir != null
                ? Path.Combine(_cacheDir, "legald_boundaries.json")
                : null;

            if (cachePath != null && IsCacheValid(cachePath))
            {
                double remaining = CACHE_TTL_DAYS
                    - (DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                       - new DateTimeOffset(File.GetLastWriteTimeUtc(cachePath)).ToUnixTimeSeconds())
                    / 86400.0;
                Console.WriteLine($"[CACHE] legald_boundaries 캐시 사용 (만료까지 {remaining:F1}일)");
                return LoadCache(cachePath);
            }

            Console.WriteLine("[CACHE] legald_boundaries API 호출 중...");
            var records = FetchLegalDistricts(address1, address2);

            if (cachePath != null)
                SaveCache(records, cachePath);

            return records;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Fetch
        // ─────────────────────────────────────────────────────────────────

        private List<LegalDistrictRecord> FetchLegalDistricts(string address1, string address2)
        {
            var allFeatures = FetchAllFeatures(address1, address2);
            var result = new List<LegalDistrictRecord>();

            foreach (var feature in allFeatures)
            {
                string? fullNm  = feature["properties"]?["full_nm"]?.GetValue<string>();
                string? emdCd   = feature["properties"]?["emd_cd"]?.GetValue<string>();
                if (fullNm == null || emdCd == null) continue;
                if (!fullNm.Contains("서울")) continue;

                var coordsNode = feature["geometry"]?["coordinates"]?[0]?[0];
                if (coordsNode == null) continue;

                var points = new List<Point3d>();
                foreach (var coordNode in coordsNode.AsArray())
                {
                    double lon = coordNode![0]!.GetValue<double>();
                    double lat = coordNode![1]!.GetValue<double>();
                    var (east, north) = GpsToUtm.LLtoUTM(lat, lon);
                    points.Add(new Point3d(east, north, 0));
                }

                if (points.Count < 3) continue;

                try
                {
                    var polyline = new Polyline(points);
                    if (!polyline.IsClosed)
                        polyline.Add(polyline[0]);

                    var curve = polyline.ToPolylineCurve();
                    var amp   = AreaMassProperties.Compute(curve);
                    if (amp == null) continue;

                    result.Add(new LegalDistrictRecord(
                        LegaldCd: emdCd,
                        Name:     fullNm,
                        Geometry: curve,
                        Area:     amp.Area,
                        Centroid: amp.Centroid));
                }
                catch
                {
                    // 좌표 변환 실패 시 스킵
                }
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Pagination
        // ─────────────────────────────────────────────────────────────────

        private List<JsonObject> FetchAllFeatures(string address1, string address2)
        {
            var (xmin, ymin) = AddressToCoord(address1);
            var (xmax, ymax) = AddressToCoord(address2);

            var all = new List<JsonObject>();
            int start = 0;
            const int batchSize = 1000;

            while (true)
            {
                var batch = FetchWfsBatch(start, batchSize, ymin, xmin, ymax, xmax);
                if (batch.Count == 0) break;
                all.AddRange(batch);
                if (batch.Count < batchSize) break;
                start += batchSize;
            }

            return all;
        }

        private List<JsonObject> FetchWfsBatch(
            int start, int count, double ymin, double xmin, double ymax, double xmax)
        {
            string url = $"{WFS_URL}" +
                $"?SERVICE=WFS&REQUEST=GetFeature&TYPENAME=lt_c_ademd_info" +
                $"&BBOX={xmin},{ymin},{xmax},{ymax}" +
                $"&VERSION=2.0.0&COUNT={count}&STARTINDEX={start}" +
                $"&SRSNAME=EPSG:4326&OUTPUT=application/json" +
                $"&EXCEPTIONS=text/xml&KEY={_apiKey}";

            var root = FetchJson(url);
            var features = root["features"]?.AsArray();
            if (features == null) return new List<JsonObject>();

            var result = new List<JsonObject>();
            foreach (var f in features)
                if (f is JsonObject obj) result.Add(obj);
            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Geocoder
        // ─────────────────────────────────────────────────────────────────

        private (double x, double y) AddressToCoord(string address)
        {
            string url = $"{GEOCODER_URL}" +
                $"?service=address&request=getcoord&crs=EPSG:4326" +
                $"&address={Uri.EscapeDataString(address)}" +
                $"&format=json&type=road&key={_apiKey}";

            var root = FetchJson(url);
            var point = root["response"]!["result"]!["point"]!;
            double x = double.Parse(point["x"]!.ToString(), System.Globalization.CultureInfo.InvariantCulture);
            double y = double.Parse(point["y"]!.ToString(), System.Globalization.CultureInfo.InvariantCulture);
            return (x, y);
        }

        // ─────────────────────────────────────────────────────────────────
        //  HTTP
        // ─────────────────────────────────────────────────────────────────

        private JsonObject FetchJson(string url)
        {
            string body = _http.GetStringAsync(url).GetAwaiter().GetResult();
            var node = JsonNode.Parse(body)
                ?? throw new InvalidOperationException("Invalid JSON response");
            return node.AsObject();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Cache (GeoJSON-compatible flat JSON)
        // ─────────────────────────────────────────────────────────────────

        private static bool IsCacheValid(string path)
        {
            if (!File.Exists(path)) return false;
            double ageDays = (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalDays;
            return ageDays < CACHE_TTL_DAYS;
        }

        private static void SaveCache(List<LegalDistrictRecord> records, string path)
        {
            var list = new List<object>();
            foreach (var r in records)
            {
                Polyline pl;
                if (r.Geometry is PolylineCurve plc)
                    pl = plc.ToPolyline();
                else
                    r.Geometry.TryGetPolyline(out pl);
                var coords = new List<double[]>();
                foreach (Point3d pt in pl)
                    coords.Add(new[] { pt.X, pt.Y });

                list.Add(new
                {
                    legald_cd = r.LegaldCd,
                    name      = r.Name,
                    area      = r.Area,
                    centroid  = new[] { r.Centroid.X, r.Centroid.Y },
                    coords
                });
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,
                JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = false }),
                System.Text.Encoding.UTF8);
            Console.WriteLine($"[CACHE] legald_boundaries 저장 완료 ({records.Count}건)");
        }

        private static List<LegalDistrictRecord> LoadCache(string path)
        {
            string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);

            var result = new List<LegalDistrictRecord>();
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                string legaldCd = elem.GetProperty("legald_cd").GetString()!;
                string name     = elem.GetProperty("name").GetString()!;
                double area     = elem.GetProperty("area").GetDouble();
                var    cen      = elem.GetProperty("centroid");
                var    centroid = new Point3d(cen[0].GetDouble(), cen[1].GetDouble(), 0);

                var points = new List<Point3d>();
                foreach (var c in elem.GetProperty("coords").EnumerateArray())
                    points.Add(new Point3d(c[0].GetDouble(), c[1].GetDouble(), 0));

                if (points.Count < 3) continue;

                var polyline = new Polyline(points);
                if (!polyline.IsClosed) polyline.Add(polyline[0]);
                var curve = polyline.ToPolylineCurve();

                result.Add(new LegalDistrictRecord(legaldCd, name, curve, area, centroid));
            }

            return result;
        }
    }

    /// <summary>법정동 경계 레코드 (VworldApiParser 출력 단위)</summary>
    public record LegalDistrictRecord(
        string       LegaldCd,
        string       Name,
        PolylineCurve Geometry,
        double       Area,
        Point3d      Centroid);
}
