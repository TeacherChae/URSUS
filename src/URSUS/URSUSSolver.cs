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
        private const double MIN_AREA    = 100.0;
        private const double SNAP_TOL    = 5.0;

        private const string ADDRESS1         = "인천 남동구 도림동";
        private const string ADDRESS2         = "경기 남양주시 해밀예당1로 272";
        private const string MAPPING_FILENAME = "adstrd_legald_mapping.json";

        // dataSet 항목 이름 상수 — GH Value List의 값과 일치해야 함
        public const string DS_AVG_INCOME  = "월평균 소득";
        public const string DS_LIVING_POP  = "생활인구";
        // public const string DS_TRANSIT  = "대중교통 총 승차 승객 수(일일 평균)"; // TBD

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

            string dllDir = System.IO.Path.GetDirectoryName(
                typeof(URSUSSolver).Assembly.Location)!;
            _mappingJsonPath = System.IO.Path.Combine(dllDir, MAPPING_FILENAME);
            _cacheDir        = dllDir;
        }

        /// <summary>
        /// 전체 파이프라인을 실행하고 SolverResult 를 반환한다.
        /// </summary>
        /// <param name="dataSet">사용할 데이터셋 이름 목록 (DS_* 상수와 일치)</param>
        public SolverResult Run(List<string> dataSet)
        {
            // ── 1. 법정동 경계 수집 ───────────────────────────────────────
            var vworldParser = new VworldApiParser(_vworldKey, _cacheDir);
            var districts = vworldParser
                .GetLegalDistricts(ADDRESS1, ADDRESS2)
                .Where(r => r.Area > MIN_AREA)
                .ToList();

            Console.WriteLine($"[Solver] 법정동 {districts.Count}개 수집");

            // ── 2. 선택된 데이터셋 수집 ──────────────────────────────────
            var seoulParser = new DataSeoulApiParser(_seoulKey);
            var adstrdDatasets = new Dictionary<string, Dictionary<string, double>>();

            if (dataSet.Contains(DS_AVG_INCOME))
                adstrdDatasets[DS_AVG_INCOME] = seoulParser.GetAvgIncomeByAdstrd(_cacheDir);

            if (dataSet.Contains(DS_LIVING_POP))
                adstrdDatasets[DS_LIVING_POP] = seoulParser.GetLivingPopByAdstrd(_cacheDir);

            // ── 3. 행정동↔법정동 매핑 로드 ───────────────────────────────
            var adstrdToLegald = MappingLoader.Load(_mappingJsonPath);

            // ── 4. 행정동 데이터 → 법정동 단위로 집계 ────────────────────
            var legaldDatasets = new Dictionary<string, Dictionary<string, double>>();
            foreach (var (name, byAdstrd) in adstrdDatasets)
                legaldDatasets[name] = MapToLegald(adstrdToLegald, byAdstrd);

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
            //    각 데이터셋을 min-max 정규화 후 균등 평균
            var values = BuildOverlayValues(codes, legaldDatasets);
            Console.WriteLine($"[Solver] overlay 값 계산 완료 ({legaldDatasets.Count}개 데이터셋)");

            // ── 7. Boolean Union → 전체 외곽선 ────────────────────────────
            PolylineCurve? unionBoundary = Union.Compute(geometries, SNAP_TOL);
            Console.WriteLine($"[Solver] Union 외곽선 생성 {(unionBoundary != null ? "성공" : "실패")}");

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
        //  Weighted overlay: min-max 정규화 후 균등 평균
        // ─────────────────────────────────────────────────────────────────

        private static List<double> BuildOverlayValues(
            List<string>                             legaldCds,
            Dictionary<string, Dictionary<string, double>> datasets)
        {
            int n = legaldCds.Count;

            if (datasets.Count == 0)
                return Enumerable.Repeat(0.0, n).ToList();

            var layers = new List<double[]>();

            foreach (var (_, data) in datasets)
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

                layers.Add(normalized);
            }

            // 균등 평균
            var result = new double[n];
            foreach (var layer in layers)
                for (int i = 0; i < n; i++)
                    result[i] += layer[i];
            for (int i = 0; i < n; i++)
                result[i] /= layers.Count;

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
