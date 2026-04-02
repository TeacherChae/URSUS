namespace URSUS.DataSources
{
    /// <summary>
    /// 모든 데이터 소스가 구현해야 할 공통 인터페이스.
    ///
    /// 설계 원칙:
    /// - 새 데이터 소스 추가 시 이 인터페이스만 구현하면 됨 (기존 코드 변경 불필요)
    /// - 쿼리(DataQuery)와 결과(DataResult)를 표준화하여 파이프라인 조합 가능
    /// - 에러는 예외가 아닌 DataResult.Failure로 반환 → 호출부에서 안전하게 처리
    /// - 메타데이터(DataSourceMetadata)로 UI 동적 구성 지원
    ///
    /// 구현 가이드:
    /// 1. Metadata 프로퍼티에 소스 정보(Id, DisplayName, Category 등) 반환
    /// 2. FetchAsync에서 DataQuery를 받아 DataResult로 반환
    /// 3. ValidateConfiguration에서 API 키 등 사전 조건 검증
    /// 4. 캐시는 소스 내부에서 DataQuery.CacheDirectory를 활용하여 처리
    ///
    /// 사용 예:
    /// <code>
    /// IDataSource source = new AvgIncomeDataSource(keyProvider);
    /// var query  = new DataQuery { CacheDirectory = cacheDir };
    /// var result = await source.FetchAsync(query);
    /// if (result.IsSuccess)
    ///     var data = result.Data;  // DistrictDataSet
    /// </code>
    /// </summary>
    public interface IDataSource
    {
        /// <summary>
        /// 데이터 소스의 메타 정보.
        /// Id, 표시 이름, 카테고리, 제공 기관, 필요 API 키 등.
        /// </summary>
        DataSourceMetadata Metadata { get; }

        /// <summary>
        /// 데이터를 수집하여 법정동 단위 결과를 반환한다.
        ///
        /// - 캐시가 유효하면 캐시에서 로드 (DataOrigin.Cache)
        /// - 캐시가 없거나 ForceRefresh면 API 호출 (DataOrigin.Api)
        /// - 에러 발생 시 DataResult.Failure 반환 (예외를 던지지 않음)
        /// </summary>
        /// <param name="query">쿼리 파라미터</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>성공 시 DistrictDataSet, 실패 시 DataSourceError 포함</returns>
        Task<DataResult<DistrictDataSet>> FetchAsync(
            DataQuery query,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 이 데이터 소스를 사용하기 위한 사전 조건을 검증한다.
        ///
        /// API 키 존재 여부, 네트워크 연결 등을 확인.
        /// 컴포넌트 로드 시 또는 실행 전에 호출하여 문제를 조기에 안내한다.
        /// </summary>
        /// <returns>
        /// null이면 유효, DataSourceError면 문제 상세 정보 반환.
        /// </returns>
        DataSourceError? ValidateConfiguration();
    }

    /// <summary>
    /// 경계(지오메트리) 데이터를 제공하는 소스용 인터페이스.
    ///
    /// IDataSource가 통계 데이터(DistrictDataSet)를 반환하는 반면,
    /// 이 인터페이스는 법정동 경계 폴리곤(BoundaryDataSet)을 반환한다.
    /// 현재는 VWorld WFS가 유일한 구현체이나,
    /// 향후 국토정보플랫폼, OpenStreetMap 등으로 확장 가능.
    /// </summary>
    public interface IBoundaryDataSource
    {
        /// <summary>데이터 소스의 메타 정보.</summary>
        DataSourceMetadata Metadata { get; }

        /// <summary>
        /// 법정동 경계 지오메트리를 수집한다.
        /// </summary>
        Task<DataResult<BoundaryDataSet>> FetchBoundariesAsync(
            DataQuery query,
            CancellationToken cancellationToken = default);

        /// <summary>사전 조건 검증.</summary>
        DataSourceError? ValidateConfiguration();
    }
}
