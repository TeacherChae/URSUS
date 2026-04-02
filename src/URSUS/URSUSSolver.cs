using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using URSUS.Config;
using URSUS.DataSources;
using URSUS.GeoOps;
using URSUS.Parsers;
using URSUS.Resources;

namespace URSUS
{
    /// <summary>
    /// 데이터 파이프라인 오케스트레이터.
    /// solver.py (URSUSSolver.run()) 포팅.
    ///
    /// v0.3.0 리팩터링: DataSeoulApiParser 직접 호출 → IDataSourceRegistry 기반으로 전환.
    /// 새 데이터 소스 추가 시 Registry에 등록만 하면 Solver가 자동으로 인식한다.
    /// </summary>
    public class URSUSSolver
    {
        private const double MIN_AREA    = 100.0;
        private const double SNAP_TOL    = 5.0;

        /// <summary>기본 분석 중심 주소 (전국 어디든 변경 가능)</summary>
        public const string DEFAULT_ADDRESS = "서울특별시 중구 세종대로 110";
        /// <summary>기본 검색 반경 (km)</summary>
        public const double DEFAULT_RADIUS_KM = 15.0;

        /// <summary>[하위 호환] 기존 BBOX 모드 기본 주소1</summary>
        [Obsolete("DEFAULT_ADDRESS + DEFAULT_RADIUS_KM 사용 권장")]
        public const string DEFAULT_ADDRESS1 = "인천 남동구 도림동";
        /// <summary>[하위 호환] 기존 BBOX 모드 기본 주소2</summary>
        [Obsolete("DEFAULT_ADDRESS + DEFAULT_RADIUS_KM 사용 권장")]
        public const string DEFAULT_ADDRESS2 = "경기 남양주시 해밀예당1로 272";

        // dataSet 항목 이름 상수 — GH Value List의 값과 일치해야 함
        public const string DS_AVG_INCOME    = "월평균 소득";
        public const string DS_RESIDENT_POP  = "상주인구";
        // public const string DS_LIVING_POP = "생활인구";  // 보류 (76만행, 성능 검토 필요)
        public const string DS_TRANSIT       = "대중교통 총 승차 승객 수(일일 평균)";
        public const string DS_LAND_PRICE    = "공시지가";
        public const string DS_ZONING        = "용도지역";

        /// <summary>
        /// 사용 가능한 전체 데이터셋 목록 (기본값으로 사용).
        /// DataSet 입력이 없을 때 이 목록이 자동 적용된다.
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultDataSets = new[]
        {
            DS_AVG_INCOME,
            DS_RESIDENT_POP,
            DS_TRANSIT
        };

        /// <summary>
        /// GH Value List 표시이름 → IDataSource.Metadata.Id 매핑.
        /// 새 데이터 소스 추가 시 여기에 한 줄 추가.
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, string> DisplayNameToSourceId
            = new Dictionary<string, string>
            {
                { DS_AVG_INCOME,   "avg_income"   },
                { DS_RESIDENT_POP, "resident_pop" },
                { DS_TRANSIT,      "transit"       },
                { DS_LAND_PRICE,   "land_price"    },
                { DS_ZONING,       "zoning"        },
            };

        private readonly string  _vworldKey;
        private readonly string  _cacheDir;
        private readonly ApiKeyProvider _keyProvider;
        private readonly IDataSourceRegistry _registry;

        /// <summary>
        /// API 키 자동 로드 생성자.
        /// 환경변수 → DLL 인접 설정 파일 → 사용자 프로필 설정 파일 순으로 자동 탐색.
        /// GH 와이어 없이도 사용 가능 (스크립트, 테스트, CLI 등).
        /// </summary>
        /// <exception cref="InvalidOperationException">필수 API 키가 누락된 경우</exception>
        public URSUSSolver()
            : this(new ApiKeyProvider())
        { }

        /// <summary>
        /// ApiKeyProvider를 직접 주입하는 생성자.
        /// GH 컴포넌트에서 명시적 오버라이드를 전달할 때 사용.
        /// DataSourceRegistry를 자동 초기화하고 내장 소스를 등록한다.
        /// </summary>
        /// <exception cref="InvalidOperationException">필수 API 키가 누락된 경우</exception>
        public URSUSSolver(ApiKeyProvider keyProvider)
            : this(keyProvider, null)
        { }

        /// <summary>
        /// ApiKeyProvider + Registry 주입 생성자 (테스트/확장용).
        /// registry가 null이면 DataSourceRegistryProvider.Instance를 사용하고
        /// 내장 소스를 자동 등록한다.
        /// </summary>
        public URSUSSolver(ApiKeyProvider keyProvider, IDataSourceRegistry? registry)
        {
            _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));

            var missing = _keyProvider.GetMissingKeys(
                ApiKeyProvider.KEY_VWORLD, ApiKeyProvider.KEY_SEOUL);
            if (missing.Count > 0)
            {
                string errorCode = missing.Contains(ApiKeyProvider.KEY_VWORLD)
                    ? ErrorCodes.VWorldKeyMissing
                    : ErrorCodes.SeoulKeyMissing;
                throw new InvalidOperationException(
                    ErrorGuideMap.FormatMessageWithGuide(
                        errorCode,
                        _keyProvider.GetDiagnosticMessage(
                            ApiKeyProvider.KEY_VWORLD, ApiKeyProvider.KEY_SEOUL)));
            }

            _vworldKey = _keyProvider.VWorldKey!;

            string dllDir = System.IO.Path.GetDirectoryName(
                typeof(URSUSSolver).Assembly.Location)!;
            _cacheDir = dllDir;

            // Registry 초기화: 외부 주입이 없으면 싱글턴 + 부트스트랩
            _registry = registry ?? DataSourceRegistryProvider.Instance;
            if (_registry.Count == 0)
            {
                DefaultDataSourceBootstrapper.RegisterAll(_registry, _keyProvider);
            }
        }

        /// <param name="vworldKey">VWorld API 키</param>
        /// <param name="seoulKey">서울 열린데이터 API 키</param>
        /// <param name="dataGoKrKey">공공데이터포털 API 키 (선택 — 공시지가용)</param>
        public URSUSSolver(string vworldKey, string seoulKey, string? dataGoKrKey = null)
        {
            _vworldKey = vworldKey;

            var overrides = new Dictionary<string, string>
            {
                { ApiKeyProvider.KEY_VWORLD, vworldKey },
                { ApiKeyProvider.KEY_SEOUL,  seoulKey  },
            };
            if (!string.IsNullOrWhiteSpace(dataGoKrKey))
                overrides[ApiKeyProvider.KEY_DATA_GO_KR] = dataGoKrKey!;

            _keyProvider = new ApiKeyProvider(overrides);

            string dllDir = System.IO.Path.GetDirectoryName(
                typeof(URSUSSolver).Assembly.Location)!;
            _cacheDir = dllDir;

            // Registry 초기화 + 부트스트랩
            _registry = DataSourceRegistryProvider.Instance;
            if (_registry.Count == 0)
            {
                DefaultDataSourceBootstrapper.RegisterAll(_registry, _keyProvider);
            }
        }

        /// <summary>키가 어디서 로드됐는지 출처 정보 (디버깅/로그용)</summary>
        public IReadOnlyDictionary<string, string> KeySources => _keyProvider.KeySources;

        /// <summary>현재 등록된 데이터 소스 레지스트리 (읽기 전용 접근)</summary>
        public IDataSourceRegistry Registry => _registry;

        /// <summary>
        /// 전체 파이프라인을 실행하고 SolverResult 를 반환한다.
        ///
        /// v0.3.0: DataSeoulApiParser 직접 호출 대신 IDataSourceRegistry를 통해
        /// 등록된 IDataSource.FetchAsync()를 사용한다.
        /// → 새 데이터 소스 추가 시 Registry 등록만 하면 자동 연동.
        /// </summary>
        /// <param name="dataSet">사용할 데이터셋 이름 목록 (DS_* 상수와 일치)</param>
        /// <param name="weights">
        /// 각 데이터셋의 가중치 배열. dataSet과 같은 순서로 대응된다.
        /// null이면 균등 가중치(1/N)가 적용된다.
        /// 합이 1이 아니어도 내부에서 정규화된다.
        /// </param>
        /// <param name="address1">
        /// 분석 영역 주소. 단독 사용 시 이 주소를 중심으로
        /// DEFAULT_RADIUS_KM 반경 내 법정동을 검색한다.
        /// null이면 DEFAULT_ADDRESS 사용.
        /// </param>
        /// <param name="address2">
        /// BBOX 모드의 우상단 주소 (선택).
        /// 입력 시 address1~address2를 꼭짓점으로 하는 사각형 영역 검색.
        /// null이면 단일 주소 + 반경 모드로 동작.
        /// </param>
        /// <param name="radiusKm">단일 주소 모드의 검색 반경 (km). 기본값 DEFAULT_RADIUS_KM.</param>
        /// <exception cref="ArgumentException">weights 길이가 dataSet 길이와 다를 때</exception>
        public SolverResult Run(
            List<string> dataSet,
            List<double>? weights = null,
            string? address1 = null,
            string? address2 = null,
            double? radiusKm = null)
        {
            // ── 0. 주소 기본값 적용 + 가중치 검증 ────────────────────────
            string addr1 = string.IsNullOrWhiteSpace(address1) ? DEFAULT_ADDRESS : address1!;

            if (weights != null && weights.Count != dataSet.Count)
                throw new ArgumentException(
                    $"weights 길이({weights.Count})가 dataSet 길이({dataSet.Count})와 일치하지 않습니다.");

            // ── 1. 법정동 경계 수집 (IBoundaryDataSource 추상화 레이어) ──
            //    Registry에 등록된 IBoundaryDataSource를 통해 경계 데이터를 수집한다.
            //    DataQuery.Parameters로 주소/반경/BBOX 모드를 전달.
            //    → 직접 VworldApiParser를 호출하지 않으므로, 향후 다른 경계 소스로 교체 가능.
            var districts = FetchBoundariesViaRegistry(addr1, address2, radiusKm);

            Console.WriteLine(ErrorMessages.Solver.DistrictsCollected(districts.Count));

            // ── 2. IDataSourceRegistry를 통한 데이터 수집 ────────────────
            //    각 dataSet 표시이름 → SourceId로 변환하여 Registry에서 조회.
            //    FetchAsync는 내부에서 행정동→법정동 매핑까지 처리하므로
            //    호출부에서 별도 매핑이 불필요하다.
            var districtCodes = districts.Select(d => d.DistrictCode).ToList();
            var legaldLayers = new List<(string name, Dictionary<string, double> data)>();

            foreach (string ds in dataSet)
            {
                var layerData = FetchViaRegistry(ds, districtCodes);
                if (layerData != null)
                {
                    legaldLayers.Add((ds, layerData));
                }
            }

            // ── 5. 결과 조립 ──────────────────────────────────────────────
            var codes      = new List<string>();
            var names      = new List<string>();
            var geometries = new List<PolylineCurve>();
            var centroids  = new List<Point3d>();
            var areas      = new List<double>();

            foreach (var d in districts)
            {
                codes.Add(d.DistrictCode);
                names.Add(d.Name);
                geometries.Add(d.Geometry);
                centroids.Add(d.Centroid);
                areas.Add(d.Area);
            }

            // ── 6. Weighted overlay → values ─────────────────────────────
            //    각 데이터셋을 min-max 정규화 후 가중 평균 (기본: 균등 가중치)
            //    weights가 실제 수집된 레이어 수에 맞게 슬라이싱
            var effectiveWeights = BuildEffectiveWeights(dataSet, weights, legaldLayers);
            var values = BuildOverlayValues(codes, legaldLayers, effectiveWeights);
            Console.WriteLine(ErrorMessages.Solver.OverlayComplete(legaldLayers.Count));

            // ── 6b. WeightConfig 조립 (결과 추적용) ─────────────────────
            WeightConfig? effectiveConfig = null;
            var activeLayers = legaldLayers.Select(l => l.name).ToList();
            if (legaldLayers.Count > 0 && effectiveWeights.Count == legaldLayers.Count)
            {
                var weightDict = new Dictionary<string, double>();
                for (int i = 0; i < legaldLayers.Count; i++)
                    weightDict[legaldLayers[i].name] = effectiveWeights[i];
                effectiveConfig = WeightConfig.Create(weightDict);

                Console.WriteLine($"[Solver] 가중치 적용: {effectiveConfig}");
            }

            // ── 7. Boolean Union → 전체 외곽선 ────────────────────────────
            PolylineCurve? unionBoundary = Union.Compute(geometries, SNAP_TOL);
            Console.WriteLine(ErrorMessages.Solver.UnionResult(unionBoundary != null));

            return new SolverResult(
                LegalCodes:    codes,
                Names:         names,
                Geometries:    geometries.Cast<Curve>().ToList(),
                Centroids:     centroids,
                Areas:         areas,
                Values:        values,
                UnionBoundary: (unionBoundary as Curve)!)
            {
                EffectiveWeights = effectiveConfig,
                ActiveLayers     = activeLayers,
            };
        }

        /// <summary>
        /// WeightConfig를 직접 전달하는 Run 오버로드.
        /// WeightSliderComponent → Solver 연결 시 사용.
        ///
        /// WeightConfig의 키는 DS_* 표시이름이어야 한다.
        /// dataSet 순서에 맞춰 가중치를 자동 추출한다.
        /// </summary>
        public SolverResult Run(
            List<string> dataSet,
            WeightConfig weightConfig,
            string? address1 = null,
            string? address2 = null,
            double? radiusKm = null)
        {
            // WeightConfig → dataSet 순서의 List<double>로 변환
            var weights = dataSet
                .Select(ds => weightConfig.Weights.TryGetValue(ds, out double w) ? w : 0.0)
                .ToList();

            return Run(dataSet, weights, address1, address2, radiusKm);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Registry 기반 데이터 수집
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 표시이름(DS_* 상수)에 대응하는 IDataSource를 Registry에서 찾아
        /// FetchAsync를 호출하고, 법정동 단위 딕셔너리를 반환한다.
        ///
        /// 행정동→법정동 매핑은 SeoulOpenDataSourceBase.FetchAsync 내부에서 처리되므로
        /// 여기서는 결과만 받아 사용한다.
        ///
        /// 실패 시 null을 반환하고 콘솔에 에러 메시지를 출력한다 (레이어 스킵).
        /// </summary>
        private Dictionary<string, double>? FetchViaRegistry(
            string displayName, List<string> districtCodes)
        {
            // 표시이름 → SourceId 매핑
            if (!DisplayNameToSourceId.TryGetValue(displayName, out string? sourceId))
            {
                Console.WriteLine($"[Solver] 알 수 없는 데이터셋: {displayName} — 스킵");
                return null;
            }

            // Registry에서 소스 조회
            var source = _registry.GetById(sourceId);
            if (source == null)
            {
                Console.WriteLine($"[Solver] 데이터 소스 미등록: {sourceId} ({displayName}) — 스킵");
                return null;
            }

            // 사전 조건 검증 (API 키 등)
            var configError = source.ValidateConfiguration();
            if (configError != null)
            {
                Console.WriteLine($"[Solver] {displayName}: {configError.Message}");
                return null;
            }

            // FetchAsync 호출 (동기 대기 — Grasshopper 메인 스레드 호환)
            try
            {
                var query = new DataQuery
                {
                    CacheDirectory = _cacheDir,
                    DistrictCodes  = districtCodes,
                };

                var result = source.FetchAsync(query).GetAwaiter().GetResult();

                if (!result.IsSuccess)
                {
                    Console.WriteLine($"[Solver] {displayName} 수집 실패: {result.Error?.Message}");
                    return null;
                }

                var data = result.Data!.ToDictionary();

                if (data.Count == 0)
                {
                    Console.WriteLine($"[Solver] {displayName} 데이터 0건 — 오버레이에서 제외");
                    return null;
                }

                string originLabel = result.Origin == DataOrigin.Cache ? "캐시" : "API";
                Console.WriteLine(
                    $"[Solver] {displayName} {data.Count}건 수집 완료 ({originLabel}, {result.Elapsed.TotalSeconds:F1}s)");

                return data;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[Solver] {displayName} 수집 실패 (오버레이에서 제외): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Registry에 등록된 IBoundaryDataSource를 통해 법정동 경계를 수집한다.
        ///
        /// 경계 소스가 미등록이면 레거시 VworldApiParser로 폴백한다 (하위 호환).
        /// 정상 경로: Registry → IBoundaryDataSource.FetchBoundariesAsync → BoundaryRecord 리스트
        /// 폴백 경로: VworldApiParser → LegalDistrictRecord → BoundaryRecord 변환
        /// </summary>
        private List<BoundaryRecord> FetchBoundariesViaRegistry(
            string address1, string? address2, double? radiusKm)
        {
            var boundarySource = _registry.GetBoundarySource();

            if (boundarySource != null)
            {
                // 사전 조건 검증
                var configError = boundarySource.ValidateConfiguration();
                if (configError != null)
                {
                    Console.WriteLine($"[Solver] 경계 소스 설정 오류: {configError.Message}");
                    Console.WriteLine("[Solver] 레거시 VworldApiParser로 폴백합니다.");
                    return FetchBoundariesLegacy(address1, address2, radiusKm);
                }

                try
                {
                    // DataQuery 조립: 주소/반경을 Parameters로 전달
                    var parameters = new Dictionary<string, string>
                    {
                        { VWorldBoundaryDataSource.PARAM_ADDRESS, address1 }
                    };

                    if (!string.IsNullOrWhiteSpace(address2))
                    {
                        parameters[VWorldBoundaryDataSource.PARAM_ADDRESS2] = address2!;
                    }
                    else
                    {
                        double radius = radiusKm ?? DEFAULT_RADIUS_KM;
                        parameters[VWorldBoundaryDataSource.PARAM_RADIUS_KM] =
                            radius.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    var query = new DataQuery
                    {
                        CacheDirectory = _cacheDir,
                        Parameters     = parameters
                    };

                    var result = boundarySource.FetchBoundariesAsync(query).GetAwaiter().GetResult();

                    if (result.IsSuccess && result.Data != null && result.Data.Records.Count > 0)
                    {
                        return result.Data.Records.ToList();
                    }

                    Console.WriteLine(
                        $"[Solver] 경계 수집 실패: {result.Error?.Message ?? "데이터 0건"} — 폴백");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[Solver] 경계 소스 예외: {ex.Message} — 레거시 폴백");
                }
            }
            else
            {
                Console.WriteLine("[Solver] 경계 소스 미등록 — 레거시 VworldApiParser 사용");
            }

            return FetchBoundariesLegacy(address1, address2, radiusKm);
        }

        /// <summary>
        /// [레거시 폴백] VworldApiParser를 직접 호출하여 경계를 수집한다.
        /// IBoundaryDataSource가 Registry에 없거나 실패 시 사용.
        /// 향후 제거 예정 (v1.0).
        /// </summary>
        private List<BoundaryRecord> FetchBoundariesLegacy(
            string address1, string? address2, double? radiusKm)
        {
            var vworldParser = new VworldApiParser(_vworldKey, _cacheDir);
            List<LegalDistrictRecord> rawDistricts;

            if (!string.IsNullOrWhiteSpace(address2))
            {
                rawDistricts = vworldParser
                    .GetLegalDistrictsByBBox(address1, address2!)
                    .Where(r => r.Area > MIN_AREA)
                    .ToList();
            }
            else
            {
                double radius = radiusKm ?? DEFAULT_RADIUS_KM;
                rawDistricts = vworldParser
                    .GetLegalDistricts(address1, radius)
                    .Where(r => r.Area > MIN_AREA)
                    .ToList();
            }

            // LegalDistrictRecord → BoundaryRecord 변환
            return rawDistricts.Select(d => new BoundaryRecord
            {
                DistrictCode = d.LegaldCd,
                Name         = d.Name,
                Geometry     = d.Geometry,
                Area         = d.Area,
                Centroid     = d.Centroid
            }).ToList();
        }

        // ─────────────────────────────────────────────────────────────────
        //  가중치 유효화: 요청 vs 실제 수집된 레이어 매칭
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// dataSet 순서로 주어진 weights를, 실제 수집에 성공한 legaldLayers에 맞게 슬라이싱.
        /// weights가 null이면 균등 가중치를 생성한다.
        ///
        /// 유효성 규칙:
        ///   - 음수 가중치 → ArgumentException
        ///   - 모든 가중치가 0 → ArgumentException (의도 불명확)
        ///   - 합 ≠ 1 → 내부에서 자동 정규화 (합 = 1)
        /// </summary>
        /// <exception cref="ArgumentException">음수 또는 전체 제로 가중치</exception>
        internal static List<double> BuildEffectiveWeights(
            List<string> dataSet,
            List<double>? weights,
            List<(string name, Dictionary<string, double> data)> legaldLayers)
        {
            if (legaldLayers.Count == 0)
                return new List<double>();

            if (weights == null)
            {
                // 균등 가중치 (자동 정규화됨: 각 1/N, 합 = 1)
                int cnt = legaldLayers.Count;
                return Enumerable.Repeat(1.0 / cnt, cnt).ToList();
            }

            // ── 음수 검증 ────────────────────────────────────────────────
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] < 0)
                    throw new ArgumentException(
                        $"가중치[{i}] = {weights[i]:F4} — 음수 가중치는 허용되지 않습니다. " +
                        "0 이상의 값을 입력하세요.");
            }

            // dataSet 순서 → 실제 수집된 레이어 이름에 해당하는 가중치만 추출
            var effective = new List<double>();
            var layerNames = new HashSet<string>(legaldLayers.Select(l => l.name));
            for (int i = 0; i < dataSet.Count; i++)
            {
                if (layerNames.Contains(dataSet[i]))
                    effective.Add(weights[i]);
            }

            // ── 전체 제로 검증 ───────────────────────────────────────────
            double sum = effective.Sum();
            if (sum < 1e-9)
                throw new ArgumentException(
                    "모든 가중치의 합이 0입니다. " +
                    "최소 하나의 데이터셋에 0보다 큰 가중치를 설정하세요.");

            // ── 정규화: 합 = 1 ───────────────────────────────────────────
            return effective.Select(w => w / sum).ToList();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Weighted overlay: min-max 정규화 후 가중 평균
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 각 데이터 레이어를 min-max 정규화 후, 주어진 가중치로 가중 평균한다.
        /// </summary>
        /// <param name="legaldCds">법정동 코드 목록</param>
        /// <param name="layers">순서가 보장된 (이름, 데이터) 레이어 목록</param>
        /// <param name="weights">layers와 같은 순서의 정규화된 가중치 (합 = 1)</param>
        private static List<double> BuildOverlayValues(
            List<string> legaldCds,
            List<(string name, Dictionary<string, double> data)> layers,
            List<double> weights)
        {
            int n = legaldCds.Count;

            if (layers.Count == 0)
                return Enumerable.Repeat(0.0, n).ToList();

            var normalizedLayers = new List<double[]>();

            foreach (var (_, data) in layers)
            {
                // 전체 값 기준 fallback = 데이터셋 평균
                double fallback = data.Count > 0 ? data.Values.Average() : 0.0;

                var raw = legaldCds
                    .Select(cd => data.TryGetValue(cd, out double v) ? v : fallback)
                    .ToArray();

                double min   = raw.Min();
                double max   = raw.Max();
                double range = max - min;

                var normalized = range < 1e-9
                    ? raw.Select(_ => 0.5).ToArray()
                    : raw.Select(v => (v - min) / range).ToArray();

                normalizedLayers.Add(normalized);
            }

            // 가중 평균
            var result = new double[n];
            for (int layerIdx = 0; layerIdx < normalizedLayers.Count; layerIdx++)
            {
                double w = weights[layerIdx];
                var layer = normalizedLayers[layerIdx];
                for (int i = 0; i < n; i++)
                    result[i] += layer[i] * w;
            }

            return result.ToList();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  SolverResult
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>URSUSSolver.Run() 반환 타입</summary>
    public record SolverResult(
        List<string>       LegalCodes,
        List<string>       Names,
        List<Curve>        Geometries,
        List<Point3d>      Centroids,
        List<double>       Areas,
        List<double>       Values,
        Curve              UnionBoundary)
    {
        /// <summary>
        /// 실제 적용된 가중치 설정 (정규화 완료).
        /// null이면 데이터 레이어가 0개인 경우.
        /// UI에서 "어떤 가중치로 계산되었는지" 확인용.
        /// </summary>
        public WeightConfig? EffectiveWeights { get; init; }

        /// <summary>
        /// 실제 수집에 성공한 데이터 레이어 이름 목록.
        /// </summary>
        public IReadOnlyList<string> ActiveLayers { get; init; }
            = Array.Empty<string>();
    }
}
