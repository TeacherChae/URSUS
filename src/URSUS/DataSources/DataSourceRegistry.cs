using System;
using System.Collections.Generic;
using System.Linq;

namespace URSUS.DataSources
{
    /// <summary>
    /// IDataSourceRegistry 기본 구현.
    ///
    /// 설계 결정:
    /// - lock 기반 스레드 안전성: Grasshopper는 메인 스레드에서 동작하지만,
    ///   FetchAsync 내부에서 Task.Run을 사용하므로 안전하게 보호
    /// - 이벤트: SourceRegistered/Unregistered로 UI 연동 지원
    /// - 덮어쓰기: 동일 Id 재등록 시 기존 소스를 교체 (업그레이드 시나리오)
    ///
    /// 사용 예:
    /// <code>
    /// var registry = new DataSourceRegistry();
    /// registry.Register(new SeoulAvgIncomeDataSource(keyProvider));
    /// registry.Register(new LandPriceDataSource(keyProvider));
    ///
    /// // 카테고리별 조회
    /// var demographic = registry.GetByCategory(DataCategory.Demographic);
    ///
    /// // 일괄 검증
    /// var errors = registry.ValidateAll();
    /// foreach (var (id, error) in errors)
    ///     Console.WriteLine($"{id}: {error}");
    /// </code>
    /// </summary>
    public class DataSourceRegistry : IDataSourceRegistry
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, IDataSource> _sources = new();
        private IBoundaryDataSource? _boundarySource;

        // ─────────────────────────────────────────────────────────────
        //  이벤트
        // ─────────────────────────────────────────────────────────────

        /// <inheritdoc />
        public event EventHandler<DataSourceRegistryEventArgs>? SourceRegistered;

        /// <inheritdoc />
        public event EventHandler<DataSourceRegistryEventArgs>? SourceUnregistered;

        // ─────────────────────────────────────────────────────────────
        //  등록 / 해제
        // ─────────────────────────────────────────────────────────────

        /// <inheritdoc />
        public void Register(IDataSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(source.Metadata.Id))
                throw new ArgumentException("데이터 소스의 Id가 비어있습니다.", nameof(source));

            lock (_lock)
            {
                _sources[source.Metadata.Id] = source;
            }

            SourceRegistered?.Invoke(this, new DataSourceRegistryEventArgs(
                source.Metadata.Id, source.Metadata));
        }

        /// <inheritdoc />
        public void RegisterBoundary(IBoundaryDataSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            lock (_lock)
            {
                _boundarySource = source;
            }
        }

        /// <inheritdoc />
        public bool Unregister(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            DataSourceMetadata? metadata;
            lock (_lock)
            {
                if (!_sources.TryGetValue(id, out var source))
                    return false;

                metadata = source.Metadata;
                _sources.Remove(id);
            }

            SourceUnregistered?.Invoke(this, new DataSourceRegistryEventArgs(
                id, metadata));
            return true;
        }

        /// <inheritdoc />
        public void Clear()
        {
            lock (_lock)
            {
                _sources.Clear();
                _boundarySource = null;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  조회
        // ─────────────────────────────────────────────────────────────

        /// <inheritdoc />
        public IDataSource? GetById(string id)
        {
            lock (_lock)
            {
                return _sources.TryGetValue(id, out var source) ? source : null;
            }
        }

        /// <inheritdoc />
        public bool Contains(string id)
        {
            lock (_lock)
            {
                return _sources.ContainsKey(id);
            }
        }

        /// <inheritdoc />
        public IBoundaryDataSource? GetBoundarySource()
        {
            lock (_lock)
            {
                return _boundarySource;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<IDataSource> GetAll()
        {
            lock (_lock)
            {
                return _sources.Values.ToList().AsReadOnly();
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<IDataSource> GetByCategory(DataCategory category)
        {
            lock (_lock)
            {
                return _sources.Values
                    .Where(s => s.Metadata.Category == category)
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<DataSourceMetadata> GetAllMetadata()
        {
            lock (_lock)
            {
                return _sources.Values
                    .Select(s => s.Metadata)
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <inheritdoc />
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _sources.Count;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  검증
        // ─────────────────────────────────────────────────────────────

        /// <inheritdoc />
        public IReadOnlyDictionary<string, DataSourceError> ValidateAll()
        {
            List<IDataSource> snapshot;
            lock (_lock)
            {
                snapshot = _sources.Values.ToList();
            }

            var errors = new Dictionary<string, DataSourceError>();
            foreach (var source in snapshot)
            {
                var error = source.ValidateConfiguration();
                if (error != null)
                    errors[source.Metadata.Id] = error;
            }

            // 경계 소스도 검증
            IBoundaryDataSource? boundary;
            lock (_lock)
            {
                boundary = _boundarySource;
            }

            if (boundary != null)
            {
                var boundaryError = boundary.ValidateConfiguration();
                if (boundaryError != null)
                    errors[boundary.Metadata.Id] = boundaryError;
            }

            return errors;
        }
    }
}
