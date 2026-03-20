using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using URSUS.GeoOps;
using URSUS.Parsers;

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
        private const double MIN_AREA    = 100.0;  // 법정동 최소 면적 필터 (m²)
        private const double SNAP_TOL    = 5.0;    // Boolean Union gap 폐합 허용 오차 (m)

        private const string ADDRESS1 = "인천 남동구 도림동";
        private const string ADDRESS2 = "경기 남양주시 해밀예당1로 272";
        private const string MAPPING_FILENAME = "adstrd_legald_mapping.json";

        private readonly string _vworldKey;
        private readonly string _seoulKey;
        private readonly string _cacheDir;
        private readonly string _mappingJsonPath;

        /// <param name="vworldKey">VWorld API 키</param>
        /// <param name="seoulKey">서울 열린데이터 API 키</param>
        public URSUSSolver(string vworldKey, string seoulKey)
        {
            _vworldKey = vworldKey;
            _seoulKey  = seoulKey;

            // DLL과 같은 폴더에 adstrd_legald_mapping.json이 있다고 가정
            string dllDir = System.IO.Path.GetDirectoryName(
                typeof(URSUSSolver).Assembly.Location)!;
            _mappingJsonPath = System.IO.Path.Combine(dllDir, MAPPING_FILENAME);
            _cacheDir        = dllDir;
        }

        /// <summary>
        /// 전체 파이프라인을 실행하고 SolverResult 를 반환한다.
        /// </summary>
        public SolverResult Run()
        {
            // ── 1. 법정동 경계 수집 ───────────────────────────────────────
            var vworldParser = new VworldApiParser(_vworldKey, _cacheDir);
            var districts = vworldParser
                .GetLegalDistricts(ADDRESS1, ADDRESS2)
                .Where(r => r.Area > MIN_AREA)
                .ToList();

            Console.WriteLine($"[Solver] 법정동 {districts.Count}개 수집");

            // ── 2. 행정동 기준 월 평균 소득 수집 ─────────────────────────
            var seoulParser = new DataSeoulApiParser(_seoulKey);
            var avgIncomeByAdstrd = seoulParser.GetAvgIncomeByAdstrd(_cacheDir);

            // ── 3. 행정동↔법정동 매핑 로드 ───────────────────────────────
            var adstrdToLegald = MappingLoader.Load(_mappingJsonPath);

            // ── 진단: 키 형식 샘플 출력 ──────────────────────────────────
            var incomeSample  = avgIncomeByAdstrd.Keys.Take(3);
            var mappingSample = adstrdToLegald.Keys.Take(3);
            var legaldSample  = districts.Take(3).Select(d => d.LegaldCd);
            Console.WriteLine($"[DEBUG] income keys:  {string.Join(", ", incomeSample)}");
            Console.WriteLine($"[DEBUG] mapping keys: {string.Join(", ", mappingSample)}");
            Console.WriteLine($"[DEBUG] legald_cd:    {string.Join(", ", legaldSample)}");

            // ── 4. 법정동 기준 평균 소득 계산 ─────────────────────────────
            //    adstrd_cd → legald_cd 매핑을 통해 행정동 소득을 법정동에 집계
            var avgIncomeByLegald = BuildIncomeByLegald(
                adstrdToLegald, avgIncomeByAdstrd);

            // ── 5. 글로벌 평균 (fallback용) ───────────────────────────────
            double globalMean = avgIncomeByAdstrd.Count > 0
                ? avgIncomeByAdstrd.Values.Average()
                : 0.0;

            // ── 6. 결과 조립 ──────────────────────────────────────────────
            var codes      = new List<string>();
            var names      = new List<string>();
            var geometries = new List<PolylineCurve>();
            var centroids  = new List<Point3d>();
            var areas      = new List<double>();
            var avgIncomes = new List<double>();

            foreach (var d in districts)
            {
                codes.Add(d.LegaldCd);
                names.Add(d.Name);
                geometries.Add(d.Geometry);
                centroids.Add(d.Centroid);
                areas.Add(d.Area);

                double income = avgIncomeByLegald.TryGetValue(d.LegaldCd, out double v)
                    ? v
                    : globalMean;
                avgIncomes.Add(income);
            }

            // ── 7. Boolean Union → 전체 외곽선 ────────────────────────────
            PolylineCurve? unionBoundary = Union.Compute(geometries, SNAP_TOL);
            Console.WriteLine($"[Solver] Union 외곽선 생성 {(unionBoundary != null ? "성공" : "실패")}");

            return new SolverResult(
                LegalCodes:    codes,
                Names:         names,
                Geometries:    geometries.Cast<Curve>().ToList(),
                Centroids:     centroids,
                Areas:         areas,
                AvgIncomes:    avgIncomes,
                UnionBoundary: (unionBoundary as Curve)!);
        }

        // ─────────────────────────────────────────────────────────────────
        //  법정동 기준 평균 소득 계산
        // ─────────────────────────────────────────────────────────────────

        private static Dictionary<string, double> BuildIncomeByLegald(
            Dictionary<string, List<string>> adstrdToLegald,
            Dictionary<string, double>       incomeByAdstrd)
        {
            // legald_cd → (sum, count)
            var acc = new Dictionary<string, (double sum, int count)>();

            foreach (var (adstrdCd, legaldCds) in adstrdToLegald)
            {
                if (!incomeByAdstrd.TryGetValue(adstrdCd, out double income))
                    continue;

                foreach (string legaldCd in legaldCds)
                {
                    if (acc.TryGetValue(legaldCd, out var prev))
                        acc[legaldCd] = (prev.sum + income, prev.count + 1);
                    else
                        acc[legaldCd] = (income, 1);
                }
            }

            var result = new Dictionary<string, double>();
            foreach (var (k, (sum, cnt)) in acc)
                result[k] = sum / cnt;
            return result;
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
        List<double>       AvgIncomes,
        Curve              UnionBoundary);
}
