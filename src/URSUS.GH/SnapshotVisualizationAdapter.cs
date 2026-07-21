using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using Rhino.Geometry;
using URSUS.Analysis;
using URSUS.Geometry;
using URSUS.Visualization;

namespace URSUS.GH;

internal sealed record SnapshotVisualizationOutput(
    VisualizerResult Result, IReadOnlyList<string> MissingCodes, string Status);

/// <summary>Immutable snapshot을 Rhino document-unit mesh로 바꾸는 유일한 native 경계.</summary>
internal static class SnapshotVisualizationAdapter
{
    internal static List<IReadOnlyList<Curve>> CreateDomainRegions(
        AnalysisSnapshot snapshot, double lengthScale, CancellationToken cancellationToken)
    {
        var regions = new List<IReadOnlyList<Curve>>();
        int pointCount = 0;
        try
        {
            foreach (string code in snapshot.DistrictIndex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!snapshot.Topologies.TryGetValue(code, out BoundaryTopology? topology)) continue;
                foreach (BoundaryPart part in topology.Parts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    pointCount += part.Outer.Points.Count +
                        part.Holes.Sum(hole => hole.Points.Count);
                    VisualizationBudget.EnsureTopologyWithinBudget(
                        pointCount, VisualizationQuality.Preview);
                    var region = new List<Curve> { ToCurve(part.Outer, lengthScale) };
                    region.AddRange(part.Holes.Select(hole => ToCurve(hole, lengthScale)));
                    regions.Add(region);
                }
            }
            if (regions.Count == 0)
                throw new InvalidOperationException("snapshot topology domain이 없습니다.");
            return regions;
        }
        catch
        {
            foreach (Curve curve in regions.SelectMany(region => region)) curve.Dispose();
            throw;
        }
    }

    internal static string DescribeProvenance(AnalysisSnapshot snapshot, string layerId)
    {
        IEnumerable<SnapshotLayer> layers = snapshot.Layers.TryGetValue(layerId, out SnapshotLayer? selected)
            ? new[] { selected } : snapshot.Layers.Values;
        SnapshotLayer[] materialized = layers.ToArray();
        if (materialized.Length == 0) return "provenance unavailable";
        string origins = string.Join(",", materialized
            .Select(layer => $"{layer.AcquisitionOrigin}/{layer.DeliveryOrigin}")
            .Distinct(StringComparer.Ordinal));
        string periods = string.Join(",", materialized.Where(layer => layer.Observation != null)
            .Select(layer => layer.Observation!.ToString()).Distinct(StringComparer.Ordinal));
        TimeSpan? oldestCache = materialized.Where(layer => layer.CacheAge.HasValue)
            .Select(layer => layer.CacheAge).Max();
        return $"coverage {materialized.Average(layer => layer.Coverage):P0} · {origins}" +
            (oldestCache.HasValue ? $" · age {oldestCache.Value:g}" : "") +
            (periods.Length == 0 ? "" : $" · period {periods}");
    }

    internal static SnapshotVisualizationOutput Build(
        AnalysisSnapshot snapshot,
        string? layerId,
        IReadOnlyList<double>? overlayValues,
        VisualizationMode mode,
        double heightScale,
        int legendSteps,
        int colorStyle,
        Color colorLow,
        Color colorHigh,
        VisualizationMeshCache cache,
        double metersPerDocumentUnit,
        double tolerance,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = ChoroplethPlanner.Create(snapshot, layerId, mode, heightScale, overlayValues);
        var unitScale = DocumentUnitScale.FromMetersPerDocumentUnit(metersPerDocumentUnit);
        var quality = VisualizationQuality.Preview;
        int pointCount = plan.Districts.Sum(district => district.Topology.Parts.Sum(part =>
            part.Outer.Points.Count + part.Holes.Sum(hole => hole.Points.Count)));
        VisualizationBudget.EnsureTopologyWithinBudget(pointCount, quality);

        string cacheKey = $"{plan.SnapshotKey}|{mode}|preview|scale:{unitScale.LengthScale:R}|" +
            $"height:{heightScale:R}|legend:{LegendContract.ClampSteps(legendSteps)}|" +
            $"color:{colorStyle}:{colorLow.ToArgb()}:{colorHigh.ToArgb()}|tol:{tolerance:R}";
        if (cache.TryGet(cacheKey, out VisualizerResult? cached) && cached != null)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new SnapshotVisualizationOutput(cached, plan.MissingCodes,
                    BuildStatus(snapshot, plan, mode, unitScale.Warning, cached: true));
            }
            catch { cached.DisposeMeshes(); throw; }
        }

        using var elevated = new Mesh();
        using var flat = new Mesh();
        foreach (ChoroplethDistrict district in plan.Districts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Color color = district.IsMissing
                ? Color.FromArgb(150, 150, 150)
                : VisualizationPalette.Map(colorStyle, district.NormalizedValue, colorLow, colorHigh);
            foreach (BoundaryPart part in district.Topology.Parts)
                AppendPart(part, district.Height * unitScale.LengthScale, unitScale.LengthScale,
                    tolerance, color, elevated, flat, cancellationToken);
        }
        elevated.Normals.ComputeNormals();
        flat.Normals.ComputeNormals();
        elevated.Compact();
        flat.Compact();
        cancellationToken.ThrowIfCancellationRequested();
        VisualizationBudget.EnsureActualWithinBudget(
            elevated.Vertices.Count + flat.Vertices.Count,
            elevated.Faces.Count + flat.Faces.Count, quality);

        Mesh legend = BuildLegend(elevated.GetBoundingBox(false), plan, legendSteps,
            colorStyle, colorLow, colorHigh, out List<TextDot> dots);
        var result = new VisualizerResult(elevated.DuplicateMesh(), flat.DuplicateMesh(),
            plan.Minimum, plan.Maximum, legend, dots);
        try { cache.Add(cacheKey, result); }
        catch { result.DisposeMeshes(); throw; }
        return new SnapshotVisualizationOutput(result, plan.MissingCodes,
            BuildStatus(snapshot, plan, mode, unitScale.Warning, cached: false));
    }

    private static void AppendPart(BoundaryPart part, double height, double scale,
        double tolerance, Color color, Mesh elevated, Mesh flat,
        CancellationToken cancellationToken)
    {
        var curves = new List<Curve> { ToCurve(part.Outer, scale) };
        curves.AddRange(part.Holes.Select(hole => ToCurve(hole, scale)));
        Brep[]? planar = null;
        try
        {
            planar = Brep.CreatePlanarBreps(curves, tolerance);
            cancellationToken.ThrowIfCancellationRequested();
            if (planar == null || planar.Length == 0) return;
            foreach (Brep baseBrep in planar)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendMeshed(baseBrep, color, flat, cancellationToken);
                if (height <= 0)
                {
                    AppendMeshed(baseBrep, color, elevated, cancellationToken);
                    continue;
                }
                using var path = new LineCurve(Point3d.Origin, new Point3d(0, 0, height));
                using Brep? extrusion = baseBrep.Faces[0].CreateExtrusion(path, true);
                cancellationToken.ThrowIfCancellationRequested();
                if (extrusion != null) AppendMeshed(extrusion, color, elevated, cancellationToken);
            }
        }
        finally
        {
            if (planar != null) foreach (Brep brep in planar) brep.Dispose();
            foreach (Curve curve in curves) curve.Dispose();
        }
    }

    private static PolylineCurve ToCurve(BoundaryRing ring, double scale)
        => new(new Polyline(ring.Points.Select(point =>
            new Point3d(point.X * scale, point.Y * scale, 0))));

    private static void AppendMeshed(Brep brep, Color color, Mesh target,
        CancellationToken cancellationToken)
    {
        Mesh[]? meshes = Mesh.CreateFromBrep(brep, MeshingParameters.FastRenderMesh);
        cancellationToken.ThrowIfCancellationRequested();
        if (meshes == null) return;
        try
        {
            foreach (Mesh mesh in meshes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                mesh.VertexColors.CreateMonotoneMesh(color);
                target.Append(mesh);
            }
        }
        finally { foreach (Mesh mesh in meshes) mesh.Dispose(); }
    }

    private static Mesh BuildLegend(BoundingBox bounds, ChoroplethPlan plan, int requestedSteps,
        int style, Color low, Color high, out List<TextDot> dots)
    {
        int steps = LegendContract.ClampSteps(requestedSteps);
        double height = Math.Max(bounds.Diagonal.Y, 1);
        double width = Math.Max(bounds.Diagonal.X * 0.03, height * 0.03);
        double x = bounds.Max.X + width;
        double y = bounds.Min.Y;
        var legend = new Mesh();
        for (int i = 0; i < steps; i++)
        {
            double y0 = y + height * i / steps;
            double y1 = y + height * (i + 1) / steps;
            int start = legend.Vertices.Count;
            legend.Vertices.Add(x, y0, 0); legend.Vertices.Add(x + width, y0, 0);
            legend.Vertices.Add(x + width, y1, 0); legend.Vertices.Add(x, y1, 0);
            legend.Faces.AddFace(start, start + 1, start + 2, start + 3);
            Color color = VisualizationPalette.Map(style, (i + 0.5) / steps, low, high);
            for (int vertex = 0; vertex < 4; vertex++) legend.VertexColors.Add(color);
        }
        dots = Enumerable.Range(0, steps + 1).Select(index =>
        {
            double t = (double)index / steps;
            double value = plan.Minimum + t * (plan.Maximum - plan.Minimum);
            return new TextDot(LegendContract.Format(value, plan.IsNormalized, plan.Unit),
                new Point3d(x + width * 1.3, y + height * t, 0));
        }).ToList();
        if (plan.MissingCodes.Count > 0)
            dots.Add(new TextDot($"No data ({plan.MissingCodes.Count})",
                new Point3d(x + width * 1.3, y - height * 0.04, 0)));
        return legend;
    }

    private static string BuildStatus(AnalysisSnapshot snapshot, ChoroplethPlan plan, VisualizationMode mode,
        string? warning, bool cached)
        => $"{mode} · {plan.LayerId} · preview · missing {plan.MissingCodes.Count} · " +
           DescribeProvenance(snapshot, plan.LayerId) +
           (cached ? " · cache" : "") +
           (string.IsNullOrWhiteSpace(warning) ? "" : $" · {warning}");
}
