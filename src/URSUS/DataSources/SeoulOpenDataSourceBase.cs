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
        protected readonly ApiKeyProvider KeyProvider;

        protected SeoulOpenDataSourceBase(ApiKeyProvider keyProvider)
        {
            KeyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        }

        /// <inheritdoc />
        public abstract DataSourceMetadata Metadata { get; }

        /// <summary>
        /// 서브클래스에서 구현: DataSeoulApiParser를 사용해 행정동 기준 원시 데이터를 반환.
        /// </summary>
        /// <param name="parser">서울 열린데이터 API 파서</param>
        /// <param name="cacheDir">캐시 디렉토리 (null 가능)</param>
        /// <returns>행정동 코드 → 값 딕셔너리</returns>
        protected abstract Dictionary<string, double> FetchRawData(
            DataSeoulApiParser parser, string? cacheDir);

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
                    "URS201");
            }
            return null;
        }

        /// <inheritdoc />
        public async Task<DataResult<DistrictDataSet>> FetchAsync(
            DataQuery query,
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();

            // 사전 조건 검증
            var configError = ValidateConfiguration();
            if (configError != null)
                return DataResult<DistrictDataSet>.Failure(configError, sw.Elapsed);

            string apiKey = KeyProvider.SeoulKey!;

            try
            {
                // 동기 파서를 Task.Run으로 래핑하여 비동기 인터페이스 충족
                var parser = new DataSeoulApiParser(apiKey);

                var rawData = await Task.Run(
                    () => FetchRawData(parser, query.CacheDirectory),
                    cancellationToken);

                if (rawData.Count == 0)
                {
                    return DataResult<DistrictDataSet>.Failure(
                        DataSourceError.NoData(Metadata.DisplayName, $"URS{Metadata.Id.GetHashCode() % 900 + 200:D3}"),
                        sw.Elapsed);
                }

                // 행정동 → 법정동 매핑
                var adstrdToLegald = MappingLoader.Load();
                var legaldData = MapToLegalDistrict(adstrdToLegald, rawData);

                var origin = sw.Elapsed.TotalSeconds < 1
                    ? DataOrigin.Cache
                    : DataOrigin.Api;

                var dataSet = DistrictDataSet.FromDictionary(legaldData, ValueUnit);
                dataSet = new DistrictDataSet(dataSet.Records)
                {
                    RawRecordCount = rawData.Count,
                    UnmappedCount  = rawData.Count - CountMappedKeys(adstrdToLegald, rawData)
                };

                sw.Stop();
                return DataResult<DistrictDataSet>.Success(dataSet, origin, sw.Elapsed);
            }
            catch (OperationCanceledException)
            {
                return DataResult<DistrictDataSet>.Failure(
                    new DataSourceError("URS210",
                        $"{Metadata.DisplayName} 데이터 수집이 취소되었습니다.",
                        ErrorSeverity.Warning),
                    sw.Elapsed);
            }
            catch (Exception ex)
            {
                return DataResult<DistrictDataSet>.Failure(
                    new DataSourceError("URS211",
                        $"{Metadata.DisplayName} 데이터 수집 실패: {ex.Message}\n" +
                        "→ API 키와 네트워크 연결을 확인해주세요.\n" +
                        "→ 발급: https://data.seoul.go.kr/",
                        ErrorSeverity.Error, ex),
                    sw.Elapsed);
            }
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
