using System;
using System.Collections.Generic;
using URSUS.Config;

namespace URSUS.DataSources
{
    /// <summary>
    /// 내장 데이터 소스를 레지스트리에 일괄 등록하는 부트스트래퍼.
    ///
    /// GH 플러그인 로드 시 한 번 호출하면 모든 내장 소스가 등록된다.
    /// 외부 플러그인은 이후 Registry.Register()로 추가 소스를 등록할 수 있다.
    ///
    /// 사용 예 (GH_AssemblyInfo.OnLoad):
    /// <code>
    /// var keyProvider = new ApiKeyProvider();
    /// var registry = DataSourceRegistryProvider.Instance;
    /// DefaultDataSourceBootstrapper.RegisterAll(registry, keyProvider);
    /// </code>
    ///
    /// 확장 가이드:
    /// 새 데이터 소스를 추가할 때:
    ///   1. IDataSource 구현 클래스를 DataSources/ 폴더에 작성
    ///   2. RegisterStatisticSources() 메서드에 생성자 호출 추가
    ///   3. 끝! — Registry를 통해 파이프라인·UI에 자동 반영
    /// </summary>
    public static class DefaultDataSourceBootstrapper
    {
        /// <summary>
        /// 모든 내장 데이터 소스를 레지스트리에 등록한다.
        /// 이미 등록된 소스가 있으면 덮어쓴다 (재호출 안전).
        /// </summary>
        /// <param name="registry">대상 레지스트리</param>
        /// <param name="keyProvider">API 키 제공자</param>
        /// <returns>등록된 통계 소스 수</returns>
        public static int RegisterAll(IDataSourceRegistry registry, ApiKeyProvider keyProvider)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (keyProvider == null) throw new ArgumentNullException(nameof(keyProvider));

            int count = 0;
            count += RegisterStatisticSources(registry, keyProvider);
            // 향후: RegisterBoundarySources(registry, keyProvider);
            return count;
        }

        /// <summary>
        /// 통계 데이터 소스만 등록한다.
        /// </summary>
        /// <returns>등록된 소스 수</returns>
        public static int RegisterStatisticSources(
            IDataSourceRegistry registry, ApiKeyProvider keyProvider)
        {
            var sources = CreateBuiltInSources(keyProvider);
            foreach (var source in sources)
            {
                registry.Register(source);
            }
            return sources.Count;
        }

        /// <summary>
        /// 내장 데이터 소스 인스턴스를 생성한다.
        ///
        /// 새 데이터 소스를 추가할 때 이 메서드에 한 줄만 추가하면 된다.
        /// API 키가 없는 소스도 등록됨 — ValidateConfiguration()에서 런타임에 검증.
        /// </summary>
        internal static IReadOnlyList<IDataSource> CreateBuiltInSources(ApiKeyProvider keyProvider)
        {
            return new List<IDataSource>
            {
                // ── 인구통계 (Demographic) ───────────────────────────
                new SeoulAvgIncomeDataSource(keyProvider),
                new SeoulResidentPopDataSource(keyProvider),

                // ── 교통 (Transportation) ────────────────────────────
                new SeoulTransitDataSource(keyProvider),

                // ── 토지이용 (LandUse) ───────────────────────────────
                new LandPriceDataSource(keyProvider),

                // ── 새 데이터 소스 추가 시 여기에 한 줄 추가 ─────────
                // new NoiseDataSource(keyProvider),
                // new GreenAreaDataSource(keyProvider),
            };
        }

        /// <summary>
        /// 레지스트리의 현재 상태를 진단 문자열로 반환한다.
        /// 디버깅 및 사용자 안내에 사용.
        /// </summary>
        public static string GetDiagnosticSummary(IDataSourceRegistry registry)
        {
            if (registry == null) return "[Registry: null]";

            var lines = new List<string>
            {
                $"[DataSourceRegistry] 등록된 소스: {registry.Count}개"
            };

            var metadata = registry.GetAllMetadata();
            foreach (var m in metadata)
            {
                lines.Add($"  - {m.Id}: {m.DisplayName} ({m.Category}, {m.Provider})");
            }

            var boundary = registry.GetBoundarySource();
            lines.Add(boundary != null
                ? $"  - [경계] {boundary.Metadata.DisplayName} ({boundary.Metadata.Provider})"
                : "  - [경계] 미등록");

            // 검증 결과
            var errors = registry.ValidateAll();
            if (errors.Count > 0)
            {
                lines.Add($"  ⚠ 설정 오류 {errors.Count}건:");
                foreach (var (id, error) in errors)
                    lines.Add($"    - {id}: {error.Message.Split('\n')[0]}");
            }
            else
            {
                lines.Add("  ✓ 모든 소스 설정 정상");
            }

            return string.Join("\n", lines);
        }
    }
}
