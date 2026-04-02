using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using URSUS.Config;
using URSUS.GeoOps;
using URSUS.Parsers;
using URSUS.Resources;

namespace URSUS
{
    /// <summary>
    /// 데이터 파이프라인 오케스트레이터.
    /// solver.py (URSUSSolver.run()) 포팅.
    ///
    /// 내부에 구현체를 두지 않는다 — Parsers/, GeoOps/ 를 조합해서 SolverResult 를 반환.
    /// </summary>
    public class URSUSSolver
    {
        private const double MIN_AREA    = 100.0;
        private const double SNAP_TOL    = 5.0;

        /// <summary>기본 주소1 (서울 분석 영역 좌하단 기준점)</summary>
        public const string DEFAULT_ADDRESS1 = "인천 남동구 도림동";
        /// <summary>기본 주소2 (서울 분석 영역 우상단 기준점)</summary>
        public const string DEFAULT_ADDRESS2 = "경기 남양주시 해밀예당1로 272";
        // dataSet 항목 이름 상수 — GH Value List의 값과 일치해야 함
        public const string DS_AVG_INCOME    = "월평균 소득";
        public const string DS_RESIDENT_POP  = "상주인구";
        // public const string DS_LIVING_POP = "생활인구";  // 보류 (76만행, 성능 검토 필요)
        public const string DS_TRANSIT       = "대중교통 총 승차 승객 수(일일 평균)";
        public const string DS_LAND_PRICE    = "공시지가";

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

        private readonly string  _vworldKey;
        private readonly string  _seoulKey;
        private readonly string? _dataGoKrKey;  // 선택 — 공시지가 데이터용
        private readonly string  _cacheDir;
        private readonly ApiKeyProvider _keyProvider;

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
        /// </summary>
        /// <exception cref="InvalidOperationException">필수 API 키가 누락된 경우</exception>
        public URSUSSolver(ApiKeyProvider keyProvider)
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

            _vworldKey   = _keyProvider.VWorldKey!;
            _seoulKey    = _keyProvider.SeoulKey!;
            _dataGoKrKey = _keyProvider.DataGoKrKey;  // null이면 공시지가 비활성

            string dllDir = System.IO.Path.GetDirectoryName(
                typeof(URSUSSolver).Assembly.Location)!;
            _cacheDir = dllDir;
        }

        /// <param name="vworldKey">VWorld API 키</param>
        /// <param name="seoulKey">서울 열린데이터 API 키</param>
        /// <param name="dataGoKrKey">공공데이터포털 API 키 (선택 — 공시지가용)</param>
        public URSUSSolver(string vworldKey, string seoulKey, string? dataGoKrKey = null)
        {
            _vworldKey   = vworldKey;
            _seoulKey    = seoulKey;
            _dataGoKrKey = dataGoKrKey;

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
        }

        /// <summary>키가 어디서 로드됐는지 출처 정보 (디버깅/로그용)</summary>
        public IReadOnlyDictionary<string, string> KeySources => _keyProvider.KeySources;

        /// <summary>
        /// 전체 파이프라인을 실행하고 SolverResult 를 반환한다.
        /// </summary>
        /// <param name="dataSet">사용할 데이터셋 이름 목록 (DS_* 상수와 일치)</param>
        /// <param name="weights">
        /// 각 데이터셋의 가중치 배열. dataSet과 같은 순서로 대응된다.
        /// null이면 균등 가중치(1/N)가 적용된다.
        /// 합이 1이 아니어도 내부에서 정규화된다.
        /// </param>
        /// <param name="address1">분석 영역 BBOX 좌하단 기준 주소 (null이면 DEFAULT_ADDRESS1 사용)</param>
        /// <param name="address2">분석 영역 BBOX 우상단 기준 주소 (null이면 DEFAULT_ADDRESS2 사용)</param>
        /// <exception cref="ArgumentException">weights 길이가 dataSet 길이와 다를 때</exception>
        public SolverResult Run(
            List<string> dataSet,
            List<double>? weights = null,
            string? address1 = null,
            string? address2 = null)
        {
            // ── 0. 주소 기본값 적용 + 가중치 검증 ────────────────────────
            string addr1 = string.IsNullOrWhiteSpace(address1) ? DEFAULT_ADDRESS1 : address1!;
            string addr2 = string.IsNullOrWhiteSpace(address2) ? DEFAULT_ADDRESS2 : address2!;

            if (weights != null && weights.Count != dataSet.Count)
                throw new ArgumentException(
                    $"weights 길이({weights.Count})가 dataSet 길이({dataSet.Count})와 일치하지 않습니다.");

            // ── 1. 법정동 경계 수집 ───────────────────────────────────────
            var vworldParser = new VworldApiParser(_vworldKey, _cacheDir);
            var districts = vworldParser
                .GetLegalDistricts(addr1, addr2)
                .Where(r => r.Area > MIN_AREA)
                .ToList();

            Console.WriteLine(ErrorMessages.Solver.DistrictsCollected(districts.Count));

            // ── 2. 선택된 데이터셋 수집 (dataSet 순서 유지) ──────────────
            var seoulParser = new DataSeoulApiParser(_seoulKey);
            var orderedDatasets = new List<(string name, Dictionary<string, double> data)>();

            foreach (string ds in dataSet)
            {
                Dictionary<string, double>? fetched = null;
                if (ds == DS_AVG_INCOME)
                    fetched = seoulParser.GetAvgIncomeByAdstrd(_cacheDir);
                else if (ds == DS_RESIDENT_POP)
                    fetched = seoulParser.GetResidentPopByAdstrd(_cacheDir);
                else if (ds == DS_TRANSIT)
                    fetched = seoulParser.GetTransitBoardingByAdstrd(_cacheDir);

                if (fetched != null)
                    orderedDatasets.Add((ds, fetched));
            }

            // ── 3. 행정동↔법정동 매핑 로드 ───────────────────────────────
            var adstrdToLegald = MappingLoader.Load();

            // ── 4. 행정동 데이터 → 법정동 단위로 집계 (순서 유지) ────────
            var legaldLayers = new List<(string name, Dictionary<string, double> data)>();
            foreach (var (name, byAdstrd) in orderedDatasets)
                legaldLayers.Add((name, MapToLegald(adstrdToLegald, byAdstrd)));

            // ── 4.5. 법정동 직접 데이터셋 추가 (행정동 매핑 불필요) ──────
            var districtCodes = districts.Select(d => d.LegaldCd).ToList();

            if (dataSet.Contains(DS_LAND_PRICE))
            {
                if (!string.IsNullOrWhiteSpace(_dataGoKrKey))
                {
                    try
                    {
                        var landPriceParser = new LandPriceApiParser(_dataGoKrKey!);
                        var landPrices = landPriceParser.GetLandPriceByLegalDistrict(
                            districtCodes, _cacheDir);
                        if (landPrices.Count > 0)
                        {
                            legaldLayers.Add((DS_LAND_PRICE, landPrices));
                            Console.WriteLine(
                                $"[Solver] 공시지가 {landPrices.Count}건 수집 완료");
                        }
                        else
                        {
                            Console.WriteLine(
                                "[Solver] 공시지가 데이터 0건 — 오버레이에서 제외");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[Solver] 공시지가 수집 실패 (오버레이에서 제외): {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine(
                        "[Solver] 공시지가 요청됨, DataGoKr API 키 미설정 — 스킵\n" +
                        "  → 환경변수 URSUS_DATA_GO_KR_KEY 또는 appsettings.json에 DataGoKrKey를 설정하세요.\n" +
                        "  → 발급: https://www.data.go.kr/data/15058747/openapi.do");
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
                codes.Add(d.LegaldCd);
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
                UnionBoundary: (unionBoundary as Curve)!);
        }

        // ─────────────────────────────────────────────────────────────────
        //  행정동 → 법정동 단위 집계
        // ─────────────────────────────────────────────────────────────────

        private static Dictionary<string, double> MapToLegald(
            Dictionary<string, List<string>> adstrdToLegald,
            Dictionary<string, double>       byAdstrd)
        {
            var acc = new Dictionary<string, (double sum, int count)>();

            foreach (var (adstrdCd, legaldCds) in adstrdToLegald)
            {
                if (!byAdstrd.TryGetValue(adstrdCd, out double val)) continue;

                foreach (string legaldCd in legaldCds)
                {
                    if (acc.TryGetValue(legaldCd, out var prev))
                        acc[legaldCd] = (prev.sum + val, prev.count + 1);
                    else
                        acc[legaldCd] = (val, 1);
                }
            }

            var result = new Dictionary<string, double>();
            foreach (var (k, (sum, cnt)) in acc)
                result[k] = sum / cnt;
            return result;
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
        Curve              UnionBoundary);
}
