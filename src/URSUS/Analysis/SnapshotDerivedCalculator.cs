using URSUS.Config;

namespace URSUS.Analysis;

public sealed record SnapshotDerivedOverlay(
    IReadOnlyDictionary<string, double> Values,
    WeightConfig? EffectiveWeights,
    IReadOnlyList<LayerCoverage> Coverage,
    IReadOnlyList<string> ActiveLayers,
    IReadOnlyList<string> MissingLayers);

/// <summary>Immutable snapshot의 raw layer만 사용하므로 source/network 호출이 불가능한 derived 계산.</summary>
public static class SnapshotDerivedCalculator
{
    public static SnapshotDerivedOverlay Recompute(
        AnalysisSnapshot snapshot,
        IReadOnlyList<string> requestedLayers,
        IReadOnlyList<double>? requestedWeights,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(requestedLayers);
        if (requestedWeights != null && requestedWeights.Count != requestedLayers.Count)
            throw new ArgumentException("layer와 weight 길이가 일치해야 합니다.", nameof(requestedWeights));

        var active = requestedLayers.Where(snapshot.Layers.ContainsKey).ToArray();
        var missing = requestedLayers.Where(layer => !snapshot.Layers.ContainsKey(layer)).ToArray();
        if (active.Length == 0)
            return new SnapshotDerivedOverlay(new Dictionary<string, double>(), null,
                Array.Empty<LayerCoverage>(), active, missing);

        var rawWeights = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (string layer in active)
        {
            int requestedIndex = IndexOf(requestedLayers, layer);
            rawWeights[layer] = requestedWeights == null ? 1.0 : requestedWeights[requestedIndex];
        }
        WeightConfig config = WeightConfig.Create(rawWeights);
        var overlay = OverlayCalculator.Compute(snapshot.DistrictIndex,
            active.Select(layer => new OverlayLayer(layer, config.Weights[layer],
                snapshot.Layers[layer].RawValues)).ToArray(), cancellationToken);
        return new SnapshotDerivedOverlay(
            snapshot.DistrictIndex.Select((code, index) => (code, value: overlay.Values[index]))
                .ToDictionary(item => item.code, item => item.value, StringComparer.Ordinal),
            config, overlay.Layers, active, missing);
    }

    private static int IndexOf(IReadOnlyList<string> values, string target)
    {
        for (int i = 0; i < values.Count; i++)
            if (string.Equals(values[i], target, StringComparison.Ordinal)) return i;
        return -1;
    }
}
