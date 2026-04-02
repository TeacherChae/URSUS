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
        /// 법정동 코드 (10자리).
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

        /// <summary>
        /// 수집된 총 원시 레코드 수 (집계 전).
        /// </summary>
        public int RawRecordCount { get; init; }

        /// <summary>
        /// 법정동 매핑에 실패한 행정동 코드 수.
        /// 0이면 모든 데이터가 정상 매핑됨.
        /// </summary>
        public int UnmappedCount { get; init; }

        public DistrictDataSet(IReadOnlyDictionary<string, DistrictDataRecord> records)
        {
            Records = records;
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
                records[code] = new DistrictDataRecord
                {
                    DistrictCode = code,
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
                result[code] = record.Value;
            return result;
        }
    }
}
