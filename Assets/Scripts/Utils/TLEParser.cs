// using System;
// using UnityEngine;

// public static class TLEParser
// {
//     private const double mu = 398600.4418; // km^3/s^2

//     public static bool TryParseTLE(string line1, string line2, out Vector3 position, out Vector3 velocity)
//     {
//         position = Vector3.zero;
//         velocity = Vector3.zero;

//         if (string.IsNullOrWhiteSpace(line1) || string.IsNullOrWhiteSpace(line2) ||
//             line1.Length < 69 || line2.Length < 69)
//         {
//             Debug.LogError("Invalid TLE input. Each line must be at least 69 characters.");
//             return false;
//         }

//         try
//         {
//             double i = DegToRad(double.Parse(line2.Substring(8, 8)));
//             double RAAN = DegToRad(double.Parse(line2.Substring(17, 8)));
//             double e = double.Parse("0." + line2.Substring(26, 7));
//             double omega = DegToRad(double.Parse(line2.Substring(34, 8)));
//             double M = DegToRad(double.Parse(line2.Substring(43, 8)));
//             double n = double.Parse(line2.Substring(52, 11));

//             double a = Math.Pow(mu / Math.Pow(n * 2 * Math.PI / 86400, 2), 1.0 / 3.0);

//             double E = SolveKepler(M, e);

//             double nu = 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(E / 2),
//                                        Math.Sqrt(1 - e) * Math.Cos(E / 2));

//             double r = a * (1 - e * Math.Cos(E));
//             double x_orb = r * Math.Cos(nu);
//             double y_orb = r * Math.Sin(nu);
//             double z_orb = 0;

//             double h = Math.Sqrt(mu * a * (1 - e * e));
//             double vx_orb = -mu / h * Math.Sin(nu);
//             double vy_orb = mu / h * (e + Math.Cos(nu));
//             double vz_orb = 0;

//             double cosO = Math.Cos(RAAN);
//             double sinO = Math.Sin(RAAN);
//             double cosi = Math.Cos(i);
//             double sini = Math.Sin(i);
//             double cosw = Math.Cos(omega);
//             double sinw = Math.Sin(omega);

//             double[,] R = new double[3, 3]
//             {
//                 {cosO*cosw - sinO*sinw*cosi, -cosO*sinw - sinO*cosw*cosi, sinO*sini},
//                 {sinO*cosw + cosO*sinw*cosi, -sinO*sinw + cosO*cosw*cosi, -cosO*sini},
//                 {sinw*sini, cosw*sini, cosi}
//             };

//             double[] rVec = MatrixVecMul(R, new double[] { x_orb, y_orb, z_orb });
//             double[] vVec = MatrixVecMul(R, new double[] { vx_orb, vy_orb, vz_orb });

//             Swap(ref rVec[1], ref rVec[2]);
//             Swap(ref vVec[1], ref vVec[2]);

//             position = new Vector3((float)(rVec[0] / 10), (float)(rVec[1] / 10), (float)(rVec[2] / 10));
//             velocity = new Vector3((float)(vVec[0] / 10), (float)(vVec[1] / 10), (float)(vVec[2] / 10));

//             return true;
//         }
//         catch (Exception e)
//         {
//             Debug.LogError($"TLE parsing error: {e.Message}");
//             return false;
//         }
//     }

//     private static double SolveKepler(double M, double e)
//     {
//         double E = M;
//         for (int i = 0; i < 10; i++)
//         {
//             double f = E - e * Math.Sin(E) - M;
//             double fPrime = 1 - e * Math.Cos(E);
//             E -= f / fPrime;
//         }
//         return E;
//     }

//     private static double DegToRad(double degrees) => degrees * Math.PI / 180.0;

//     private static double[] MatrixVecMul(double[,] m, double[] v)
//     {
//         return new double[]
//         {
//             m[0,0]*v[0] + m[0,1]*v[1] + m[0,2]*v[2],
//             m[1,0]*v[0] + m[1,1]*v[1] + m[1,2]*v[2],
//             m[2,0]*v[0] + m[2,1]*v[1] + m[2,2]*v[2]
//         };
//     }

//     private static void Swap(ref double a, ref double b)
//     {
//         double temp = a;
//         a = b;
//         b = temp;
//     }
// }

using System;
using UnityEngine;
using System.Globalization;

/// <summary>
/// Parses Two-Line Element sets (TLEs) into position and velocity vectors.
/// </summary>
public static class TLEParser
{
    // Earth's gravitational parameter (km^3/s^2)
    private const double mu = 398600.4418;

    /// <summary>
    /// Parses a TLE and computes position and velocity vectors.
    /// Returns false if input is malformed or can't be parsed.
    /// </summary>
    /// <param name="line1">First TLE line (usually not used in orbital calc).</param>
    /// <param name="line2">Second TLE line containing orbital elements.</param>
    /// <param name="position">Resulting position vector (km, scaled).</param>
    /// <param name="velocity">Resulting velocity vector (km/s, scaled).</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryParseTLE(string line1, string line2, out Vector3 position, out Vector3 velocity)
    {
        position = Vector3.zero;
        velocity = Vector3.zero;

        if (string.IsNullOrWhiteSpace(line1) || string.IsNullOrWhiteSpace(line2) ||
            line1.Length < 69 || line2.Length < 69)
        {
            Debug.LogError("Invalid TLE input. Each line must be at least 69 characters.");
            return false;
        }

        try
        {
            // Extract raw substrings
            string iStr = line2.Substring(8, 8).Trim();
            string raanStr = line2.Substring(17, 8).Trim();
            string eStr = "0." + line2.Substring(26, 7).Trim();
            string omegaStr = line2.Substring(34, 8).Trim();
            string mStr = line2.Substring(43, 8).Trim();
            string nStr = line2.Substring(52, 11).Trim();

            // Pre-validate all the fields
            if (!IsParsable(iStr) || !IsParsable(raanStr) || !IsParsable(eStr) ||
                !IsParsable(omegaStr) || !IsParsable(mStr) || !IsParsable(nStr))
            {
                Debug.LogError("Invalid TLE input. One or more fields are non-numeric or malformed.");
                return false;
            }

            // Now safe to parse
            double i = DegToRad(double.Parse(iStr));
            double RAAN = DegToRad(double.Parse(raanStr));
            double e = double.Parse(eStr);
            double omega = DegToRad(double.Parse(omegaStr));
            double M = DegToRad(double.Parse(mStr));
            double n = double.Parse(nStr);

            double a = Math.Pow(mu / Math.Pow(n * 2 * Math.PI / 86400, 2), 1.0 / 3.0);
            double E = SolveKepler(M, e);

            double nu = 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(E / 2),
                                       Math.Sqrt(1 - e) * Math.Cos(E / 2));

            double r = a * (1 - e * Math.Cos(E));
            double x_orb = r * Math.Cos(nu);
            double y_orb = r * Math.Sin(nu);
            double z_orb = 0;

            double h = Math.Sqrt(mu * a * (1 - e * e));
            double vx_orb = -mu / h * Math.Sin(nu);
            double vy_orb = mu / h * (e + Math.Cos(nu));
            double vz_orb = 0;

            double cosO = Math.Cos(RAAN);
            double sinO = Math.Sin(RAAN);
            double cosi = Math.Cos(i);
            double sini = Math.Sin(i);
            double cosw = Math.Cos(omega);
            double sinw = Math.Sin(omega);

            double[,] R = new double[3, 3]
            {
            {cosO*cosw - sinO*sinw*cosi, -cosO*sinw - sinO*cosw*cosi, sinO*sini},
            {sinO*cosw + cosO*sinw*cosi, -sinO*sinw + cosO*cosw*cosi, -cosO*sini},
            {sinw*sini, cosw*sini, cosi}
            };

            double[] rVec = MatrixVecMul(R, new double[] { x_orb, y_orb, z_orb });
            double[] vVec = MatrixVecMul(R, new double[] { vx_orb, vy_orb, vz_orb });

            Swap(ref rVec[1], ref rVec[2]);
            Swap(ref vVec[1], ref vVec[2]);

            position = new Vector3((float)(rVec[0] / 10), (float)(rVec[1] / 10), (float)(rVec[2] / 10));
            velocity = new Vector3((float)(vVec[0] / 10), (float)(vVec[1] / 10), (float)(vVec[2] / 10));

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"TLE parsing error: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Solves Kepler's Equation using Newton-Raphson.
    /// </summary>
    /// <param name="M">Mean anomaly (radians).</param>
    /// <param name="e">Eccentricity.</param>
    /// <returns>Eccentric anomaly (radians).</returns>
    private static double SolveKepler(double M, double e)
    {
        double E = M;
        for (int i = 0; i < 10; i++)
        {
            double f = E - e * Math.Sin(E) - M;
            double fPrime = 1 - e * Math.Cos(E);
            E -= f / fPrime;
        }
        return E;
    }

    /// <summary>
    /// Converts degrees to radians.
    /// </summary>
    /// <param name="degrees">Angle in degrees.</param>
    /// <returns>Angle in radians.</returns>
    private static double DegToRad(double degrees) => degrees * Math.PI / 180.0;

    /// <summary>
    /// Returns true if the string can be parsed as a double (invariant culture).
    /// </summary>
    /// <param name="s">Input string.</param>
    /// <returns>True if it's numeric.</returns>
    private static bool IsParsable(string s)
    {
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>
    /// Multiplies a 3x3 matrix by a 3D vector.
    /// </summary>
    /// <param name="m">3x3 matrix.</param>
    /// <param name="v">3-element vector.</param>
    /// <returns>Resulting vector.</returns>
    private static double[] MatrixVecMul(double[,] m, double[] v)
    {
        return new double[]
        {
        m[0,0]*v[0] + m[0,1]*v[1] + m[0,2]*v[2],
        m[1,0]*v[0] + m[1,1]*v[1] + m[1,2]*v[2],
        m[2,0]*v[0] + m[2,1]*v[1] + m[2,2]*v[2]
        };
    }

    /// <summary>
    /// Swaps two doubles by reference.
    /// </summary>
    /// <param name="a">First value (ref).</param>
    /// <param name="b">Second value (ref).</param>
    private static void Swap(ref double a, ref double b)
    {
        double temp = a;
        a = b;
        b = temp;
    }
}