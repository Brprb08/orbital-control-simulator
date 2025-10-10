using System;
using UnityEngine;

/// <summary>
/// Kepler/ECI utilities: transforms classical orbital elements into ECI position/velocity.
/// Uses the standard Q = R3(Ω) * R1(i) * R3(ω) rotation sequence from perifocal to ECI.
/// </summary>
public static class KeplerUtils
{
    private struct M33
    {
        public double m00, m01, m02, m10, m11, m12, m20, m21, m22;

        public static M33 operator *(M33 A, M33 B)
        {
            M33 C;
            C.m00 = A.m00 * B.m00 + A.m01 * B.m10 + A.m02 * B.m20;
            C.m01 = A.m00 * B.m01 + A.m01 * B.m11 + A.m02 * B.m21;
            C.m02 = A.m00 * B.m02 + A.m01 * B.m12 + A.m02 * B.m22;
            C.m10 = A.m10 * B.m00 + A.m11 * B.m10 + A.m12 * B.m20;
            C.m11 = A.m10 * B.m01 + A.m11 * B.m11 + A.m12 * B.m21;
            C.m12 = A.m10 * B.m02 + A.m11 * B.m12 + A.m12 * B.m22;
            C.m20 = A.m20 * B.m00 + A.m21 * B.m10 + A.m22 * B.m20;
            C.m21 = A.m20 * B.m01 + A.m21 * B.m11 + A.m22 * B.m21;
            C.m22 = A.m20 * B.m02 + A.m21 * B.m12 + A.m22 * B.m22;
            return C;
        }
    }

    private static Vector3d Mul(M33 A, Vector3d v) =>
        new Vector3d(A.m00 * v.x + A.m01 * v.y + A.m02 * v.z,
                     A.m10 * v.x + A.m11 * v.y + A.m12 * v.z,
                     A.m20 * v.x + A.m21 * v.y + A.m22 * v.z);

    private static M33 R3(double a)
    {
        var c = Math.Cos(a); var s = Math.Sin(a);
        return new M33 { m00 = c, m01 = -s, m02 = 0, m10 = s, m11 = c, m12 = 0, m20 = 0, m21 = 0, m22 = 1 };
    }

    private static M33 R1(double a)
    {
        var c = Math.Cos(a); var s = Math.Sin(a);
        return new M33 { m00 = 1, m01 = 0, m02 = 0, m10 = 0, m11 = c, m12 = -s, m20 = 0, m21 = s, m22 = c };
    }

    private static double Deg2Rad(double d) => d * Math.PI / 180.0;
    private static double Wrap2Pi(double x) { var t = 2 * Math.PI; x %= t; if (x < 0) x += t; return x; }

    /// <summary>
    /// Computes ECI state from classical orbital elements (elliptic only).
    /// </summary>
    /// <param name="a">Semi-major axis (m).</param>
    /// <param name="e">Eccentricity (0 ≤ e &lt; 1).</param>
    /// <param name="iDeg">Inclination (deg).</param>
    /// <param name="raanDeg">Right ascension of the ascending node Ω (deg).</param>
    /// <param name="argpDeg">Argument of perigee ω (deg).</param>
    /// <param name="trueAnomDeg">True anomaly ν (deg).</param>
    /// <param name="mu">Gravitational parameter μ (m³/s²).</param>
    /// <returns>(rECI [m], vECI [m/s]).</returns>
    /// <exception ArgumentException Thrown for non-elliptic or invalid elements.</exception>
    public static (Vector3d r, Vector3d v) FromElements(
        double a, double e, double iDeg, double raanDeg, double argpDeg, double trueAnomDeg, double mu)
    {
        if (e >= 1.0) throw new ArgumentException("Only elliptical orbits supported (e < 1).");
        if (a <= 0) throw new ArgumentException("Semi-major axis must be > 0.");

        double i = Deg2Rad(iDeg);
        double Ω = Deg2Rad(raanDeg);
        double ω = Deg2Rad(argpDeg);
        double ν = Deg2Rad(trueAnomDeg);

        // Handle circular/equatorial degeneracies by absorbing angles consistently
        if (Math.Abs(e) < 1e-8) { ν = Wrap2Pi(ω + ν); ω = 0.0; }
        if (Math.Abs(Math.Sin(i)) < 1e-8) { ω = Wrap2Pi(Ω + ω); Ω = 0; }

        double p = a * (1 - e * e);
        double cν = Math.Cos(ν), sν = Math.Sin(ν);
        double rMag = p / (1 + e * cν);

        var r_pf = new Vector3d(rMag * cν, rMag * sν, 0);
        var v_pf = new Vector3d(-Math.Sqrt(mu / p) * sν, Math.Sqrt(mu / p) * (e + cν), 0);

        // Q = R3(Ω) * R1(i) * R3(ω): perifocal -> ECI
        var Q = Mul(R3(Ω), Mul(R1(i), R3(ω)));
        var rEci = Mul(Q, r_pf);
        var vEci = Mul(Q, v_pf);

        return (rEci, vEci);
    }

    private static M33 Mul(M33 A, M33 B) => A * B;
}

/// <summary>
/// Minimal double-precision 3D vector for orbital math (implicit cast to Unity's Vector3).
/// </summary>
public struct Vector3d
{
    public double x, y, z;

    public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }

    public double magnitude => Math.Sqrt(x * x + y * y + z * z);

    public static Vector3d operator *(double s, Vector3d v) => new Vector3d(s * v.x, s * v.y, s * v.z);

    public static implicit operator Vector3(Vector3d v) => new Vector3((float)v.x, (float)v.y, (float)v.z);
}