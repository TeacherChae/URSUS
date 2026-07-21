using System;
using System.Linq;

namespace URSUS.DataSources
{
    /// <summary>
    /// 법정동 코드 표현을 VWorld의 8자리 식별자로 정규화한다.
    /// 행정동 코드는 이 메서드로 법정동으로 추정하지 않고 MappingLoader를 통해 변환해야 한다.
    /// </summary>
    public static class DistrictCode
    {
        public const int CanonicalLegalLength = 8;

        public static string CanonicalizeLegal(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string digits = value.Trim();
            if (!digits.All(char.IsDigit))
                return string.Empty;

            if (digits.Length == CanonicalLegalLength)
                return digits;

            // 공공 API의 10자리 법정동 코드와 19자리 PNU는 앞 8자리가
            // VWorld emd_cd와 동일한 법정동 식별자다.
            if (digits.Length == 10 || digits.Length == 19)
                return digits.Substring(0, CanonicalLegalLength);

            return string.Empty;
        }
    }
}
