using System;
using System.Collections.Generic;
using System.Linq;
using URSUS.DataSources;

namespace URSUS.Config
{
    /// <summary>
    /// WeightConfig의 생성/갱신/레지스트리 연동을 담당하는 상태 관리자.
    ///
    /// 설계 원칙:
    /// - DataSourceRegistry 변경 시 WeightConfig 자동 동기화
    /// - 현재 설정을 유지하면서 데이터셋 추가/제거에 대응
    /// - lock 기반 스레드 안전성 (Grasshopper Task.Run 대응)
    ///
    /// 사용 예:
    /// <code>
    /// var registry = new DataSourceRegistry();
    /// var manager = new WeightConfigManager(registry);
    ///
    /// // 레지스트리에 소스 등록 → WeightConfig 자동 갱신
    /// registry.Register(incomeSource);
    /// registry.Register(populationSource);
    ///
    /// // 현재 설정 조회
    /// WeightConfig config = manager.Current;
    /// // → CreateEqual(["avg_income", "resident_pop"])
    ///
    /// // 사용자가 가중치 변경
    /// manager.UpdateWeight("avg_income", 0.7);
    /// // → avg_income=0.7, resident_pop=0.3 (자동 정규화)
    ///
    /// // Solver에 전달
    /// List&lt;double&gt; weights = manager.Current.ToOrderedList(dataSetIds);
    /// </code>
    /// </summary>
    public sealed class WeightConfigManager : IDisposable
    {
        private readonly object _lock = new();
        private readonly IDataSourceRegistry? _registry;
        private WeightConfig _current;
        private bool _disposed;

        // ─────────────────────────────────────────────────────────────
        //  이벤트
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// WeightConfig가 변경되었을 때 발생.
        /// GH 컴포넌트에서 UI 갱신 트리거로 활용.
        /// </summary>
        public event EventHandler<WeightConfigChangedEventArgs>? ConfigChanged;

        // ─────────────────────────────────────────────────────────────
        //  생성자
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// DataSourceRegistry와 연동하는 생성자.
        /// 레지스트리의 현재 등록된 소스 기반으로 균등 배분 초기화.
        /// 소스 등록/해제 시 자동 동기화.
        /// </summary>
        public WeightConfigManager(IDataSourceRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));

            // 현재 등록된 소스로 초기화
            var ids = registry.GetAll().Select(s => s.Metadata.Id).ToList();
            _current = ids.Count > 0
                ? WeightConfig.CreateEqual(ids)
                : WeightConfig.CreateEqual(new[] { "_placeholder" });

            // 레지스트리 이벤트 구독
            _registry.SourceRegistered += OnSourceRegistered;
            _registry.SourceUnregistered += OnSourceUnregistered;
        }

        /// <summary>
        /// 데이터셋 Id 목록으로 직접 초기화하는 생성자.
        /// 레지스트리 없이 독립적으로 사용할 때.
        /// </summary>
        public WeightConfigManager(IEnumerable<string> dataSetIds)
        {
            _current = WeightConfig.CreateEqual(dataSetIds);
        }

        /// <summary>
        /// 기존 WeightConfig로 초기화하는 생성자.
        /// </summary>
        public WeightConfigManager(WeightConfig initial)
        {
            _current = initial ?? throw new ArgumentNullException(nameof(initial));
        }

        // ─────────────────────────────────────────────────────────────
        //  상태 조회
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 현재 WeightConfig (불변 스냅샷).
        /// </summary>
        public WeightConfig Current
        {
            get
            {
                lock (_lock)
                {
                    return _current;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  상태 변경
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 특정 데이터셋의 가중치를 변경한다.
        /// 나머지 데이터셋은 자동 재정규화된다.
        /// </summary>
        public void UpdateWeight(string dataSetId, double newWeight)
        {
            WeightConfig prev, next;
            lock (_lock)
            {
                prev = _current;
                next = _current.WithWeight(dataSetId, newWeight);
                _current = next;
            }
            RaiseConfigChanged(prev, next, WeightChangeReason.UserModified);
        }

        /// <summary>
        /// 전체 가중치를 한번에 교체한다.
        /// </summary>
        public void SetWeights(IDictionary<string, double> weights)
        {
            WeightConfig prev, next;
            lock (_lock)
            {
                prev = _current;
                next = WeightConfig.Create(weights);
                _current = next;
            }
            RaiseConfigChanged(prev, next, WeightChangeReason.UserModified);
        }

        /// <summary>
        /// 균등 배분으로 리셋한다.
        /// </summary>
        public void ResetToEqual()
        {
            WeightConfig prev, next;
            lock (_lock)
            {
                prev = _current;
                next = WeightConfig.CreateEqual(_current.Weights.Keys);
                _current = next;
            }
            RaiseConfigChanged(prev, next, WeightChangeReason.ResetToEqual);
        }

        /// <summary>
        /// 외부에서 생성한 WeightConfig를 직접 설정한다.
        /// </summary>
        public void SetConfig(WeightConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            WeightConfig prev;
            lock (_lock)
            {
                prev = _current;
                _current = config;
            }
            RaiseConfigChanged(prev, config, WeightChangeReason.UserModified);
        }

        // ─────────────────────────────────────────────────────────────
        //  레지스트리 연동 (자동 동기화)
        // ─────────────────────────────────────────────────────────────

        private void OnSourceRegistered(object? sender, DataSourceRegistryEventArgs e)
        {
            WeightConfig prev, next;
            lock (_lock)
            {
                prev = _current;
                if (_current.Weights.ContainsKey(e.SourceId))
                    return; // 이미 존재하면 변경 없음

                next = _current.WithAddedDataSet(e.SourceId);
                _current = next;
            }
            RaiseConfigChanged(prev, next, WeightChangeReason.DataSetAdded);
        }

        private void OnSourceUnregistered(object? sender, DataSourceRegistryEventArgs e)
        {
            WeightConfig prev, next;
            lock (_lock)
            {
                prev = _current;
                if (!_current.Weights.ContainsKey(e.SourceId))
                    return; // 존재하지 않으면 변경 없음

                if (_current.Count <= 1)
                    return; // 마지막 하나는 제거하지 않음

                next = _current.WithRemovedDataSet(e.SourceId);
                _current = next;
            }
            RaiseConfigChanged(prev, next, WeightChangeReason.DataSetRemoved);
        }

        // ─────────────────────────────────────────────────────────────
        //  이벤트 발생
        // ─────────────────────────────────────────────────────────────

        private void RaiseConfigChanged(
            WeightConfig previous, WeightConfig current, WeightChangeReason reason)
        {
            ConfigChanged?.Invoke(this,
                new WeightConfigChangedEventArgs(previous, current, reason));
        }

        // ─────────────────────────────────────────────────────────────
        //  Dispose
        // ─────────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_registry != null)
            {
                _registry.SourceRegistered -= OnSourceRegistered;
                _registry.SourceUnregistered -= OnSourceUnregistered;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  이벤트 인자 / 열거형
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>WeightConfig 변경 사유</summary>
    public enum WeightChangeReason
    {
        /// <summary>사용자가 가중치를 수동 변경</summary>
        UserModified,

        /// <summary>균등 배분으로 리셋</summary>
        ResetToEqual,

        /// <summary>데이터셋이 추가되어 자동 갱신</summary>
        DataSetAdded,

        /// <summary>데이터셋이 제거되어 자동 갱신</summary>
        DataSetRemoved
    }

    /// <summary>WeightConfig 변경 이벤트 인자</summary>
    public class WeightConfigChangedEventArgs : EventArgs
    {
        /// <summary>변경 전 설정</summary>
        public WeightConfig Previous { get; }

        /// <summary>변경 후 설정</summary>
        public WeightConfig Current { get; }

        /// <summary>변경 사유</summary>
        public WeightChangeReason Reason { get; }

        public WeightConfigChangedEventArgs(
            WeightConfig previous, WeightConfig current, WeightChangeReason reason)
        {
            Previous = previous;
            Current = current;
            Reason = reason;
        }
    }
}
