using System.Collections.Generic;
using URSUS.Config;
using URSUS.Parsers;

namespace URSUS.DataSources
{
    /// <summary>
    /// 서울 열린데이터 광장 — 대중교통 승차 승객 수 데이터 소스.
    ///
    /// API: tpssPassengerCnt (행정동별 대중교통 이용 통계)
    /// 원시 키: 행정동 코드 (DONG_ID) → 내부에서 법정동으로 매핑
    /// </summary>
    public class SeoulTransitDataSource : SeoulOpenDataSourceBase
    {
        public SeoulTransitDataSource(ApiKeyProvider keyProvider)
            : base(keyProvider) { }

        public override DataSourceMetadata Metadata { get; } = new DataSourceMetadata
        {
            Id              = "transit",
            DisplayName     = "대중교통 총 승차 승객 수(일일 평균)",
            Description     = "서울시 행정동별 일평균 대중교통 승차 승객 수 (서울 열린데이터 광장)",
            Category        = DataCategory.Transportation,
            Provider        = "서울 열린데이터 광장",
            UpdateFrequency = "월별",
            CoverageArea    = "서울특별시",
            RequiredApiKeys = new[] { ApiKeyProvider.KEY_SEOUL },
            CacheTtlDays    = 30
        };

        protected override string ValueUnit => "명/일";

        protected override Dictionary<string, double> FetchRawData(
            DataSeoulApiParser parser, string? cacheDir)
            => parser.GetTransitBoardingByAdstrd(cacheDir);
    }
}
