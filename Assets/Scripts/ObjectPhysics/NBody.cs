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
    public float mass = 5.0e21f;
    public Vector3 velocity = new Vector3(0, 0, 20);
    public bool isCentralBody = false;
    public float radius = 637.8137f;
    public Vector3 force = Vector3.zero;
    public float centralBodyMass = 5.972e24f;
    public float cameraDistanceRadius = 637f;

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

    public double3 truePosition;
    public double3 trueVelocity;
    public double trueMass = 5.0e21;

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

        thrustController = GravityManager.Instance.GetComponent<ThrustController>();
        if (thrustController == null)
        {
            Debug.LogError("[NBody]: ThrustController not found on GravityManager.");
        }

        Debug.Log($"[NBODY]: {gameObject.name} Start Pos: {transform.position}, Vel: {velocity}");

        truePosition = new double3(transform.position.x, transform.position.y, transform.position.z);
        trueVelocity = new double3(velocity.x, velocity.y, velocity.z);
    }

    /// <summary>
    /// Updates the physics state of the body at fixed intervals.
    /// Handles motion integration and rotation for the central body.
    /// </summary>
    void FixedUpdate()
    {
        if (HasNaNPosition())
        {
            Debug.LogError($"[NBODY]: {name} has NaN transform.position! velocity={velocity}, force={force}");
        }

        if (mass <= 1e-6f)
        {
            force = Vector3.zero;
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

        force = Vector3.zero;
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
        const float earthRotationRate = 360f / (24f * 60f * 60f);
        transform.Rotate(Vector3.up, -earthRotationRate * Time.fixedDeltaTime);
    }

    /// <summary>
    /// Applies gravity using Dormand-Prince integration and updates position/velocity.
    /// </summary>
    void SimulateOrbitalMotion()
    {
        List<NBody> bodies = GravityManager.Instance?.Bodies;
        if (bodies == null || bodies.Count == 0) return;

        var bodiesFiltered = new List<NBody>();
        foreach (var b in GravityManager.Instance.Bodies)
        {
            if (b != this && (b.isCentralBody || b.name == "Moon"))
                bodiesFiltered.Add(b);
        }
        int numBodies = bodiesFiltered.Count;

        var positions = new Vector3[numBodies];
        var masses = new double[numBodies];

        for (int i = 0; i < numBodies; i++)
        {
            positions[i] = bodiesFiltered[i].transform.position;
            masses[i] = bodiesFiltered[i].trueMass;
        }

        const float dtMax = 0.002f;
        int substeps = Mathf.CeilToInt(Time.fixedDeltaTime / dtMax);
        float dt = Time.fixedDeltaTime / substeps;

        float crossSectionArea = Mathf.PI * radius * radius;
        Vector3 thrustImpulse = force;


        for (int s = 0; s < substeps; s++)
        {
            NativePhysics.DormandPrinceSingle(ref truePosition, ref trueVelocity, mass, positions, masses, numBodies, dt, thrustImpulse, dragCoefficient, crossSectionArea);
        }

        transform.position = new Vector3(
            (float)truePosition.x,
            (float)truePosition.y,
            (float)truePosition.z
        );

        velocity = new Vector3(
            (float)trueVelocity.x,
            (float)trueVelocity.y,
            (float)trueVelocity.z
        );



        CheckCollisionWithEarth();
    }

    /// <summary>
    /// Checks for collision with the central body and triggers a removal event if detected.
    /// </summary>
    void CheckCollisionWithEarth()
    {
        NBody earth = GravityManager.Instance.CentralBody;
        if (earth == null || earth == this) return;

        float distance = Vector3.Distance(transform.position, earth.transform.position);
        float collisionThreshold = radius + earth.radius;

        if (distance < collisionThreshold)
        {
            Debug.Log($"[NBODY]: [COLLISION] {name} collided with Earth");
            GravityManager.Instance.HandleCollision(this, earth);
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
        force += additionalForce;
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
            float earthRadiusKm = 637.8137f;
            return distanceInKm - earthRadiusKm;
        }
    }

    /// <summary>
    /// Represents the state of an orbit (position and velocity).
    /// Used for physics calculations.
    /// </summary>
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
