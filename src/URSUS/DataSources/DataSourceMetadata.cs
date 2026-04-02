namespace URSUS.DataSources
{
    /// <summary>
    /// 데이터 소스의 메타 정보.
    ///
    /// GH 컴포넌트 UI에서 데이터셋 목록을 동적으로 표시하거나,
    /// 사용자에게 각 데이터 소스의 출처/갱신 주기를 안내할 때 사용.
    /// </summary>
    public record DataSourceMetadata
    {
        /// <summary>
        /// 고유 식별자 (코드에서 사용).
        /// 예: "avg_income", "resident_pop", "boundary"
        /// </summary>
        public required string Id { get; init; }

        /// <summary>
        /// 사용자에게 표시할 이름 (한글).
        /// 예: "월평균 소득", "상주인구"
        /// </summary>
        public required string DisplayName { get; init; }

        /// <summary>
        /// 설명 (한글, 1~2문장).
        /// 예: "서울시 행정동별 월 평균 소득 데이터 (서울 열린데이터)"
        /// </summary>
        public required string Description { get; init; }

        /// <summary>
        /// 데이터 카테고리.
        /// </summary>
        public required DataCategory Category { get; init; }

        /// <summary>
        /// 데이터 제공 기관/출처명.
        /// 예: "서울 열린데이터 광장", "VWorld"
        /// </summary>
        public required string Provider { get; init; }

        /// <summary>
        /// 데이터 갱신 주기 설명.
        /// 예: "분기별", "연 1회", "실시간"
        /// </summary>
        public string? UpdateFrequency { get; init; }

        /// <summary>
        /// 지원 지역 범위.
        /// 예: "서울특별시", "전국"
        /// </summary>
        public string? CoverageArea { get; init; }

        /// <summary>
        /// 필요한 API 키 이름 목록 (ApiKeyProvider 키 이름과 일치).
        /// 빈 목록이면 API 키 없이 사용 가능.
        /// </summary>
        public IReadOnlyList<string> RequiredApiKeys { get; init; } = Array.Empty<string>();

        /// <summary>
        /// 기본 캐시 TTL (일 단위).
        /// </summary>
        public int CacheTtlDays { get; init; } = 30;
    }

    /// <summary>데이터 카테고리 — 관련 데이터를 그룹핑</summary>
    public enum DataCategory
    {
        /// <summary>법정동/행정동 경계, 지형 등 공간 데이터</summary>
        Boundary,

        /// <summary>인구, 소득 등 인구통계 데이터</summary>
        Demographic,

        /// <summary>대중교통, 도로 등 교통 데이터</summary>
        Transportation,

        /// <summary>환경, 소음, 녹지 등</summary>
        Environment,

        /// <summary>토지이용, 용도지역 등 도시계획 데이터</summary>
        LandUse,

        /// <summary>기타</summary>
        Other
    }
}
