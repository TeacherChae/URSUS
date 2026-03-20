using System;

namespace URSUS.Utils
{
    /// <summary>
    /// WGS84 위도/경도 ↔ UTM 좌표 변환.
    /// gps_to_upm.py (GPStoUTM) 포팅.
    /// </summary>
    public static class GpsToUtm
    {
        // WGS84 상수
        private const double WGS84_A  = 6378137.0;
        private const double WGS84_E2 = 0.00669437999014;   // e² = (2f - f²)
        private const double WGS84_EP2 = 0.00673949674228;  // e'²= e²/(1-e²)
        private const double UTM_K0  = 0.9996;
        private const double UTM_FE  = 500000.0;
        private const double UTM_FN_S = 10000000.0;
        private const double RAD = Math.PI / 180.0;
        private const double DEG = 180.0 / Math.PI;

        /// <summary>
        /// 위도/경도 → UTM Easting/Northing (미터 단위).
        /// </summary>
        public static (double easting, double northing) LLtoUTM(double lat, double lon)
        {
            double latRad   = lat * RAD;
            double lonTemp  = (lon + 180) - (int)((lon + 180) / 360) * 360 - 180;
            double lonRad   = lonTemp * RAD;

            int zoneNumber  = (int)((lonTemp + 180) / 6) + 1;
            double lonOrigin    = (zoneNumber - 1) * 6 - 180 + 3;
            double lonOriginRad = lonOrigin * RAD;

            double N = WGS84_A / Math.Sqrt(1 - WGS84_E2 * Math.Sin(latRad) * Math.Sin(latRad));
            double T = Math.Tan(latRad) * Math.Tan(latRad);
            double C = WGS84_EP2 * Math.Cos(latRad) * Math.Cos(latRad);
            double A = Math.Cos(latRad) * (lonRad - lonOriginRad);

            double e2 = WGS84_E2;
            double e4 = e2 * e2;
            double e6 = e4 * e2;
            double M = WGS84_A * (
                (1 - e2 / 4 - 3 * e4 / 64 - 5 * e6 / 256) * latRad
                - (3 * e2 / 8 + 3 * e4 / 32 + 45 * e6 / 1024) * Math.Sin(2 * latRad)
                + (15 * e4 / 256 + 45 * e6 / 1024) * Math.Sin(4 * latRad)
                - (35 * e6 / 3072) * Math.Sin(6 * latRad));

            double easting = UTM_K0 * N * (A
                + (1 - T + C) * Math.Pow(A, 3) / 6
                + (5 - 18 * T + T * T + 72 * C - 58 * WGS84_EP2) * Math.Pow(A, 5) / 120)
                + UTM_FE;

            double northing = UTM_K0 * (M + N * Math.Tan(latRad) * (
                A * A / 2
                + (5 - T + 9 * C + 4 * C * C) * Math.Pow(A, 4) / 24
                + (61 - 58 * T + T * T + 600 * C - 330 * WGS84_EP2) * Math.Pow(A, 6) / 720));

            if (lat < 0)
                northing += UTM_FN_S;

            return (easting, northing);
        }

        /// <summary>
        /// UTM Easting/Northing → 위도/경도.
        /// </summary>
        public static (double lat, double lon) UTMtoLL(
            double northing, double easting, int zoneNumber, char zoneLetter)
        {
            double e1 = (1 - Math.Sqrt(1 - WGS84_E2)) / (1 + Math.Sqrt(1 - WGS84_E2));

            double x = easting - UTM_FE;
            double y = northing;
            if (zoneLetter < 'N')
                y -= UTM_FN_S;

            double lonOrigin = (zoneNumber - 1) * 6 - 180 + 3;

            double e2 = WGS84_E2;
            double e4 = e2 * e2;
            double e6 = e4 * e2;

            double M = y / UTM_K0;
            double mu = M / (WGS84_A * (1 - e2 / 4 - 3 * e4 / 64 - 5 * e6 / 256));

            double phi1Rad = mu
                + (3 * e1 / 2 - 27 * Math.Pow(e1, 3) / 32) * Math.Sin(2 * mu)
                + (21 * e1 * e1 / 16 - 55 * Math.Pow(e1, 4) / 32) * Math.Sin(4 * mu)
                + (151 * Math.Pow(e1, 3) / 96) * Math.Sin(6 * mu);

            double N1 = WGS84_A / Math.Sqrt(1 - e2 * Math.Sin(phi1Rad) * Math.Sin(phi1Rad));
            double T1 = Math.Tan(phi1Rad) * Math.Tan(phi1Rad);
            double C1 = WGS84_EP2 * Math.Cos(phi1Rad) * Math.Cos(phi1Rad);
            double R1 = WGS84_A * (1 - e2) / Math.Pow(1 - e2 * Math.Sin(phi1Rad) * Math.Sin(phi1Rad), 1.5);
            double D  = x / (N1 * UTM_K0);

            double lat = phi1Rad - (N1 * Math.Tan(phi1Rad) / R1) * (
                D * D / 2
                - (5 + 3 * T1 + 10 * C1 - 4 * C1 * C1 - 9 * WGS84_EP2) * Math.Pow(D, 4) / 24
                + (61 + 90 * T1 + 298 * C1 + 45 * T1 * T1 - 252 * WGS84_EP2 - 3 * C1 * C1)
                  * Math.Pow(D, 6) / 720);

            double lon = (D
                - (1 + 2 * T1 + C1) * Math.Pow(D, 3) / 6
                + (5 - 2 * C1 + 28 * T1 - 3 * C1 * C1 + 8 * WGS84_EP2 + 24 * T1 * T1)
                  * Math.Pow(D, 5) / 120) / Math.Cos(phi1Rad);

            return (lat * DEG, lonOrigin + lon * DEG);
        }

        /// <summary>
        /// 위도로부터 UTM 밴드 문자를 반환한다.
        /// </summary>
        public static char UTMLetterDesignator(double lat)
        {
            if      (lat >=  84) return 'Z';
            else if (lat >=  72) return 'X';
            else if (lat >=  64) return 'W';
            else if (lat >=  56) return 'V';
            else if (lat >=  48) return 'U';
            else if (lat >=  40) return 'T';
            else if (lat >=  32) return 'S';
            else if (lat >=  24) return 'R';
            else if (lat >=  16) return 'Q';
            else if (lat >=   8) return 'P';
            else if (lat >=   0) return 'N';
            else if (lat >=  -8) return 'M';
            else if (lat >= -16) return 'L';
            else if (lat >= -24) return 'K';
            else if (lat >= -32) return 'J';
            else if (lat >= -40) return 'H';
            else if (lat >= -48) return 'G';
            else if (lat >= -56) return 'F';
            else if (lat >= -64) return 'E';
            else if (lat >= -72) return 'D';
            else if (lat >= -80) return 'C';
            else return 'Z';
        }
    }
}
