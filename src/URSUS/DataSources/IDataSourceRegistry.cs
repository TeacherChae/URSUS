using System;
using System.Collections.Generic;

namespace URSUS.DataSources
{
    /// <summary>
    /// 데이터 소스 등록/해제 이벤트 인자.
    /// </summary>
    public class DataSourceRegistryEventArgs : EventArgs
    {
        /// <summary>등록/해제된 데이터 소스의 ID</summary>
        public string SourceId { get; }

        /// <summary>등록/해제된 데이터 소스의 메타데이터</summary>
        public DataSourceMetadata Metadata { get; }

        public DataSourceRegistryEventArgs(string sourceId, DataSourceMetadata metadata)
        {
            SourceId = sourceId;
            Metadata = metadata;
        }
    }

    /// <summary>
    /// 데이터 소스를 등록/조회하는 레지스트리.
    ///
    /// 새 데이터 소스를 추가할 때:
    ///   1. IDataSource 구현 클래스 작성
    ///   2. Registry에 Register() 호출
    ///   → 기존 코드 수정 없이 파이프라인에 자동 포함
    ///
    /// GH 컴포넌트에서는 GetAll()로 Value List를 동적 생성하고,
    /// Solver에서는 GetById()로 선택된 소스만 실행한다.
    ///
    /// 확장 시나리오:
    ///   - 외부 플러그인이 Register()로 커스텀 데이터 소스 추가
    ///   - SourceRegistered 이벤트로 UI 자동 갱신
    ///   - GetByCategory()로 카테고리별 그룹핑
    /// </summary>
    public interface IDataSourceRegistry
    {
        // ─────────────────────────────────────────────────────────────
        //  등록 / 해제
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 통계 데이터 소스를 등록한다.
        /// 동일한 Id가 이미 있으면 덮어쓴다.
        /// </summary>
        void Register(IDataSource source);

        /// <summary>
        /// 경계 데이터 소스를 등록한다.
        /// </summary>
        void RegisterBoundary(IBoundaryDataSource source);

        /// <summary>
        /// Id로 통계 데이터 소스를 해제한다.
        /// </summary>
        /// <returns>해제 성공 여부 (해당 Id가 없으면 false)</returns>
        bool Unregister(string id);

        /// <summary>
        /// 등록된 모든 소스를 제거한다 (경계 소스 포함).
        /// 재초기화 시 사용.
        /// </summary>
        void Clear();

        // ─────────────────────────────────────────────────────────────
        //  조회
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Id로 통계 데이터 소스를 조회한다.
        /// </summary>
        /// <returns>해당 소스, 없으면 null</returns>
        IDataSource? GetById(string id);

        /// <summary>
        /// Id로 통계 데이터 소스 존재 여부를 확인한다.
        /// </summary>
        bool Contains(string id);

        /// <summary>
        /// 경계 데이터 소스를 조회한다.
        /// </summary>
        IBoundaryDataSource? GetBoundarySource();

        /// <summary>
        /// 등록된 모든 통계 데이터 소스를 반환한다.
        /// </summary>
        IReadOnlyList<IDataSource> GetAll();

        /// <summary>
        /// 카테고리별로 통계 데이터 소스를 조회한다.
        /// </summary>
        IReadOnlyList<IDataSource> GetByCategory(DataCategory category);

        /// <summary>
        /// 등록된 모든 소스의 메타데이터를 반환한다.
        /// GH Value List 동적 생성에 사용.
        /// </summary>
        IReadOnlyList<DataSourceMetadata> GetAllMetadata();

        /// <summary>
        /// 등록된 통계 데이터 소스 수.
        /// </summary>
        int Count { get; }

        // ─────────────────────────────────────────────────────────────
        //  검증
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 등록된 모든 소스의 설정(API 키 등)을 일괄 검증한다.
        /// </summary>
        /// <returns>
        /// Id → DataSourceError 딕셔너리. 에러가 없는 소스는 포함되지 않음.
        /// 빈 딕셔너리면 모든 소스가 정상.
        /// </returns>
        IReadOnlyDictionary<string, DataSourceError> ValidateAll();

        // ─────────────────────────────────────────────────────────────
        //  이벤트
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 데이터 소스가 등록되었을 때 발생.
        /// GH 컴포넌트에서 Value List 갱신 등에 활용.
        /// </summary>
        event EventHandler<DataSourceRegistryEventArgs>? SourceRegistered;

        /// <summary>
        /// 데이터 소스가 해제되었을 때 발생.
        /// </summary>
        event EventHandler<DataSourceRegistryEventArgs>? SourceUnregistered;
    }
}
