using URSUS.Caching;
using URSUS.DataSources;
using URSUS.Geometry;
using URSUS.Preprocessing;
using System.Collections.ObjectModel;

namespace URSUS.Analysis;

public enum SnapshotWarningSeverity { Info, Warning, High }
public sealed record SnapshotWarning(SnapshotWarningSeverity Severity, string Code, string Message);
public sealed record SnapshotLayer(
    string Id,
    string? Unit,
    IReadOnlyDictionary<string, double> RawValues,
    ObservationWindow? Observation,
    DateTimeOffset RetrievedAt,
    AcquisitionOrigin AcquisitionOrigin,
    DeliveryOrigin DeliveryOrigin,
    double Coverage,
    TimeSpan? CacheAge = null,
    MappingQuality? MappingQuality = null,
    IReadOnlyDictionary<string, ZoningCategoryHistogram>? CategoricalHistograms = null,
    IReadOnlyDictionary<string, int>? SampleCounts = null);
public sealed record SnapshotFailure(string SourceId, string Code, string Message);
public sealed record NormalizationStatistics(double? Minimum, double? Maximum, int FiniteCount);

public sealed class AnalysisSnapshot
{
    public IReadOnlyList<string> ProjectionOrder { get; }
    public IReadOnlyList<string> DistrictIndex { get; }
    public IReadOnlyDictionary<string, SnapshotLayer> Layers { get; }
    public IReadOnlyDictionary<string, BoundaryTopology> Topologies { get; }
    public IReadOnlyDictionary<string, ExactSpatialLayerBinding> SpatialLayers { get; }
    public IReadOnlyList<SnapshotWarning> Warnings { get; }
    public IReadOnlyList<SnapshotFailure> Failures { get; }
    public IReadOnlyDictionary<string, NormalizationStatistics> Normalization { get; }
    public CoordinateReferenceSystem Crs { get; }

    public AnalysisSnapshot(
        IEnumerable<string> districtIndex,
        IEnumerable<SnapshotLayer> layers,
        IReadOnlyDictionary<string, BoundaryTopology>? topologies = null,
        IEnumerable<SnapshotWarning>? warnings = null,
        IEnumerable<SnapshotFailure>? failures = null,
        CoordinateReferenceSystem crs = CoordinateReferenceSystem.Epsg5179,
        IEnumerable<ExactSpatialLayerBinding>? spatialLayers = null)
    {
        ProjectionOrder = Array.AsReadOnly(districtIndex.Select(DistrictCode.CanonicalizeLegal)
            .Where(code => code.Length > 0).Distinct(StringComparer.Ordinal).ToArray());
        DistrictIndex = Array.AsReadOnly(
            ProjectionOrder.OrderBy(code => code, StringComparer.Ordinal).ToArray());
        Layers = new ReadOnlyDictionary<string, SnapshotLayer>(layers.ToDictionary(
            layer => layer.Id,
            layer => layer with
            {
                RawValues = new ReadOnlyDictionary<string, double>(
                    new Dictionary<string, double>(layer.RawValues, StringComparer.Ordinal)),
                Observation = layer.Observation == null ? null : layer.Observation with
                {
                    MissingIds = Array.AsReadOnly(
                        layer.Observation.MissingIds.ToArray()),
                },
                CategoricalHistograms = layer.CategoricalHistograms == null
                    ? null
                    : new ReadOnlyDictionary<string, ZoningCategoryHistogram>(
                        new Dictionary<string, ZoningCategoryHistogram>(
                            layer.CategoricalHistograms, StringComparer.Ordinal)),
                SampleCounts = layer.SampleCounts == null
                    ? null
                    : new ReadOnlyDictionary<string, int>(
                        new Dictionary<string, int>(
                            layer.SampleCounts, StringComparer.Ordinal)),
            }, StringComparer.Ordinal));
        Topologies = new ReadOnlyDictionary<string, BoundaryTopology>(
            new Dictionary<string, BoundaryTopology>(
                topologies ?? new Dictionary<string, BoundaryTopology>(), StringComparer.Ordinal));
        var spatialLayerCopy = spatialLayers?.ToArray() ??
            Array.Empty<ExactSpatialLayerBinding>();
        string[] duplicateSpatialLayerIds = spatialLayerCopy
            .GroupBy(layer => layer.LayerId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateSpatialLayerIds.Length > 0)
            throw new ArgumentException(
                $"spatial layer ID가 중복되었습니다: {string.Join(", ", duplicateSpatialLayerIds)}",
                nameof(spatialLayers));
        SpatialLayers = new ReadOnlyDictionary<string, ExactSpatialLayerBinding>(
            spatialLayerCopy.ToDictionary(
                layer => layer.LayerId,
                layer => layer,
                StringComparer.Ordinal));
        Warnings = Array.AsReadOnly(warnings?.ToArray() ?? Array.Empty<SnapshotWarning>());
        Failures = Array.AsReadOnly(failures?.ToArray() ?? Array.Empty<SnapshotFailure>());
        Normalization = new ReadOnlyDictionary<string, NormalizationStatistics>(
            Layers.ToDictionary(pair => pair.Key, pair =>
            {
                var finite = pair.Value.RawValues.Values.Where(double.IsFinite).ToArray();
                return new NormalizationStatistics(
                    finite.Length == 0 ? null : finite.Min(),
                    finite.Length == 0 ? null : finite.Max(),
                    finite.Length);
            }, StringComparer.Ordinal));
        Crs = crs;
    }
}
