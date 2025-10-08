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
    public double trueMass = 5.0e21;
    public float radius = 637.8137f;
    public float cameraDistanceRadius = 637f;
    public bool isCentralBody = false;
    public OrbitalState state;

    [Header("Trajectory Prediction Settings")]
    public float predictionDeltaTime = 0.5f;

    [Header("References - Scripts")]
    private TrajectoryComputeController tcc;
    private GravityManager gravityManager;
    private ManeuverNodeManager maneuverNodeManager;
    private ThrustController thrustController;
    private LineVisibilityManager lineVisibilityManager;
    private RocketThrustAudio rocketThrustAudio;
    private BodyService bodyService;

    [Header("References - Relevant Bodies")]
    private List<NBody> relevantBodies;

    [Header("Atmosphere & Drag")]
    [Tooltip("Sea-level density (kg/km³)")]
    public float atmosphericDensity0 = 1.225e9f;
    [Tooltip("Scale height (km)")]
    public float atmosphericScaleHeight = 8.5f;
    [Tooltip("Dimensionless drag coefficient")]
    public float dragCoefficient = 2.2f;

    private bool isThrusting = false;

    [Header("Constants")]
    private const float EarthRotationRate = 360f / (24f * 60f * 60f);
    const double EarthRadiusUnits = 637.8137;
    private const double UnitToKm = 10.0;

    private const float MaxDistanceFromEarth = 40000f;

    public float cumulativeDeltaVUsed = 0f;
    private bool wasThrustingLastFrame = false;
    private Vector3 burnStartVelocity;
    private Vector3 burnEndVelocity;

    // public LineRenderer progradeLine;
    // public LineRenderer normalLine;
    // public LineRenderer radialInLine;

    private SimContext ctx;

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.gravityManager = ctx.GravityManager;
        this.maneuverNodeManager = ctx.ManeuverNodeManager;
        this.lineVisibilityManager = ctx.LineVisibilityManager;
        this.tcc = ctx.TrajectoryComputeController;
        this.thrustController = ctx.ThrustController;
        this.rocketThrustAudio = ctx.RocketThrustAudio;
        this.bodyService = ctx.BodyService;
    }

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

        //     relevantBodies = gravityManager.Bodies
        //    .Where(b => b != this && (b.isCentralBody || b.name == "Moon"))
        //    .ToList();
        var all = bodyService != null ? bodyService.Bodies : null;
        if (all != null)
        {
            relevantBodies = new List<NBody>();
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null || b == this) continue;
                if (b.isCentralBody || b.name == "Moon")
                    relevantBodies.Add(b);
            }
        }
        else
        {
            relevantBodies = new List<NBody>();
        }
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
            CheckForNodeBurns();

            Vector3 thrustForceThisFrame = state.force;

            SimulateOrbitalMotion();

            Vector3 acceleration = thrustForceThisFrame / (float)mass;
            float deltaVThisFrame = acceleration.magnitude * Time.fixedDeltaTime;

            bool isThrustingNow = thrustForceThisFrame != Vector3.zero;

            if (isThrustingNow)
            {
                if (!wasThrustingLastFrame)
                {
                    // Just started burning
                    burnStartVelocity = velocity;
                }
                cumulativeDeltaVUsed += deltaVThisFrame * 10f; // Unity units → km/s
            }
            else if (wasThrustingLastFrame && !isThrustingNow)
            {
                // Just stopped burning
                burnEndVelocity = velocity;

                float deltaVVectorMagnitude = (burnEndVelocity - burnStartVelocity).magnitude * 10f;
                Debug.Log(
                    $"Delta-V used in burn: {cumulativeDeltaVUsed:F3} km/s\n" +
                    $"Start Velocity: {burnStartVelocity.magnitude * 10f:F3} km/s\n" +
                    $"End Velocity:   {burnEndVelocity.magnitude * 10f:F3} km/s\n" +
                    $"Vector Δv:      {deltaVVectorMagnitude:F3} km/s"
                );

                cumulativeDeltaVUsed = 0f;
            }

            wasThrustingLastFrame = isThrustingNow;
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
    /// Iterates through all maneuver nodes and triggers thrust if a burn window is active.
    /// Finalized nodes that have completed their burn are removed from execution.
    /// </summary>
    void CheckForNodeBurns()
    {
        if (isCentralBody || maneuverNodeManager == null)
            return;

        float simTime = gravityManager.simulationTime;
        bool burnInProgress = false;
        var toExecute = new List<ManeuverNode>();

        foreach (var node in maneuverNodeManager.nodes)
        {
            if (ShouldSkipNode(node, this, simTime))
                continue;

            if (IsBurnOngoing(node, simTime))
            {
                ExecuteNodeBurn(node, this);
                burnInProgress = true;
            }
            else
            {
                thrustController.StopAllThrust();
                toExecute.Add(node); // burn complete
            }
        }

        if (burnInProgress)
        {
            if (!isThrusting)
            {
                rocketThrustAudio.StartThrust();
                isThrusting = true;
            }
        }
        else
        {
            if (isThrusting)
            {
                rocketThrustAudio.StopThrust();
                thrustController.StopAllThrust();
                isThrusting = false;
            }
        }

        foreach (var node in toExecute)
            maneuverNodeManager.RemoveNode(node);
    }

    /// <summary>
    /// Determines if a maneuver node should be skipped based on its target, finalization status, or timing.
    /// </summary>
    /// <param name="node">The maneuver node being evaluated.</param>
    /// <param name="body">The NBody this system is currently processing.</param>
    /// <param name="simTime">The current simulation time.</param>
    /// <returns>True if the node should be skipped; otherwise, false.</returns>

    bool ShouldSkipNode(ManeuverNode node, NBody body, float simTime)
    {
        return node.targetBody != body || !node.isFinalized || simTime < node.burnTime;
    }

    /// <summary>
    /// Checks if the current simulation time falls within the active burn window of the node.
    /// </summary>
    /// <param name="node">The maneuver node being evaluated.</param>
    /// <param name="simTime">The current simulation time.</param>
    /// <returns>True if the burn is still ongoing; otherwise, false.</returns>

    bool IsBurnOngoing(ManeuverNode node, float simTime)
    {
        float timeSinceStart = simTime - node.burnTime;
        return timeSinceStart < node.duration;
    }

    /// <summary>
    /// Applies thrust in the correct direction for a maneuver node burn,
    /// using the node’s assigned burn type and the associated thrust system.
    /// </summary>
    /// <param name="node">The active maneuver node.</param>
    /// <param name="body">The NBody receiving thrust.</param>

    void ExecuteNodeBurn(ManeuverNode node, NBody body)
    {
        Vector3 burnDirection = maneuverNodeManager.GetBurnDirectionFromDropdown(body);

        thrustController.ApplyThrust(body, 10f, burnDirection);
        thrustController.SetDirectionalThrust(node.burnType);
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
        CheckEscapeFromEarth();
    }

    /// <summary>
    /// Checks for collision with the central body and triggers a removal event if detected.
    /// </summary>
    void CheckCollisionWithEarth()
    {
        // NBody earth = gravityManager.CentralBody;
        NBody earth = bodyService != null ? bodyService.CentralBody : null;
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
    /// Checks if the object has escaped the Earth's sphere of influence and removes it if so.
    /// </summary>
    void CheckEscapeFromEarth()
    {
        // NBody earth = gravityManager.CentralBody;
        NBody earth = bodyService != null ? bodyService.CentralBody : null;
        if (earth == null || earth == this) return;

        float distance = Vector3.Distance(transform.position, earth.transform.position);
        if (distance > MaxDistanceFromEarth)
        {
            Debug.Log($"[NBODY]: [ESCAPE] {name} exceeded {MaxDistanceFromEarth * 10f:N0} km and is removed.");
            gravityManager.HandleCollision(this, earth); // You can replace this with a new handler like HandleEscape()
        }
    }

    /// <summary>
    /// Cleans up line rendering references when this body is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        if (lineVisibilityManager != null)
        {
            lineVisibilityManager.DeregisterNBody(this);
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
        Action<List<Vector3>> onComplete,
        Vector3? overrideStartPosition = null,
        Vector3? overrideStartVelocity = null
    )
    {
        if (relevantBodies == null || relevantBodies.Count == 0) return;
        Vector3[] otherPositions = relevantBodies.Select(b => b.transform.position).ToArray();
        float[] otherMasses = relevantBodies.Select(b => (float)b.mass).ToArray();

        if (tcc == null)
        {
            Debug.LogError("[NBODY]: TrajectoryComputeController (tcc) is null. Ensure it is assigned before calling this method.");
            onComplete?.Invoke(null);
            return;
        }

        tcc.CalculateTrajectoryGPU_Async(
            startPos: overrideStartPosition ?? transform.position,
            startVel: overrideStartVelocity ?? velocity,
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
    /// Gets the current altitude above the surface of the central body.
    /// </summary>
    public double altitude
    {
        get
        {
            double rUnits = transform.position.magnitude;
            return rUnits - EarthRadiusUnits;
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

