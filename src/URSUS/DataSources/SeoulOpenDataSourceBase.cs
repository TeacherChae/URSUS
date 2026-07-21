using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using URSUS.Config;
using URSUS.Parsers;
using URSUS.Resources;
using URSUS.Analysis;
using URSUS.Caching;
using URSUS.Net;

namespace URSUS.DataSources
{
    /// <summary>
    /// 서울 열린데이터 광장 API 기반 IDataSource의 공통 베이스.
    ///
    /// 설계 의도:
    /// - DataSeoulApiParser의 서비스별 호출을 IDataSource 인터페이스로 래핑
    /// - 행정동 → 법정동 매핑을 데이터 소스 내부에서 처리 (호출부 부담 제거)
    /// - 서브클래스는 Metadata와 FetchRawData만 구현하면 됨
    ///
    /// 데이터 흐름:
    ///   API → 행정동별 Dictionary → MappingLoader로 법정동 변환 → DistrictDataSet
    /// </summary>
    public abstract class SeoulOpenDataSourceBase : IDataSource
    {
        internal const int CacheSchemaVersion = 3;

        protected readonly ApiKeyProvider KeyProvider;
        private readonly HttpPipeline _http;
        private readonly IClock _clock;
        private readonly AtomicCacheStore _cache;

        protected SeoulOpenDataSourceBase(ApiKeyProvider keyProvider)
            : this(keyProvider, null, null, null) { }

        protected SeoulOpenDataSourceBase(
            ApiKeyProvider keyProvider,
            HttpPipeline? http,
            IClock? clock,
            AtomicCacheStore? cache)
        {
            KeyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
            _http = http ?? new HttpPipeline(HttpClientLifetime.Shared, maxConcurrency: 8);
            _clock = clock ?? SystemClock.Instance;
            _cache = cache ?? new AtomicCacheStore(clock: _clock);
        }

        /// <inheritdoc />
        public abstract DataSourceMetadata Metadata { get; }

        /// <summary>
        /// 서브클래스에서 구현: DataSeoulApiParser를 사용해 행정동 기준 원시 데이터를 반환.
        /// </summary>
        /// <param name="parser">서울 열린데이터 API 파서</param>
        /// <param name="cacheDir">캐시 디렉토리 (null 가능)</param>
        /// <returns>행정동 코드 → 값 딕셔너리</returns>
        protected virtual Dictionary<string, double> FetchRawData(
            DataSeoulApiParser parser, string? cacheDir)
            => throw new NotSupportedException("Async source 구현이 필요합니다.");

        protected virtual Task<SeoulAggregate> FetchRawDataAsync(
            DataSeoulApiParser parser,
            DataQuery query,
            CancellationToken cancellationToken)
        {
            var legacy = FetchRawData(parser, query.CacheDirectory);
            return Task.FromResult(new SeoulAggregate(
                legacy,
                new ObservationWindow("legacy", false, legacy.Count,
                    SeoulExpectedDistricts.Ids.Count),
                legacy.Count,
                false,
                new[] { "legacy synchronous source path" }));
        }

        protected virtual MetricSemantics MetricSemantics => MetricSemantics.Mean;

        /// <summary>
        /// 결과 값의 단위 문자열.
        /// 예: "원", "명", "명/일"
        /// </summary>
        protected abstract string ValueUnit { get; }

        /// <inheritdoc />
        public DataSourceError? ValidateConfiguration()
        {
            string? key = KeyProvider.SeoulKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                return DataSourceError.ApiKeyMissing(
                    "SeoulKey (서울 열린데이터 광장)",
                    ErrorCodes.SeoulKeyMissing);
            }
            return null;
        }

        /// <inheritdoc />
        public async Task<DataResult<DistrictDataSet>> FetchAsync(
            DataQuery query,
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            cancellationToken.ThrowIfCancellationRequested();

            if (query.DistrictCodes != null &&
                !SeoulCoveragePolicy.Supports(query.DistrictCodes))
            {
                return DataResult<DistrictDataSet>.Failure(
                    new DataSourceError(
                        ErrorCodes.UnsupportedCoverage,
                        $"{Metadata.DisplayName}은(는) 서울특별시 법정동만 지원합니다.",
                        ErrorSeverity.Warning),
                    sw.Elapsed);
            }

            // 사전 조건 검증
            var configError = ValidateConfiguration();
            if (configError != null)
                return DataResult<DistrictDataSet>.Failure(configError, sw.Elapsed);

            string apiKey = KeyProvider.SeoulKey!;

            try
            {
                var parser = new DataSeoulApiParser(apiKey, _http, _clock);
                var cacheParameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["transport.allowInsecureSeoulHttp"] =
                        query.TransportPolicy.AllowInsecureSeoulHttp ? "true" : "false",
                    ["explicitPeriod"] = query.ExplicitPeriod ?? string.Empty,
                    ["expectedSet"] = SeoulExpectedDistricts.Version,
                };
                if (query.Parameters != null)
                    foreach (var pair in query.Parameters) cacheParameters[pair.Key] = pair.Value;
                var cacheKey = PersistentCacheKey.Create(Metadata.Id, CacheSchemaVersion,
                    query.QueryIntent,
                    cacheParameters, query.DistrictCodes, CoordinateReferenceSystem.Epsg5179);
                var cached = await _cache.GetOrFetchAsync(
                    cacheKey,
                    query.ForceRefresh,
                    TimeSpan.FromDays(Metadata.CacheTtlDays),
                    async token => EnsurePaginationComplete(
                        await FetchRawDataAsync(parser, query, token).ConfigureAwait(false)),
                    cancellationToken).ConfigureAwait(false);
                var raw = EnsurePaginationComplete(cached.Value);
                var rawData = raw.Values.ToDictionary(pair => pair.Key, pair => pair.Value,
                    StringComparer.Ordinal);

                if (rawData.Count == 0)
                {
                    return DataResult<DistrictDataSet>.Failure(
                        DataSourceError.NoData(Metadata.DisplayName, GetNoDataErrorCode(Metadata.Id)),
                        sw.Elapsed);
                }

                // 행정동 → 법정동 매핑
                var adstrdToLegald = MappingLoader.Load();
                var declaredMapping = adstrdToLegald.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<string>)pair.Value,
                    StringComparer.Ordinal);
                var mapped = DistrictMetricMapper.MapAdministrativeToLegal(
                    declaredMapping, rawData, MetricSemantics);
                var legaldData = mapped.Values.ToDictionary(pair => pair.Key, pair => pair.Value,
                    StringComparer.Ordinal);

                var origin = cached.DeliveryOrigin == DeliveryOrigin.Cache
                    ? DataOrigin.Cache
                    : DataOrigin.Api;

                var dataSet = DistrictDataSet.FromDictionary(legaldData, ValueUnit);
                dataSet = new DistrictDataSet(dataSet.Records)
                {
                    RawRecordCount = rawData.Count,
                    UnmappedCount  = rawData.Count - CountMappedKeys(adstrdToLegald, rawData),
                    Observation    = raw.Observation,
                    MappingQuality = mapped.Quality,
                    Warnings       = raw.Warnings,
                };

                sw.Stop();
                return DataResult<DistrictDataSet>.Success(dataSet, origin, sw.Elapsed,
                    cached.RetrievedAt, cached.AcquisitionOrigin,
                    cached.DeliveryOrigin, cached.CacheAge);
            }
            catch (OperationCanceledException) { throw; }
            catch (SeoulPaginationException ex)
            {
                string safeMessage = SecretRedactor.Redact(ex.Message);
                return DataResult<DistrictDataSet>.Failure(
                    DataSourceError.ParseError(
                        safeMessage,
                        ErrorCodes.SeoulPaginationIncomplete,
                        new InvalidOperationException(safeMessage)),
                    sw.Elapsed);
            }
            catch (Exception ex)
            {
                string safeMessage = SecretRedactor.Redact(ex.Message);
                return DataResult<DistrictDataSet>.Failure(
                    new DataSourceError(ErrorCodes.DataSourceFailed,
                        $"{Metadata.DisplayName} 데이터 수집 실패: {safeMessage}\n" +
                        "→ API 키와 네트워크 연결을 확인해주세요.\n" +
                        "→ 발급: https://data.seoul.go.kr/",
                        ErrorSeverity.Error,
                        new InvalidOperationException(safeMessage)),
                    sw.Elapsed);
            }
        }

        internal static string GetNoDataErrorCode(string sourceId)
            => ErrorCodes.SeoulNoData;

        private static SeoulAggregate EnsurePaginationComplete(SeoulAggregate aggregate)
        {
            if (!aggregate.PaginationComplete)
                throw new SeoulPaginationException(
                    $"서울 API pagination이 완결되지 않았습니다 " +
                    $"(received={aggregate.RawRecordCount}).");
            return aggregate;
        }

        // ─────────────────────────────────────────────────────────────────
        //  행정동 → 법정동 매핑 (URSUSSolver.MapToLegald 로직 이전)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 행정동 코드 기반 데이터를 법정동 코드 기반으로 변환한다.
        /// 하나의 행정동이 여러 법정동에 매핑되면 동일 값을 배분,
        /// 여러 행정동이 하나의 법정동에 매핑되면 평균을 계산한다.
        /// </summary>
        internal static Dictionary<string, double> MapToLegalDistrict(
            Dictionary<string, List<string>> adstrdToLegald,
            Dictionary<string, double> byAdstrd)
        {
            var acc = new Dictionary<string, (double sum, int count)>();

            foreach (var (adstrdCd, legaldCds) in adstrdToLegald)
            {
                if (!byAdstrd.TryGetValue(adstrdCd, out double val)) continue;

                foreach (string legaldCd in legaldCds)
                {
                    if (acc.TryGetValue(legaldCd, out var prev))
                        acc[legaldCd] = (prev.sum + val, prev.count + 1);
                    else
                        acc[legaldCd] = (val, 1);
                }
            }

            var result = new Dictionary<string, double>();
            foreach (var (k, (sum, cnt)) in acc)
                result[k] = sum / cnt;
            return result;
        }

        /// <summary>매핑에 성공한 행정동 키 수를 카운트 (품질 지표용)</summary>
        private static int CountMappedKeys(
            Dictionary<string, List<string>> adstrdToLegald,
            Dictionary<string, double> byAdstrd)
        {
            return byAdstrd.Keys.Count(k => adstrdToLegald.ContainsKey(k));
        }
    }
}
