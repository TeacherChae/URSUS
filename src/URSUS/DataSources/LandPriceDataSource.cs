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

namespace URSUS.DataSources
{
    /// <summary>
    /// 공공데이터포털(data.go.kr) 표준지공시지가 데이터 소스.
    ///
    /// - IDataSource 인터페이스 구현 → Registry에 등록하면 자동으로 파이프라인에 포함
    /// - 법정동코드 기반 직접 조회 (행정동→법정동 매핑 불필요)
    /// - DataGoKrKey가 없으면 ValidateConfiguration에서 안내 메시지 반환
    ///
    /// API 발급: https://www.data.go.kr/data/15058747/openapi.do
    /// </summary>
    public class LandPriceDataSource : IDataSource
    {
        internal const int CacheSchemaVersion = 3;

        private readonly ApiKeyProvider _keyProvider;
        private readonly HttpPipeline _http;
        private readonly IClock _clock;
        private readonly AtomicCacheStore _cache;

        public LandPriceDataSource(ApiKeyProvider keyProvider)
            : this(keyProvider, null, null, null) { }

        public LandPriceDataSource(ApiKeyProvider keyProvider, HttpPipeline? http,
            IClock? clock = null, AtomicCacheStore? cache = null)
        {
            _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
            _http = http ?? new HttpPipeline(HttpClientLifetime.Shared, maxConcurrency: 8);
            _clock = clock ?? SystemClock.Instance;
            _cache = cache ?? new AtomicCacheStore(clock: _clock);
        }

        public DataSourceMetadata Metadata { get; } = new DataSourceMetadata
        {
            Id              = "land_price",
            DisplayName     = "공시지가",
            Description     = "표준지 공시지가 (원/㎡) — 법정동별 평균 토지 가격 (공공데이터포털)",
            Category        = DataCategory.LandUse,
            Provider        = "공공데이터포털 (data.go.kr)",
            UpdateFrequency = "연 1회 (매년 1월 1일 기준)",
            CoverageArea    = "전국",
            RequiredApiKeys = new[] { ApiKeyProvider.KEY_DATA_GO_KR },
            CacheTtlDays    = 30
        };

        public DataSourceError? ValidateConfiguration()
        {
            string? key = _keyProvider.DataGoKrKey;
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

            string apiKey = _keyProvider.DataGoKrKey!;

            // 법정동 코드 목록이 없으면 에러
            if (query.DistrictCodes == null || query.DistrictCodes.Count == 0)
            {
                return DataResult<DistrictDataSet>.Failure(
                    new DataSourceError(ErrorCodes.LandPriceCodesMissing,
                        "공시지가 조회에 필요한 법정동 코드 목록이 없습니다.\n" +
                        "→ 경계 데이터(VWorld)가 먼저 수집되어야 합니다."),
                    sw.Elapsed);
            }

            try
            {
                var parser = new LandPriceApiParser(apiKey, _http, _clock);
                var codesList = query.DistrictCodes.ToList();
                int standardYear = ResolveStandardYear(query);

                // v3 stores LandPriceAggregate rather than a value-only dictionary.
                // The key bump prevents an older entry from inventing provenance.
                var cacheKey = PersistentCacheKey.Create(Metadata.Id, CacheSchemaVersion,
                    query.QueryIntent,
                    new Dictionary<string, string>
                    {
                        ["stdrYear"] = standardYear.ToString(
                            (System.IFormatProvider?)System.Globalization.CultureInfo.InvariantCulture),
                    }, codesList, CoordinateReferenceSystem.Epsg5179);
                async Task<Dictionary<string, LandPriceAggregate>> FetchValidated(
                    CancellationToken token)
                {
                    var fetched = await parser.GetLandPriceAggregatesByLegalDistrictAsync(
                            codesList, null, token, standardYear)
                        .ConfigureAwait(false);
                    if (!HasValidProvenance(fetched))
                        throw new InvalidDataException(
                            "공시지가 API 집계의 값 또는 표본 수가 유효하지 않습니다.");
                    return fetched;
                }

                var cached = await _cache.GetOrFetchAsync(cacheKey, query.ForceRefresh,
                    TimeSpan.FromDays(Metadata.CacheTtlDays), FetchValidated,
                    cancellationToken).ConfigureAwait(false);
                if (!HasValidProvenance(cached.Value))
                {
                    // A semantically corrupt but JSON-valid v3 envelope is not a hit.
                    // Force-refresh repairs it atomically; network values are validated
                    // before AtomicCacheStore is allowed to persist them.
                    cached = await _cache.GetOrFetchAsync(cacheKey, forceRefresh: true,
                        TimeSpan.FromDays(Metadata.CacheTtlDays), FetchValidated,
                        cancellationToken).ConfigureAwait(false);
                }
                var data = cached.Value;

                if (data.Count == 0)
                {
                    return DataResult<DistrictDataSet>.Failure(
                        DataSourceError.NoData("공시지가", ErrorCodes.LandPriceNoData),
                        sw.Elapsed);
                }

                var origin = cached.DeliveryOrigin == DeliveryOrigin.Cache
                    ? DataOrigin.Cache : DataOrigin.Api;

                var records = data.ToDictionary(
                    pair => pair.Key,
                    pair => new DistrictDataRecord
                    {
                        DistrictCode = pair.Key,
                        Value = pair.Value.Mean,
                        Unit = "원/㎡",
                        SampleCount = pair.Value.SampleCount,
                    }, StringComparer.Ordinal);
                var dataSet = new DistrictDataSet(records)
                {
                    RawRecordCount = data.Values.Sum(item => item.SampleCount),
                    Observation = new ObservationWindow(
                        standardYear.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        true, data.Count, data.Count),
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
                    new DataSourceError(ErrorCodes.LandPriceFailed,
                        $"공시지가 데이터 수집 실패: {safeMessage}\n" +
                        "→ API 키와 네트워크 연결을 확인해주세요.\n" +
                        "→ 발급: https://www.data.go.kr/data/15058747/openapi.do",
                        ErrorSeverity.Error, new InvalidOperationException(safeMessage)),
                    sw.Elapsed);
            }
        }

        private int ResolveStandardYear(DataQuery query)
        {
            if (query.QueryIntent != QueryIntent.ExplicitPeriod)
                return _clock.UtcNow.Year - 1;
            if (!int.TryParse(query.ExplicitPeriod,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int year) ||
                year < 1900 || year > _clock.UtcNow.Year)
                throw new ArgumentException(
                    "공시지가 명시 기간은 1900~현재연도의 4자리 stdrYear여야 합니다.",
                    nameof(query));
            return year;
        }

        private static bool HasValidProvenance(
            IReadOnlyDictionary<string, LandPriceAggregate> data)
            => data.All(pair =>
                pair.Value is not null &&
                DistrictCode.CanonicalizeLegal(pair.Key) == pair.Key &&
                double.IsFinite(pair.Value.Mean) && pair.Value.Mean > 0 &&
                pair.Value.SampleCount > 0);
    }
}
