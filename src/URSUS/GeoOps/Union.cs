using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using Rhino.Geometry;

namespace URSUS.GeoOps
{
    /// <summary>
    /// 법정동 경계 PolylineCurve 목록 → Boolean Union → 단일 외곽선.
    /// GeoUnion.cs (GH 컴포넌트) 대체. GH 와이어에는 노출하지 않는다.
    ///
    /// 파이프라인:
    ///  1. 유효 닫힌 Curve 수집
    ///  2. Clipper2 Paths64로 변환 (1mm 정밀도 스케일)
    ///  3. InflatePaths 로 snapTol(gap 폐합)
    ///  4. Clipper.Union (NonZero 채움)
    ///  5. 면적 최대 경로 선택
    ///  6. Rhino PolylineCurve 로 반환
    /// </summary>
    public static class Union
    {
        // UTM 좌표(미터) → Clipper2 int64 변환 배율 (1mm 정밀도)
        private const double SCALE = 1_000.0;

        /// <summary>
        /// 법정동 경계 커브 목록의 Boolean Union을 수행하고 외곽선을 반환한다.
        /// </summary>
        /// <param name="curves">입력 PolylineCurve 목록</param>
        /// <param name="snapTol">인접 간격 폐합 허용 오차 (미터, 기본 5.0)</param>
        /// <returns>면적이 가장 큰 외곽선 PolylineCurve</returns>
        public static PolylineCurve? Compute(IEnumerable<PolylineCurve> curves, double snapTol = 5.0)
        {
            // ── 1. 유효한 닫힌 Curve 수집 ─────────────────────────────────
            var valid = curves
                .Where(c => c != null && c.IsClosed)
                .ToList();

            if (valid.Count == 0)
            {
                Console.WriteLine("[Union] 유효한 닫힌 Curve 없음");
                return null;
            }

            // ── 2. Rhino Curve → Clipper2 Paths64 ────────────────────────
            var paths = new Paths64(valid.Count);
            foreach (var curve in valid)
            {
                var path = CurveToPath64(curve);
                if (path.Count >= 3)
                    paths.Add(path);
            }

            if (paths.Count == 0)
            {
                Console.WriteLine("[Union] Paths64 변환 실패");
                return null;
            }

            // ── 3. InflatePaths: snapTol 만큼 외부로 확장 (gap 폐합) ──────
            if (snapTol > 0)
            {
                long delta = (long)(snapTol * SCALE);
                paths = Clipper.InflatePaths(
                    paths, delta,
                    JoinType.Miter,
                    EndType.Polygon);
            }

            // ── 4. Clipper.Union ──────────────────────────────────────────
            Paths64 solution = Clipper.Union(paths, FillRule.NonZero);
            Console.WriteLine($"[Union] 입력={valid.Count}개 → Union={solution.Count}개 경로");

            if (solution.Count == 0)
            {
                Console.WriteLine("[Union] Union 실패 — 원본 첫 Curve 반환");
                return valid[0];
            }

            // ── 5. 면적 최대 경로 선택 ────────────────────────────────────
            Path64 largest = solution
                .OrderByDescending(p => Math.Abs(Clipper.Area(p)))
                .First();

            // ── 6. Path64 → Rhino PolylineCurve ──────────────────────────
            return Path64ToCurve(largest);
        }

        // ─────────────────────────────────────────────────────────────────
        //  변환 헬퍼
        // ─────────────────────────────────────────────────────────────────

        private static Path64 CurveToPath64(PolylineCurve curve)
        {
            var pl   = curve.ToPolyline();
            var path = new Path64(pl.Count);
            foreach (Point3d pt in pl)
                path.Add(new Point64((long)(pt.X * SCALE), (long)(pt.Y * SCALE)));

            // 닫힌 폴리라인의 마지막 점은 첫 점 중복이므로 제거
            if (path.Count > 1
                && path[0].X == path[^1].X
                && path[0].Y == path[^1].Y)
                path.RemoveAt(path.Count - 1);

            return path;
        }

        private static PolylineCurve Path64ToCurve(Path64 path)
        {
            var points = new List<Point3d>(path.Count + 1);
            foreach (var pt in path)
                points.Add(new Point3d(pt.X / SCALE, pt.Y / SCALE, 0));

            // 닫기
            if (points.Count > 0)
                points.Add(points[0]);

            var polyline = new Polyline(points);
            return polyline.ToPolylineCurve();
        }
    }
}
