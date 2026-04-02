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
    /// 공공데이터포털(data.go.kr) 토지이용규제정보 → 법정동별 용도지역 개발밀도 점수.
    ///
    /// - IDataSource 인터페이스 구현 → Registry에 등록하면 자동으로 파이프라인에 포함
    /// - 용도지역 유형(상업/주거/공업/녹지 등)을 1~5 점수로 변환
    /// - 점수가 높을수록 상업/도심 성격이 강한 지역
    /// - DataGoKrKey가 없으면 ValidateConfiguration에서 안내 메시지 반환
    ///
    /// 점수 해석:
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
        private readonly ApiKeyProvider _keyProvider;

        public ZoningDataSource(ApiKeyProvider keyProvider)
        {
            _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        }

        public DataSourceMetadata Metadata { get; } = new DataSourceMetadata
        {
            Id              = "zoning",
            DisplayName     = "용도지역",
            Description     = "용도지역 개발밀도 점수 (1~5) — 상업→5, 주거→3, 녹지→1 (공공데이터포털)",
            Category        = DataCategory.LandUse,
            Provider        = "공공데이터포털 (data.go.kr)",
            UpdateFrequency = "비정기 (도시계획 변경 시)",
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
                    "URS311");
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
                    new DataSourceError("URS312",
                        "용도지역 조회에 필요한 법정동 코드 목록이 없습니다.\n" +
                        "→ 경계 데이터(VWorld)가 먼저 수집되어야 합니다."),
                    sw.Elapsed);
            }

            try
            {
                var parser = new ZoningApiParser(apiKey);
                var codesList = query.DistrictCodes.ToList();

                var data = await Task.Run(
                    () => parser.GetZoningScoreByDistrict(
                        codesList, query.CacheDirectory),
                    cancellationToken);

                if (data.Count == 0)
                {
                    return DataResult<DistrictDataSet>.Failure(
                        DataSourceError.NoData("용도지역", "URS313"),
                        sw.Elapsed);
                }

                var origin = sw.Elapsed.TotalSeconds < 1
                    ? DataOrigin.Cache
                    : DataOrigin.Api;

                var dataSet = DistrictDataSet.FromDictionary(data, "점");
                sw.Stop();

                return DataResult<DistrictDataSet>.Success(dataSet, origin, sw.Elapsed);
            }
            catch (OperationCanceledException)
            {
                return DataResult<DistrictDataSet>.Failure(
                    new DataSourceError("URS314",
                        "용도지역 데이터 수집이 취소되었습니다.",
                        ErrorSeverity.Warning),
                    sw.Elapsed);
            }
            catch (Exception ex)
            {
                return DataResult<DistrictDataSet>.Failure(
                    new DataSourceError("URS315",
                        $"용도지역 데이터 수집 실패: {ex.Message}\n" +
                        "→ API 키와 네트워크 연결을 확인해주세요.\n" +
                        "→ 발급: https://www.data.go.kr/data/15056930/openapi.do",
                        ErrorSeverity.Error, ex),
                    sw.Elapsed);
            }
        }
    }
}
