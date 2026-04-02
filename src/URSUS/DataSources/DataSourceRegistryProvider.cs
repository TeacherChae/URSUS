using System;

namespace URSUS.DataSources
{
    /// <summary>
    /// DataSourceRegistry의 싱글턴 접근자.
    ///
    /// Grasshopper 플러그인 생명주기에서 레지스트리를 공유하기 위한 정적 진입점.
    /// GH_AssemblyInfo.OnLoad()에서 초기화하고,
    /// 각 컴포넌트에서 Instance로 접근한다.
    ///
    /// 설계 결정:
    /// - Lazy 초기화: 최초 접근 시 자동 생성 (명시적 Initialize 호출 없이도 동작)
    /// - 교체 가능: 테스트 시 모킹된 레지스트리로 교체 가능
    /// - Grasshopper는 단일 AppDomain이므로 static이 안전
    ///
    /// 사용 예 (GH 컴포넌트):
    /// <code>
    /// var registry = DataSourceRegistryProvider.Instance;
    /// var sources  = registry.GetAll();
    /// </code>
    ///
    /// 사용 예 (플러그인 초기화):
    /// <code>
    /// // GH_AssemblyInfo.OnLoad()
    /// var registry = DataSourceRegistryProvider.Instance;
    /// DefaultDataSourceBootstrapper.RegisterAll(registry, keyProvider);
    /// </code>
    /// </summary>
    public static class DataSourceRegistryProvider
    {
        private static readonly object _lock = new();
        private static IDataSourceRegistry? _instance;

        /// <summary>
        /// 전역 레지스트리 인스턴스.
        /// 최초 접근 시 기본 DataSourceRegistry가 자동 생성된다.
        /// </summary>
        public static IDataSourceRegistry Instance
        {
            get
            {
                if (_instance != null) return _instance;

                lock (_lock)
                {
                    _instance ??= new DataSourceRegistry();
                }

                return _instance;
            }
        }

        /// <summary>
        /// 레지스트리 인스턴스를 교체한다.
        ///
        /// 용도:
        /// - 테스트 시 모킹된 레지스트리 주입
        /// - 플러그인 재로드 시 새 인스턴스로 교체
        /// </summary>
        /// <param name="registry">새 레지스트리 (null이면 다음 Instance 접근 시 자동 생성)</param>
        public static void SetInstance(IDataSourceRegistry? registry)
        {
            lock (_lock)
            {
                _instance = registry;
            }
        }

        /// <summary>
        /// 레지스트리가 초기화되었는지 확인한다.
        /// 아직 접근된 적 없으면 false.
        /// </summary>
        public static bool IsInitialized
        {
            get
            {
                lock (_lock)
                {
                    return _instance != null;
                }
            }
        }

        /// <summary>
        /// 레지스트리를 초기화 해제한다 (테스트 정리용).
        /// </summary>
        internal static void Reset()
        {
            lock (_lock)
            {
                _instance = null;
            }
        }
    }
}
