using System.Globalization;
using URSUS.Geometry;

namespace URSUS.Visualization;

public enum VisualizationQuality { Preview, Final }

public static class VisualizationBudget
{
    public const int PreviewVertexLimit = 50_000;
    public const int FinalVertexLimit = 250_000;
    public const int FinalFaceLimit = 500_000;
    public const long WorkingSetLimitBytes = 192L * 1024 * 1024;

    public static int VertexLimit(VisualizationQuality quality)
        => quality == VisualizationQuality.Preview ? PreviewVertexLimit : FinalVertexLimit;

    public static double ClampResolution(double requested, double width, double height,
        VisualizationQuality quality)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        double resolution = double.IsFinite(requested) && requested > 0
            ? requested : Math.Max(width, height) / 100.0;
        int limit = VertexLimit(quality);
        if (EstimateVertices(width, height, resolution) <= limit) return resolution;
        double minimum = Math.Sqrt(width * height / Math.Max(1, limit - 2 * Math.Sqrt(limit) - 1));
        while (EstimateVertices(width, height, minimum) > limit) minimum *= 1.01;
        return Math.Max(resolution, minimum);
    }

    public static long EstimateVertices(double width, double height, double resolution)
        => checked(((long)Math.Ceiling(width / resolution) + 1) *
                   ((long)Math.Ceiling(height / resolution) + 1));

    public static long EstimateWorkingSet(long vertices, long faces, int simultaneousCopies = 3)
        => checked((vertices * 160L + faces * 80L) * simultaneousCopies);

    public static void EnsureEstimatedWithinBudget(double width, double height, double resolution,
        int boundaryPointCount, VisualizationQuality quality)
    {
        long grid = EstimateVertices(width, height, resolution);
        long vertices = checked(grid + Math.Max(0, boundaryPointCount) * 16L);
        long faces = checked(vertices * 2L + Math.Max(0, boundaryPointCount) * 8L);
        if (vertices > VertexLimit(quality) || faces > FinalFaceLimit ||
            EstimateWorkingSet(vertices, faces) > WorkingSetLimitBytes)
            throw new InvalidOperationException(
                $"시각화 mesh 사전 예산 초과: estimated vertices={vertices}, faces={faces}.");
    }

    public static void EnsureActualWithinBudget(int vertices, int faces, VisualizationQuality quality)
    {
        if (vertices > VertexLimit(quality) || faces > FinalFaceLimit ||
            EstimateWorkingSet(vertices, faces) > WorkingSetLimitBytes)
            throw new InvalidOperationException(
                $"시각화 mesh 예산 초과: vertices={vertices}, faces={faces}.");
    }

    public static void EnsureTopologyWithinBudget(int pointCount, VisualizationQuality quality)
    {
        if (pointCount < 0) throw new ArgumentOutOfRangeException(nameof(pointCount));
        // flat + capped extrusion + temporary native meshes의 보수적 상한.
        long estimatedVertices = pointCount * 8L;
        long estimatedFaces = pointCount * 12L;
        if (estimatedVertices > VertexLimit(quality) || estimatedFaces > FinalFaceLimit ||
            EstimateWorkingSet(estimatedVertices, estimatedFaces) > WorkingSetLimitBytes)
            throw new InvalidOperationException(
                $"topology mesh 사전 예산 초과: points={pointCount}.");
    }
}

public static class VisualizationPalette
{
    public static System.Drawing.Color Map(int style, double value,
        System.Drawing.Color customLow, System.Drawing.Color customHigh)
    {
        var stops = style switch
        {
            1 => new[] { C(44, 123, 182), C(215, 25, 28) },
            2 => new[] { C(0, 128, 0), C(255, 255, 0), C(255, 0, 0) },
            3 => new[] { C(0, 0, 200), C(0, 200, 200), C(0, 200, 0), C(255, 255, 0), C(220, 0, 0) },
            4 => new[] { C(68, 1, 84), C(58, 82, 139), C(32, 144, 140), C(94, 201, 98), C(253, 231, 37) },
            5 => new[] { C(44, 123, 182), C(255, 255, 255), C(215, 25, 28) },
            6 => new[] { C(255, 255, 255), C(0, 0, 0) },
            _ => new[] { customLow, customHigh },
        };
        double t = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.5;
        double scaled = t * (stops.Length - 1);
        int lower = (int)scaled;
        int upper = Math.Min(lower + 1, stops.Length - 1);
        double local = scaled - lower;
        return System.Drawing.Color.FromArgb(
            Lerp(stops[lower].R, stops[upper].R, local),
            Lerp(stops[lower].G, stops[upper].G, local),
            Lerp(stops[lower].B, stops[upper].B, local));
    }

    private static System.Drawing.Color C(int r, int g, int b)
        => System.Drawing.Color.FromArgb(r, g, b);
    private static int Lerp(int a, int b, double t)
        => Math.Clamp((int)Math.Round(a + (b - a) * t), 0, 255);
}

public static class ExactIdw
{
    public static double Evaluate(Coordinate2D query, IReadOnlyList<Coordinate2D> points,
        IReadOnlyList<double> values, double power, CancellationToken cancellationToken = default)
    {
        if (points.Count != values.Count || points.Count == 0)
            throw new ArgumentException("IDW points와 values 길이는 같고 비어 있지 않아야 합니다.");
        if (!double.IsFinite(power) || power <= 0) throw new ArgumentOutOfRangeException(nameof(power));
        double numerator = 0, denominator = 0;
        for (int i = 0; i < points.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!double.IsFinite(values[i])) continue;
            double dx = query.X - points[i].X;
            double dy = query.Y - points[i].Y;
            double squared = dx * dx + dy * dy;
            if (squared < 1e-20) return values[i];
            double weight = 1.0 / Math.Pow(Math.Sqrt(squared), power);
            numerator += weight * values[i];
            denominator += weight;
        }
        return denominator <= 1e-20 ? double.NaN : numerator / denominator;
    }
}

public static class LegendContract
{
    public static int ClampSteps(int steps) => Math.Clamp(steps, 2, 20);

    public static string Format(double value, bool normalized, string? unit = null)
    {
        if (!double.IsFinite(value)) return "No data";
        string formatted = normalized
            ? value.ToString("0.###", CultureInfo.InvariantCulture)
            : Math.Abs(value) >= 1000
                ? value.ToString("0,0.##", CultureInfo.InvariantCulture)
                : value.ToString("0.###", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(unit) ? formatted : $"{formatted} {unit}";
    }

    public static double Normalize(double value, double minimum, double maximum)
        => !double.IsFinite(value) ? double.NaN
            : Math.Abs(maximum - minimum) < 1e-12 ? 0.5
            : Math.Clamp((value - minimum) / (maximum - minimum), 0, 1);
}

public sealed record UnitScaleResult(double LengthScale, double AreaScale, string? Warning);

public static class DocumentUnitScale
{
    public static UnitScaleResult FromMetersPerDocumentUnit(double metersPerDocumentUnit)
    {
        if (!double.IsFinite(metersPerDocumentUnit) || metersPerDocumentUnit <= 0)
            return new UnitScaleResult(1, 1, "유효하지 않은 Rhino 문서 단위입니다. meter로 처리합니다.");
        double length = 1.0 / metersPerDocumentUnit;
        return new UnitScaleResult(length, length * length, null);
    }
}

public sealed class BoundedLruCache<TKey, TValue> : IDisposable
    where TKey : notnull where TValue : IDisposable
{
    private sealed record Entry(TValue Value, long Bytes, LinkedListNode<TKey> Node);
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private readonly Dictionary<TKey, Entry> _entries = new();
    private readonly LinkedList<TKey> _lru = new();
    private long _bytes;

    public BoundedLruCache(int maxEntries = 2, long maxBytes = 256L * 1024 * 1024)
    {
        if (maxEntries <= 0 || maxBytes <= 0) throw new ArgumentOutOfRangeException();
        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
    }
    public int Count => _entries.Count;
    public long Bytes => _bytes;

    public bool TryGet(TKey key, out TValue? value)
    {
        if (!_entries.TryGetValue(key, out var entry)) { value = default; return false; }
        _lru.Remove(entry.Node);
        _lru.AddLast(entry.Node);
        value = entry.Value;
        return true;
    }

    public void Add(TKey key, TValue value, long bytes)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        if (_entries.Remove(key, out var previous))
        {
            _lru.Remove(previous.Node);
            _bytes -= previous.Bytes;
            previous.Value.Dispose();
        }
        var node = _lru.AddLast(key);
        _entries[key] = new Entry(value, bytes, node);
        _bytes += bytes;
        while (_entries.Count > _maxEntries || _bytes > _maxBytes)
            EvictOldest();
    }

    private void EvictOldest()
    {
        var node = _lru.First!;
        _lru.RemoveFirst();
        var entry = _entries[node.Value];
        _entries.Remove(node.Value);
        _bytes -= entry.Bytes;
        entry.Value.Dispose();
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values) entry.Value.Dispose();
        _entries.Clear();
        _lru.Clear();
        _bytes = 0;
    }
}

public sealed class VisualizationMeshCache : IDisposable
{
    private sealed class Bundle : IDisposable
    {
        public Bundle(VisualizerResult result)
        {
            Rhino.Geometry.Mesh? mesh = null, flat = null, legend = null;
            try
            {
                mesh = result.Mesh.DuplicateMesh();
                flat = result.FlatMesh.DuplicateMesh();
                legend = result.LegendMesh.DuplicateMesh();
                Mesh = mesh; Flat = flat; Legend = legend;
                Min = result.MinVal;
                Max = result.MaxVal;
                Dots = result.LegendDots.Select(dot => new Rhino.Geometry.TextDot(
                    dot.Text, dot.Point) { FontHeight = dot.FontHeight }).ToList();
            }
            catch
            {
                mesh?.Dispose(); flat?.Dispose(); legend?.Dispose();
                throw;
            }
        }
        public Rhino.Geometry.Mesh Mesh { get; }
        public Rhino.Geometry.Mesh Flat { get; }
        public Rhino.Geometry.Mesh Legend { get; }
        public double Min { get; }
        public double Max { get; }
        public List<Rhino.Geometry.TextDot> Dots { get; }
        public VisualizerResult Duplicate()
        {
            Rhino.Geometry.Mesh? mesh = null, flat = null, legend = null;
            try
            {
                mesh = Mesh.DuplicateMesh();
                flat = Flat.DuplicateMesh();
                legend = Legend.DuplicateMesh();
                return new VisualizerResult(mesh, flat, Min, Max, legend,
                    Dots.Select(dot => new Rhino.Geometry.TextDot(
                        dot.Text, dot.Point) { FontHeight = dot.FontHeight }).ToList());
            }
            catch
            {
                mesh?.Dispose(); flat?.Dispose(); legend?.Dispose();
                throw;
            }
        }
        public long Bytes => VisualizationBudget.EstimateWorkingSet(
            Mesh.Vertices.Count + Flat.Vertices.Count + Legend.Vertices.Count,
            Mesh.Faces.Count + Flat.Faces.Count + Legend.Faces.Count, 1);
        public void Dispose() { Mesh.Dispose(); Flat.Dispose(); Legend.Dispose(); }
    }

    private readonly object _sync = new();
    private readonly BoundedLruCache<string, Bundle> _cache = new(2, 256L * 1024 * 1024);
    private bool _disposed;
    public int Count { get { lock (_sync) return _cache.Count; } }
    public bool TryGet(string key, out VisualizerResult? result)
    {
        lock (_sync)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VisualizationMeshCache));
            if (!_cache.TryGet(key, out Bundle? bundle) || bundle == null)
            { result = null; return false; }
            result = bundle.Duplicate();
            return true;
        }
    }
    public void Add(string key, VisualizerResult result)
    {
        lock (_sync)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VisualizationMeshCache));
            var bundle = new Bundle(result);
            try { _cache.Add(key, bundle, bundle.Bytes); }
            catch { bundle.Dispose(); throw; }
        }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _cache.Dispose();
        }
    }
}
