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
        private readonly ApiKeyProvider _keyProvider;

        public LandPriceDataSource(ApiKeyProvider keyProvider)
        {
            _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
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
                    "URS301");
            }
            return null;
        }

        public async Task<DataResult<DistrictDataSet>> FetchAsync(
            DataQuery query,
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();

            // 사전 조건 검증
            var configError = ValidateConfiguration();
            if (configError != null)
                return DataResult<DistrictDataSet>.Failure(configError, sw.Elapsed);

            string apiKey = _keyProvider.DataGoKrKey!;

            // 법정동 코드 목록이 없으면 에러
            if (query.DistrictCodes == null || query.DistrictCodes.Count == 0)
            {
                return DataResult<DistrictDataSet>.Failure(
                    new DataSourceError("URS302",
                        "공시지가 조회에 필요한 법정동 코드 목록이 없습니다.\n" +
                        "→ 경계 데이터(VWorld)가 먼저 수집되어야 합니다."),
                    sw.Elapsed);
            }

            try
            {
                // 동기 파서를 Task.Run으로 래핑하여 비동기 인터페이스 충족
                var parser = new LandPriceApiParser(apiKey);
                var codesList = query.DistrictCodes.ToList();

                var data = await Task.Run(
                    () => parser.GetLandPriceByLegalDistrict(
                        codesList, query.CacheDirectory),
                    cancellationToken);

                if (data.Count == 0)
                {
                    return DataResult<DistrictDataSet>.Failure(
                        DataSourceError.NoData("공시지가", "URS303"),
                        sw.Elapsed);
                }

                var origin = sw.Elapsed.TotalSeconds < 1
                    ? DataOrigin.Cache
                    : DataOrigin.Api;

                var dataSet = DistrictDataSet.FromDictionary(data, "원/㎡");
                sw.Stop();

                return DataResult<DistrictDataSet>.Success(dataSet, origin, sw.Elapsed);
            }
            catch (OperationCanceledException)
            {
                return DataResult<DistrictDataSet>.Failure(
                    new DataSourceError("URS304",
                        "공시지가 데이터 수집이 취소되었습니다.",
                        ErrorSeverity.Warning),
                    sw.Elapsed);
            }
            catch (Exception ex)
            {
                return DataResult<DistrictDataSet>.Failure(
                    new DataSourceError("URS305",
                        $"공시지가 데이터 수집 실패: {ex.Message}\n" +
                        "→ API 키와 네트워크 연결을 확인해주세요.\n" +
                        "→ 발급: https://www.data.go.kr/data/15058747/openapi.do",
                        ErrorSeverity.Error, ex),
                    sw.Elapsed);
            }
        }
    }
}
