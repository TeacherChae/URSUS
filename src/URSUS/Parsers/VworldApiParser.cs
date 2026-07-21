using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rhino.Geometry;
using URSUS.Utils;
using URSUS.Geometry;
using URSUS.Net;
using URSUS.DataSources;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace URSUS.Parsers
{
    /// <summary>
    /// VWorld WFS API → 법정동 경계 레코드 목록.
    /// vworld_api_parser.py (VworldOpenAPIParser) 포팅.
    ///
    /// - 전국 어디든 주소 기반 법정동 경계 조회 (서울 한정 아님)
    /// - 단일 주소 + 반경(km) 또는 두 주소 BBOX 모드 지원
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
        private readonly HttpPipeline _http;

        public VworldApiParser(string apiKey, string? cacheDir = null)
            : this(apiKey, cacheDir, null) { }

        public VworldApiParser(string apiKey, string? cacheDir, HttpPipeline? http)
        {
            _apiKey   = apiKey;
            _cacheDir = cacheDir;
            _http     = http ?? new HttpPipeline(HttpClientLifetime.Shared, maxConcurrency: 8);
        }

        /// <summary>기본 반경 (km) — 단일 주소 모드에서 BBOX 자동 생성 시 사용</summary>
        private const double DEFAULT_RADIUS_KM = 5.0;

        // ─────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 단일 주소 + 반경으로 법정동 경계를 수집한다 (캐시 적용).
        /// 전국 어디든 주소를 입력하면 해당 위치를 중심으로
        /// 반경 내 법정동 경계를 자동으로 조회한다.
        /// </summary>
        /// <param name="centerAddress">중심 주소 (전국 가능, 예: "부산 해운대구 우동")</param>
        /// <param name="radiusKm">검색 반경 (km). 기본값 5km.</param>
        public List<LegalDistrictRecord> GetLegalDistricts(
            string centerAddress, double radiusKm = DEFAULT_RADIUS_KM)
            => GetLegalDistrictsAsync(centerAddress, radiusKm).GetAwaiter().GetResult();

        public async Task<List<LegalDistrictRecord>> GetLegalDistrictsAsync(
            string centerAddress, double radiusKm = DEFAULT_RADIUS_KM,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(centerAddress))
                throw new ArgumentException(
                    "주소를 입력해주세요. 전국 어디든 주소 기반으로 검색할 수 있습니다.",
                    nameof(centerAddress));

            if (!double.IsFinite(radiusKm) || radiusKm <= 0 || radiusKm > 100)
                throw new ArgumentOutOfRangeException(nameof(radiusKm), "반경은 0 초과 100km 이하여야 합니다.");
            var (cx, cy) = await AddressToCoordAsync(centerAddress, cancellationToken).ConfigureAwait(false);

            // 위도 1도 ≈ 111km, 경도 1도 ≈ 111km × cos(lat)
            double latDelta = radiusKm / 111.0;
            double lonDelta = radiusKm / (111.0 * Math.Cos(cy * Math.PI / 180.0));

            double xmin = cx - lonDelta;
            double ymin = cy - latDelta;
            double xmax = cx + lonDelta;
            double ymax = cy + latDelta;

            string cacheKey = BuildCacheKey(
                $"{cx.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                cy.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                $"r{radiusKm.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}km");
            return await GetLegalDistrictsFromBBoxAsync(cacheKey, ymin, xmin, ymax, xmax,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 두 주소의 BBOX로 법정동 경계를 수집한다 (캐시 적용).
        /// 전국 어디든 두 주소를 지정하면 해당 영역 내
        /// 법정동 경계를 조회한다.
        /// </summary>
        /// <param name="address1">BBOX 좌하단 기준 주소 (전국 가능)</param>
        /// <param name="address2">BBOX 우상단 기준 주소 (전국 가능)</param>
        public List<LegalDistrictRecord> GetLegalDistrictsByBBox(string address1, string address2)
            => GetLegalDistrictsByBBoxAsync(address1, address2).GetAwaiter().GetResult();

        public async Task<List<LegalDistrictRecord>> GetLegalDistrictsByBBoxAsync(
            string address1, string address2, CancellationToken cancellationToken = default)
        {
            var firstTask = AddressToCoordAsync(address1, cancellationToken);
            var secondTask = AddressToCoordAsync(address2, cancellationToken);
            await Task.WhenAll(firstTask, secondTask).ConfigureAwait(false);
            var first = await firstTask.ConfigureAwait(false);
            var second = await secondTask.ConfigureAwait(false);
            double xmin = Math.Min(first.x, second.x);
            double ymin = Math.Min(first.y, second.y);
            double xmax = Math.Max(first.x, second.x);
            double ymax = Math.Max(first.y, second.y);

            string cacheKey = BuildCacheKey(
                $"{xmin.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                ymin.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                $"{xmax.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                ymax.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            return await GetLegalDistrictsFromBBoxAsync(cacheKey, ymin, xmin, ymax, xmax,
                cancellationToken).ConfigureAwait(false);
        }

        public Task<List<LegalDistrictRecord>> GetLegalDistrictsByBoundsAsync(
            SpatialBounds bounds, CancellationToken cancellationToken = default)
        {
            var wgs = bounds.ToWgs84().Normalize();
            string cacheKey = BuildCacheKey(
                $"{wgs.MinX.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                wgs.MinY.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                $"{wgs.MaxX.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                wgs.MaxY.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            return GetLegalDistrictsFromBBoxAsync(cacheKey,
                wgs.MinY, wgs.MinX, wgs.MaxY, wgs.MaxX, cancellationToken);
        }

        /// <summary>
        /// [하위 호환] 두 주소의 BBOX로 법정동 경계를 수집한다.
        /// GetLegalDistrictsByBBox와 동일.
        /// </summary>
        [Obsolete("GetLegalDistrictsByBBox 또는 단일 주소 오버로드를 사용하세요.")]
        public List<LegalDistrictRecord> GetLegalDistricts(string address1, string address2)
        {
            return GetLegalDistrictsByBBox(address1, address2);
        }

        // ─────────────────────────────────────────────────────────────────
        //  BBOX 기반 공통 로직
        // ─────────────────────────────────────────────────────────────────

        private async Task<List<LegalDistrictRecord>> GetLegalDistrictsFromBBoxAsync(
            string cacheKey, double ymin, double xmin, double ymax, double xmax,
            CancellationToken cancellationToken)
        {
            string? cachePath = _cacheDir != null
                ? Path.Combine(_cacheDir, $"legald_boundaries_v2_{cacheKey}.json")
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
            var records = await FetchLegalDistrictsFromBBoxAsync(
                ymin, xmin, ymax, xmax, cancellationToken).ConfigureAwait(false);

            if (cachePath != null)
                SaveCache(records, cachePath);

            return records;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Fetch
        // ─────────────────────────────────────────────────────────────────

        private async Task<List<LegalDistrictRecord>> FetchLegalDistrictsFromBBoxAsync(
            double ymin, double xmin, double ymax, double xmax,
            CancellationToken cancellationToken)
        {
            var result = new List<LegalDistrictRecord>();

            await foreach (var feature in StreamFeaturesAsync(
                ymin, xmin, ymax, xmax, cancellationToken).ConfigureAwait(false))
            {
                string? fullNm  = feature["properties"]?["full_nm"]?.GetValue<string>();
                string? emdCd   = feature["properties"]?["emd_cd"]?.GetValue<string>();
                if (fullNm == null || emdCd == null)
                    throw new BoundaryTopologyParseException(
                        "VWorld feature identity(full_nm/emd_cd)가 없습니다.");

                BoundaryTopology topology;
                IReadOnlyList<string> topologyWarnings;
                try
                {
                    topology = GeoJsonBoundaryParser.Parse(
                        feature["geometry"], out topologyWarnings);
                }
                catch (BoundaryTopologyParseException) { throw; }
                catch (Exception ex)
                {
                    throw new BoundaryTopologyParseException(
                        $"{emdCd} {fullNm}: boundary topology 변환 실패", ex);
                }

                var outer = topology.Parts.OrderByDescending(part =>
                        Math.Abs(part.Outer.SignedArea)).First().Outer;
                var points = outer.Points.Select(point =>
                    new Point3d(point.X, point.Y, 0)).ToList();
                var polyline = new Polyline(points);
                var curve = polyline.ToPolylineCurve();
                result.Add(new LegalDistrictRecord(
                    LegaldCd: emdCd,
                    Name: fullNm,
                    Geometry: curve,
                    Area: topology.Area,
                    Centroid: new Point3d(topology.Centroid.X, topology.Centroid.Y, 0),
                    Topology: topology,
                    Warnings: topologyWarnings.Select(warning =>
                        $"BOUNDARY_TOPOLOGY_PART_DROPPED: {emdCd} {fullNm}: {warning}")
                        .ToArray()));
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Pagination
        // ─────────────────────────────────────────────────────────────────

        private async IAsyncEnumerable<JsonObject> StreamFeaturesAsync(
            double ymin, double xmin, double ymax, double xmax,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var identities = new HashSet<string>(StringComparer.Ordinal);
            int start = 0;
            int received = 0;
            int? expectedTotal = null;
            const int batchSize = 1000;
            const int maxPages = 100;

            for (int page = 0; page < maxPages; page++)
            {
                var batch = await FetchWfsBatchAsync(start, batchSize, ymin, xmin, ymax, xmax,
                    cancellationToken).ConfigureAwait(false);
                if (expectedTotal is null) expectedTotal = batch.NumberMatched;
                else if (expectedTotal.Value != batch.NumberMatched)
                    throw new InvalidOperationException("VWorld numberMatched가 페이지 사이에 변경되었습니다.");
                if (batch.NumberReturned != batch.Features.Count)
                    throw new InvalidOperationException("VWorld numberReturned와 실제 feature 수가 일치하지 않습니다.");
                if (batch.Features.Count == 0)
                {
                    if (received == expectedTotal.Value) break;
                    throw new InvalidOperationException(
                        $"VWorld pagination이 조기 종료되었습니다 ({received}/{expectedTotal}).");
                }
                foreach (var feature in batch.Features)
                {
                    string rawIdentity = $"{feature["properties"]?["emd_cd"]}|" +
                        feature["geometry"]?.ToJsonString();
                    string identity = Convert.ToHexString(
                        SHA256.HashData(Encoding.UTF8.GetBytes(rawIdentity)));
                    if (!identities.Add(identity))
                        throw new InvalidOperationException("VWorld pagination duplicate feature가 감지되었습니다.");
                    yield return feature;
                }
                received += batch.Features.Count;
                if (received > expectedTotal.Value)
                    throw new InvalidOperationException("VWorld 수신 feature 수가 numberMatched를 초과했습니다.");
                if (received == expectedTotal.Value) break;
                if (batch.Features.Count < batchSize)
                    throw new InvalidOperationException(
                        $"VWorld pagination이 조기 종료되었습니다 ({received}/{expectedTotal}).");
                start += batch.Features.Count;
                if (page == maxPages - 1)
                    throw new InvalidOperationException("VWorld pagination 안전 상한을 초과했습니다.");
            }

            if (expectedTotal is null || received != expectedTotal.Value)
                throw new InvalidOperationException(
                    $"VWorld pagination이 완결되지 않았습니다 ({received}/{expectedTotal?.ToString() ?? "?"}).");

        }

        private sealed record WfsBatch(List<JsonObject> Features, int NumberMatched, int NumberReturned);

        private async Task<WfsBatch> FetchWfsBatchAsync(
            int start, int count, double ymin, double xmin, double ymax, double xmax,
            CancellationToken cancellationToken)
        {
            string url = $"{WFS_URL}" +
                $"?SERVICE=WFS&REQUEST=GetFeature&TYPENAME=lt_c_ademd_info" +
                $"&BBOX={xmin},{ymin},{xmax},{ymax}" +
                $"&VERSION=2.0.0&COUNT={count}&STARTINDEX={start}" +
                $"&SRSNAME=EPSG:4326&OUTPUT=application/json" +
                $"&EXCEPTIONS=text/xml&KEY={_apiKey}";

            var root = await FetchJsonAsync(url, cancellationToken).ConfigureAwait(false);
            var features = root["features"]?.AsArray();
            if (features == null)
                throw new InvalidOperationException("VWorld WFS features 배열이 없습니다.");
            int matched = ReadRequiredCount(root, "numberMatched");
            int returned = ReadRequiredCount(root, "numberReturned");

            var result = new List<JsonObject>();
            foreach (var f in features)
                if (f is JsonObject obj) result.Add(obj);
            return new WfsBatch(result, matched, returned);
        }

        private static int ReadRequiredCount(JsonObject root, string name)
        {
            JsonNode? node = root[name];
            if (node is null || !int.TryParse(node.ToString(), out int value) || value < 0)
                throw new InvalidOperationException($"VWorld WFS {name}가 없거나 유효하지 않습니다.");
            return value;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Geocoder
        // ─────────────────────────────────────────────────────────────────

        private async Task<(double x, double y)> AddressToCoordAsync(
            string address, CancellationToken cancellationToken)
        {
            string url = $"{GEOCODER_URL}" +
                $"?service=address&request=getcoord&crs=EPSG:4326" +
                $"&address={Uri.EscapeDataString(address)}" +
                $"&format=json&type=road&key={_apiKey}";

            var root = await FetchJsonAsync(url, cancellationToken).ConfigureAwait(false);
            var point = root["response"]!["result"]!["point"]!;
            double x = double.Parse(point["x"]!.ToString(), System.Globalization.CultureInfo.InvariantCulture);
            double y = double.Parse(point["y"]!.ToString(), System.Globalization.CultureInfo.InvariantCulture);
            return (x, y);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Cache Key
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 주소 쌍으로부터 캐시 파일명에 사용할 안전한 키를 생성한다.
        /// 동일 주소 쌍이면 같은 캐시를 재사용한다.
        /// </summary>
        private static string BuildCacheKey(string address1, string address2)
        {
            string combined = $"{address1}|{address2}";
            using var sha = System.Security.Cryptography.SHA256.Create();
            byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined));
            return BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();
        }

        // ─────────────────────────────────────────────────────────────────
        //  HTTP
        // ─────────────────────────────────────────────────────────────────

        private async Task<JsonObject> FetchJsonAsync(string url, CancellationToken cancellationToken)
        {
            string body = await _http.GetStringAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
            var node = JsonNode.Parse(body)
                ?? throw new InvalidOperationException("API 응답이 올바른 JSON 형식이 아닙니다.");
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
                    coords,
                    parts = r.Topology?.Parts.Select(part => new
                    {
                        outer = part.Outer.Points.Select(point => new[] { point.X, point.Y }),
                        holes = part.Holes.Select(hole =>
                            hole.Points.Select(point => new[] { point.X, point.Y }))
                    }),
                    warnings = r.Warnings,
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

                BoundaryTopology? topology = null;
                if (elem.TryGetProperty("parts", out var partsElement) &&
                    partsElement.ValueKind == JsonValueKind.Array)
                {
                    var parts = new List<BoundaryPart>();
                    foreach (var partElement in partsElement.EnumerateArray())
                    {
                        var outer = ReadRing(partElement.GetProperty("outer"));
                        var holes = partElement.GetProperty("holes").EnumerateArray()
                            .Select(ReadRing).ToArray();
                        parts.Add(new BoundaryPart(outer, holes));
                    }
                    topology = BoundaryTopology.Create(parts);
                }

                IReadOnlyList<string> warnings = elem.TryGetProperty("warnings", out var warningElement) &&
                    warningElement.ValueKind == JsonValueKind.Array
                    ? warningElement.EnumerateArray()
                        .Select(item => item.GetString() ?? string.Empty)
                        .Where(item => item.Length > 0).ToArray()
                    : Array.Empty<string>();
                result.Add(new LegalDistrictRecord(
                    legaldCd, name, curve, area, centroid, topology, warnings));
            }

            return result;
        }

        private static BoundaryRing ReadRing(JsonElement element)
            => new(element.EnumerateArray()
                .Select(point => new Coordinate2D(point[0].GetDouble(), point[1].GetDouble()))
                .ToArray());
    }

    /// <summary>법정동 경계 레코드 (VworldApiParser 출력 단위)</summary>
    public record LegalDistrictRecord(
        string       LegaldCd,
        string       Name,
        PolylineCurve Geometry,
        double       Area,
        Point3d      Centroid,
        BoundaryTopology? Topology = null,
        IReadOnlyList<string>? Warnings = null);

    public sealed class BoundaryTopologyParseException : Exception
    {
        public BoundaryTopologyParseException(string message, Exception? inner = null)
            : base(message, inner) { }
    }

    public static class GeoJsonBoundaryParser
    {
        public static BoundaryTopology Parse(JsonNode? geometry)
            => Parse(geometry, out _);

        public static BoundaryTopology Parse(
            JsonNode? geometry, out IReadOnlyList<string> warnings)
        {
            string type = geometry?["type"]?.GetValue<string>()
                ?? throw new ArgumentException("GeoJSON geometry type이 없습니다.");
            var coordinates = geometry["coordinates"]?.AsArray()
                ?? throw new ArgumentException("GeoJSON coordinates가 없습니다.");
            var polygonNodes = type switch
            {
                "Polygon" => new[] { coordinates },
                "MultiPolygon" => coordinates.Select(node => node!.AsArray()).ToArray(),
                _ => throw new ArgumentException($"지원하지 않는 GeoJSON geometry: {type}"),
            };
            var parts = new List<BoundaryPart>();
            var issues = new List<string>();
            int partIndex = 0;
            foreach (var polygon in polygonNodes)
            {
                if (polygon.Count == 0)
                {
                    issues.Add($"part {partIndex} missing");
                    partIndex++;
                    continue;
                }
                BoundaryRing outer;
                try { outer = ParseRing(polygon[0]!.AsArray()); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    issues.Add($"part {partIndex} outer invalid");
                    partIndex++;
                    continue;
                }
                var holes = new List<BoundaryRing>();
                for (int i = 1; i < polygon.Count; i++)
                {
                    try { holes.Add(ParseRing(polygon[i]!.AsArray())); }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                    { issues.Add($"part {partIndex} hole {i - 1} invalid"); }
                }
                parts.Add(new BoundaryPart(outer, holes));
                partIndex++;
            }
            if (parts.Count == 0)
                throw new BoundaryTopologyParseException(
                    "모든 boundary topology part가 유효하지 않습니다: " +
                    string.Join(", ", issues));
            var topology = BoundaryTopology.Create(parts);
            issues.AddRange(topology.Warnings);
            warnings = issues.AsReadOnly();
            return topology;
        }

        private static BoundaryRing ParseRing(JsonArray coordinates)
        {
            var points = new List<Coordinate2D>(coordinates.Count + 1);
            foreach (var coordinate in coordinates)
            {
                var pair = coordinate?.AsArray();
                if (pair == null || pair.Count < 2) continue;
                double longitude = pair[0]!.GetValue<double>();
                double latitude = pair[1]!.GetValue<double>();
                var projected = Epsg5179.FromWgs84(longitude, latitude);
                points.Add(new Coordinate2D(projected.X, projected.Y));
            }
            if (points.Count > 0 && points[0] != points[^1]) points.Add(points[0]);
            return new BoundaryRing(points);
        }
    }
}
