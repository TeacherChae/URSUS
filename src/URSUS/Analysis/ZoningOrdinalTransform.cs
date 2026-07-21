namespace URSUS.Analysis;

using System.Collections.ObjectModel;

public sealed record ZoningCategoryHistogram
{
    public IReadOnlyDictionary<string, int> Counts { get; }
    public int SampleCount { get; }

    public ZoningCategoryHistogram(IReadOnlyDictionary<string, int> counts)
    {
        Counts = new ReadOnlyDictionary<string, int>(counts
            .Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value,
                StringComparer.OrdinalIgnoreCase));
        SampleCount = Counts.Values.Sum();
    }
}

public sealed record ZoningOrdinalValue(double Value, string TransformVersion, int KnownSampleCount);

public sealed class ZoningOrdinalTransform
{
    public const bool DefaultEnabled = false;
    public static ZoningOrdinalTransform V1 { get; } = new("zoning-ordinal-v1",
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["commercial"] = 5,
            ["residential"] = 3,
            ["industrial"] = 2,
            ["green"] = 1,
        });

    private readonly IReadOnlyDictionary<string, double> _scores;
    public string Version { get; }

    public ZoningOrdinalTransform(string version, IReadOnlyDictionary<string, double> scores)
    {
        Version = version;
        _scores = new Dictionary<string, double>(scores, StringComparer.OrdinalIgnoreCase);
    }

    public ZoningOrdinalValue? Transform(ZoningCategoryHistogram histogram)
    {
        double sum = 0;
        int count = 0;
        foreach (var pair in histogram.Counts)
        {
            if (!_scores.TryGetValue(pair.Key, out double score) &&
                !TryResolveKoreanCategory(pair.Key, out score)) continue;
            sum += score * pair.Value;
            count += pair.Value;
        }
        return count == 0 ? null : new ZoningOrdinalValue(sum / count, Version, count);
    }

    private static bool TryResolveKoreanCategory(string category, out double score)
    {
        score = 0;
        if (category.Contains("중심상업") || category.Contains("일반상업")) score = 5;
        else if (category.Contains("근린상업") || category.Contains("유통상업")) score = 4;
        else if (category.Contains("준주거")) score = 3.5;
        else if (category.Contains("제2종") || category.Contains("제3종") || category.Contains("일반주거")) score = 3;
        else if (category.Contains("제1종") || category.Contains("준공업")) score = 2.5;
        else if (category.Contains("전용주거") || category.Contains("일반공업") || category.Contains("계획관리")) score = 2;
        else if (category.Contains("전용공업") || category.Contains("생산관리")) score = 1.5;
        else if (category.Contains("녹지") || category.Contains("보전") ||
                 category.Contains("농림") || category.Contains("자연환경")) score = 1;
        return score > 0;
    }
}
