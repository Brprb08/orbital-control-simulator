using UnityEngine;
using System;
using Unity.Mathematics;

/// <summary>
/// Computes stable orbital parameters from position/velocity relative to a central body.
/// Prioritizes numerically robust formulas and quiet early-outs when inputs aren’t usable.
/// </summary>
public static class OrbitalCalculations
{
    private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

    /// <summary>
    /// Calculates classical orbital parameters (supports closed and open orbits).
    /// Uses energy/ang. momentum forms to improve stability near e≈1.
    /// </summary>
    public static OrbitalParameters CalculateOrbitalParameters(
        float centralBodyMass,
        Vector3 centralBodyPosition,
        double3 position_d,
        double3 velocity_d)
    {
        OrbitalParameters result = new OrbitalParameters(false);

        // --- Parameters & state in double ---
        double mu = (double)PhysicsConstants.G * (double)centralBodyMass;

        // Relative state vectors (r, v)
        double3 r = position_d - new double3(centralBodyPosition.x, centralBodyPosition.y, centralBodyPosition.z);
        double3 v = velocity_d;

        // Squared magnitudes (avoid sqrt where possible)
        double r2 = r.x * r.x + r.y * r.y + r.z * r.z;
        double v2 = v.x * v.x + v.y * v.y + v.z * v.z;

        // Guards: compare squared values with squared thresholds
        const double R_MIN = 1.0;
        const double V_MIN = 1e-12;
        const double H_MIN = 1e-12;
        if (r2 < R_MIN * R_MIN || v2 < V_MIN * V_MIN || !(mu > 0.0))
        {
            Debug.LogError("[ERROR] Position or velocity magnitude too small. Cannot compute orbital parameters.");
            return result;
        }

        // Angular momentum: h = r × v   (use h^2)
        double3 h = new double3(
            r.y * v.z - r.z * v.y,
            r.z * v.x - r.x * v.z,
            r.x * v.y - r.y * v.x
        );
        double h2 = h.x * h.x + h.y * h.y + h.z * h.z;
        if (h2 < H_MIN * H_MIN)
        {
            Debug.LogError("[ERROR] Angular momentum too small. Cannot compute orbital parameters.");
            return result;
        }

        // 1/|r| (one sqrt reused)
        double invR = 1.0 / Math.Sqrt(r2);

        // Specific orbital energy: E = v^2/2 - μ/|r|
        double energy = 0.5 * v2 - mu * invR;

        // Eccentricity vector direction: e_vec = (v × h)/μ - r/|r|
        double3 cxh = new double3(
            v.y * h.z - v.z * h.y,
            v.z * h.x - v.x * h.z,
            v.x * h.y - v.y * h.x
        );
        double invMu = 1.0 / mu;
        double3 eVec = cxh * invMu - r * invR;
        double e2_vec = eVec.x * eVec.x + eVec.y * eVec.y + eVec.z * eVec.z; // for direction only

        // ----- Stable eccentricity magnitude -----
        bool isOpen = (energy >= 0.0);
        double ecc;
        double a = 0.0;
        if (isOpen)
        {
            double term = 1.0 + (2.0 * energy * h2) / (mu * mu);
            ecc = Math.Sqrt(Math.Max(0.0, term));
        }
        else
        {
            a = -mu / (2.0 * energy); // no sqrt
            double term = 1.0 - (h2 / (a * mu));
            ecc = Math.Sqrt(Math.Max(0.0, term));
        }
        result.eccentricity = (float)ecc;

        // Node vector for RAAN (Y-up world)
        double3 n = new double3(h.z, 0.0, -h.x);
        double n2 = n.x * n.x + n.y * n.y + n.z * n.z;

        // Inclination (use -h for Y-up)
        double invH = 1.0 / Math.Sqrt(h2);
        double cosInc = Clamp((-h.y) * invH, -1.0, 1.0);
        double incDeg = Math.Acos(cosInc) * (180.0 / Math.PI);
        result.inclination = (float)incDeg;

        // RAAN (atan2(nz, nx))
        double raanDeg = 0.0;
        if (n2 > H_MIN * H_MIN)
        {
            double invN = 1.0 / Math.Sqrt(n2);
            double nx_n = n.x * invN;
            double nz_n = n.z * invN;
            double raan = Math.Atan2(nz_n, nx_n) * (180.0 / Math.PI);
            if (raan < 0.0) raan += 360.0;
            raanDeg = raan;
        }
        result.RAAN = (float)raanDeg;

        // ---------- Open vs. closed ----------
        if (isOpen)
        {
            result.semiMajorAxis = 0f;
            result.isCircular = false;

            // Periapsis distance for open orbits: rp = h^2 / (μ (1+e))
            double eSafe = Math.Max(ecc, 1.0);
            double rp_open = h2 / (mu * (1.0 + eSafe));

            // Periapsis direction: ê if available; fallback n̂; else +X
            double3 dir;
            if (e2_vec > 1e-18)
            {
                double invE = 1.0 / Math.Sqrt(e2_vec);
                dir = eVec * invE;
            }
            else if (n2 > H_MIN * H_MIN)
            {
                double invN = 1.0 / Math.Sqrt(n2);
                dir = n * invN;
            }
            else
            {
                dir = new double3(1.0, 0.0, 0.0);
            }

            Vector3 perigeeOffset = new Vector3(
                (float)(dir.x * rp_open),
                (float)(dir.y * rp_open),
                (float)(dir.z * rp_open));

            result.perigeePosition = centralBodyPosition + perigeeOffset;
            result.apogeePosition = Vector3.zero; // undefined for open
            result.isValid = true;
            return result;
        }
        else
        {
            // Closed ellipse
            result.semiMajorAxis = (float)a;

            // Period: T = 2π √(a^3/μ)
            double period = 2.0 * Math.PI * Math.Sqrt((a * a * a) / mu);
            result.orbitalPeriod = (float)period;

            // Radii using h^2/μ forms
            double rp = h2 / (mu * (1.0 + ecc));
            double ra = h2 / (mu * Math.Max(1e-15, (1.0 - ecc)));

            // Periapsis direction from ê if defined; fallback n̂; else +X
            const double ECC_TINY = 5e-6;
            const double ECC_CIRC = 1e-6;

            double3 periDir;
            if (ecc > ECC_TINY && e2_vec > 0.0)
            {
                double invE = 1.0 / Math.Sqrt(e2_vec);
                periDir = eVec * invE;
            }
            else if (n2 > H_MIN * H_MIN)
            {
                double invN = 1.0 / Math.Sqrt(n2);
                periDir = n * invN;
            }
            else
            {
                periDir = new double3(1.0, 0.0, 0.0);
            }

            result.isCircular = (ecc < ECC_CIRC);

            Vector3 centerF = centralBodyPosition;
            Vector3 periOff = new Vector3((float)(periDir.x * rp), (float)(periDir.y * rp), (float)(periDir.z * rp));
            Vector3 apoOff = new Vector3((float)(periDir.x * ra), (float)(periDir.y * ra), (float)(periDir.z * ra));

            result.perigeePosition = centerF + periOff;
            result.apogeePosition = centerF - apoOff;

            result.isValid = true;
            return result;
        }
    }

    private static double3 ToD3(Vector3 v) => new double3(v.x, v.y, v.z);

    /// <summary>
    /// Safe wrapper around CalculateOrbitalParameters:
    /// validates state/central mass first and avoids noisy logs until inputs are sane.
    /// </summary>
    public static OrbitalParameters TryParams(NBody body, BodyService svc)
    {
        if (body == null) return default;

        // Resolve center & mass safely
        Vector3 centerPos = svc?.CentralBody?.transform?.position ?? Vector3.zero;

        // Prefer the state’s centralBodyMass; fall back to real central body if 0
        float centralMass = body.state.centralBodyMass > 0f
            ? body.state.centralBodyMass
            : (svc?.CentralBody != null ? (float)svc.CentralBody.mass : 0f);

        // Prefer double-precision state; fall back to current Transform/velocity if needed
        double3 pos = body.state.position;
        double3 vel = body.state.velocity;

        // If the state is basically default/garbage, use fallback
        bool posBad = !(double.IsFinite(pos.x) && double.IsFinite(pos.y) && double.IsFinite(pos.z));
        bool velBad = !(double.IsFinite(vel.x) && double.IsFinite(vel.y) && double.IsFinite(vel.z));
        if (posBad) pos = ToD3(body.transform.position);
        if (velBad) vel = ToD3(body.velocity);

        // Early, quiet rejection until inputs are usable
        const double R_MIN = 1.0;
        const double V_MIN = 1e-6;

        double3 rel = pos - ToD3(centerPos);
        double r2 = rel.x * rel.x + rel.y * rel.y + rel.z * rel.z;
        double v2 = vel.x * vel.x + vel.y * vel.y + vel.z * vel.z;

        if (!(centralMass > 0f) || r2 < R_MIN * R_MIN || v2 < V_MIN * V_MIN)
            return default; // isValid == false

        // Safe to compute now
        return CalculateOrbitalParameters(centralMass, centerPos, pos, vel);
    }
}

/// <summary>
/// Key orbital elements and derived values for rendering/UX.
/// </summary>
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

    /// <summary>Initializes the struct with a validity flag.</summary>
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
    }
}
