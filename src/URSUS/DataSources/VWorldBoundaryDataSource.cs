using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using URSUS.Config;
using URSUS.Parsers;
using URSUS.Resources;
using URSUS.Net;
using URSUS.Caching;
using URSUS.Geometry;

namespace URSUS.DataSources
{
    /// <summary>
    /// VWorld WFS API 기반 법정동 경계 데이터 소스.
    ///
    /// - IBoundaryDataSource 인터페이스 구현 → Registry에 등록하면 자동으로 파이프라인에 포함
    /// - 기존 VworldApiParser를 래핑하여 DataSource 추상화 레이어에 통합
    /// - 단일 주소 + 반경 또는 두 주소 BBOX 모드 지원
    /// - DataQuery.Bounds 또는 Parameters로 검색 영역 지정
    ///
    /// 사용 예:
    /// <code>
    /// var source = new VWorldBoundaryDataSource(keyProvider);
    /// var query = new DataQuery
    /// {
    ///     CacheDirectory = cacheDir,
    ///     Parameters = new Dictionary&lt;string, string&gt;
    ///     {
    ///         { "address", "서울특별시 중구 세종대로 110" },
    ///         { "radiusKm", "15.0" }
    ///     }
    /// };
    /// var result = await source.FetchBoundariesAsync(query);
    /// </code>
    /// </summary>
    public class VWorldBoundaryDataSource : IBoundaryDataSource
    {
        private readonly ApiKeyProvider _keyProvider;
        private readonly HttpPipeline _http;
        private readonly AtomicCacheStore _cache;

        /// <summary>파라미터 키: 중심 주소 (단일 주소 + 반경 모드)</summary>
        public const string PARAM_ADDRESS = "address";

        /// <summary>파라미터 키: 검색 반경 km (단일 주소 모드, 기본 15.0)</summary>
        public const string PARAM_RADIUS_KM = "radiusKm";

        /// <summary>파라미터 키: BBOX 모드의 두 번째 주소</summary>
        public const string PARAM_ADDRESS2 = "address2";

        /// <summary>최소 면적 필터 (㎡) — 이보다 작은 폴리곤은 제외</summary>
        public const double MIN_AREA = 100.0;

        public VWorldBoundaryDataSource(ApiKeyProvider keyProvider)
            : this(keyProvider, null, null) { }

        public VWorldBoundaryDataSource(ApiKeyProvider keyProvider, HttpPipeline? http,
            AtomicCacheStore? cache = null)
        {
            _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
            _http = http ?? new HttpPipeline(HttpClientLifetime.Shared, maxConcurrency: 8);
            _cache = cache ?? new AtomicCacheStore();
        }

        public DataSourceMetadata Metadata { get; } = new DataSourceMetadata
        {
            Id              = "vworld_boundary",
            DisplayName     = "법정동 경계",
            Description     = "VWorld WFS API 기반 법정동 경계 폴리곤 (전국)",
            Category        = DataCategory.Boundary,
            Provider        = "VWorld (국토교통부 공간정보 오픈플랫폼)",
            UpdateFrequency = "비정기 (행정구역 변경 시)",
            CoverageArea    = "전국",
            RequiredApiKeys = new[] { ApiKeyProvider.KEY_VWORLD },
            CacheTtlDays    = 30
        };

        public DataSourceError? ValidateConfiguration()
        {
            string? key = _keyProvider.VWorldKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                return DataSourceError.ApiKeyMissing(
                    "VWorldKey (VWorld 공간정보 오픈플랫폼)",
                    ErrorCodes.VWorldKeyMissing);
            }
            return null;
        }

        /// <summary>
        /// 법정동 경계를 수집한다.
        ///
        /// DataQuery.Parameters로 검색 모드를 결정한다:
        ///   - "address" + "radiusKm" → 단일 주소 + 반경 모드
        ///   - "address" + "address2" → BBOX 모드 (두 주소가 대각선 꼭짓점)
        ///   - "address"만 → 단일 주소 + 기본 반경(15km) 모드
        /// </summary>
        public async Task<DataResult<BoundaryDataSet>> FetchBoundariesAsync(
            DataQuery query,
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            cancellationToken.ThrowIfCancellationRequested();

            // 사전 조건 검증
            var configError = ValidateConfiguration();
            if (configError != null)
                return DataResult<BoundaryDataSet>.Failure(configError, sw.Elapsed);

            string apiKey = _keyProvider.VWorldKey!;

            // 파라미터에서 주소 정보 추출
            string? address = query.Parameters?.TryGetValue(PARAM_ADDRESS, out var a) == true ? a : null;
            string? address2 = query.Parameters?.TryGetValue(PARAM_ADDRESS2, out var a2) == true ? a2 : null;
            string? radiusStr = query.Parameters?.TryGetValue(PARAM_RADIUS_KM, out var r) == true ? r : null;

            SpatialBounds? typedBounds = query.TypedBounds;
            if (typedBounds == null && query.Bounds is { } legacyBounds && legacyBounds.IsValid)
                typedBounds = new SpatialBounds(
                    legacyBounds.Min.X, legacyBounds.Min.Y,
                    legacyBounds.Max.X, legacyBounds.Max.Y,
                    CoordinateReferenceSystem.Epsg5179);

            if (typedBounds == null && string.IsNullOrWhiteSpace(address))
            {
                return DataResult<BoundaryDataSet>.Failure(
                    new DataSourceError(ErrorCodes.BoundaryAddressMissing,
                        "경계 데이터 조회에 주소가 필요합니다.\n" +
                        "→ DataQuery.Parameters에 \"address\" 키로 주소를 전달해주세요."),
                    sw.Elapsed);
            }

            try
            {
                var parser = new VworldApiParser(apiKey, null, _http);
                var identity = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["address"] = address?.Trim() ?? string.Empty,
                    ["address2"] = address2?.Trim() ?? string.Empty,
                    ["radiusKm"] = radiusStr?.Trim() ?? "15",
                };
                if (typedBounds != null)
                {
                    var normalized = typedBounds.Normalize();
                    identity["bounds"] = string.Join(",",
                        normalized.MinX.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        normalized.MinY.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        normalized.MaxX.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        normalized.MaxY.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    identity["boundsCrs"] = normalized.Crs.ToString();
                }
                var cacheKey = PersistentCacheKey.Create(Metadata.Id, 3, query.QueryIntent,
                    identity, null, CoordinateReferenceSystem.Epsg5179);
                var cached = await _cache.GetOrFetchAsync(cacheKey, query.ForceRefresh,
                    TimeSpan.FromDays(Metadata.CacheTtlDays), async token =>
                    {
                        List<LegalDistrictRecord> fetched;
                        if (typedBounds != null)
                        {
                            fetched = await parser.GetLegalDistrictsByBoundsAsync(
                                typedBounds, token).ConfigureAwait(false);
                        }
                        else if (!string.IsNullOrWhiteSpace(address2))
                        {
                            fetched = await parser.GetLegalDistrictsByBBoxAsync(
                                address!, address2, token).ConfigureAwait(false);
                        }
                        else
                        {
                            double radiusKm = 15.0;
                            if (!string.IsNullOrWhiteSpace(radiusStr) &&
                                !double.TryParse(radiusStr, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out radiusKm))
                                throw new ArgumentException("radiusKm는 invariant numeric이어야 합니다.");
                            if (!double.IsFinite(radiusKm) || radiusKm <= 0 || radiusKm > 100)
                                throw new ArgumentOutOfRangeException(nameof(radiusKm),
                                    "radiusKm는 0 초과 100 이하여야 합니다.");
                            fetched = await parser.GetLegalDistrictsAsync(
                                address!, radiusKm, token).ConfigureAwait(false);
                        }
                        return fetched.Select(BoundaryCacheRecord.From).ToList();
                    }, cancellationToken).ConfigureAwait(false);
                var rawDistricts = cached.Value.Select(record => record.ToLegalRecord()).ToList();

                int rawCount = rawDistricts.Count;

                // 면적 필터 적용
                var filtered = rawDistricts
                    .Where(r => r.Area > MIN_AREA)
                    .ToList();

                if (filtered.Count == 0)
                {
                    return DataResult<BoundaryDataSet>.Failure(
                        DataSourceError.NoData("법정동 경계", ErrorCodes.BoundaryNoData),
                        sw.Elapsed);
                }

                // LegalDistrictRecord → BoundaryRecord 변환
                var records = filtered.Select(d => new BoundaryRecord
                {
                    DistrictCode = d.LegaldCd,
                    Name         = d.Name,
                    Geometry     = d.Geometry,
                    Area         = d.Area,
                    Centroid     = d.Centroid,
                    Topology     = d.Topology,
                }).ToList();

                var origin = cached.DeliveryOrigin == DeliveryOrigin.Cache
                    ? DataOrigin.Cache : DataOrigin.Api;

                var dataSet = new BoundaryDataSet(records)
                {
                    RawFeatureCount  = rawCount,
                    FilteredOutCount = rawCount - filtered.Count,
                    Warnings = filtered.SelectMany(record =>
                        record.Warnings ?? Array.Empty<string>()).ToArray(),
                };

                sw.Stop();
                Console.WriteLine(
                    $"[Solver] 경계 {filtered.Count}건 수집 완료 " +
                    $"(원시 {rawCount}건, 필터 제외 {rawCount - filtered.Count}건, " +
                    $"{(origin == DataOrigin.Cache ? "캐시" : "API")}, {sw.Elapsed.TotalSeconds:F1}s)");

                return DataResult<BoundaryDataSet>.Success(dataSet, origin, sw.Elapsed,
                    cached.RetrievedAt, cached.AcquisitionOrigin,
                    cached.DeliveryOrigin, cached.CacheAge);
            }
            catch (OperationCanceledException) { throw; }
            catch (BoundaryTopologyParseException ex)
            {
                string safeMessage = SecretRedactor.Redact(ex.Message);
                return DataResult<BoundaryDataSet>.Failure(
                    new DataSourceError(ErrorCodes.BoundaryTopologyInvalid,
                        $"경계 topology가 유효하지 않습니다: {safeMessage}",
                        ErrorSeverity.Error, new InvalidDataException(safeMessage)),
                    sw.Elapsed);
            }
            catch (Exception ex)
            {
                string safeMessage = SecretRedactor.Redact(ex.Message);
                return DataResult<BoundaryDataSet>.Failure(
                    new DataSourceError(ErrorCodes.BoundaryFailed,
                        $"경계 데이터 수집 실패: {safeMessage}\n" +
                        "→ VWorld API 키와 네트워크 연결을 확인해주세요.\n" +
                        "→ 발급: https://www.vworld.kr/",
                        ErrorSeverity.Error, new InvalidOperationException(safeMessage)),
                    sw.Elapsed);
            }
        }

        private sealed record BoundaryCacheRecord(
            string DistrictCode,
            string Name,
            IReadOnlyList<IReadOnlyList<IReadOnlyList<double[]>>> Parts,
            IReadOnlyList<string>? Warnings)
        {
            public static BoundaryCacheRecord From(LegalDistrictRecord record)
            {
                var topology = record.Topology ?? BoundaryTopology.Create(new[]
                {
                    new BoundaryPart(new BoundaryRing(record.Geometry.ToPolyline()
                        .Select(point => new Coordinate2D(point.X, point.Y)).ToArray()),
                        Array.Empty<BoundaryRing>()),
                });
                var parts = topology.Parts.Select(part =>
                    (IReadOnlyList<IReadOnlyList<double[]>>)new[] { part.Outer }.Concat(part.Holes)
                        .Select(ring => (IReadOnlyList<double[]>)ring.Points
                            .Select(point => new[] { point.X, point.Y }).ToArray())
                        .ToArray()).ToArray();
                return new BoundaryCacheRecord(record.LegaldCd, record.Name, parts,
                    record.Warnings?.ToArray() ?? Array.Empty<string>());
            }

            public LegalDistrictRecord ToLegalRecord()
            {
                var parts = Parts.Select(part => new BoundaryPart(
                    ReadRing(part[0]), part.Skip(1).Select(ReadRing).ToArray())).ToArray();
                var topology = BoundaryTopology.Create(parts);
                var outer = topology.Parts.OrderByDescending(part => Math.Abs(part.Outer.SignedArea))
                    .First().Outer;
                var curve = new Rhino.Geometry.Polyline(outer.Points
                    .Select(point => new Rhino.Geometry.Point3d(point.X, point.Y, 0))).ToPolylineCurve();
                return new LegalDistrictRecord(DistrictCode, Name, curve, topology.Area,
                    new Rhino.Geometry.Point3d(topology.Centroid.X, topology.Centroid.Y, 0),
                    topology, Warnings ?? Array.Empty<string>());
            }

            private static BoundaryRing ReadRing(IReadOnlyList<double[]> points)
                => new(points.Select(point => new Coordinate2D(point[0], point[1])).ToArray());
        }
    }
}
