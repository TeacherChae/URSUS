using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using URSUS.Analysis;
using URSUS.DataSources;
using URSUS.Geometry;
using URSUS.Preprocessing;

namespace URSUS.Visualization;

public enum VisualizationMode { Choropleth = 0, Extrusion = 1, Idw = 2 }

public sealed record ChoroplethDistrict(
    string Code,
    BoundaryTopology Topology,
    double RawValue,
    double NormalizedValue,
    bool IsMissing,
    double Height);

public sealed record ChoroplethPlan(
    string LayerId,
    string? Unit,
    double Minimum,
    double Maximum,
    IReadOnlyList<ChoroplethDistrict> Districts,
    IReadOnlyList<string> MissingCodes,
    string SnapshotKey,
    bool IsNormalized);

/// <summary>
/// Rhino와 네트워크에 의존하지 않는 snapshot→시각화 계획 변환기다.
/// 원본 multipart/hole topology를 그대로 넘기고 GH adapter는 이 계획만 mesh로 변환한다.
/// </summary>
public static class ChoroplethPlanner
{
    public static ChoroplethPlan Create(
        AnalysisSnapshot snapshot,
        string? requestedLayerId,
        VisualizationMode mode,
        double heightScale,
        IReadOnlyList<double>? overlayValues = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (mode == VisualizationMode.Idw)
            throw new ArgumentException("IDW는 choropleth 계획 대상이 아닙니다.", nameof(mode));
        if (!double.IsFinite(heightScale) || heightScale < 0)
            throw new ArgumentOutOfRangeException(nameof(heightScale));

        bool useOverlay = string.IsNullOrWhiteSpace(requestedLayerId);
        if (!useOverlay &&
            snapshot.SpatialLayers.TryGetValue(requestedLayerId!.Trim(),
                out ExactSpatialLayerBinding? spatialLayer))
            return CreateSpatial(spatialLayer, mode, heightScale);
        if (useOverlay && snapshot.SpatialLayers.Count > 0)
            throw new InvalidOperationException(
                "native spatial layer는 global DistrictIndex overlay로 암묵적으로 합칠 수 없습니다.");

        string layerId;
        SnapshotLayer layer;
        if (useOverlay)
        {
            if (overlayValues == null || overlayValues.Count != snapshot.ProjectionOrder.Count)
                throw new ArgumentException(
                    "Layer Id가 비어 있으면 Solver Values가 snapshot projection order와 같은 길이로 필요합니다.",
                    nameof(overlayValues));
            layerId = "overlay";
            layer = new SnapshotLayer(layerId, null,
                snapshot.ProjectionOrder.Select((code, index) => (code, value: overlayValues[index]))
                    .ToDictionary(item => item.code, item => item.value, StringComparer.Ordinal),
                null, DateTimeOffset.UnixEpoch, Caching.AcquisitionOrigin.Network,
                Caching.DeliveryOrigin.Network, 1);
        }
        else
        {
            layerId = string.IsNullOrWhiteSpace(requestedLayerId)
                ? snapshot.Layers.Keys.OrderBy(key => key, StringComparer.Ordinal).FirstOrDefault()
                    ?? throw new InvalidOperationException("시각화 가능한 snapshot layer가 없습니다.")
                : requestedLayerId.Trim();
            if (!snapshot.Layers.TryGetValue(layerId, out layer!))
                throw new KeyNotFoundException($"snapshot layer '{layerId}'를 찾을 수 없습니다.");
        }

        var finite = snapshot.DistrictIndex
            .Select(code => layer.RawValues.TryGetValue(code, out double value) ? value : double.NaN)
            .Where(double.IsFinite).ToArray();
        if (finite.Length == 0)
            throw new InvalidOperationException($"layer '{layerId}'에 유한한 값이 없습니다.");
        double minimum = finite.Min();
        double maximum = finite.Max();

        var districts = new List<ChoroplethDistrict>();
        var missing = new List<string>();
        foreach (string code in snapshot.DistrictIndex)
        {
            if (!snapshot.Topologies.TryGetValue(code, out BoundaryTopology? topology))
            {
                missing.Add(code);
                continue;
            }
            bool hasValue = layer.RawValues.TryGetValue(code, out double raw) && double.IsFinite(raw);
            double normalized = hasValue ? LegendContract.Normalize(raw, minimum, maximum) : double.NaN;
            if (!hasValue) missing.Add(code);
            districts.Add(new ChoroplethDistrict(code, topology, raw, normalized, !hasValue,
                mode == VisualizationMode.Extrusion && hasValue ? normalized * heightScale : 0));
        }
        if (districts.Count == 0)
            throw new InvalidOperationException("mesh로 변환할 snapshot topology가 없습니다.");

        return new ChoroplethPlan(layerId, layer.Unit, minimum, maximum, districts,
            missing.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToArray(),
            ComputeSnapshotKey(snapshot, layer), useOverlay);
    }

    private static ChoroplethPlan CreateSpatial(
        ExactSpatialLayerBinding layer,
        VisualizationMode mode,
        double heightScale)
    {
        if (layer.Crs != CoordinateReferenceSystem.Epsg5179)
            throw new InvalidOperationException(
                $"spatial layer '{layer.LayerId}'는 EPSG:5179 Geometry로 투영된 뒤 시각화해야 합니다.");
        if (layer.Features.Count == 0)
            throw new InvalidOperationException(
                $"spatial layer '{layer.LayerId}'에 feature가 없습니다.");

        double minimum = layer.Features.Min(feature => feature.Value);
        double maximum = layer.Features.Max(feature => feature.Value);
        var districts = layer.Features
            .OrderBy(feature => feature.UnitId.Value, StringComparer.Ordinal)
            .Select(feature =>
            {
                double normalized = LegendContract.Normalize(feature.Value, minimum, maximum);
                return new ChoroplethDistrict(
                    feature.UnitId.Value,
                    feature.Geometry,
                    feature.Value,
                    normalized,
                    false,
                    mode == VisualizationMode.Extrusion ? normalized * heightScale : 0);
            })
            .ToArray();
        return new ChoroplethPlan(
            layer.LayerId,
            layer.Unit,
            minimum,
            maximum,
            districts,
            Array.Empty<string>(),
            ComputeSpatialLayerKey(layer),
            false);
    }

    private static string ComputeSpatialLayerKey(ExactSpatialLayerBinding layer)
    {
        var canonical = new StringBuilder("spatial-layer-v1|")
            .Append(layer.LayerId).Append('|')
            .Append(layer.Unit).Append('|')
            .Append(layer.Schema.Identity).Append('|')
            .Append(layer.Crs).Append('|')
            .Append(layer.StatisticSource.ProviderId).Append('/')
            .Append(layer.StatisticSource.DatasetId).Append('/')
            .Append(layer.StatisticSource.SchemaVersion).Append('/')
            .Append(layer.StatisticSource.EvidenceReference).Append('|')
            .Append(layer.GeometrySource.ProviderId).Append('/')
            .Append(layer.GeometrySource.DatasetId).Append('/')
            .Append(layer.GeometrySource.SchemaVersion).Append('/')
            .Append(layer.GeometrySource.EvidenceReference).Append('|')
            .Append(layer.StatisticProjection.ProjectionId).Append('/')
            .Append(layer.StatisticProjection.EvidenceReference).Append('|')
            .Append(layer.GeometryProjection.ProjectionId).Append('/')
            .Append(layer.GeometryProjection.EvidenceReference);
        foreach (ExactSpatialFeature feature in layer.Features
                     .OrderBy(item => item.UnitId.Value, StringComparer.Ordinal))
        {
            canonical.Append("|u:").Append(feature.UnitId.Value)
                .Append("|v:").Append(feature.Value.ToString("R", CultureInfo.InvariantCulture));
            foreach (BoundaryPart part in feature.Geometry.Parts)
            {
                AppendRing(canonical, 'o', part.Outer);
                foreach (BoundaryRing hole in part.Holes) AppendRing(canonical, 'h', hole);
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static string ComputeSnapshotKey(AnalysisSnapshot snapshot, SnapshotLayer layer)
    {
        var canonical = new StringBuilder("snapshot-v1|")
            .Append(snapshot.Crs).Append('|').Append(layer.Id).Append('|').Append(layer.Unit);
        foreach (string code in snapshot.DistrictIndex)
        {
            canonical.Append("|d:").Append(code).Append("|v:")
                .Append(layer.RawValues.TryGetValue(code, out double value)
                    ? value.ToString("R", CultureInfo.InvariantCulture) : "missing");
            if (!snapshot.Topologies.TryGetValue(code, out BoundaryTopology? topology)) continue;
            foreach (BoundaryPart part in topology.Parts)
            {
                AppendRing(canonical, 'o', part.Outer);
                foreach (BoundaryRing hole in part.Holes) AppendRing(canonical, 'h', hole);
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static void AppendRing(StringBuilder target, char kind, BoundaryRing ring)
    {
        target.Append('|').Append(kind).Append(':');
        foreach (Coordinate2D point in ring.Points)
            target.Append(point.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.Y.ToString("R", CultureInfo.InvariantCulture)).Append(';');
    }
}
