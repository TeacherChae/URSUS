namespace URSUS.Utils;

/// <summary>Korea 2000 / Unified CS (EPSG:5179), GRS80 Transverse Mercator.</summary>
public static class Epsg5179
{
    private const double A = 6378137.0;
    private const double InvF = 298.257222101;
    private const double Lat0 = 38.0 * Math.PI / 180.0;
    private const double Lon0 = 127.5 * Math.PI / 180.0;
    private const double K0 = 0.9996;
    private const double FalseEasting = 1_000_000.0;
    private const double FalseNorthing = 2_000_000.0;
    private static readonly double F = 1.0 / InvF;
    private static readonly double E2 = 2 * F - F * F;
    private static readonly double Ep2 = E2 / (1 - E2);
    private static readonly double M0 = MeridianArc(Lat0);

    public static (double X, double Y) FromWgs84(double longitude, double latitude)
    {
        if (!double.IsFinite(longitude) || !double.IsFinite(latitude) || latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude));
        double phi = latitude * Math.PI / 180.0;
        double lambda = longitude * Math.PI / 180.0;
        double sin = Math.Sin(phi);
        double cos = Math.Cos(phi);
        double tan = Math.Tan(phi);
        double n = A / Math.Sqrt(1 - E2 * sin * sin);
        double t = tan * tan;
        double c = Ep2 * cos * cos;
        double aa = (lambda - Lon0) * cos;
        double m = MeridianArc(phi);
        double x = FalseEasting + K0 * n * (aa + (1 - t + c) * Math.Pow(aa, 3) / 6 +
            (5 - 18 * t + t * t + 72 * c - 58 * Ep2) * Math.Pow(aa, 5) / 120);
        double y = FalseNorthing + K0 * ((m - M0) + n * tan * (aa * aa / 2 +
            (5 - t + 9 * c + 4 * c * c) * Math.Pow(aa, 4) / 24 +
            (61 - 58 * t + t * t + 600 * c - 330 * Ep2) * Math.Pow(aa, 6) / 720));
        return (x, y);
    }

    public static (double Longitude, double Latitude) ToWgs84(double x, double y)
    {
        double m = M0 + (y - FalseNorthing) / K0;
        double mu = m / (A * (1 - E2 / 4 - 3 * E2 * E2 / 64 - 5 * E2 * E2 * E2 / 256));
        double e1 = (1 - Math.Sqrt(1 - E2)) / (1 + Math.Sqrt(1 - E2));
        double phi1 = mu + (3 * e1 / 2 - 27 * Math.Pow(e1, 3) / 32) * Math.Sin(2 * mu)
            + (21 * e1 * e1 / 16 - 55 * Math.Pow(e1, 4) / 32) * Math.Sin(4 * mu)
            + 151 * Math.Pow(e1, 3) / 96 * Math.Sin(6 * mu)
            + 1097 * Math.Pow(e1, 4) / 512 * Math.Sin(8 * mu);
        double sin = Math.Sin(phi1);
        double cos = Math.Cos(phi1);
        double tan = Math.Tan(phi1);
        double n1 = A / Math.Sqrt(1 - E2 * sin * sin);
        double r1 = A * (1 - E2) / Math.Pow(1 - E2 * sin * sin, 1.5);
        double t1 = tan * tan;
        double c1 = Ep2 * cos * cos;
        double d = (x - FalseEasting) / (n1 * K0);
        double phi = phi1 - (n1 * tan / r1) * (d * d / 2 -
            (5 + 3 * t1 + 10 * c1 - 4 * c1 * c1 - 9 * Ep2) * Math.Pow(d, 4) / 24 +
            (61 + 90 * t1 + 298 * c1 + 45 * t1 * t1 - 252 * Ep2 - 3 * c1 * c1) * Math.Pow(d, 6) / 720);
        double lambda = Lon0 + (d - (1 + 2 * t1 + c1) * Math.Pow(d, 3) / 6 +
            (5 - 2 * c1 + 28 * t1 - 3 * c1 * c1 + 8 * Ep2 + 24 * t1 * t1) * Math.Pow(d, 5) / 120) / cos;
        return (lambda * 180.0 / Math.PI, phi * 180.0 / Math.PI);
    }

    private static double MeridianArc(double phi)
    {
        double e4 = E2 * E2;
        double e6 = e4 * E2;
        return A * ((1 - E2 / 4 - 3 * e4 / 64 - 5 * e6 / 256) * phi
            - (3 * E2 / 8 + 3 * e4 / 32 + 45 * e6 / 1024) * Math.Sin(2 * phi)
            + (15 * e4 / 256 + 45 * e6 / 1024) * Math.Sin(4 * phi)
            - 35 * e6 / 3072 * Math.Sin(6 * phi));
    }
}
