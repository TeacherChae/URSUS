using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using URSUS.Config;
using URSUS.Parsers;

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

        /// <summary>파라미터 키: 중심 주소 (단일 주소 + 반경 모드)</summary>
        public const string PARAM_ADDRESS = "address";

        /// <summary>파라미터 키: 검색 반경 km (단일 주소 모드, 기본 15.0)</summary>
        public const string PARAM_RADIUS_KM = "radiusKm";

        /// <summary>파라미터 키: BBOX 모드의 두 번째 주소</summary>
        public const string PARAM_ADDRESS2 = "address2";

        /// <summary>최소 면적 필터 (㎡) — 이보다 작은 폴리곤은 제외</summary>
        public const double MIN_AREA = 100.0;

        public VWorldBoundaryDataSource(ApiKeyProvider keyProvider)
        {
            _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
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
                    "URS101");
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

            // 사전 조건 검증
            var configError = ValidateConfiguration();
            if (configError != null)
                return DataResult<BoundaryDataSet>.Failure(configError, sw.Elapsed);

            string apiKey = _keyProvider.VWorldKey!;

            // 파라미터에서 주소 정보 추출
            string? address = query.Parameters?.TryGetValue(PARAM_ADDRESS, out var a) == true ? a : null;
            string? address2 = query.Parameters?.TryGetValue(PARAM_ADDRESS2, out var a2) == true ? a2 : null;
            string? radiusStr = query.Parameters?.TryGetValue(PARAM_RADIUS_KM, out var r) == true ? r : null;

            if (string.IsNullOrWhiteSpace(address))
            {
                return DataResult<BoundaryDataSet>.Failure(
                    new DataSourceError("URS102",
                        "경계 데이터 조회에 주소가 필요합니다.\n" +
                        "→ DataQuery.Parameters에 \"address\" 키로 주소를 전달해주세요."),
                    sw.Elapsed);
            }

            try
            {
                var parser = new VworldApiParser(apiKey, query.CacheDirectory);

                // 동기 파서를 Task.Run으로 래핑
                List<LegalDistrictRecord> rawDistricts;

                if (!string.IsNullOrWhiteSpace(address2))
                {
                    // BBOX 모드
                    rawDistricts = await Task.Run(
                        () => parser.GetLegalDistrictsByBBox(address, address2),
                        cancellationToken);
                }
                else
                {
                    // 단일 주소 + 반경 모드
                    double radiusKm = 15.0;
                    if (!string.IsNullOrWhiteSpace(radiusStr) &&
                        double.TryParse(radiusStr, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                    {
                        radiusKm = parsed;
                    }

                    rawDistricts = await Task.Run(
                        () => parser.GetLegalDistricts(address, radiusKm),
                        cancellationToken);
                }

                int rawCount = rawDistricts.Count;

                // 면적 필터 적용
                var filtered = rawDistricts
                    .Where(r => r.Area > MIN_AREA)
                    .ToList();

                if (filtered.Count == 0)
                {
                    return DataResult<BoundaryDataSet>.Failure(
                        DataSourceError.NoData("법정동 경계", "URS103"),
                        sw.Elapsed);
                }

                // LegalDistrictRecord → BoundaryRecord 변환
                var records = filtered.Select(d => new BoundaryRecord
                {
                    DistrictCode = d.LegaldCd,
                    Name         = d.Name,
                    Geometry     = d.Geometry,
                    Area         = d.Area,
                    Centroid     = d.Centroid
                }).ToList();

                var origin = sw.Elapsed.TotalSeconds < 1
                    ? DataOrigin.Cache
                    : DataOrigin.Api;

                var dataSet = new BoundaryDataSet(records)
                {
                    RawFeatureCount  = rawCount,
                    FilteredOutCount = rawCount - filtered.Count
                };

                sw.Stop();
                Console.WriteLine(
                    $"[Solver] 경계 {filtered.Count}건 수집 완료 " +
                    $"(원시 {rawCount}건, 필터 제외 {rawCount - filtered.Count}건, " +
                    $"{(origin == DataOrigin.Cache ? "캐시" : "API")}, {sw.Elapsed.TotalSeconds:F1}s)");

                return DataResult<BoundaryDataSet>.Success(dataSet, origin, sw.Elapsed);
            }
            catch (OperationCanceledException)
            {
                return DataResult<BoundaryDataSet>.Failure(
                    new DataSourceError("URS104",
                        "경계 데이터 수집이 취소되었습니다.",
                        ErrorSeverity.Warning),
                    sw.Elapsed);
            }
            catch (Exception ex)
            {
                return DataResult<BoundaryDataSet>.Failure(
                    new DataSourceError("URS105",
                        $"경계 데이터 수집 실패: {ex.Message}\n" +
                        "→ VWorld API 키와 네트워크 연결을 확인해주세요.\n" +
                        "→ 발급: https://www.vworld.kr/",
                        ErrorSeverity.Error, ex),
                    sw.Elapsed);
            }
        }
    }
}
