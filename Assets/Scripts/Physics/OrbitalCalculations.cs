using UnityEngine;
using System;
using Unity.Mathematics;

/// <summary>
/// Computes orbital parameters from position/velocity relative to a central body.
/// Uses energy + angular momentum formulations for better stability near e ≈ 1,
/// and tries to fail quietly when inputs aren’t usable.
/// </summary>
public static class OrbitalCalculations
{
    private const double R_MIN = 1.0;
    private const double V_MIN = 1e-12;
    private const double H_MIN = 1e-12;
    private const double ECC_TINY = 5e-6;
    private const double ECC_CIRC = 1e-6;
    private const double TWO_PI = 2.0 * Math.PI;

    private static double Clamp(double v, double min, double max) =>
        v < min ? min : (v > max ? max : v);

    /// <summary>
    /// Calculates classical orbital parameters (supports closed and open orbits).
    /// </summary>
    public static OrbitalParameters CalculateOrbitalParameters(
        float centralBodyMass,
        Vector3 centralBodyPosition,
        double3 position_d,
        double3 velocity_d)
    {
        var result = new OrbitalParameters(false);

        double mu = PhysicsConstants.G * (double)centralBodyMass;
        if (!(mu > 0.0))
            return result;

        // Relative state (r, v)
        double3 r = position_d - new double3(centralBodyPosition.x, centralBodyPosition.y, centralBodyPosition.z);
        double3 v = velocity_d;

        // Magnitudes (squared)
        double r2 = math.lengthsq(r);
        double v2 = math.lengthsq(v);

        if (r2 < R_MIN * R_MIN || v2 < V_MIN * V_MIN)
        {
            Debug.LogWarning("OrbitalCalculations: Position or velocity magnitude too small.");
            return result;
        }

        // Angular momentum h = r × v
        double3 h = new double3(
            r.y * v.z - r.z * v.y,
            r.z * v.x - r.x * v.z,
            r.x * v.y - r.y * v.x
        );
        double h2 = math.lengthsq(h);
        if (h2 < H_MIN * H_MIN)
        {
            Debug.LogWarning("OrbitalCalculations: Angular momentum too small.");
            return result;
        }

        double invR = 1.0 / Math.Sqrt(r2);

        // Specific orbital energy: E = v²/2 - μ/|r|
        double energy = 0.5 * v2 - mu * invR;

        // Eccentricity vector direction: e = (v × h)/μ - r/|r|
        double3 cxh = new double3(
            v.y * h.z - v.z * h.y,
            v.z * h.x - v.x * h.z,
            v.x * h.y - v.y * h.x
        );
        double invMu = 1.0 / mu;
        double3 eVec = cxh * invMu - r * invR;
        double e2_vec = math.lengthsq(eVec);

        // Eccentricity magnitude + semi-major axis
        bool isOpen = energy >= 0.0;
        double ecc;
        double a = 0.0;

        if (isOpen)
        {
            double term = 1.0 + (2.0 * energy * h2) / (mu * mu);
            ecc = Math.Sqrt(Math.Max(0.0, term));
        }
        else
        {
            a = -mu / (2.0 * energy);
            double term = 1.0 - (h2 / (a * mu));
            ecc = Math.Sqrt(Math.Max(0.0, term));
        }

        result.eccentricity = (float)ecc;

        // Node vector (Y-up)
        double3 n = new double3(h.z, 0.0, -h.x);
        double n2 = math.lengthsq(n);

        // Inclination
        double invH = 1.0 / Math.Sqrt(h2);
        double cosInc = Clamp((-h.y) * invH, -1.0, 1.0);
        double incDeg = Math.Acos(cosInc) * (180.0 / Math.PI);
        result.inclination = (float)incDeg;

        // RAAN (atan2(nz, nx))
        result.RAAN = (float)ComputeRaanDegrees(n, n2);

        // Branch: open vs closed orbit
        if (isOpen)
        {
            PopulateOpenOrbit(
                ref result,
                mu,
                centralBodyPosition,
                h2,
                ecc,
                eVec,
                e2_vec,
                n,
                n2
            );
        }
        else
        {
            PopulateClosedOrbit(
                ref result,
                mu,
                centralBodyPosition,
                h2,
                ecc,
                eVec,
                e2_vec,
                n,
                n2,
                r,
                v,
                r2,
                a
            );
        }

        result.isValid = true;
        return result;
    }

    /// <summary>
    /// Computes RAAN in degrees from the node vector (Y-up world).
    /// </summary>
    private static double ComputeRaanDegrees(double3 n, double n2)
    {
        if (n2 <= H_MIN * H_MIN)
            return 0.0;

        double invN = 1.0 / Math.Sqrt(n2);
        double nx_n = n.x * invN;
        double nz_n = n.z * invN;
        double raan = Math.Atan2(nz_n, nx_n) * (180.0 / Math.PI);
        if (raan < 0.0) raan += 360.0;

        return raan;
    }

    /// <summary>
    /// Fills parameters for open (hyperbolic/parabolic) cases.
    /// </summary>
    private static void PopulateOpenOrbit(
        ref OrbitalParameters result,
        double mu,
        Vector3 centralBodyPosition,
        double h2,
        double ecc,
        double3 eVec,
        double e2_vec,
        double3 n,
        double n2)
    {
        result.semiMajorAxis = 0f;
        result.isCircular = false;

        // Periapsis distance: rp = h² / (μ (1+e))
        double eSafe = Math.Max(ecc, 1.0);
        double rp = h2 / (mu * (1.0 + eSafe));

        // Periapsis direction: use ê if available, else node, else +X
        double3 dir = SelectPeriapsisDirectionOpen(eVec, e2_vec, n, n2);

        Vector3 perigeeOffset = new Vector3(
            (float)(dir.x * rp),
            (float)(dir.y * rp),
            (float)(dir.z * rp)
        );

        result.perigeePosition = centralBodyPosition + perigeeOffset;
        result.apogeePosition = Vector3.zero; // not defined for open orbits
        result.orbitalPeriod = 0f;
        result.meanAnomaly = 0f;
        result.trueAnomaly = 0f;
        result.timeToPerigee = 0f;
        result.timeToApogee = 0f;
    }

    /// <summary>
    /// Fills parameters for closed (elliptical) orbits.
    /// </summary>
    private static void PopulateClosedOrbit(
        ref OrbitalParameters result,
        double mu,
        Vector3 centralBodyPosition,
        double h2,
        double ecc,
        double3 eVec,
        double e2_vec,
        double3 n,
        double n2,
        double3 r,
        double3 v,
        double r2,
        double a)
    {
        result.semiMajorAxis = (float)a;

        // Period: T = 2π √(a³ / μ)
        double period = 2.0 * Math.PI * Math.Sqrt((a * a * a) / mu);
        result.orbitalPeriod = (float)period;

        // Radii from h²/μ forms
        double rp = h2 / (mu * (1.0 + ecc));
        double ra = h2 / (mu * Math.Max(1e-15, (1.0 - ecc)));

        // Periapsis direction
        double3 periDir = SelectPeriapsisDirectionClosed(ecc, eVec, e2_vec, n, n2);
        result.isCircular = (ecc < ECC_CIRC);

        Vector3 center = centralBodyPosition;
        Vector3 periOff = new Vector3((float)(periDir.x * rp), (float)(periDir.y * rp), (float)(periDir.z * rp));
        Vector3 apoOff = new Vector3((float)(periDir.x * ra), (float)(periDir.y * ra), (float)(periDir.z * ra));

        result.perigeePosition = center + periOff;
        result.apogeePosition = center - apoOff;

        // Anomalies and times
        ComputeAnomaliesAndTimes(
            ref result,
            mu,
            a,
            ecc,
            periDir,
            r,
            v,
            r2
        );
    }

    /// <summary>
    /// Picks a periapsis direction for open orbits.
    /// </summary>
    private static double3 SelectPeriapsisDirectionOpen(double3 eVec, double e2_vec, double3 n, double n2)
    {
        if (e2_vec > 1e-18)
        {
            double invE = 1.0 / Math.Sqrt(e2_vec);
            return eVec * invE;
        }

        if (n2 > H_MIN * H_MIN)
        {
            double invN = 1.0 / Math.Sqrt(n2);
            return n * invN;
        }

        return new double3(1.0, 0.0, 0.0);
    }

    /// <summary>
    /// Picks a periapsis direction for closed orbits.
    /// </summary>
    private static double3 SelectPeriapsisDirectionClosed(
        double ecc,
        double3 eVec,
        double e2_vec,
        double3 n,
        double n2)
    {
        if (ecc > ECC_TINY && e2_vec > 0.0)
        {
            double invE = 1.0 / Math.Sqrt(e2_vec);
            return eVec * invE;
        }

        if (n2 > H_MIN * H_MIN)
        {
            double invN = 1.0 / Math.Sqrt(n2);
            return n * invN;
        }

        return new double3(1.0, 0.0, 0.0);
    }

    /// <summary>
    /// Computes mean/true anomaly and time to next perigee/apogee for closed orbits.
    /// </summary>
    private static void ComputeAnomaliesAndTimes(
        ref OrbitalParameters result,
        double mu,
        double a,
        double ecc,
        double3 periDir,
        double3 r,
        double3 v,
        double r2)
    {
        double M_now;
        double nu_now;

        if (ecc < ECC_TINY)
        {
            // Approximate ν ≈ E ≈ M for circular orbits.
            double r_mag = Math.Sqrt(r2);
            M_now = 0.0;
            nu_now = 0.0;

            if (r_mag > 0.0)
            {
                double cosNu = (r.x * periDir.x + r.y * periDir.y + r.z * periDir.z) / r_mag;
                cosNu = Clamp(cosNu, -1.0, 1.0);
                nu_now = Math.Acos(cosNu);

                double rv = r.x * v.x + r.y * v.y + r.z * v.z;
                if (rv < 0.0)
                    nu_now = TWO_PI - nu_now;

                M_now = nu_now;
            }
        }
        else
        {
            // True anomaly from eVec and r
            double r_mag = Math.Sqrt(r2);
            double dotEr = eVecDotR(periDir, ecc, r, r_mag); // helper below

            double cosNu = dotEr / (ecc * r_mag);
            cosNu = Clamp(cosNu, -1.0, 1.0);
            nu_now = Math.Acos(cosNu);

            double rv = r.x * v.x + r.y * v.y + r.z * v.z;
            if (rv < 0.0)
                nu_now = TWO_PI - nu_now;

            // Eccentric anomaly
            double cosNuVal = Math.Cos(nu_now);
            double sinNuVal = Math.Sin(nu_now);

            double cosE = (ecc + cosNuVal) / (1.0 + ecc * cosNuVal);
            double sinE = Math.Sqrt(1.0 - ecc * ecc) * sinNuVal / (1.0 + ecc * cosNuVal);

            double E = Math.Atan2(sinE, cosE);
            if (E < 0.0) E += TWO_PI;

            M_now = E - ecc * Math.Sin(E);
            M_now %= TWO_PI;
            if (M_now < 0.0) M_now += TWO_PI;
        }

        result.trueAnomaly = (float)nu_now;
        result.meanAnomaly = (float)M_now;

        // Mean motion + times
        double n_mean = Math.Sqrt(mu / (a * a * a)); // rad/s

        double dM_peri = TWO_PI - M_now;
        double timeToPeri = dM_peri / n_mean;

        double dM_apo = Math.PI - M_now;
        if (dM_apo < 0.0) dM_apo += TWO_PI;
        double timeToApo = dM_apo / n_mean;

        result.timeToPerigee = (float)timeToPeri;
        result.timeToApogee = (float)timeToApo;
    }

    /// <summary>
    /// Helper for dot(e, r) in the anomaly calculation, keeping it explicit.
    /// </summary>
    private static double eVecDotR(double3 periDir, double ecc, double3 r, double r_mag)
    {
        // ê is along periDir when ecc is nonzero, scale back by ecc * |r|.
        // For stability just compute dot(e, r) directly from direction.
        return ecc * r_mag * (periDir.x * (r.x / r_mag) + periDir.y * (r.y / r_mag) + periDir.z * (r.z / r_mag));
    }

    private static double3 ToD3(Vector3 v) => new double3(v.x, v.y, v.z);

    /// <summary>
    /// Safer wrapper around CalculateOrbitalParameters: validates state/central mass
    /// and falls back to Transform/velocity when the double state isn’t usable.
    /// </summary>
    public static OrbitalParameters TryParams(NBody body, BodyService svc)
    {
        if (body == null) return default;

        Vector3 centerPos = svc?.CentralBody?.transform?.position ?? Vector3.zero;

        float centralMass = body.state.centralBodyMass > 0f
            ? body.state.centralBodyMass
            : (svc?.CentralBody != null ? (float)svc.CentralBody.mass : 0f);

        double3 pos = body.state.position;
        double3 vel = body.state.velocity;

        bool posBad = !(double.IsFinite(pos.x) && double.IsFinite(pos.y) && double.IsFinite(pos.z));
        bool velBad = !(double.IsFinite(vel.x) && double.IsFinite(vel.y) && double.IsFinite(vel.z));

        if (posBad) pos = ToD3(body.transform.position);
        if (velBad) vel = ToD3(body.velocity);

        double3 rel = pos - ToD3(centerPos);
        double r2 = math.lengthsq(rel);
        double v2 = math.lengthsq(vel);

        const double R_MIN_TRY = 1.0;
        const double V_MIN_TRY = 1e-6;

        if (!(centralMass > 0f) || r2 < R_MIN_TRY * R_MIN_TRY || v2 < V_MIN_TRY * V_MIN_TRY)
            return default; // isValid == false

        return CalculateOrbitalParameters(centralMass, centerPos, pos, vel);
    }
}

public struct OrbitalParameters
{
    public float semiMajorAxis;
    public float eccentricity;
    public float orbitalPeriod;
    public Vector3 apogeePosition;
    public Vector3 perigeePosition;
    public float inclination;
    public float RAAN;
    public bool isCircular;
    public bool isValid;

    public float meanAnomaly;   // radians
    public float trueAnomaly;   // radians
    public float timeToPerigee; // seconds
    public float timeToApogee;  // seconds

    public OrbitalParameters(bool valid)
    {
        semiMajorAxis = 0;
        eccentricity = 0;
        orbitalPeriod = 0;
        apogeePosition = Vector3.zero;
        perigeePosition = Vector3.zero;
        inclination = 0;
        RAAN = 0;
        isCircular = false;
        isValid = valid;

        meanAnomaly = 0;
        trueAnomaly = 0;
        timeToPerigee = 0;
        timeToApogee = 0;
    }
}
