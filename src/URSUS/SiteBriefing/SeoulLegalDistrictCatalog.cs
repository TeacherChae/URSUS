using URSUS.DataSources;
using URSUS.Parsers;

namespace URSUS.SiteBriefing;

public static class SeoulLegalDistrictCatalog
{
    public const string SourceVersion = "embedded-adstrd-legald-v1";

    private static readonly Lazy<IReadOnlyList<string>> LazyIds = new(() =>
        Array.AsReadOnly(MappingLoader.Load().Values
            .SelectMany(ids => ids)
            .Select(DistrictCode.CanonicalizeLegal)
            .Where(id => id.Length == DistrictCode.CanonicalLegalLength &&
                id.StartsWith("11", StringComparison.Ordinal) &&
                !id.EndsWith("000", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray()));

    public static IReadOnlyList<string> Ids => LazyIds.Value;

    public static IReadOnlyList<string> ForSigungu(string sigunguCode)
    {
        if (sigunguCode.Length != 5 || !sigunguCode.All(char.IsDigit))
            throw new ArgumentException("시군구 코드는 5자리 숫자여야 합니다.", nameof(sigunguCode));
        string[] ids = Ids.Where(id => id.StartsWith(sigunguCode, StringComparison.Ordinal)).ToArray();
        if (ids.Length == 0)
            throw new ArgumentException("지원되는 서울 시군구 법정동이 없습니다.", nameof(sigunguCode));
        return Array.AsReadOnly(ids);
    }

    public static ReferenceCohort CreateCohort(
        string targetLegalDistrictCode,
        string sigunguName)
    {
        string target = DistrictCode.CanonicalizeLegal(targetLegalDistrictCode);
        if (target.Length == 0 || !target.StartsWith("11", StringComparison.Ordinal))
            throw new ArgumentException("서울 canonical 법정동 코드가 필요합니다.",
                nameof(targetLegalDistrictCode));
        string sigunguCode = target[..5];
        ReferenceCohort cohort = CreateCohortForSigungu(sigunguCode, sigunguName);
        IReadOnlyList<string> ids = cohort.DistrictIds;
        if (!ids.Contains(target, StringComparer.Ordinal))
            throw new ArgumentException("대상 법정동이 versioned catalog에 없습니다.",
                nameof(targetLegalDistrictCode));
        return cohort;
    }

    public static ReferenceCohort CreateCohortForSigungu(
        string sigunguCode,
        string sigunguName)
        => new(sigunguCode, sigunguName, SourceVersion, ForSigungu(sigunguCode));
}
