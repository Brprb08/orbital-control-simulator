using UnityEngine;

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

    /**
        * Returns the apogee and perigee Vector3 positions
        * @param centralBodyMass - Mass of central body (Earth)
        **/
    public void GetOrbitalApogeePerigee(float centralBodyMass, Vector3 centralBodyPosition,
                                    out Vector3 apogeePosition, out Vector3 perigeePosition, out bool isCircular, Transform transform, Vector3 velocity)
    {
        // Compute the orbital elements: semi-major axis and eccentricity
        ComputeOrbitalElements(out float semiMajorAxis, out float eccentricity, centralBodyMass, transform, velocity);

        if (float.IsNaN(semiMajorAxis) || float.IsNaN(eccentricity))
        {
            Debug.LogError("[ERROR] Invalid orbital elements. Cannot compute apogee and perigee.");
            apogeePosition = perigeePosition = centralBodyPosition;
            isCircular = false;
            return;
        }

        Debug.Log($"Semi-Major Axis: {semiMajorAxis}, Eccentricity: {eccentricity}");

        float mu = PhysicsConstants.G * centralBodyMass;

        Vector3 r = transform.position - centralBodyPosition;
        Vector3 v = velocity;
        Vector3 hVec = Vector3.Cross(r, v);
        float hMag = hVec.magnitude;

        float nearZeroThreshold = 1e-6f;

        // Handle near-circular orbits.
        if (eccentricity < nearZeroThreshold)
        {
            isCircular = true;
            float orbitRadius = r.magnitude;
            Vector3 radialDirection = r.normalized;
            perigeePosition = centralBodyPosition + radialDirection * orbitRadius;
            apogeePosition = centralBodyPosition - radialDirection * orbitRadius;
            Debug.Log("Nearly circular orbit. Using current radial direction as fallback.");
            return;
        }
        else
        {
            isCircular = false;
        }
        isCircular = false;

        // Compute the eccentricity vector
        Vector3 eVec = (Vector3.Cross(v, hVec) / mu) - (r / r.magnitude);


        // Use a fallback if the computed eccentricity vectors magnitude is too low
        Vector3 eUnit = eVec.magnitude > nearZeroThreshold ? eVec.normalized : r.normalized;

        // Check for non-elliptical trajectories
        if (eccentricity >= 1f)
        {
            Debug.LogWarning("Hyperbolic or parabolic orbit detected. Computing closest approach only.");
            float perigeeDistance = (hMag * hMag) / (mu * (1f + eccentricity));
            perigeePosition = centralBodyPosition + eUnit * perigeeDistance;
            apogeePosition = Vector3.zero; // Apogee not defined for open trajectories.
            return;
        }

        float perigeeDistanceElliptical = semiMajorAxis * (1f - eccentricity);
        float apogeeDistanceElliptical = semiMajorAxis * (1f + eccentricity);

        perigeePosition = centralBodyPosition + eUnit * perigeeDistanceElliptical;
        apogeePosition = centralBodyPosition - eUnit * apogeeDistanceElliptical;

        Debug.Log($"Perigee Distance: {perigeeDistanceElliptical}, Apogee Distance: {apogeeDistanceElliptical}");
    }

    /**
    * Computes and returns the Semi Major Axis and Eccentricity of an orbit
    * @param centralBodyMass - Mass of central body (Earth)
    **/
    public void ComputeOrbitalElements(out float semiMajorAxis, out float eccentricity, float centralBodyMass, Transform transform, Vector3 velocity)
    {
        float mu = PhysicsConstants.G * centralBodyMass;
        Vector3 r = transform.position;
        Vector3 v = velocity;

        float rMag = r.magnitude;
        float vMag = v.magnitude;

        if (rMag < 1f || vMag < 1e-6f)
        {
            Debug.LogError("[ERROR] Position or velocity magnitude too small. Cannot compute orbital elements.");
            semiMajorAxis = 0f;
            eccentricity = 0f;
            return;
        }

        float energy = (vMag * vMag) / 2f - (mu / rMag);

        if (energy >= 0f) // Hyperbolic or parabolic orbit
        {
            semiMajorAxis = 0f;
            eccentricity = 1f + (rMag * vMag * vMag) / mu;
            Debug.LogWarning($"Hyperbolic orbit detected. Eccentricity set to {eccentricity:F3}.");
            return;
        }

        semiMajorAxis = -mu / (2f * energy);

        Vector3 hVec = Vector3.Cross(r, v);
        float hMag = hVec.magnitude;

        if (hMag < 1e-6f)
        {
            Debug.LogError("[ERROR] Angular momentum too small. Cannot compute orbital elements.");
            eccentricity = 0f;
            return;
        }

        float innerSqrt = 1f + (2f * energy * hMag * hMag) / (mu * mu);
        innerSqrt = Mathf.Max(innerSqrt, 0f);
        eccentricity = Mathf.Sqrt(innerSqrt);
    }
}