using System.Collections.Generic;
using System.Linq;

namespace URSUS.DataSources
{
    public static class SeoulCoveragePolicy
    {
        public static bool Supports(IEnumerable<string> legalDistrictCodes)
        {
            if (legalDistrictCodes == null)
                return false;

            return legalDistrictCodes
                .Select(DistrictCode.CanonicalizeLegal)
                .Any(code => code.StartsWith("11", System.StringComparison.Ordinal));
        }
    }
}
