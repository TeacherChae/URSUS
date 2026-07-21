using System.Collections.Generic;
using URSUS.Config;
using URSUS.Parsers;
using URSUS.Analysis;
using URSUS.Caching;
using URSUS.Net;

namespace URSUS.DataSources
{
    /// <summary>
    /// 서울 열린데이터 광장 — 상주인구 데이터 소스.
    ///
    /// API: VwsmAdstrdRepopW (행정동별 상주인구 통계)
    /// 원시 키: 행정동 코드 (ADSTRD_CD) → 내부에서 법정동으로 매핑
    /// </summary>
    public class SeoulResidentPopDataSource : SeoulOpenDataSourceBase
    {
        public SeoulResidentPopDataSource(ApiKeyProvider keyProvider)
            : this(keyProvider, null, null, null) { }

        public SeoulResidentPopDataSource(ApiKeyProvider keyProvider, HttpPipeline? http, IClock? clock = null,
            AtomicCacheStore? cache = null)
            : base(keyProvider, http, clock, cache) { }

        protected override MetricSemantics MetricSemantics => MetricSemantics.Sum;

        public override DataSourceMetadata Metadata { get; } = new DataSourceMetadata
        {
            Id              = "resident_pop",
            DisplayName     = "상주인구",
            Description     = "서울시 행정동별 상주인구 데이터 (서울 열린데이터 광장)",
            Category        = DataCategory.Demographic,
            Provider        = "서울 열린데이터 광장",
            UpdateFrequency = "분기별",
            CoverageArea    = "서울특별시",
            RequiredApiKeys = new[] { ApiKeyProvider.KEY_SEOUL },
            CacheTtlDays    = 30
        };

        protected override string ValueUnit => "명";

        protected override Task<SeoulAggregate> FetchRawDataAsync(
            DataSeoulApiParser parser, DataQuery query, CancellationToken cancellationToken)
            => parser.GetResidentPopByAdstrdAsync(query, cancellationToken);
    }
}
