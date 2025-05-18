using UnityEngine;

/// <summary>
/// Responsible for calculating orbital mechanics data for satellites.
/// Provides utilities to compute various orbital parameters such as
/// semi-major axis, eccentricity, apogee/perigee positions, and orbital period
/// using classical Newtonian physics.
/// </summary>
public class OrbitalCalculations : MonoBehaviour
{
    public static OrbitalCalculations Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
    }


    /// <summary>
    /// Calculates key orbital parameters for a satellite orbiting a central body.
    /// Uses classical orbital mechanics based on position and velocity vectors.
    /// </summary>
    /// <param name="centralBodyMass">Mass of the central body in kilograms.</param>
    /// <param name="centralBodyPosition">World-space position of the central body.</param>
    /// <param name="bodyTransform">Transform of the orbiting body.</param>
    /// <param name="velocity">Velocity vector of the orbiting body in world-space.</param>
    /// <returns>
    /// An <see cref="OrbitalParameters"/> struct containing calculated orbital elements such as:
    /// semi-major axis, eccentricity, apogee/perigee positions, orbital period,
    /// inclination, RAAN, and orbit validity flags.
    /// </returns>
    public OrbitalParameters CalculateOrbitalParameters(float centralBodyMass, Vector3 centralBodyPosition, Transform bodyTransform, Vector3 velocity)
    {
        OrbitalParameters result = new OrbitalParameters(false);

        float mu = PhysicsConstants.G * centralBodyMass;

        Vector3 r = bodyTransform.position - centralBodyPosition;
        Vector3 v = velocity;

        float rMag = r.magnitude;
        float vMag = v.magnitude;

        if (rMag < 1f || vMag < 1e-6f)
        {
            Debug.LogError("[ERROR] Position or velocity magnitude too small. Cannot compute orbital parameters.");
            return result;
        }

        float energy = (vMag * vMag) / 2f - (mu / rMag);
        Vector3 hVec = Vector3.Cross(r, v);
        float hMag = hVec.magnitude;

        if (hMag < 1e-6f)
        {
            Debug.LogError("[ERROR] Angular momentum too small. Cannot compute orbital parameters.");
            return result;
        }

        // Eccentricity
        float innerSqrt = 1f + (2f * energy * hMag * hMag) / (mu * mu);
        innerSqrt = Mathf.Max(innerSqrt, 0f);
        result.eccentricity = Mathf.Sqrt(innerSqrt);

        if (energy >= 0f)
        {
            result.semiMajorAxis = 0f;
            result.isCircular = false;
            result.isValid = true;
            return result;
        }

        result.semiMajorAxis = -mu / (2f * energy);
        result.orbitalPeriod = 2f * Mathf.PI * Mathf.Sqrt(Mathf.Pow(result.semiMajorAxis, 3) / mu);

        Vector3 eVec = (Vector3.Cross(v, hVec) / mu) - (r / r.magnitude);
        Vector3 eUnit = eVec.magnitude > 1e-6f ? eVec.normalized : r.normalized;

        if (result.eccentricity < 1e-6f)
        {
            result.isCircular = true;
            float orbitRadius = r.magnitude;
            result.perigeePosition = centralBodyPosition + r.normalized * orbitRadius;
            result.apogeePosition = centralBodyPosition - r.normalized * orbitRadius;
        }
        else if (result.eccentricity >= 1f)
        {
            Debug.LogWarning("Hyperbolic or parabolic orbit detected.");
            float perigeeDistance = (hMag * hMag) / (mu * (1f + result.eccentricity));
            result.perigeePosition = centralBodyPosition + eUnit * perigeeDistance;
            result.apogeePosition = Vector3.zero;
        }
        else
        {
            float perigeeDistance = result.semiMajorAxis * (1f - result.eccentricity);
            float apogeeDistance = result.semiMajorAxis * (1f + result.eccentricity);
            result.perigeePosition = centralBodyPosition + eUnit * perigeeDistance;
            result.apogeePosition = centralBodyPosition - eUnit * apogeeDistance;
        }

        Vector3 hUnit = -hVec.normalized;
        result.inclination = Mathf.Acos(Vector3.Dot(hUnit, Vector3.up)) * Mathf.Rad2Deg;


        // RAAN (angle between X axis and ascending node)
        Vector3 nodeVec = Vector3.Cross(Vector3.up, hVec); // Unity Y-up
        float nodeMag = nodeVec.magnitude;

        if (nodeMag > 1e-6f)
        {
            result.RAAN = Mathf.Acos(nodeVec.x / nodeMag) * Mathf.Rad2Deg;
            if (nodeVec.z < 0) result.RAAN = 360f - result.RAAN;

            if (result.RAAN == 360f)
            {
                result.RAAN = 0f;
            }
        }
        else
        {
            result.RAAN = 0f;
        }

        result.isValid = true;
        return result;
    }
}

/// <summary>
/// A data structure representing the key elements of an orbit.
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

    /// <summary>
    /// Constructor for initializing orbital parameters.
    /// </summary>
    /// <param name="valid">Whether the calculated orbit is considered valid.</param>
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