using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Rhino.Geometry;
using URSUS.Resources;
using URSUS.Geometry;

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
        private readonly bool         _normalizedLegend;
        private readonly string?      _unit;
        private readonly VisualizationMeshCache? _cache;

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
            : this(centroids, values, resolution, power, heightScale, heightRatio,
                legendSteps, colorStyle, colorLow, colorHigh, true, null, null)
        { }

        public IDWVisualizer(
            List<Point3d> centroids, List<double> values, double resolution, double power,
            double heightScale, double heightRatio, int legendSteps, int colorStyle,
            Color? colorLow, Color? colorHigh, bool normalizedLegend, string? unit)
            : this(centroids, values, resolution, power, heightScale, heightRatio,
                legendSteps, colorStyle, colorLow, colorHigh, normalizedLegend, unit, null)
        { }

        public IDWVisualizer(
            List<Point3d> centroids, List<double> values, double resolution, double power,
            double heightScale, double heightRatio, int legendSteps, int colorStyle,
            Color? colorLow, Color? colorHigh, bool normalizedLegend, string? unit,
            VisualizationMeshCache? cache)
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
            _legendSteps = LegendContract.ClampSteps(legendSteps);
            _normalizedLegend = normalizedLegend;
            _unit = unit;
            _cache = cache;
        }

        /// <summary>
        /// 외곽선 boundary로 Mesh + 범례를 생성한다.
        /// </summary>
        public VisualizerResult Build(Curve boundary)
            => Build(boundary, CancellationToken.None, VisualizationQuality.Final);

        public VisualizerResult Build(Curve boundary, CancellationToken cancellationToken,
            VisualizationQuality quality = VisualizationQuality.Final)
            => Build(boundary, cancellationToken, quality, null, 1.0);

        public VisualizerResult Build(Curve boundary, CancellationToken cancellationToken,
            VisualizationQuality quality, string? snapshotTopologyKey, double unitScale)
            => Build(new IReadOnlyList<Curve>[] { new[] { boundary } }, cancellationToken,
                quality, snapshotTopologyKey, unitScale);

        public VisualizerResult Build(IReadOnlyList<Curve> boundaries,
            CancellationToken cancellationToken, VisualizationQuality quality,
            string? snapshotTopologyKey, double unitScale)
            => Build(new[] { boundaries }, cancellationToken, quality,
                snapshotTopologyKey, unitScale);

        public VisualizerResult Build(IReadOnlyList<IReadOnlyList<Curve>> regions,
            CancellationToken cancellationToken, VisualizationQuality quality,
            string? snapshotTopologyKey, double unitScale)
        {
            if (regions == null || regions.Count == 0 || regions.Any(region =>
                    region == null || region.Count == 0 || region.Any(boundary => boundary == null)))
                throw new ArgumentException("하나 이상의 유효 boundary region이 필요합니다.", nameof(regions));
            cancellationToken.ThrowIfCancellationRequested();
            string? cacheKey = snapshotTopologyKey == null ? null
                : CreateCacheKey(snapshotTopologyKey, quality, unitScale);
            if (cacheKey != null && _cache != null && _cache.TryGet(cacheKey, out var cached))
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return cached!;
                }
                catch
                {
                    cached?.DisposeMeshes();
                    throw;
                }
            }

            var builder = new MeshBuilder(
                _field, _mapper, _power, _heightScale,
                _resolution, _heightRatio, cancellationToken, quality);

            var (elevated, flat) = builder.Build(regions);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                BoundingBox bbox  = elevated.GetBoundingBox(false);
                double bboxW = bbox.Max.X - bbox.Min.X;
                double bboxH = bbox.Max.Y - bbox.Min.Y;
                Point3d anchor = new Point3d(bbox.Max.X + bboxW * 0.02, bbox.Min.Y, 0.0);
                double legendW = Math.Max(bboxW * 0.03, 100.0);
                var legendBuilder = new LegendBuilder(
                    _field, _mapper, anchor, legendW, bboxH, _legendSteps,
                    _normalizedLegend, _unit);
                Mesh legend = legendBuilder.BuildMesh();
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = new VisualizerResult(elevated, flat, _field.MinValue, _field.MaxValue,
                        legend, legendBuilder.BuildDots());
                    if (cacheKey != null && _cache != null) _cache.Add(cacheKey, result);
                    return result;
                }
                catch { legend.Dispose(); throw; }
            }
            catch
            {
                elevated.Dispose();
                flat.Dispose();
                throw;
            }
        }

        public double Evaluate(Point3d point, CancellationToken cancellationToken = default)
            => _field.IDW(point, _power, cancellationToken);

        public IReadOnlyList<string> BuildLegendLabels()
        {
            var labels = Enumerable.Range(0, _legendSteps + 1)
                .Select(index =>
                {
                    double t = (double)index / _legendSteps;
                    double value = _field.MinValue + t * (_field.MaxValue - _field.MinValue);
                    return LegendContract.Format(value, _normalizedLegend, _unit);
                }).ToList();
            if (_field.HasMissing) labels.Add("No data");
            return labels;
        }

        /// <summary>
        /// unitScale은 snapshot adapter가 geometry/centroid에 이미 적용한 meter→document scale의
        /// cache identity다. 이 IDW legacy 경로에서 geometry를 두 번 변환하지 않는다.
        /// </summary>
        public string CreateCacheKey(string snapshotTopologyKey, VisualizationQuality quality,
            double preAppliedUnitScale)
            => $"{snapshotTopologyKey}|idw|quality:{quality}|{_resolution:R}|{_power:R}|" +
               $"{_heightScale:R}|{_heightRatio:R}|{_legendSteps}|unit:{preAppliedUnitScale:R}|" +
               $"{_normalizedLegend}|{_unit}|color:{_mapper.Fingerprint}";
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
        List<TextDot>   LegendDots)
    {
        public void DisposeMeshes()
        {
            Mesh.Dispose();
            FlatMesh.Dispose();
            LegendMesh.Dispose();
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SpatialField
    // ═════════════════════════════════════════════════════════════════════

    internal class SpatialField
    {
        public readonly List<Point3d> Points;
        public readonly List<double>  Values;
        public readonly double        MinValue;
        public readonly double        MaxValue;
        public readonly bool          HasMissing;
        private readonly Coordinate2D[] _coordinates;

        public SpatialField(List<Point3d> points, List<double> values)
        {
            var finite = points.Zip(values, (point, value) => (point, value))
                .Where(sample => double.IsFinite(sample.value) &&
                    double.IsFinite(sample.point.X) && double.IsFinite(sample.point.Y)).ToArray();
            if (finite.Length == 0) throw new ArgumentException("유한한 시각화 sample이 없습니다.");
            HasMissing = finite.Length != values.Count;
            Points = finite.Select(sample => sample.point).ToList();
            Values = finite.Select(sample => sample.value).ToList();
            _coordinates = Points.Select(point => new Coordinate2D(point.X, point.Y)).ToArray();
            MinValue = Values.Min();
            MaxValue = Values.Max();
        }

        /// IDW: F(q) = Σ[vᵢ / dᵢᵖ] / Σ[1 / dᵢᵖ]
        public double IDW(Point3d query, double power, CancellationToken cancellationToken = default)
        {
            return ExactIdw.Evaluate(new Coordinate2D(query.X, query.Y),
                _coordinates, Values, power, cancellationToken);
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
        public string Fingerprint { get; }

        public ColorMapper(Color[] stops, string name)
        {
            _stops = stops;
            StyleName = name;
            Fingerprint = name + ":" + string.Join(",", stops.Select(color => color.ToArgb()));
        }

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
            if (!double.IsFinite(t)) t = 0.5;
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
        private readonly CancellationToken _cancellationToken;
        private readonly VisualizationQuality _quality;

        private const double DEFAULT_TOL = 0.001;

        public MeshBuilder(SpatialField field, ColorMapper mapper,
                           double power, double heightScale, double maxEdgeLen,
                           double heightRatio, CancellationToken cancellationToken,
                           VisualizationQuality quality)
        {
            _field       = field;
            _mapper      = mapper;
            _power       = power;
            _heightScale = heightScale;
            _maxEdgeLen  = maxEdgeLen;
            _heightRatio = heightRatio;
            _cancellationToken = cancellationToken;
            _quality = quality;
        }

        public (Mesh elevated, Mesh flat) Build(Curve boundary)
            => Build(new IReadOnlyList<Curve>[] { new[] { boundary } });

        public (Mesh elevated, Mesh flat) Build(IReadOnlyList<Curve> boundaries)
            => Build(new[] { boundaries });

        public (Mesh elevated, Mesh flat) Build(IReadOnlyList<IReadOnlyList<Curve>> regions)
        {
            double tol = DEFAULT_TOL;
            _cancellationToken.ThrowIfCancellationRequested();

            Curve[] boundaries = regions.SelectMany(region => region).ToArray();
            BoundingBox sourceBounds = BoundingBox.Unset;
            foreach (Curve boundary in boundaries) sourceBounds.Union(boundary.GetBoundingBox(false));
            double width = sourceBounds.Max.X - sourceBounds.Min.X;
            double height = sourceBounds.Max.Y - sourceBounds.Min.Y;
            double resolvedEdge = VisualizationBudget.ClampResolution(
                _maxEdgeLen, width, height, _quality);
            int boundaryPointCount = boundaries.Sum(boundary =>
                boundary.TryGetPolyline(out Polyline boundaryPolyline)
                    ? boundaryPolyline.Count : Math.Max(4, boundary.SpanCount * 4));
            VisualizationBudget.EnsureEstimatedWithinBudget(
                width, height, resolvedEdge, boundaryPointCount, _quality);

            var breps = new List<Brep>();
            try
            {
                foreach (IReadOnlyList<Curve> region in regions)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    Brep[]? regionBreps = Brep.CreatePlanarBreps(region, tol);
                    if (regionBreps != null) breps.AddRange(regionBreps);
                }
                if (breps.Count == 0) return (new Mesh(), new Mesh());
            }
            catch
            {
                foreach (Brep brep in breps) brep.Dispose();
                throw;
            }

            var mp = new MeshingParameters
            {
                MaximumEdgeLength = resolvedEdge,
                MinimumEdgeLength = resolvedEdge * 0.1,
                JaggedSeams       = false,
                RefineGrid        = true,
            };

            using var baseMesh = new Mesh();
            try
            {
                foreach (Brep brep in breps)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    Mesh[]? meshes = Mesh.CreateFromBrep(brep, mp);
                    if (meshes == null) continue;
                    try
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        foreach (Mesh mesh in meshes)
                        {
                            _cancellationToken.ThrowIfCancellationRequested();
                            baseMesh.Append(mesh);
                        }
                    }
                    finally { foreach (Mesh mesh in meshes) mesh.Dispose(); }
                }
            }
            finally
            {
                foreach (Brep brep in breps) brep.Dispose();
            }
            baseMesh.Compact();
            _cancellationToken.ThrowIfCancellationRequested();

            int n = baseMesh.Vertices.Count;
            if (n == 0) return (new Mesh(), new Mesh());
            VisualizationBudget.EnsureActualWithinBudget(
                n, baseMesh.Faces.Count, _quality);

            BoundingBox bbox      = sourceBounds;
            double      bboxWidth = bbox.IsValid ? (bbox.Max.X - bbox.Min.X) : 1.0;

            // ── 버텍스별 IDW 보간 → Z 계산 ──────────────────────────────
            var    zValues = new double[n];
            var    fieldValues = new double[n];
            double zMax    = 0.0;

            for (int vi = 0; vi < n; vi++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                Point3d q  = baseMesh.Vertices.Point3dAt(vi);
                double  f  = _field.IDW(new Point3d(q.X, q.Y, 0.0), _power, _cancellationToken);
                fieldValues[vi] = f;
                double  zd = _field.Normalize(f, 0.0, bboxWidth * _heightRatio) * _heightScale;
                if (double.IsNaN(zd) || double.IsInfinity(zd)) zd = 0.0;
                zValues[vi] = zd;
                if (zd > zMax) zMax = zd;
            }

            // 색상 = Z 기준 정규화
            var colors = new Color[n];
            for (int vi = 0; vi < n; vi++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                double t = LegendContract.Normalize(
                    fieldValues[vi], _field.MinValue, _field.MaxValue);
                colors[vi] = _mapper.Map(t);
            }

            // elevated mesh
            Mesh? elevated = null;
            Mesh? flat = null;
            try
            {
                elevated = baseMesh.DuplicateMesh();
                _cancellationToken.ThrowIfCancellationRequested();
                elevated.TextureCoordinates.Clear();
                for (int vi = 0; vi < n; vi++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    Point3d vd = baseMesh.Vertices.Point3dAt(vi);
                    elevated.Vertices.SetVertex(vi, new Point3d(vd.X, vd.Y, zValues[vi]));
                }
                ApplyColors(elevated, colors, _cancellationToken);
                elevated.Normals.ComputeNormals();
                _cancellationToken.ThrowIfCancellationRequested();
                elevated.FaceNormals.ComputeFaceNormals();
                _cancellationToken.ThrowIfCancellationRequested();

                flat = baseMesh.DuplicateMesh();
                _cancellationToken.ThrowIfCancellationRequested();
                flat.TextureCoordinates.Clear();
                ApplyColors(flat, colors, _cancellationToken);
                flat.Normals.ComputeNormals();
                _cancellationToken.ThrowIfCancellationRequested();
                flat.FaceNormals.ComputeFaceNormals();
                _cancellationToken.ThrowIfCancellationRequested();
                return (elevated, flat);
            }
            catch { elevated?.Dispose(); flat?.Dispose(); throw; }
        }

        private static void ApplyColors(Mesh mesh, Color[] colors, CancellationToken cancellationToken)
        {
            mesh.VertexColors.CreateMonotoneMesh(Color.White);
            for (int i = 0; i < colors.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                mesh.VertexColors.SetColor(i, colors[i]);
            }
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
        private readonly bool         _normalized;
        private readonly string?      _unit;

        public LegendBuilder(SpatialField field, ColorMapper mapper,
                             Point3d anchor, double width, double height, int steps,
                             bool normalized, string? unit)
        {
            _field  = field;
            _mapper = mapper;
            _anchor = anchor;
            _width  = width;
            _height = height;
            _steps  = steps;
            _normalized = normalized;
            _unit = unit;
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
                var    dot = new TextDot(LegendContract.Format(val, _normalized, _unit),
                    new Point3d(labelX, y, 0.0));
                dot.FontHeight = 12;
                dots.Add(dot);
            }
            if (_field.HasMissing)
                dots.Add(new TextDot("No data", new Point3d(labelX, _anchor.Y - stepH, 0)));
            return dots;
        }
    }
}
