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
     double centralMass,
     double3 centerPos,
     double3 pos,
     double3 vel)
    {
        var result = new OrbitalParameters(false);

        double mu = PhysicsConstants.G * centralMass;
        if (!(mu > 0.0))
            return result;

        double3 r = pos - centerPos;
        double3 v = vel;

        double r2 = math.lengthsq(r);
        double v2 = math.lengthsq(v);

        if (r2 < R_MIN * R_MIN || v2 < V_MIN * V_MIN)
        {
            Debug.LogWarning("OrbitalCalculations: Position or velocity magnitude too small.");
            return result;
        }

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

        // Kept for reference/debug if needed.
        double energy = 0.5 * v2 - mu * invR;

        double3 cxh = new double3(
            v.y * h.z - v.z * h.y,
            v.z * h.x - v.x * h.z,
            v.x * h.y - v.y * h.x
        );
        double invMu = 1.0 / mu;
        double3 eVec = cxh * invMu - r * invR;
        double e2_vec = math.lengthsq(eVec);

        double ecc = Math.Sqrt(Math.Max(0.0, e2_vec));
        bool isOpen = ecc >= 1.0;

        double a = 0.0;
        if (!isOpen)
        {
            double p = h2 / mu;
            double denom = Math.Max(1e-12, 1.0 - ecc * ecc);
            a = p / denom;
        }

        result.eccentricity = (float)ecc;

        double3 n = new double3(h.z, 0.0, -h.x);
        double n2 = math.lengthsq(n);

        double invH = 1.0 / Math.Sqrt(h2);
        double cosInc = Clamp((-h.y) * invH, -1.0, 1.0);
        double incDeg = Math.Acos(cosInc) * (180.0 / Math.PI);
        result.inclination = (float)incDeg;

        result.RAAN = (float)ComputeRaanDegrees(n, n2);

        if (isOpen)
        {
            PopulateOpenOrbit(
                ref result,
                mu,
                Vector3.zero,
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
                Vector3.zero,
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

        double eSafe = Math.Max(ecc, 1.0);
        double rp = h2 / (mu * (1.0 + eSafe));

        result.perigeeRadius = (float)rp;
        result.apogeeRadius = -1f;

        double3 dir = SelectPeriapsisDirectionOpen(eVec, e2_vec, n, n2);

        Vector3 perigeeOffset = new Vector3(
            (float)(dir.x * rp),
            (float)(dir.y * rp),
            (float)(dir.z * rp)
        );

        result.perigeePosition = centralBodyPosition + perigeeOffset;
        result.apogeePosition = Vector3.zero;
        result.orbitalPeriod = 0f;
        result.meanAnomaly = 0f;
        result.trueAnomaly = 0f;
        result.timeToPerigee = 0f;
        result.timeToApogee = 0f;
    }

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

        double period = 2.0 * Math.PI * Math.Sqrt((a * a * a) / mu);
        result.orbitalPeriod = (float)period;

        double p = h2 / mu;
        double rp = p / (1.0 + ecc);
        double ra = p / Math.Max(1e-12, (1.0 - ecc));

        result.perigeeRadius = (float)rp;
        result.apogeeRadius = (float)ra;

        double3 periDir = SelectPeriapsisDirectionClosed(ecc, eVec, e2_vec, n, n2);
        result.isCircular = (ecc < ECC_CIRC);

        Vector3 center = centralBodyPosition;
        Vector3 periOff = new Vector3(
            (float)(periDir.x * rp),
            (float)(periDir.y * rp),
            (float)(periDir.z * rp)
        );
        Vector3 apoOff = new Vector3(
            (float)(periDir.x * ra),
            (float)(periDir.y * ra),
            (float)(periDir.z * ra)
        );

        result.perigeePosition = center + periOff;
        result.apogeePosition = center - apoOff;

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
            double r_mag = Math.Sqrt(r2);
            double dotEr = eVecDotR(periDir, ecc, r, r_mag);

            double cosNu = dotEr / (ecc * r_mag);
            cosNu = Clamp(cosNu, -1.0, 1.0);
            nu_now = Math.Acos(cosNu);

            double rv = r.x * v.x + r.y * v.y + r.z * v.z;
            if (rv < 0.0)
                nu_now = TWO_PI - nu_now;

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

        double n_mean = Math.Sqrt(mu / (a * a * a));

        double dM_peri = TWO_PI - M_now;
        double timeToPeri = dM_peri / n_mean;

        double dM_apo = Math.PI - M_now;
        if (dM_apo < 0.0) dM_apo += TWO_PI;
        double timeToApo = dM_apo / n_mean;

        result.timeToPerigee = (float)timeToPeri;
        result.timeToApogee = (float)timeToApo;
    }

    private static double eVecDotR(double3 periDir, double ecc, double3 r, double r_mag)
    {
        return ecc * r_mag * (
            periDir.x * (r.x / r_mag) +
            periDir.y * (r.y / r_mag) +
            periDir.z * (r.z / r_mag)
        );
    }

    private static double3 ToD3(Vector3 v) => new double3(v.x, v.y, v.z);

    public static OrbitalParameters TryParams(NBody body, BodyService svc)
    {
        if (body == null || svc == null || svc.CentralBody == null)
            return default;

        var central = svc.CentralBody;

        double centralMass = central.trueMass;
        double3 centerPos = central.state.position;

        double3 pos = body.state.position;
        double3 vel = body.state.velocity;

        bool posBad = !(double.IsFinite(pos.x) && double.IsFinite(pos.y) && double.IsFinite(pos.z));
        bool velBad = !(double.IsFinite(vel.x) && double.IsFinite(vel.y) && double.IsFinite(vel.z));

        if (posBad || velBad)
            return default;

        double3 rel = pos - centerPos;
        double r2 = math.lengthsq(rel);
        double v2 = math.lengthsq(vel);

        const double R_MIN_TRY = 1.0;
        const double V_MIN_TRY = 1e-6;

        if (!(centralMass > 0.0) || r2 < R_MIN_TRY * R_MIN_TRY || v2 < V_MIN_TRY * V_MIN_TRY)
            return default;

        return CalculateOrbitalParameters(
            centralMass,
            centerPos,
            pos,
            vel
        );
    }

    public struct OrbitalInvariantDebug
    {
        public bool valid;
        public double specificEnergy;
        public double angularMomentumMag;
        public double eccentricity;
        public double semiMajorAxis;
        public double radius;
        public double speed;
        public double apogeeRadius;
        public double perigeeRadius;
    }

    public static OrbitalInvariantDebug GetInvariantDebug(NBody body, BodyService svc)
    {
        var dbg = new OrbitalInvariantDebug { valid = false };

        if (body == null || svc?.CentralBody == null)
            return dbg;

        double mu = PhysicsConstants.G * (double)svc.CentralBody.mass;
        if (!(mu > 0.0))
            return dbg;

        double3 center = new double3(
            svc.CentralBody.transform.position.x,
            svc.CentralBody.transform.position.y,
            svc.CentralBody.transform.position.z
        );

        double3 r = body.state.position - center;
        double3 v = body.state.velocity;

        double r2 = math.lengthsq(r);
        double v2 = math.lengthsq(v);

        if (!(r2 > 0.0) || !(v2 >= 0.0))
            return dbg;

        double rMag = Math.Sqrt(r2);
        double vMag = Math.Sqrt(v2);

        double3 h = new double3(
            r.y * v.z - r.z * v.y,
            r.z * v.x - r.x * v.z,
            r.x * v.y - r.y * v.x
        );
        double hMag = math.length(h);

        double invR = 1.0 / rMag;
        double energy = 0.5 * v2 - mu * invR;

        double3 cxh = new double3(
            v.y * h.z - v.z * h.y,
            v.z * h.x - v.x * h.z,
            v.x * h.y - v.y * h.x
        );
        double3 eVec = cxh * (1.0 / mu) - r * invR;
        double ecc = math.length(eVec);

        double a = double.NaN;
        double rp = double.NaN;
        double ra = double.NaN;

        if (ecc < 1.0)
        {
            double p = hMag * hMag / mu;
            double denom = Math.Max(1e-12, 1.0 - ecc * ecc);
            a = p / denom;
            rp = p / (1.0 + ecc);
            ra = p / Math.Max(1e-12, 1.0 - ecc);
        }

        dbg.valid = true;
        dbg.specificEnergy = energy;
        dbg.angularMomentumMag = hMag;
        dbg.eccentricity = ecc;
        dbg.semiMajorAxis = a;
        dbg.radius = rMag;
        dbg.speed = vMag;
        dbg.apogeeRadius = ra;
        dbg.perigeeRadius = rp;
        return dbg;
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

    public float meanAnomaly;
    public float trueAnomaly;
    public float timeToPerigee;
    public float timeToApogee;

    public float apogeeRadius;
    public float perigeeRadius;

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

        apogeeRadius = 0;
        perigeeRadius = 0;

        meanAnomaly = 0;
        trueAnomaly = 0;
        timeToPerigee = 0;
        timeToApogee = 0;
    }
}