using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using System.Linq;
using TMPro;
using System;

/**
* NBody class represents a celestial body in the gravitational system.
* It simulates gravitational interactions, velocity, and trajectory prediction.
**/
[RequireComponent(typeof(LineRenderer))]
public class NBody : MonoBehaviour
{
    [Header("Celestial Body Properties")]
    public float mass = 5.0e21f;
    public Vector3 velocity = new Vector3(0, 0, 20);
    public bool isCentralBody = false;
    public float radius = 637.8137f;
    public Vector3 force = Vector3.zero;
    public float centralBodyMass = 5.972e24f;

    [Header("Trajectory Prediction Settings")]
    public float predictionDeltaTime = .5f;

    [Header("Thrust Feedback")]
    private ThrustController thrustController;

    [Header("References")]
    private TrajectoryComputeController tcc;

    /**
    * Called when the script instance is being loaded.
    * Registers this NBody with the GravityManager.
    **/
    void Awake()
    {
        if (GravityManager.Instance != null)
        {
            GravityManager.Instance.RegisterBody(this);
        }
    }

    /**
    * Start method initializes line renderers and sets up trajectory predictions.
    **/
    void Start()
    {
        if (isCentralBody)
        {
            velocity = Vector3.zero;
            Debug.Log($"{gameObject.name} is the central body and will not move.");
        }

        thrustController = GravityManager.Instance.GetComponent<ThrustController>();
        if (thrustController == null)
        {
            Debug.LogError("NBody: ThrustController not found on GravityManager.");
        }

        tcc = FindFirstObjectByType<TrajectoryComputeController>();
        if (!tcc)
        {
            Debug.LogError("No TrajectoryComputeController found in scene!");
        }
        Debug.Log($"[DEBUG] Moon Start Pos: {transform.position}, Vel: {velocity}");

    }

    /**
    * FixedUpdate is called at a consistent interval and updates the NBody's physics state.
    **/
    void FixedUpdate()
    {
        if (float.IsNaN(transform.position.x) || float.IsNaN(transform.position.y) || float.IsNaN(transform.position.z))
        {
            Debug.LogError($"[ERROR] {name} has NaN transform.position! velocity={velocity}, force={force}");
        }

        if (mass <= 1e-6f)
        {
            force = Vector3.zero;
            return;
        }

        if (isCentralBody)
        {
            float earthRotationRate = 360f / (24f * 60f * 60f);
            transform.Rotate(Vector3.up, -earthRotationRate * Time.fixedDeltaTime);
        }
        else
        {
            List<NBody> bodies = GravityManager.Instance.Bodies;
            int numBodies = bodies.Count;

            Vector3[] positions = new Vector3[numBodies];
            Vector3[] velocities = new Vector3[numBodies];
            float[] masses = new float[numBodies];

            for (int i = 0; i < numBodies; i++)
            {
                positions[i] = bodies[i].transform.position;
                velocities[i] = bodies[i].velocity;
                masses[i] = bodies[i].mass;
            }

            Vector3 thrustImpulse = force;
            force = Vector3.zero;

            Vector3 tempPosition = transform.position;
            Vector3 tempVelocity = velocity;

            NativePhysics.RungeKuttaSingle(ref tempPosition, ref tempVelocity, mass, positions, masses, numBodies, Time.fixedDeltaTime, ref thrustImpulse);

            transform.position = tempPosition;
            velocity = tempVelocity;

            // Check for collisions.
            foreach (var body in GravityManager.Instance.Bodies)
            {
                if (body == this) continue;

                float distance = Vector3.Distance(transform.position, body.transform.position);
                float collisionThreshold = radius + body.radius;

                if (body.isCentralBody && distance < collisionThreshold)
                {
                    Debug.Log($"[COLLISION] {body.name} collided with central body {body.name}");
                    GravityManager.Instance.HandleCollision(this, body);
                    return;
                }
            }
        }
        force = Vector3.zero;
    }

    private void OnDestroy()
    {
        if (LineVisibilityManager.Instance != null)
        {
            LineVisibilityManager.Instance.DeregisterNBody(this);
        }
    }

    /**
    * Calculates the predicted trajectory of an orbit by passing calculation to the GPU
    * @param steps - Number of prediction line render points to render
    * @param deltaTime - Time step for simulation
    * @param onComplete - Async call from GPU to let know that calculations are complete
    **/
    public void CalculatePredictedTrajectoryGPU_Async(
        int steps,
        float deltaTime,
        Action<List<Vector3>> onComplete
    )
    {
        var otherBodies = GravityManager.Instance.Bodies.Where(b => b != this).ToList();
        Vector3[] otherPositions = otherBodies.Select(b => b.transform.position).ToArray();
        float[] otherMasses = otherBodies.Select(b => b.mass).ToArray();

        if (tcc == null)
        {
            Debug.LogError("TrajectoryComputeController (tcc) is null. Ensure it is assigned before calling this method.");
            onComplete?.Invoke(null);
            return;
        }

        tcc.CalculateTrajectoryGPU_Async(
            startPos: transform.position,
            startVel: velocity,
            bodyMass: mass,
            otherBodyPositions: otherPositions,
            otherBodyMasses: otherMasses,
            dt: deltaTime,
            steps: steps,
            onComplete: (positionsArray) =>
            {
                // Called when GPU readback is complete
                if (positionsArray == null)
                {
                    // Means there was an error in readback
                    onComplete?.Invoke(new List<Vector3>());
                }
                else
                {
                    onComplete?.Invoke(new List<Vector3>(positionsArray));
                }
            }
        );
    }

    /**
    * Adds a force vector to this NBody.
    * @param additionalForce - Additional force being applied to object (Thrust)
    **/
    public void AddForce(Vector3 additionalForce)
    {
        force += additionalForce;
    }

    /**
    * Computes and returns the Semi Major Axis and Eccentricity of an orbit
    * @param centralBodyMass - Mass of central body (Earth)
    **/
    public void ComputeOrbitalElements(out float semiMajorAxis, out float eccentricity, float centralBodyMass)
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

    /**
    * Returns the apogee and perigee Vector3 positions
    * @param centralBodyMass - Mass of central body (Earth)
    **/
    public void GetOrbitalApogeePerigee(float centralBodyMass, Vector3 centralBodyPosition,
                                    out Vector3 apogeePosition, out Vector3 perigeePosition, out bool isCircular)
    {
        // Compute the orbital elements: semi-major axis and eccentricity
        ComputeOrbitalElements(out float semiMajorAxis, out float eccentricity, centralBodyMass);

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
    * Adjusts the trajectory prediction settings based on time scale.
    * @param timeScale - Current time scale of the simulation
    **/
    public void AdjustPredictionSettings(float timeScale)
    {
        float distance = transform.position.magnitude;
        float speed = 300f;

        float baseDeltaTime = 0.5f;
        float minDeltaTime = 0.5f;
        float maxDeltaTime = 3f;

        float adjustedDelta = baseDeltaTime * (1 + distance / 1000f) / (1 + speed / 10f);
        adjustedDelta = Mathf.Clamp(adjustedDelta, minDeltaTime, maxDeltaTime);

        predictionDeltaTime = adjustedDelta;
    }

    /**
    * Returns the altitude above the reference central body.
    **/
    public float altitude
    {
        get
        {
            float distanceFromCenter = transform.position.magnitude;
            float distanceInKm = distanceFromCenter;
            float earthRadiusKm = 637.8137f;
            return distanceInKm - earthRadiusKm;
        }
    }

    /**
    * OrbitalState struct holds the position and velocity for Runge-Kutta calculations.
    **/
    public struct OrbitalState
    {
        public Vector3 position;
        public Vector3 velocity;

        public OrbitalState(Vector3 position, Vector3 velocity)
        {
            this.position = position;
            this.velocity = velocity;
        }
    }
}
