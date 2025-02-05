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
    public float radius = 637.1f;
    public Vector3 force = Vector3.zero;
    public float centralBodyMass = 5.972e24f;

    [Header("Trajectory Prediction Settings")]
    public int predictionSteps = 5000;
    public float predictionDeltaTime = .5f;
    private static Material lineMaterial;
    public TrajectoryRenderer trajectoryRenderer;
    public float tolerance = 0f;

    [Header("Thrust Feedback")]
    private ThrustController thrustController;

    [Header("UI Elements")]
    public TextMeshProUGUI apogeeText;
    public TextMeshProUGUI perigeeText;

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

        if (lineMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("Shader 'Sprites/Default' not found. Please ensure it exists in your project.");
            }
            else
            {
                lineMaterial = new Material(shader);
            }
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
            Dictionary<NBody, Vector3> bodyPositions = new Dictionary<NBody, Vector3>();
            foreach (var body in GravityManager.Instance.Bodies)
            {
                bodyPositions[body] = body.transform.position;
            }

            OrbitalState currentState = new OrbitalState(transform.position, velocity);
            OrbitalState newState = RungeKuttaStep(currentState, Time.fixedDeltaTime, bodyPositions);

            velocity = newState.velocity;
            transform.position = newState.position;

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
        // Safely deregister so the manager no longer keeps a reference to this body.
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
            onComplete?.Invoke(null); // Return null to signal failure
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
    * Performs a single Runge-Kutta (RK4) step to update position and velocity.
    * Calculates derivatives for the Runge-Kutta integration.
    * @param state - The position and velocity of NBody object
    * @param bodyPositions - Current positions of all NBody objects
    * @param isTrajectory - If True RK4 is used for precise object motion, otherwise RK2 for line render
    * @param thrustImpulse - The current thrust value applied to object
    **/
    private OrbitalState RungeKuttaStep(OrbitalState currentState, float deltaTime, Dictionary<NBody, Vector3> bodyPositions, Vector3 thrustImpulse = default)
    {
        OrbitalState k1;
        OrbitalState k2;
        OrbitalState k3;
        OrbitalState k4;
        k1 = CalculateDerivatives(currentState, bodyPositions, thrustImpulse);
        k2 = CalculateDerivatives(new OrbitalState(
            currentState.position + k1.position * (deltaTime / 2f),
            currentState.velocity + k1.velocity * (deltaTime / 2f)
        ), bodyPositions, thrustImpulse);

        k3 = CalculateDerivatives(new OrbitalState(
            currentState.position + k2.position * (deltaTime / 2f),
            currentState.velocity + k2.velocity * (deltaTime / 2f)
        ), bodyPositions, thrustImpulse);

        k4 = CalculateDerivatives(new OrbitalState(
            currentState.position + k3.position * deltaTime,
            currentState.velocity + k3.velocity * deltaTime
        ), bodyPositions, thrustImpulse);

        Vector3 newPosition = currentState.position + (deltaTime / 6f) * (k1.position + 2f * k2.position + 2f * k3.position + k4.position);
        Vector3 newVelocity = currentState.velocity + (deltaTime / 6f) * (k1.velocity + 2f * k2.velocity + 2f * k3.velocity + k4.velocity);
        return new OrbitalState(newPosition, newVelocity);

    }

    /**
    * Calculates derivatives for the Runge-Kutta integration.
    * @param state - The position and velocity of NBody object
    * @param bodyPositions - Current positions of all NBody objects
    * @param thrustImpulse - The current thrust value applied to object
    **/
    private OrbitalState CalculateDerivatives(OrbitalState state, Dictionary<NBody, Vector3> bodyPositions, Vector3 thrustImpulse = default)
    {
        Vector3 acceleration = ComputeAccelerationFromData(state.position, bodyPositions, thrustImpulse);
        return new OrbitalState(state.velocity, acceleration);
    }

    /**
    * Computes the gravitational acceleration for a given position.
    * @param position - Current position of runge-kutta step
    * @param bodyPositions - Current positions of all NBody objects
    * @param thrustImpulse - The current thrust value applied to object
    **/
    private Vector3 ComputeAccelerationFromData(Vector3 position, Dictionary<NBody, Vector3> bodyPositions, Vector3 thrustImpulse = default)
    {
        Vector3 totalForce = Vector3.zero;
        float minDistance = 0.001f;  // Prevent divide-by-zero issues.

        foreach (var body in bodyPositions.Keys)
        {
            if (body != this)
            {
                Vector3 direction = bodyPositions[body] - position;
                float distanceSquared = Mathf.Max(direction.sqrMagnitude, minDistance * minDistance);
                float forceMagnitude = PhysicsConstants.G * (mass * body.mass) / distanceSquared;
                totalForce += direction.normalized * forceMagnitude;
            }
        }

        Vector3 externalAcceleration = (force / mass) + (thrustImpulse / mass);

        // Total acceleration plus external acceleration
        Vector3 totalAcceleration = (totalForce / mass) + externalAcceleration;
        return totalAcceleration;
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

        // Specific orbital energy
        float energy = (vMag * vMag) / 2f - (mu / rMag);

        if (energy >= 0f) // Hyperbolic or parabolic orbit
        {
            semiMajorAxis = 0f;
            eccentricity = 1f + (rMag * vMag * vMag) / mu; // Hyperbolic eccentricity (> 1)
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

        if (innerSqrt < 1e-8f)
        {
            eccentricity = 1e-8f; // a tiny nonzero eccentricity for visualization
        }
        else
        {
            eccentricity = Mathf.Sqrt(innerSqrt);
        }

        eccentricity = Mathf.Max(eccentricity, 1e-8f);
    }

    /**
    * Returns the apogee and perigee Vector3 positions
    * @param centralBodyMass - Mass of central body (Earth)
    **/
    public void GetOrbitalApogeePerigee(float centralBodyMass, out Vector3 apogeePosition, out Vector3 perigeePosition, out bool isCircular)
    {
        ComputeOrbitalElements(out float semiMajorAxis, out float eccentricity, centralBodyMass);

        if (float.IsNaN(semiMajorAxis) || float.IsNaN(eccentricity))
        {
            Debug.LogError("[ERROR] Invalid orbital elements. Cannot compute apogee and perigee.");
            apogeePosition = Vector3.zero;
            perigeePosition = Vector3.zero;
            isCircular = false;
            return;
        }

        float mu = PhysicsConstants.G * centralBodyMass;

        Vector3 r = transform.position; // Current position relative to the central body
        Vector3 v = velocity;           // Current velocity
        Vector3 hVec = Vector3.Cross(r, v); // Angular momentum vector
        float hMag = hVec.magnitude; // Angular momentum vector

        // Compute the eccentricity vector
        Vector3 eVec = (Vector3.Cross(v, hVec) / mu) - (r / r.magnitude);
        Vector3 eUnit = eVec.normalized;

        float perigeeDistance;

        if (eccentricity >= 1f) // Hyperbolic or parabolic orbit
        {
            Debug.LogWarning("Hyperbolic orbit detected. Showing true closest approach for perigee.");

            float semiLatusRectum = (hMag * hMag) / (mu * (1f + eccentricity));

            perigeeDistance = semiLatusRectum;

            perigeePosition = Vector3.zero + (eUnit * perigeeDistance);
            apogeePosition = Vector3.zero;
            isCircular = false;
            return;
        }

        if (eccentricity <= 0f)
        {
            isCircular = true;
        }
        else
        {
            isCircular = false;
        }

        // If elliptical orbit, compute apogee and perigee
        float apogeeDistance = semiMajorAxis * (1f + eccentricity);
        perigeeDistance = semiMajorAxis * (1f - eccentricity);

        perigeePosition = Vector3.zero + (eUnit * perigeeDistance);
        apogeePosition = Vector3.zero - (eUnit * apogeeDistance);
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
            float earthRadiusKm = 637.1f;
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