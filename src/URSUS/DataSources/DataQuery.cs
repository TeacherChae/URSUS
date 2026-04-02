using Rhino.Geometry;

namespace URSUS.DataSources
{
    /// <summary>
    /// 데이터 소스에 전달하는 쿼리 파라미터.
    ///
    /// 모든 데이터 소스가 동일한 쿼리 객체를 받되,
    /// 소스마다 필요한 필드만 사용한다.
    /// 예: 경계 데이터 소스는 BoundingBox를, 통계 소스는 DistrictCodes를 참조.
    /// </summary>
    public class DataQuery
    {
        /// <summary>
        /// 분석 대상 영역의 바운딩 박스 (UTM 좌표).
        /// 경계/지리 데이터 소스에서 공간 범위 지정에 사용.
        /// </summary>
        public BoundingBox? Bounds { get; init; }

        /// <summary>
        /// 분석 대상 법정동 코드 목록.
        /// 통계 데이터 소스에서 필터링에 사용.
        /// null이면 전체 조회.
        /// </summary>
        public IReadOnlyList<string>? DistrictCodes { get; init; }

        /// <summary>
        /// 캐시 디렉토리 경로.
        /// null이면 캐시를 사용하지 않는다.
        /// </summary>
        public string? CacheDirectory { get; init; }

        /// <summary>
        /// 캐시를 강제로 무효화할지 여부.
        /// true면 기존 캐시를 무시하고 API를 재호출한다.
        /// </summary>
        public bool ForceRefresh { get; init; } = false;

        /// <summary>
        /// 소스별 추가 파라미터 (확장용).
        /// 예: 특정 API의 날짜 범위, 필터 조건 등.
        /// </summary>
        public IReadOnlyDictionary<string, string>? Parameters { get; init; }
    }
}
