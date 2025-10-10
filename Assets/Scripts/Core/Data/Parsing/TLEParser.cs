using System;
using System.Globalization;

/// <summary>
/// Provides basic TLE parsing and two-body propagation utilities.
/// Converts TLE lines into approximate ECI position and velocity vectors in meters.
/// This is a simplified model (not SGP4) intended for visualization and prototyping.
/// </summary>
public static class TLEParser
{
    // Gravitational parameter μ for Earth (m^3/s^2). Match your scene value to avoid drift.
    private const double MU = 3.986004418e14;

    /// <summary>
    /// Parses a TLE (line 1 and line 2), propagates to whenUtc using a two-body model,
    /// and returns ECI-like position and velocity in meters and meters/second.
    /// This is an approximation (not SGP4).
    /// </summary>
    /// <param name="line1">TLE line 1.</param>
    /// <param name="line2">TLE line 2.</param>
    /// <param name="whenUtc">UTC time to which to propagate.</param>
    /// <param name="rEci_m">Output: ECI position (meters).</param>
    /// <param name="vEci_mps">Output: ECI velocity (meters/second).</param>
    /// <param name="tleEpochUtc">Output: TLE epoch in UTC.</param>
    /// <returns>True if parsing/propagation succeeded; otherwise false.</returns>
    public static bool TryPropagate(
        string line1, string line2, DateTime whenUtc,
        out Vector3d rEci_m, out Vector3d vEci_mps, out DateTime tleEpochUtc)
    {
        rEci_m = default;
        vEci_mps = default;
        tleEpochUtc = default;

        try
        {
            // Line 1 fields
            // Col 19-20: epoch year (YY)
            // Col 21-32: epoch day of year (with fraction)
            string l1 = line1;
            string l2 = line2;

            int epochYY = int.Parse(l1.Substring(18, 2), CultureInfo.InvariantCulture);
            double epochDay = double.Parse(l1.Substring(20, 12), CultureInfo.InvariantCulture);
            tleEpochUtc = TleEpochToUtc(epochYY, epochDay);

            // Line 2 fields
            // Col  9-16: inclination (deg)
            // Col 18-25: RAAN (deg)
            // Col 27-33: eccentricity (assumed decimal point)
            // Col 35-42: argument of perigee (deg)
            // Col 44-51: mean anomaly (deg)
            // Col 53-63: mean motion (rev/day)
            double incDeg = double.Parse(l2.Substring(8, 8), CultureInfo.InvariantCulture);
            double raanDeg = double.Parse(l2.Substring(17, 8), CultureInfo.InvariantCulture);
            double ecc = ParseEcc(l2.Substring(26, 7));
            double argpDeg = double.Parse(l2.Substring(34, 8), CultureInfo.InvariantCulture);
            double mDeg = double.Parse(l2.Substring(43, 8), CultureInfo.InvariantCulture);
            double nRevPerDay = double.Parse(l2.Substring(52, 11), CultureInfo.InvariantCulture);

            // Mean motion (rad/s) and semi-major axis (m)
            double n_rad_s = nRevPerDay * 2.0 * Math.PI / 86400.0;
            double a_m = Math.Pow(MU / (n_rad_s * n_rad_s), 1.0 / 3.0);

            // Propagate mean anomaly from epoch to target time (two-body)
            double dt_s = (whenUtc - tleEpochUtc).TotalSeconds;
            double M0 = Deg2Rad(mDeg);
            double M = Wrap2Pi(M0 + n_rad_s * dt_s);

            // Solve Kepler's equation for E, then convert to true anomaly ν
            double E = SolveKeplerE(M, ecc);
            double nuR = TrueAnomalyFromE(E, ecc);          // radians
            double nuD = nuR * 180.0 / Math.PI;             // degrees (for FromElements)

            // Build r, v (meters, m/s) using your existing utility
            (rEci_m, vEci_mps) = KeplerUtils.FromElements(
                a_m, ecc, incDeg, raanDeg, argpDeg, nuD, MU);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Converts TLE epoch year/day-of-year to UTC. Uses 57/2000 split (YY &gt;= 57 → 19YY, else 20YY).
    /// </summary>
    private static DateTime TleEpochToUtc(int yy, double dayOfYear)
    {
        int year = (yy >= 57) ? (1900 + yy) : (2000 + yy);
        DateTime jan1 = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        int whole = (int)Math.Floor(dayOfYear) - 1; // day 1 → Jan 1
        double frac = dayOfYear - Math.Floor(dayOfYear);
        return jan1.AddDays(whole).AddSeconds(frac * 86400.0);
    }

    /// <summary>
    /// Parses a TLE eccentricity field like "0001234" into 0.0001234.
    /// </summary>
    private static double ParseEcc(string s)
    {
        s = s.Trim();
        if (string.IsNullOrEmpty(s)) return 0.0;
        return long.TryParse(s, out long v) ? v * 1e-7 : 0.0;
    }

    private static double Deg2Rad(double d) => d * Math.PI / 180.0;

    private static double Wrap2Pi(double x)
    {
        double t = 2.0 * Math.PI;
        x %= t;
        if (x < 0) x += t;
        return x;
    }

    /// <summary>
    /// Solves Kepler's equation (elliptic) for eccentric anomaly E using Newton's method.
    /// </summary>
    private static double SolveKeplerE(double M, double e)
    {
        double E = (e < 0.8) ? M : Math.PI; // starter
        for (int k = 0; k < 20; k++)
        {
            double f = E - e * Math.Sin(E) - M;
            double fp = 1.0 - e * Math.Cos(E);
            double dE = -f / fp;
            E += dE;
            if (Math.Abs(dE) < 1e-12) break;
        }
        return E;
    }

    /// <summary>
    /// Converts eccentric anomaly E to true anomaly ν (radians).
    /// </summary>
    private static double TrueAnomalyFromE(double E, double e)
    {
        double c = Math.Cos(E);
        double s = Math.Sin(E);
        double beta = Math.Sqrt(1 - e * e);
        double cosNu = (c - e) / (1 - e * c);
        double sinNu = (beta * s) / (1 - e * c);
        return Math.Atan2(sinNu, cosNu);
    }
}