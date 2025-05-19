using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
using Unity.Mathematics;

/// <summary>
/// Represents a celestial body in the gravitational system.
/// Simulates gravity, thrust, drag, and integrates with prediction and rendering systems.
/// </summary>
// [RequireComponent(typeof(LineRenderer))]
public class NBody : MonoBehaviour
{
    [Header("Celestial Body Properties")]
    public Vector3 velocity = new Vector3(0, 0, 20);
    public float mass = 5.0e21f;
    public bool isCentralBody = false;
    public float radius = EarthRadiusKm;
    public float cameraDistanceRadius = 637f;
    public double trueMass = 5.0e21;

    [Header("Trajectory Prediction Settings")]
    public float predictionDeltaTime = .5f;

    [Header("Thrust Feedback")]
    private ThrustController thrustController;

    [Header("References")]
    private TrajectoryComputeController tcc;

    [Header("Atmosphere & Drag")]
    [Tooltip("Sea-level density (kg/km³)")]
    public float atmosphericDensity0 = 1.225e9f;
    [Tooltip("Scale height (km)")]
    public float atmosphericScaleHeight = 8.5f;
    [Tooltip("Dimensionless drag coefficient")]
    public float dragCoefficient = 2.2f;

    [Header("Constants")]
    private const float EarthRotationRate = 360f / (24f * 60f * 60f);
    private const float EarthRadiusKm = 637.8137f;

    public OrbitalState state;

    private GravityManager gravityManager;
    private List<NBody> relevantBodies;

    /// <summary>
    /// Initializes trajectory data and sets the body to static if it's the central body.
    /// </summary>
    void Start()
    {
        if (isCentralBody)
        {
            velocity = Vector3.zero;
            Debug.Log($"[NBODY]: {gameObject.name} is the central body and will not move.");
        }

        gravityManager = GravityManager.Instance;
        if (gravityManager == null)
        {
            Debug.LogError("[NBody]: GravityManager not found.");
            return;
        }

        thrustController = gravityManager.GetComponent<ThrustController>();
        if (thrustController == null)
        {
            Debug.LogError("[NBody]: ThrustController not found on GravityManager.");
        }

        Debug.Log($"[NBODY]: {gameObject.name} Start Pos: {transform.position}, Vel: {velocity}");

        state = new OrbitalState(
            new double3(transform.position.x, transform.position.y, transform.position.z),
            new double3(velocity.x, velocity.y, velocity.z),
            0f,
            trueMass,
            radius,
            dragCoefficient,
            Vector3.zero
        );

        relevantBodies = gravityManager.Bodies
       .Where(b => b != this && (b.isCentralBody || b.name == "Moon"))
       .ToList();
    }

    /// <summary>
    /// Updates the physics state of the body at fixed intervals.
    /// Handles motion integration and rotation for the central body.
    /// </summary>
    void FixedUpdate()
    {
        if (HasNaNPosition())
        {
            Debug.LogError($"[NBODY]: {name} has NaN transform.position! velocity={velocity}, force={state.force}");
        }

        if (mass <= 1e-6f)
        {
            state.force = Vector3.zero;
            return;
        }

        if (isCentralBody)
        {
            RotateCentralBody();
        }
        else
        {
            SimulateOrbitalMotion();
        }

        state.force = Vector3.zero;
    }

    /// <summary>
    /// Checks if the body's position has become NaN (indicative of numerical instability).
    /// </summary>
    /// <returns>True if any component of position is NaN.</returns>
    bool HasNaNPosition()
    {
        Vector3 pos = transform.position;
        return float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z);
    }

    /// <summary>
    /// Simulates Earth-like rotation for the central body.
    /// </summary>
    void RotateCentralBody()
    {
        transform.Rotate(Vector3.up, -EarthRotationRate * Time.fixedDeltaTime);
    }

    /// <summary>
    /// Applies gravity using Dormand-Prince integration and updates position/velocity.
    /// </summary>
    void SimulateOrbitalMotion()
    {
        if (relevantBodies == null || relevantBodies.Count == 0) return;

        int numBodies = relevantBodies.Count;

        var positions = new Vector3[numBodies];
        var masses = new double[numBodies];

        for (int i = 0; i < numBodies; i++)
        {
            positions[i] = relevantBodies[i].transform.position;
            masses[i] = relevantBodies[i].trueMass;
        }

        const float dtMax = 0.002f;
        int substeps = Mathf.CeilToInt(Time.fixedDeltaTime / dtMax);
        float dt = Time.fixedDeltaTime / substeps;

        for (int s = 0; s < substeps; s++)
        {
            NativePhysics.DormandPrinceSingle(
                ref state.position,
                ref state.velocity,
                state.mass,
                positions,
                masses,
                numBodies,
                dt,
                state.force,
                (float)state.dragCoefficient,
                (float)state.crossSectionArea
            );
        }

        transform.position = state.position.ToVector3();
        velocity = state.velocity.ToVector3();

        CheckCollisionWithEarth();
    }

    /// <summary>
    /// Checks for collision with the central body and triggers a removal event if detected.
    /// </summary>
    void CheckCollisionWithEarth()
    {
        NBody earth = gravityManager.CentralBody;
        if (earth == null || earth == this) return;

        float distance = Vector3.Distance(transform.position, earth.transform.position);
        float collisionThreshold = cameraDistanceRadius + earth.radius;

        if (distance < collisionThreshold)
        {
            Debug.Log($"[NBODY]: [COLLISION] {name} collided with Earth");
            gravityManager.HandleCollision(this, earth);
        }
    }

    /// <summary>
    /// Cleans up line rendering references when this body is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        if (LineVisibilityManager.Instance != null)
        {
            LineVisibilityManager.Instance.DeregisterNBody(this);
        }
    }

    /// <summary>
    /// Asynchronously calculates the trajectory prediction using GPU compute shaders.
    /// </summary>
    /// <param name="steps">Number of prediction points to compute.</param>
    /// <param name="deltaTime">Timestep for each prediction step.</param>
    /// <param name="onComplete">Callback invoked when prediction is finished.</param>
    public void CalculatePredictedTrajectoryGPU_Async(
        int steps,
        float deltaTime,
        Action<List<Vector3>> onComplete
    )
    {
        var otherBodies = GravityManager.Instance.Bodies.Where(b => b != this).ToList();
        Vector3[] otherPositions = otherBodies.Select(b => b.transform.position).ToArray();
        float[] otherMasses = otherBodies.Select(b => (float)b.mass).ToArray();

        if (tcc == null && (tcc = TrajectoryComputeController.Instance) == null)
        {
            Debug.LogError("[NBODY]: TrajectoryComputeController (tcc) is null. Ensure it is assigned before calling this method.");
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

    /// <summary>
    /// Adds an external force (thrust) to the body.
    /// </summary>
    /// <param name="additionalForce">Force vector to apply.</param>
    public void AddForce(Vector3 additionalForce)
    {
        state.force += additionalForce;
    }

    /// <summary>
    /// Dynamically adjusts the trajectory prediction delta time based on position and simulation time scale.
    /// </summary>
    /// <param name="timeScale">Current simulation time scale.</param>
    public void AdjustPredictionSettings(float timeScale)
    {
        float distance = transform.position.magnitude;
        float speed = 300f;

        float baseDeltaTime = 0.5f;
        float minDeltaTime = 0.5f;
        float maxDeltaTime = 1f;

        float adjustedDelta = baseDeltaTime * (1 + distance / 1000f) / (1 + speed / 10f);
        adjustedDelta = Mathf.Clamp(adjustedDelta, minDeltaTime, maxDeltaTime);

        predictionDeltaTime = adjustedDelta;
    }

    /// <summary>
    /// Gets the current altitude above the surface of the central body.
    /// </summary>
    public double altitude
    {
        get
        {
            float distanceFromCenter = transform.position.magnitude;
            float distanceInKm = distanceFromCenter;
            float earthRadiusKm = EarthRadiusKm;
            return distanceInKm - earthRadiusKm;
        }
    }

    /// <summary>
    /// Represents the state of an orbit (position and velocity).
    /// Used for physics calculations.
    /// </summary>
    public struct OrbitalState
    {
        public double3 position;         // Position in ECI
        public double3 velocity;         // Velocity in ECI
        public float centralBodyMass;    // Earth mass
        public double mass;              // In kg
        public double radius;            // For drag & collision (in sim units)
        public double crossSectionArea;  // Precomputed for drag force
        public float dragCoefficient;    // Default ~2.2
        public Vector3 force;            // External impulse (thrust)

        public OrbitalState(
            double3 position,
            double3 velocity,
            float centralBodyMass,
            double mass,
            double radius,
            float dragCoefficient,
            Vector3 force)
        {
            this.position = position;
            this.velocity = velocity;
            this.centralBodyMass = 5.972e24f;
            this.mass = mass;
            this.radius = radius;
            this.dragCoefficient = dragCoefficient;
            this.force = force;

            this.crossSectionArea = Math.PI * radius * radius; // compute once
        }
    }
}
