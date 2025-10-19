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
    private ThrustController thrustController;
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

    private OrbitalFrameFilter basisFilter = new OrbitalFrameFilter();
    public bool projectLateralPerSubstep = false;


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

    /// <summary>
    /// Seeds initial state and establishes the subset of bodies relevant for forces.
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
    /// Physics step: rotates the central body; otherwise integrates motion,
    /// applies thrust impulses, tracks burn Δv, and clears accumulated force.
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
                    burnStartVelocity = velocity; // burn start
                }
                cumulativeDeltaVUsed += deltaVThisFrame * 10f; // Unity units → km/s
            }
            else if (wasThrustingLastFrame && !isThrustingNow)
            {
                burnEndVelocity = velocity; // burn end

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
        transform.Rotate(Vector3.up, -EarthRotationRate * Time.fixedDeltaTime);
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

        // ----- time slicing -----
        const float dtMax = 0.002f; // ~2 ms substep cap
        int substeps = Mathf.Max(1, Mathf.CeilToInt(Time.fixedDeltaTime / dtMax));
        float dt = Time.fixedDeltaTime / substeps;

        // ----- lateral projection gating -----
        // bool lateralActive = thrustController != null &&
        //                      (thrustController.isRightThrustActive || thrustController.isLeftThrustActive);

        // // ThrustController should set this on the ship when left/right is down
        // bool shouldProject = projectLateralPerSubstep && lateralActive;
        var att = GetComponent<AttitudeController>();
        bool shouldProject = false;
        if (att && att.mode == AttitudeController.PointingMode.Normal || att.mode == AttitudeController.PointingMode.AntiNormal) shouldProject = true;

        // If attitude is holding a fixed world orientation, do NOT re-project (we’d fight the hold)

        if (att && att.mode == AttitudeController.PointingMode.HoldCurrent)
            shouldProject = false;

        for (int s = 0; s < substeps; s++)
        {
            // Start with force accumulated this frame
            Vector3 F = state.force;

            // Keep lateral burns energy-neutral by enforcing F ⟂ v (i.e., F ∥ n̂)
            if (shouldProject && F.sqrMagnitude > 0f)
            {
                Vector3 rInst = state.position.ToVector3();
                Vector3 vInst = state.velocity.ToVector3();

                float r2 = rInst.sqrMagnitude;
                float v2 = vInst.sqrMagnitude;

                if (r2 > 1e-18f && v2 > 1e-18f)
                {
                    Vector3 rHat = rInst / Mathf.Sqrt(r2);
                    Vector3 vHat = vInst / Mathf.Sqrt(v2);
                    Vector3 nHat = Vector3.Cross(rHat, vHat);
                    float n2 = nHat.sqrMagnitude;

                    if (n2 > 1e-18f)
                    {
                        nHat /= Mathf.Sqrt(n2);

                        float Fmag = F.magnitude;
                        // Preserve the intended left/right sign relative to n̂
                        float sign = Mathf.Sign(Vector3.Dot(F, nHat));
                        if (sign == 0f) sign = 1f;

                        F = nHat * (Fmag * sign);
                    }
                    // else: orbital plane is degenerate this substep — skip projection
                }
                // else: r or v invalid — skip projection
            }

            // NaN clamp just in case
            if (float.IsNaN(F.x) || float.IsNaN(F.y) || float.IsNaN(F.z))
                F = Vector3.zero;

            // Integrate one substep with per-substep (possibly projected) force
            NativePhysics.DormandPrinceSingle(
                ref state.position,
                ref state.velocity,
                state.mass,
                positions,
                masses,
                numBodies,
                dt,
                F,
                (float)state.dragCoefficient,
                (float)state.crossSectionArea
            );
        }

        // Sync Transform / cached velocity
        transform.position = state.position.ToVector3();
        velocity = state.velocity.ToVector3();

        CheckCollisionWithEarth();
        CheckEscapeFromEarth();
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

    /// <summary>
    /// Compact orbital state container used by integrators and predictors.
    /// </summary>
    public struct OrbitalState
    {
        public double3 position;         // ECI position
        public double3 velocity;         // ECI velocity
        public float centralBodyMass;    // Earth mass
        public double mass;              // kg
        public double radius;            // sim units (for drag/collision)
        public double crossSectionArea;  // precomputed area for drag
        public float dragCoefficient;    // ~2.2 default
        public Vector3 force;            // accumulated external force

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


