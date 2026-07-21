using System;
using System.Collections.Generic;
using System.Linq;
using URSUS.DataSources;

namespace URSUS.Analysis
{
    public sealed record OverlayLayer(
        string Name,
        double Weight,
        IReadOnlyDictionary<string, double> Values);

    public enum LayerAvailability
    {
        Unavailable,
        Partial,
        Complete
    }

    public sealed record LayerCoverage(
        string Name,
        double Coverage,
        int AvailableCount,
        int RequestedCount,
        LayerAvailability Availability);

    public sealed record OverlayComputation(
        IReadOnlyList<double> Values,
        IReadOnlyList<LayerCoverage> Layers,
        IReadOnlyList<string> MissingLayers);

    /// <summary>
    /// 레이어별 observed value만 정규화하고, 결측 셀에서는 존재하는 레이어의
    /// 가중치만 다시 정규화한다. 결측을 전체 평균으로 대치하지 않는다.
    /// </summary>
    public static class OverlayCalculator
    {
        public static OverlayComputation Compute(
            IReadOnlyList<string> districtCodes,
            IReadOnlyList<OverlayLayer> layers,
            CancellationToken cancellationToken = default)
        {
            if (districtCodes == null) throw new ArgumentNullException(nameof(districtCodes));
            if (layers == null) throw new ArgumentNullException(nameof(layers));

            var uniqueCodes = new List<string>(districtCodes.Count);
            var seenCodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (string rawCode in districtCodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string code = ToComparisonKey(rawCode);
                if (seenCodes.Add(code)) uniqueCodes.Add(code);
            }
            var normalizedByLayer = new List<Dictionary<string, double>>(layers.Count);
            var coverage = new List<LayerCoverage>(layers.Count);

            foreach (var layer in layers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (layer.Weight < 0 || !double.IsFinite(layer.Weight))
                    throw new ArgumentException($"레이어 '{layer.Name}'의 가중치는 유한한 0 이상 값이어야 합니다.");

                var sums = new Dictionary<string, (double Sum, int Count)>(StringComparer.Ordinal);
                foreach (var pair in layer.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!double.IsFinite(pair.Value)) continue;
                    string code = ToComparisonKey(pair.Key);
                    var previous = sums.GetValueOrDefault(code);
                    sums[code] = (previous.Sum + pair.Value, previous.Count + 1);
                }
                var canonicalLayerValues = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var pair in sums)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    canonicalLayerValues[pair.Key] = pair.Value.Sum / pair.Value.Count;
                }
                var observed = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (string code in uniqueCodes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (canonicalLayerValues.TryGetValue(code, out double value))
                        observed[code] = value;
                }

                double ratio = uniqueCodes.Count == 0
                    ? 0.0
                    : (double)observed.Count / uniqueCodes.Count;
                var availability = observed.Count == 0
                    ? LayerAvailability.Unavailable
                    : observed.Count == uniqueCodes.Count
                        ? LayerAvailability.Complete
                        : LayerAvailability.Partial;

                coverage.Add(new LayerCoverage(
                    layer.Name, ratio, observed.Count, uniqueCodes.Count, availability));

                if (observed.Count == 0)
                {
                    normalizedByLayer.Add(new Dictionary<string, double>());
                    continue;
                }

                double min = double.PositiveInfinity;
                double max = double.NegativeInfinity;
                foreach (double value in observed.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }
                double range = max - min;
                var normalized = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var pair in observed)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    normalized[pair.Key] = range < 1e-9 ? 0.5 : (pair.Value - min) / range;
                }
                normalizedByLayer.Add(normalized);
            }

            var result = new List<double>(districtCodes.Count);
            foreach (string rawCode in districtCodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string code = ToComparisonKey(rawCode);
                double weighted = 0.0;
                double weightSum = 0.0;

                for (int i = 0; i < layers.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double weight = layers[i].Weight;
                    if (weight <= 0 || !normalizedByLayer[i].TryGetValue(code, out double value))
                        continue;

                    weighted += value * weight;
                    weightSum += weight;
                }

                result.Add(weightSum > 1e-12 ? weighted / weightSum : double.NaN);
            }

            var missing = new List<string>();
            foreach (var item in coverage)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Availability == LayerAvailability.Unavailable) missing.Add(item.Name);
            }
            return new OverlayComputation(result, coverage, missing);
        }

        public static IReadOnlyList<string> FindMissingLayers(
            IReadOnlyList<string> requestedLayers,
            IReadOnlyList<LayerCoverage> coverage)
            => FindMissingLayers(requestedLayers, coverage, CancellationToken.None);

        public static IReadOnlyList<string> FindMissingLayers(
            IReadOnlyList<string> requestedLayers,
            IReadOnlyList<LayerCoverage> coverage,
            CancellationToken cancellationToken)
        {
            var available = new HashSet<string>(StringComparer.Ordinal);
            foreach (var layer in coverage)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (layer.Availability != LayerAvailability.Unavailable) available.Add(layer.Name);
            }
            var missing = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in requestedLayers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!available.Contains(name) && seen.Add(name)) missing.Add(name);
            }
            return missing;
        }

        private static string ToComparisonKey(string value)
        {
            string canonical = DistrictCode.CanonicalizeLegal(value);
            return string.IsNullOrEmpty(canonical) ? value : canonical;
        }
    }
}
