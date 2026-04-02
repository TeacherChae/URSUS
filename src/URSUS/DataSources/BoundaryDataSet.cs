using Rhino.Geometry;

namespace URSUS.DataSources
{
    /// <summary>
    /// 법정동 경계 데이터의 결과 집합.
    ///
    /// IBoundaryDataSource의 반환 타입.
    /// 기존 LegalDistrictRecord 리스트와 호환되면서,
    /// 추가 메타 정보(수집 범위, 필터링 결과 등)를 제공한다.
    /// </summary>
    public class BoundaryDataSet
    {
        /// <summary>
        /// 법정동 경계 레코드 목록.
        /// </summary>
        public IReadOnlyList<BoundaryRecord> Records { get; }

        /// <summary>
        /// 전체 영역의 외곽선 (Boolean Union 결과).
        /// null이면 Union 실패.
        /// </summary>
        public Curve? UnionBoundary { get; init; }

        /// <summary>
        /// API에서 수집된 원시 피처 수 (필터링 전).
        /// </summary>
        public int RawFeatureCount { get; init; }

        /// <summary>
        /// 면적 필터 등으로 제외된 피처 수.
        /// </summary>
        public int FilteredOutCount { get; init; }

        public BoundaryDataSet(IReadOnlyList<BoundaryRecord> records)
        {
            Records = records;
        }
    }

    /// <summary>
    /// 법정동 경계 레코드 — IBoundaryDataSource 출력 단위.
    ///
    /// 기존 LegalDistrictRecord와 동일한 정보를 담되,
    /// DataSources 네임스페이스의 표준 타입으로 정의.
    /// </summary>
    public record BoundaryRecord
    {
        /// <summary>법정동 코드 (10자리)</summary>
        public required string DistrictCode { get; init; }

        /// <summary>법정동 전체 이름 (예: "서울특별시 종로구 청운동")</summary>
        public required string Name { get; init; }

        /// <summary>경계 폴리곤 (UTM 좌표)</summary>
        public required PolylineCurve Geometry { get; init; }

        /// <summary>면적 (㎡)</summary>
        public required double Area { get; init; }

        /// <summary>중심점 (UTM 좌표)</summary>
        public required Point3d Centroid { get; init; }
    }
}
