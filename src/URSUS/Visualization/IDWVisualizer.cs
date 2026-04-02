using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Rhino.Geometry;
using URSUS.Resources;

namespace URSUS.Visualization
{
    /// <summary>
    /// IDW 공간 보간 + Mesh 시각화.
    /// IDWVisualizer.cs (GH 컴포넌트) 포팅 — GH 의존성 제거.
    /// </summary>
    public class IDWVisualizer
    {
        private readonly SpatialField _field;
        private readonly ColorMapper  _mapper;
        private readonly double       _power;
        private readonly double       _heightScale;
        private readonly double       _resolution;
        private readonly double       _heightRatio;
        private readonly int          _legendSteps;

        /// <param name="centroids">법정동 중심점</param>
        /// <param name="values">스칼라 값 (avg_incomes 등)</param>
        /// <param name="resolution">Mesh 최대 엣지 길이</param>
        /// <param name="power">IDW 지수 p (기본 3.0)</param>
        /// <param name="heightScale">Z 높이 배율 (기본 0.5)</param>
        /// <param name="heightRatio">Z 최대 높이 = bboxWidth × heightRatio (기본 0.25)</param>
        /// <param name="legendSteps">범례 단계 수 (기본 8)</param>
        /// <param name="colorStyle">색상 스타일 0~6</param>
        /// <param name="colorLow">colorStyle=0 최솟값 색상</param>
        /// <param name="colorHigh">colorStyle=0 최댓값 색상</param>
        public IDWVisualizer(
            List<Point3d> centroids,
            List<double>  values,
            double        resolution  = 100.0,
            double        power       = 2.5,
            double        heightScale = 0.5,
            double        heightRatio = 0.5,
            int           legendSteps = 8,
            int           colorStyle  = 4,
            Color?        colorLow    = null,
            Color?        colorHigh   = null)
        {
            if (centroids == null || centroids.Count == 0)
                throw new ArgumentException(
                    ErrorGuideMap.FormatMessageWithGuide(
                        ErrorCodes.CentroidsEmpty,
                        ErrorMessages.Data.CentroidsEmpty));
            if (values == null || values.Count == 0)
                throw new ArgumentException(
                    ErrorGuideMap.FormatMessageWithGuide(
                        ErrorCodes.ValuesEmpty,
                        ErrorMessages.Data.ValuesEmpty));
            if (centroids.Count != values.Count)
                throw new ArgumentException(
                    ErrorGuideMap.FormatMessageWithGuide(
                        ErrorCodes.CentroidsValuesMismatch,
                        ErrorMessages.Data.CentroidsValuesMismatch));

            _field       = new SpatialField(centroids, values);
            _mapper      = ColorMapper.FromStyle(
                               colorStyle,
                               colorLow  ?? Color.FromArgb(44,  123, 182),
                               colorHigh ?? Color.FromArgb(215,  25,  28));
            _power       = power       > 0 ? power       : 3.0;
            _heightScale = heightScale > 0 ? heightScale : 0.5;
            _resolution  = resolution  > 0 ? resolution  : 100.0;
            _heightRatio = heightRatio > 0 ? heightRatio : 0.25;
            _legendSteps = legendSteps > 1 ? legendSteps : 8;
        }

        /// <summary>
        /// 외곽선 boundary로 Mesh + 범례를 생성한다.
        /// </summary>
        public VisualizerResult Build(Curve boundary)
        {
            if (boundary == null)
                throw new ArgumentNullException(nameof(boundary));

            var builder = new MeshBuilder(
                _field, _mapper, _power, _heightScale,
                _resolution, _heightRatio);

            var (elevated, flat) = builder.Build(boundary);

            // 범례 위치: bbox 오른쪽
            BoundingBox bbox  = elevated.GetBoundingBox(false);
            double      bboxW = bbox.Max.X - bbox.Min.X;
            double      bboxH = bbox.Max.Y - bbox.Min.Y;
            Point3d     anchor = new Point3d(bbox.Max.X + bboxW * 0.02, bbox.Min.Y, 0.0);
            double      legendW = Math.Max(bboxW * 0.03, 100.0);

            var legendBuilder = new LegendBuilder(
                _field, _mapper, anchor, legendW, bboxH, _legendSteps);

            return new VisualizerResult(
                Mesh:         elevated,
                FlatMesh:     flat,
                MinVal:       _field.MinValue,
                MaxVal:       _field.MaxValue,
                LegendMesh:   legendBuilder.BuildMesh(),
                LegendDots:   legendBuilder.BuildDots());
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  VisualizerResult
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>IDWVisualizer.Build() 반환 타입</summary>
    public record VisualizerResult(
        Mesh            Mesh,
        Mesh            FlatMesh,
        double          MinVal,
        double          MaxVal,
        Mesh            LegendMesh,
        List<TextDot>   LegendDots);

    // ═════════════════════════════════════════════════════════════════════
    //  SpatialField
    // ═════════════════════════════════════════════════════════════════════

    internal class SpatialField
    {
        public readonly List<Point3d> Points;
        public readonly List<double>  Values;
        public readonly double        MinValue;
        public readonly double        MaxValue;

        public SpatialField(List<Point3d> points, List<double> values)
        {
            Points   = points;
            Values   = values;
            MinValue = values.Min();
            MaxValue = values.Max();
        }

        /// IDW: F(q) = Σ[vᵢ / dᵢᵖ] / Σ[1 / dᵢᵖ]
        public double IDW(Point3d query, double power)
        {
            double num = 0.0, den = 0.0;
            double qx = query.X, qy = query.Y;
            int    ip  = (int)power;
            bool   intPow = (ip >= 1 && ip <= 6 && Math.Abs(power - ip) < 1e-9);

            for (int i = 0; i < Points.Count; i++)
            {
                double dx = qx - Points[i].X;
                double dy = qy - Points[i].Y;
                double d2 = dx * dx + dy * dy;
                if (d2 < 1e-20) return Values[i];

                double w;
                if (intPow)
                {
                    // Math.Pow 없이 정수 지수 처리 (p=2,3이 가장 흔함)
                    double d = Math.Sqrt(d2);
                    double dp = d;
                    for (int j = 1; j < ip; j++) dp *= d;
                    w = 1.0 / dp;
                }
                else
                {
                    w = 1.0 / Math.Pow(Math.Sqrt(d2), power);
                }
                num += w * Values[i];
                den += w;
            }
            double result = den < 1e-12 ? 0.0 : num / den;
            return double.IsNaN(result) || double.IsInfinity(result) ? 0.0 : result;
        }

        public double Normalize(double v)
        {
            double range = MaxValue - MinValue;
            return range < 1e-12 ? 0.5 : (v - MinValue) / range;
        }

        public double Normalize(double v, double outMin, double outMax)
            => outMin + Normalize(v) * (outMax - outMin);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  ColorMapper
    // ═════════════════════════════════════════════════════════════════════

    internal class ColorMapper
    {
        private readonly Color[] _stops;
        public  readonly string  StyleName;

        public ColorMapper(Color[] stops, string name) { _stops = stops; StyleName = name; }

        public static ColorMapper FromStyle(int style, Color customLow, Color customHigh)
        {
            return style switch
            {
                1 => new ColorMapper(new[] { C(44, 123, 182), C(215, 25, 28) },                                  "BlueRed"),
                2 => new ColorMapper(new[] { C(0, 128, 0), C(255, 255, 0), C(255, 0, 0) },                      "Heatmap"),
                3 => new ColorMapper(new[] { C(0, 0, 200), C(0, 200, 200), C(0, 200, 0), C(255, 255, 0), C(220, 0, 0) }, "Spectral"),
                4 => new ColorMapper(new[] { C(68, 1, 84), C(58, 82, 139), C(32, 144, 140), C(94, 201, 98), C(253, 231, 37) }, "Viridis"),
                5 => new ColorMapper(new[] { C(44, 123, 182), C(255, 255, 255), C(215, 25, 28) },               "Diverging"),
                6 => new ColorMapper(new[] { C(255, 255, 255), C(0, 0, 0) },                                    "Grayscale"),
                _ => new ColorMapper(new[] { customLow, customHigh },                                           "Custom"),
            };
        }

        private static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);

        public Color Map(double t)
        {
            t = Math.Max(0.0, Math.Min(1.0, t));
            double scaled = t * (_stops.Length - 1);
            int    lo     = (int)scaled;
            int    hi     = Math.Min(lo + 1, _stops.Length - 1);
            double lt     = scaled - lo;
            return Color.FromArgb(
                Lerp(_stops[lo].R, _stops[hi].R, lt),
                Lerp(_stops[lo].G, _stops[hi].G, lt),
                Lerp(_stops[lo].B, _stops[hi].B, lt));
        }

        private static int Lerp(int a, int b, double t)
            => Math.Max(0, Math.Min(255, (int)Math.Round(a + (b - a) * t)));
    }

    // ═════════════════════════════════════════════════════════════════════
    //  MeshBuilder
    // ═════════════════════════════════════════════════════════════════════

    internal class MeshBuilder
    {
        private readonly SpatialField _field;
        private readonly ColorMapper  _mapper;
        private readonly double       _power;
        private readonly double       _heightScale;
        private readonly double       _maxEdgeLen;
        private readonly double       _heightRatio;

        private const double DEFAULT_TOL = 0.001;

        public MeshBuilder(SpatialField field, ColorMapper mapper,
                           double power, double heightScale, double maxEdgeLen,
                           double heightRatio)
        {
            _field       = field;
            _mapper      = mapper;
            _power       = power;
            _heightScale = heightScale;
            _maxEdgeLen  = maxEdgeLen;
            _heightRatio = heightRatio;
        }

        public (Mesh elevated, Mesh flat) Build(Curve boundary)
        {
            double tol = DEFAULT_TOL;

            Brep[] breps = Brep.CreatePlanarBreps(boundary, tol);
            if (breps == null || breps.Length == 0)
                return (new Mesh(), new Mesh());

            var mp = new MeshingParameters
            {
                MaximumEdgeLength = _maxEdgeLen,
                MinimumEdgeLength = _maxEdgeLen * 0.1,
                JaggedSeams       = false,
                RefineGrid        = true,
            };

            var baseMesh = new Mesh();
            foreach (Brep brep in breps)
            {
                Mesh[]? meshes = Mesh.CreateFromBrep(brep, mp);
                if (meshes != null) foreach (Mesh m in meshes) baseMesh.Append(m);
            }
            baseMesh.Compact();

            int n = baseMesh.Vertices.Count;
            if (n == 0) return (new Mesh(), new Mesh());

            BoundingBox bbox      = boundary.GetBoundingBox(false);
            double      bboxWidth = bbox.IsValid ? (bbox.Max.X - bbox.Min.X) : 1.0;

            // ── 버텍스별 IDW 보간 → Z 계산 ──────────────────────────────
            var    zValues = new double[n];
            double zMax    = 0.0;

            for (int vi = 0; vi < n; vi++)
            {
                Point3d q  = baseMesh.Vertices.Point3dAt(vi);
                double  f  = _field.IDW(new Point3d(q.X, q.Y, 0.0), _power);
                double  zd = _field.Normalize(f, 0.0, bboxWidth * _heightRatio) * _heightScale;
                if (double.IsNaN(zd) || double.IsInfinity(zd)) zd = 0.0;
                zValues[vi] = zd;
                if (zd > zMax) zMax = zd;
            }

            // 색상 = Z 기준 정규화
            var colors = new Color[n];
            for (int vi = 0; vi < n; vi++)
            {
                double t = zMax > 1e-12 ? zValues[vi] / zMax : 0.0;
                colors[vi] = _mapper.Map(t);
            }

            // elevated mesh
            Mesh elevated = baseMesh.DuplicateMesh();
            elevated.TextureCoordinates.Clear();
            for (int vi = 0; vi < n; vi++)
            {
                Point3d vd = baseMesh.Vertices.Point3dAt(vi);
                elevated.Vertices.SetVertex(vi, new Point3d(vd.X, vd.Y, zValues[vi]));
            }
            ApplyColors(elevated, colors);
            elevated.Normals.ComputeNormals();
            elevated.FaceNormals.ComputeFaceNormals();

            // flat mesh: Z=0, 동일한 색상
            Mesh flat = baseMesh.DuplicateMesh();
            flat.TextureCoordinates.Clear();
            ApplyColors(flat, colors);
            flat.Normals.ComputeNormals();
            flat.FaceNormals.ComputeFaceNormals();

            return (elevated, flat);
        }

        private static void ApplyColors(Mesh mesh, Color[] colors)
        {
            mesh.VertexColors.CreateMonotoneMesh(Color.White);
            for (int i = 0; i < colors.Length; i++)
                mesh.VertexColors.SetColor(i, colors[i]);
        }

    }

    // ═════════════════════════════════════════════════════════════════════
    //  LegendBuilder
    // ═════════════════════════════════════════════════════════════════════

    internal class LegendBuilder
    {
        private readonly SpatialField _field;
        private readonly ColorMapper  _mapper;
        private readonly Point3d      _anchor;
        private readonly double       _width;
        private readonly double       _height;
        private readonly int          _steps;

        public LegendBuilder(SpatialField field, ColorMapper mapper,
                             Point3d anchor, double width, double height, int steps)
        {
            _field  = field;
            _mapper = mapper;
            _anchor = anchor;
            _width  = width;
            _height = height;
            _steps  = steps;
        }

        public Mesh BuildMesh()
        {
            var    mesh  = new Mesh();
            var    cols  = new List<Color>();
            double stepH = _height / _steps;

            for (int i = 0; i <= _steps; i++)
            {
                double t = (double)i / _steps;
                Color  c = _mapper.Map(t);
                float  y = (float)(_anchor.Y + i * stepH);
                mesh.Vertices.Add((float)_anchor.X,            y, 0f);
                mesh.Vertices.Add((float)(_anchor.X + _width), y, 0f);
                cols.Add(c);
                cols.Add(c);
            }
            for (int i = 0; i < _steps; i++)
            {
                int b = i * 2;
                mesh.Faces.AddFace(b, b + 2, b + 3, b + 1);
            }
            mesh.VertexColors.CreateMonotoneMesh(Color.White);
            for (int i = 0; i < cols.Count; i++)
                mesh.VertexColors.SetColor(i, cols[i]);
            mesh.Normals.ComputeNormals();
            return mesh;
        }

        public List<TextDot> BuildDots()
        {
            var    dots   = new List<TextDot>();
            double stepH  = _height / _steps;
            double labelX = _anchor.X + _width * 1.4;

            for (int i = 0; i <= _steps; i++)
            {
                double t   = (double)i / _steps;
                double val = _field.MinValue + t * (_field.MaxValue - _field.MinValue);
                double y   = _anchor.Y + i * stepH;
                var    dot = new TextDot(val.ToString("N0"), new Point3d(labelX, y, 0.0));
                dot.FontHeight = 12;
                dots.Add(dot);
            }
            return dots;
        }
    }
}
