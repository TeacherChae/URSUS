namespace URSUS.DataSources
{
    /// <summary>
    /// 법정동/행정동 단위의 통계 데이터 레코드.
    ///
    /// 모든 통계 데이터 소스(소득, 인구, 교통 등)의 공통 출력 단위.
    /// 데이터 소스가 행정동 기준이면 매핑 후 법정동 단위로 변환하여 이 레코드를 생성한다.
    /// </summary>
    public record DistrictDataRecord
    {
        /// <summary>
        /// 법정동 canonical ID (VWorld emd_cd와 동일한 8자리).
        /// 모든 데이터 소스가 동일한 코드 체계를 사용해야 비교/합산이 가능하다.
        /// </summary>
        public required string DistrictCode { get; init; }

        /// <summary>
        /// 데이터 값 (정규화 전 원시 값).
        /// 예: 월평균 소득(원), 상주인구(명), 일평균 승차 수(명)
        /// </summary>
        public required double Value { get; init; }

        /// <summary>
        /// 값의 단위 (사용자 표시용).
        /// 예: "원", "명", "건"
        /// </summary>
        public string? Unit { get; init; }

        /// <summary>
        /// Number of source observations contributing to <see cref="Value"/>.
        /// Null means that the source does not expose sample-count provenance.
        /// </summary>
        public int? SampleCount { get; init; }
    }

    /// <summary>
    /// 통계 데이터 소스의 결과 집합.
    ///
    /// 법정동 코드 → 값 딕셔너리와 함께
    /// 데이터 품질 정보(수집 건수, 누락 건수 등)를 제공한다.
    /// </summary>
    public class DistrictDataSet
    {
        /// <summary>
        /// 법정동 코드별 데이터 레코드.
        /// </summary>
        public IReadOnlyDictionary<string, DistrictDataRecord> Records { get; }

        /// <summary>Per-district source sample counts when supplied by the source.</summary>
        public IReadOnlyDictionary<string, int> SampleCounts { get; }

        /// <summary>
        /// 집계 값에 실제로 기여한 원천 관측치 수.
        /// 파서가 유효하지 않은 행을 제외하는 경우 전체 수신 행 수와 다를 수 있다.
        /// </summary>
        public int RawRecordCount { get; init; }

        /// <summary>
        /// 법정동 매핑에 실패한 행정동 코드 수.
        /// 0이면 모든 데이터가 정상 매핑됨.
        /// </summary>
        public int UnmappedCount { get; init; }

        /// <summary>선택된 단일 관측기간과 완결성.</summary>
        public ObservationWindow? Observation { get; init; }

        /// <summary>행정동→법정동 변환 시 사용된 불확실성 정책.</summary>
        public Analysis.MappingQuality? MappingQuality { get; init; }

        /// <summary>expected-set, pagination, mapping 등 구조화된 품질 경고.</summary>
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

        /// <summary>범주형 원천을 숫자 overlay로 투영하기 전의 법정동별 histogram.</summary>
        public IReadOnlyDictionary<string, Analysis.ZoningCategoryHistogram> CategoricalHistograms
            { get; init; } = new Dictionary<string, Analysis.ZoningCategoryHistogram>();

        public DistrictDataSet(IReadOnlyDictionary<string, DistrictDataRecord> records)
        {
            Records = new System.Collections.ObjectModel.ReadOnlyDictionary<string, DistrictDataRecord>(
                new Dictionary<string, DistrictDataRecord>(records, StringComparer.Ordinal));
            SampleCounts = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(
                records.Where(pair => pair.Value.SampleCount.HasValue)
                    .ToDictionary(pair => pair.Key, pair => pair.Value.SampleCount!.Value,
                        StringComparer.Ordinal));
        }

        /// <summary>
        /// 기존 Dictionary&lt;string, double&gt; 형식에서 변환.
        /// 레거시 파서 호환용.
        /// </summary>
        public static DistrictDataSet FromDictionary(
            Dictionary<string, double> data, string? unit = null)
        {
            var records = new Dictionary<string, DistrictDataRecord>();
            foreach (var (code, value) in data)
            {
                string canonical = DistrictCode.CanonicalizeLegal(code);
                if (string.IsNullOrEmpty(canonical))
                    continue;
                records[canonical] = new DistrictDataRecord
                {
                    DistrictCode = canonical,
                    Value        = value,
                    Unit         = unit
                };
            }

            return new DistrictDataSet(records) { RawRecordCount = data.Count };
        }

        /// <summary>
        /// 기존 Dictionary&lt;string, double&gt; 형식으로 변환.
        /// 레거시 코드(URSUSSolver 등)와의 호환용.
        /// </summary>
        public Dictionary<string, double> ToDictionary()
        {
            var result = new Dictionary<string, double>();
            foreach (var (code, record) in Records)
            {
                string canonical = DistrictCode.CanonicalizeLegal(code);
                if (!string.IsNullOrEmpty(canonical))
                    result[canonical] = record.Value;
            }
            return result;
        }
    }
}
