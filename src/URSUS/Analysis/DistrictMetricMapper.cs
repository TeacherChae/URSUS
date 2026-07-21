namespace URSUS.Analysis;

public enum MetricSemantics { Mean, Sum }
public enum MappingQuality { AssumedUniform, EstimatedEqualSplit }

public sealed record DistrictMappingResult(
    IReadOnlyDictionary<string, double> Values,
    MappingQuality Quality);

public static class DistrictMetricMapper
{
    public static DistrictMappingResult MapAdministrativeToLegal(
        IReadOnlyDictionary<string, IReadOnlyList<string>> mapping,
        IReadOnlyDictionary<string, double> source,
        MetricSemantics semantics)
    {
        var accumulated = new Dictionary<string, (double sum, int count)>();
        foreach (var pair in mapping)
        {
            if (!source.TryGetValue(pair.Key, out double value) || !double.IsFinite(value)) continue;
            var legalIds = pair.Value.Distinct(StringComparer.Ordinal).ToArray();
            if (legalIds.Length == 0) continue;
            double mappedValue = semantics == MetricSemantics.Sum ? value / legalIds.Length : value;
            foreach (string legalId in legalIds)
            {
                if (accumulated.TryGetValue(legalId, out var previous))
                    accumulated[legalId] = (previous.sum + mappedValue, previous.count + 1);
                else
                    accumulated[legalId] = (mappedValue, 1);
            }
        }

        return new DistrictMappingResult(
            accumulated.ToDictionary(
                pair => pair.Key,
                pair => semantics == MetricSemantics.Sum
                    ? pair.Value.sum
                    : pair.Value.sum / pair.Value.count,
                StringComparer.Ordinal),
            semantics == MetricSemantics.Sum
                ? MappingQuality.EstimatedEqualSplit
                : MappingQuality.AssumedUniform);
    }
}
