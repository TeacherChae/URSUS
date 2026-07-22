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
using URSUS.Analysis;

namespace URSUS.DataSources
{
    /// <summary>
    /// 공공데이터포털(data.go.kr) 토지이용규제정보 → 법정동별 용도지역 범주 legacy histogram.
    ///
    /// - 기본 dataset에는 포함되지 않으며 호출자가 명시적으로 선택할 때만 사용
    /// - 기본 결과는 범주별 count를 보존하며 numeric overlay를 만들지 않음
    /// - enableOrdinalTransform=true 명시 opt-in에서만 versioned 1~5 점수를 파생
    /// - deprecated DataGoKrKey가 없으면 ValidateConfiguration에서 안내 메시지 반환
    ///
    /// opt-in 점수 해석:
    ///   5.0: 중심/일반상업지역 — 도심 핵심 상업 지역
    ///   4.0: 근린/유통상업지역 — 근린 상업 활동 지역
    ///   3.0~3.5: 주거지역 (준주거~일반주거) — 주거 중심 혼합 지역
    ///   2.0~2.5: 전용주거/공업지역 — 단일 용도 지역
    ///   1.0: 녹지/보전지역 — 개발 제한 지역
    ///
    /// API 발급: https://www.data.go.kr/data/15056930/openapi.do
    /// </summary>
    public class ZoningDataSource : IDataSource
    {
        public const string PARAM_ENABLE_ORDINAL_TRANSFORM = "enableOrdinalTransform";

        private readonly ApiKeyProvider _keyProvider;
        private readonly HttpPipeline _http;
        private readonly AtomicCacheStore _cache;

        public ZoningDataSource(ApiKeyProvider keyProvider)
            : this(keyProvider, null, null) { }

        public ZoningDataSource(ApiKeyProvider keyProvider, HttpPipeline? http,
            AtomicCacheStore? cache = null)
        {
            _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
            _http = http ?? new HttpPipeline(HttpClientLifetime.Shared, maxConcurrency: 8);
            _cache = cache ?? new AtomicCacheStore();
        }

        public DataSourceMetadata Metadata { get; } = new DataSourceMetadata
        {
            Id              = "zoning",
            DisplayName     = "용도지역",
            Description     = "[Legacy] 법정동별 용도지역 범주 histogram (명시 opt-in 시에만 ordinal overlay)",
            Category        = DataCategory.LandUse,
            Provider        = "공공데이터포털 (data.go.kr)",
            UpdateFrequency = "비정기 (도시계획 변경 시)",
            CoverageArea    = "전국",
            RequiredApiKeys = new[] { ApiKeyProvider.LegacyDataGoKrKeyName },
            CacheTtlDays    = 30
        };

        public DataSourceError? ValidateConfiguration()
        {
            string? key = _keyProvider.LegacyDataGoKrKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                return DataSourceError.ApiKeyMissing(
                    "DataGoKrKey (공공데이터포털)",
                    ErrorCodes.DataGoKrKeyMissing);
            }
            return null;
        }

        public async Task<DataResult<DistrictDataSet>> FetchAsync(
            DataQuery query,
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            cancellationToken.ThrowIfCancellationRequested();

            // 사전 조건 검증
            var configError = ValidateConfiguration();
            if (configError != null)
                return DataResult<DistrictDataSet>.Failure(configError, sw.Elapsed);

            string apiKey = _keyProvider.LegacyDataGoKrKey!;

            // 법정동 코드 목록이 없으면 에러
            if (query.DistrictCodes == null || query.DistrictCodes.Count == 0)
            {
                return DataResult<DistrictDataSet>.Failure(
                    new DataSourceError(ErrorCodes.ZoningCodesMissing,
                        "용도지역 조회에 필요한 법정동 코드 목록이 없습니다.\n" +
                        "→ 경계 데이터(VWorld)가 먼저 수집되어야 합니다."),
                    sw.Elapsed);
            }

            try
            {
                var parser = new ZoningApiParser(apiKey, _http);
                var codesList = query.DistrictCodes.ToList();
                bool ordinalEnabled = query.Parameters != null &&
                    query.Parameters.TryGetValue(PARAM_ENABLE_ORDINAL_TRANSFORM,
                        out string? enabled) &&
                    bool.TryParse(enabled, out bool parsed) && parsed;

                var cacheKey = PersistentCacheKey.Create(Metadata.Id, 2, query.QueryIntent,
                    parameters: null, codesList, CoordinateReferenceSystem.Epsg5179);
                var cached = await _cache.GetOrFetchAsync(cacheKey, query.ForceRefresh,
                    TimeSpan.FromDays(Metadata.CacheTtlDays),
                    token => parser.GetZoningHistogramByDistrictAsync(codesList, token),
                    cancellationToken).ConfigureAwait(false);
                var histograms = cached.Value;
                var data = ordinalEnabled
                    ? histograms
                        .Select(pair => (pair.Key,
                            Value: ZoningOrdinalTransform.V1.Transform(pair.Value)))
                        .Where(pair => pair.Value != null)
                        .ToDictionary(pair => pair.Key, pair => pair.Value!.Value,
                            StringComparer.Ordinal)
                    : new Dictionary<string, double>(StringComparer.Ordinal);

                if (histograms.Count == 0)
                {
                    return DataResult<DistrictDataSet>.Failure(
                        DataSourceError.NoData("용도지역", ErrorCodes.ZoningNoData),
                        sw.Elapsed);
                }

                var origin = cached.DeliveryOrigin == DeliveryOrigin.Cache
                    ? DataOrigin.Cache : DataOrigin.Api;

                var baseDataSet = DistrictDataSet.FromDictionary(data, "점");
                var dataSet = new DistrictDataSet(baseDataSet.Records)
                {
                    RawRecordCount = baseDataSet.RawRecordCount,
                    Warnings = ordinalEnabled
                        ? new[] { "zoning overlay: explicit count-weighted ordinal transform zoning-ordinal-v1" }
                        : new[] { "zoning overlay disabled: categorical histogram preserved; set enableOrdinalTransform=true to opt in" },
                    CategoricalHistograms = histograms,
                };
                sw.Stop();

                return DataResult<DistrictDataSet>.Success(dataSet, origin, sw.Elapsed,
                    cached.RetrievedAt, cached.AcquisitionOrigin,
                    cached.DeliveryOrigin, cached.CacheAge);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                string safeMessage = SecretRedactor.Redact(ex.Message);
                return DataResult<DistrictDataSet>.Failure(
                    new DataSourceError(ErrorCodes.ZoningFailed,
                        $"용도지역 데이터 수집 실패: {safeMessage}\n" +
                        "→ API 키와 네트워크 연결을 확인해주세요.\n" +
                        "→ 발급: https://www.data.go.kr/data/15056930/openapi.do",
                        ErrorSeverity.Error, new InvalidOperationException(safeMessage)),
                    sw.Elapsed);
            }
        }
    }
}
