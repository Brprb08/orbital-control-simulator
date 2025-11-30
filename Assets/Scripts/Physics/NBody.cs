using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
using Unity.Mathematics;

/// <summary>
/// Dynamic body in the gravity sim: integrates motion (gravity, thrust, drag),
/// coordinates with maneuver nodes/trajectory prediction, and handles collision/escape.
/// </summary>
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
    private BodyRuntimeCoordinator bodyRuntimeCoordinator;
    private ManeuverNodeManager maneuverNodeManager;
    public ThrustController thrustController;
    private LineVisibilityController lineVisibilityController;
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

    public bool isThrusting = false;

    [Header("Constants")]
    private const float EarthRotationRate = 360f / 86164f; // deg/sec, sidereal
    const double EarthRadiusUnits = 637.8137;
    private const float MaxDistanceFromEarth = 40000f;

    [Header("Flags")]
    public bool isReferenceOrbit = false;

    public float cumulativeDeltaVUsed = 0f;

    public bool projectLateralPerSubstep = false;

    private double[] otherMassCache;

    private AttitudeController att;


    private SimContext ctx;

    /// <summary>
    /// Injects context dependencies used by integration, prediction, and UI systems.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.bodyRuntimeCoordinator = ctx.BodyRuntimeCoordinator;
        this.maneuverNodeManager = ctx.ManeuverNodeManager;
        this.lineVisibilityController = ctx.LineVisibilityController;
        this.tcc = ctx.TrajectoryComputeController;
        this.thrustController = ctx.ThrustController;
        this.rocketThrustAudio = ctx.RocketThrustAudio;
        this.bodyService = ctx.BodyService;
    }

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

        att = GetComponent<AttitudeController>();

        // Build relevantBodies and caches once here
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

        AllocateRelevantCaches();
    }

    private void AllocateRelevantCaches()
    {
        int n = (relevantBodies != null) ? relevantBodies.Count : 0;
        otherMassCache = (n > 0) ? new double[n] : Array.Empty<double>();

        for (int i = 0; i < n; i++)
        {
            var b = relevantBodies[i];
            otherMassCache[i] = (b != null) ? b.trueMass : 0.0;
        }
    }

    /// <summary>
    /// One physics tick for this body.
    /// If <see cref="BodyService.DrivePhysics"/> is <c>true</c>, this method only
    /// updates burns/audio and leaves integration to the central batch step.
    /// If <c>false</c>, it performs the legacy per-body integration path.
    /// </summary>
    /// <remarks>
    /// - In service-driven mode, we do <b>not</b> zero <see cref="state.force"/> here;
    ///   the batch step consumes it and clears it afterward.
    /// - Central body still rotates here in both modes (visual spin only).
    /// </remarks>
    void FixedUpdate()
    {
        if (ctx != null && ctx.BodyService != null && ctx.BodyService.DrivePhysics)
        {
            if (HasNaNPosition())
                Debug.LogError($"[NBODY]: {name} has NaN transform.position! velocity={velocity}, force={state.force}");

            if (isCentralBody)
            {
                RotateCentralBody();
                return;
            }

            if (!isReferenceOrbit)
            {
                CheckForNodeBurns();
            }
            return;
        }
    }

    /// <summary>
    /// Applies state produced by the batch integrator to the Unity <see cref="Transform"/>,
    /// runs post-integration safety checks, and clears consumed force.
    /// Call this once per body after the manager’s native batch step completes.
    /// </summary>
    public void SyncAfterBatch()
    {
        transform.position = state.position.ToVector3();
        velocity = state.velocity.ToVector3();

        CheckCollisionWithEarth();
        CheckEscapeFromEarth();

        state.force = Vector3.zero;
    }

    /// <summary>
    /// NaN guard for transform position (useful for detecting numerical blow-ups).
    /// </summary>
    bool HasNaNPosition()
    {
        Vector3 pos = transform.position;
        return float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z);
    }

    /// <summary>
    /// Simple Earth-like rotation for the central body.
    /// </summary>
    void RotateCentralBody()
    {
        // float dtSim = Time.fixedDeltaTime * Time.timeScale;
        float deltaAngle = -EarthRotationRate * Time.deltaTime;
        transform.Rotate(Vector3.up, deltaAngle);
    }

    /// <summary>
    /// Executes finalized maneuver nodes whose burn windows overlap the current sim time.
    /// Manages thrust audio/UI and prunes completed nodes.
    /// </summary>
    void CheckForNodeBurns()
    {
        if (isCentralBody || maneuverNodeManager == null)
            return;

        float simTime = bodyRuntimeCoordinator.simulationTime;
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
    /// Skip if node targets another body, isn’t finalized, or hasn’t reached its burn time.
    /// </summary>
    bool ShouldSkipNode(ManeuverNode node, NBody body, float simTime)
    {
        return node.targetBody != body || !node.isFinalized || simTime < node.burnTime;
    }

    /// <summary>
    /// True while the current sim time remains inside the node’s burn duration.
    /// </summary>
    bool IsBurnOngoing(ManeuverNode node, float simTime)
    {
        float timeSinceStart = simTime - node.burnTime;
        return timeSinceStart < node.duration;
    }

    /// <summary>
    /// Applies thrust per node burn type/direction for this frame.
    /// </summary>
    void ExecuteNodeBurn(ManeuverNode node, NBody body)
    {
        Vector3 burnDirection = maneuverNodeManager.GetBurnDirectionFromDropdown(body);

        thrustController.ApplyThrust(body, 10f, burnDirection);
        thrustController.SetDirectionalThrust(node.burnType);
    }

    /// <summary>
    /// Collision with central body → delegate removal to the coordinator.
    /// </summary>
    void CheckCollisionWithEarth()
    {
        NBody earth = bodyService != null ? bodyService.CentralBody : null;
        if (earth == null || earth == this) return;

        float distance = Vector3.Distance(transform.position, earth.transform.position);
        float collisionThreshold = cameraDistanceRadius + earth.radius;

        if (distance < collisionThreshold)
        {
            Debug.Log($"[NBODY]: [COLLISION] {name} collided with Earth");
            bodyRuntimeCoordinator.HandleCollision(this, earth);
        }
    }

    /// <summary>
    /// Exceeded sim boundary → treat as escape and delegate removal.
    /// </summary>
    void CheckEscapeFromEarth()
    {
        NBody earth = bodyService != null ? bodyService.CentralBody : null;
        if (earth == null || earth == this) return;

        float distance = Vector3.Distance(transform.position, earth.transform.position);
        if (distance > MaxDistanceFromEarth)
        {
            Debug.Log($"[NBODY]: [ESCAPE] {name} exceeded {MaxDistanceFromEarth * 10f:N0} km and is removed.");
            bodyRuntimeCoordinator.HandleCollision(this, earth); // replace with a dedicated escape handler if desired
        }
    }

    /// <summary>
    /// Clean up line-visibility registration for this body.
    /// </summary>
    private void OnDestroy()
    {
        if (lineVisibilityController != null)
        {
            lineVisibilityController.DeregisterNBody(this);
        }
    }

    /// <summary>
    /// Asynchronously samples a forward trajectory (GPU) from a given state with external bodies.
    /// </summary>
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
                if (positionsArray == null)
                {
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
    /// Accumulates an external force to be applied this physics step (e.g., thrust).
    /// </summary>
    public void AddForce(Vector3 additionalForce)
    {
        state.force += additionalForce;
    }

    /// <summary>
    /// Altitude above the central body's mean radius (sim units).
    /// </summary>
    public double altitude
    {
        get
        {
            double rUnits = transform.position.magnitude;
            return rUnits - EarthRadiusUnits;
        }
    }

    public struct OrbitalState
    {
        public double3 position;
        public double3 velocity;
        public float centralBodyMass;
        public double mass;
        public double radius;
        public double crossSectionArea;
        public float dragCoefficient;
        public Vector3 force;

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
            this.centralBodyMass = (centralBodyMass > 0f) ? centralBodyMass : 5.972e24f;
            this.mass = mass;
            this.radius = radius;
            this.dragCoefficient = dragCoefficient;
            this.force = force;
            this.crossSectionArea = Math.PI * radius * radius;
        }
    }
}


