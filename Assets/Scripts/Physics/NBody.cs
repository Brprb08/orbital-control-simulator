using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Dynamic body in the gravity sim:
/// - Integrates motion (gravity, thrust, drag) via the batch integrator
/// - Coordinates with maneuver nodes and trajectory prediction
/// - Handles collision/escape with the central body
/// </summary>
public class NBody : MonoBehaviour
{
    [Header("Celestial Body Properties")]
    public Vector3 velocity = new Vector3(0f, 0f, 20f);
    public float mass = 5.0e21f;
    public double trueMass = 5.0e21;
    public float radius = 637.8137f;
    public float cameraDistanceRadius = 637f;
    public bool isCentralBody = false;
    public OrbitalState state;

    [Header("Trajectory Prediction Settings")]
    public float predictionDeltaTime = 0.5f;

    [Header("References - Scripts")]
    private TrajectoryComputeController _tcc;
    private BodyRuntimeCoordinator _bodyRuntimeCoordinator;
    private ManeuverNodeManager _maneuverNodeManager;
    public ThrustController thrustController;
    private LineVisibilityController _lineVisibilityController;
    private RocketThrustAudio _rocketThrustAudio;
    private BodyService _bodyService;
    private AttitudeController _attitudeController;

    [Header("References - Relevant Bodies")]
    private List<NBody> _relevantBodies;

    [Header("Atmosphere & Drag")]
    [Tooltip("Sea-level density (kg/km³)")]
    public float atmosphericDensity0 = 1.225e9f;

    [Tooltip("Scale height (km)")]
    public float atmosphericScaleHeight = 8.5f;

    [Tooltip("Dimensionless drag coefficient")]
    public float dragCoefficient = 2.2f;

    [Header("Thrust State")]
    public bool isThrusting = false;

    [Header("Constants")]
    private const float EarthRotationRate = 360f / 86164f; // deg/sec, sidereal
    private const double EarthRadiusUnits = 637.8137;
    private const float MaxDistanceFromEarth = 40000f;

    [Header("Flags")]
    public bool isReferenceOrbit = false;
    public bool projectLateralPerSubstep = false;

    [Header("Telemetry")]
    public float cumulativeDeltaVUsed = 0f;

    private const float AttitudeLeadTime = 20f;

    // Caches & components
    private double[] _otherMassCache;
    private SimContext _ctx;

    /// <summary>
    /// Injects context dependencies used by integration, prediction, and UI systems.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        _ctx = ctx;

        _bodyRuntimeCoordinator = ctx.BodyRuntimeCoordinator;
        _maneuverNodeManager = ctx.ManeuverNodeManager;
        _lineVisibilityController = ctx.LineVisibilityController;
        _tcc = ctx.TrajectoryComputeController;
        thrustController = ctx.ThrustController;
        _rocketThrustAudio = ctx.RocketThrustAudio;
        _bodyService = ctx.BodyService;
    }

    private void Start()
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

        _attitudeController = GetComponent<AttitudeController>();

        // Build relevantBodies and caches once here.
        var allBodies = _bodyService != null ? _bodyService.Bodies : null;
        if (allBodies != null)
        {
            _relevantBodies = new List<NBody>();
            for (int i = 0; i < allBodies.Count; i++)
            {
                var body = allBodies[i];
                if (body == null || body == this)
                    continue;

                if (body.isCentralBody || body.name == "Moon")
                    _relevantBodies.Add(body);
            }
        }
        else
        {
            _relevantBodies = new List<NBody>();
        }

        AllocateRelevantCaches();
    }

    /// <summary>
    /// Allocates caches for relevant body data (e.g., masses).
    /// </summary>
    private void AllocateRelevantCaches()
    {
        int count = _relevantBodies != null ? _relevantBodies.Count : 0;
        _otherMassCache = count > 0 ? new double[count] : Array.Empty<double>();

        for (int i = 0; i < count; i++)
        {
            var body = _relevantBodies[i];
            _otherMassCache[i] = body != null ? body.trueMass : 0.0;
        }
    }

    /// <summary>
    /// One physics tick for this body.
    /// If <see cref="BodyService.DrivePhysics"/> is <c>true</c>, this method only
    /// updates burns/audio and leaves integration to the central batch step.
    /// If <c>false</c>, it performs the legacy per-body integration path (currently disabled).
    /// </summary>
    /// <remarks>
    /// - In service-driven mode, we do <b>not</b> zero <see cref="state.force"/> here;
    ///   the batch step consumes it and clears it afterward.
    /// - Central body still rotates here in both modes (visual spin only).
    /// </remarks>
    private void FixedUpdate()
    {
        if (_ctx == null || _ctx.BodyService == null)
            return;

        if (!_ctx.BodyService.DrivePhysics)
        {
            // Legacy per-body integration path could go here
            return;
        }

        if (HasNaNPosition())
        {
            Debug.LogError(
                $"[NBODY]: {name} has NaN transform.position! " +
                $"velocity={velocity}, force={state.force}"
            );
        }

        if (isCentralBody)
        {
            RotateCentralBody();
            return;
        }

        if (!isReferenceOrbit)
        {
            CheckForNodeBurns();
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
    private bool HasNaNPosition()
    {
        Vector3 pos = transform.position;
        return float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z);
    }

    /// <summary>
    /// Simple Earth-like rotation for the central body (visual-only).
    /// </summary>
    private void RotateCentralBody()
    {
        float deltaAngle = -EarthRotationRate * Time.deltaTime;
        transform.Rotate(Vector3.up, deltaAngle);
    }

    /// <summary>
    /// Executes finalized maneuver nodes whose burn windows overlap the current sim time.
    /// Manages thrust audio/UI and prunes completed nodes.
    /// </summary>
    private void CheckForNodeBurns()
    {
        if (isCentralBody || _maneuverNodeManager == null || _bodyRuntimeCoordinator == null)
            return;

        float simTime = _bodyRuntimeCoordinator.simulationTime;
        int currentStep = _bodyRuntimeCoordinator.simulationStep;

        bool burnInProgress = false;
        bool shouldRemoveNode = false;

        var node = _maneuverNodeManager.CurrentNode;

        if (node != null && node.targetBody == this && node.isFinalized)
        {
            // Start slewing attitude before burnTime
            if (_attitudeController != null)
            {
                bool inBurnPhase =
                    simTime >= node.burnTime - AttitudeLeadTime &&
                    currentStep < node.burnStartStep + node.burnStepCount;

                if (inBurnPhase)
                {
                    var desiredMode = MapBurnTypeToAttitude(node.burnType);
                    if (_attitudeController.mode != desiredMode)
                        _attitudeController.SetMode(desiredMode);

                    _attitudeController.lockNormalParity = true;
                }
                else
                {
                    _attitudeController.lockNormalParity = false;
                }
            }

            // Only check actual burn execution once the burn start step is reached
            if (currentStep >= node.burnStartStep)
            {
                if (IsBurnOngoing(node, currentStep))
                {
                    ExecuteNodeBurn(node, this);
                    burnInProgress = true;
                }
                else
                {
                    thrustController?.StopAllThrust();
                    shouldRemoveNode = true; // burn complete
                }
            }
        }
        else
        {
            if (_attitudeController != null)
                _attitudeController.lockNormalParity = false;
        }

        if (burnInProgress)
        {
            if (!isThrusting)
            {
                _rocketThrustAudio?.StartThrust();
                isThrusting = true;
            }
        }
        else
        {
            if (isThrusting)
            {
                _rocketThrustAudio?.StopThrust();
                thrustController?.StopAllThrust();
                isThrusting = false;
            }
        }

        if (shouldRemoveNode)
            _maneuverNodeManager.RemoveNode(node);
    }

    /// <summary>
    /// Runtime burn execution is step-based, not float-time-based.
    /// </summary>
    private bool IsBurnOngoing(ManeuverNode node, int currentStep)
    {
        return currentStep >= node.burnStartStep &&
               currentStep < node.burnStartStep + node.burnStepCount;
    }

    /// <summary>
    /// Applies thrust per node burn type/direction for this frame.
    /// </summary>
    private void ExecuteNodeBurn(ManeuverNode node, NBody body)
    {
        if (thrustController == null || _maneuverNodeManager == null)
            return;

        thrustController.StartNodeBurn(node);
    }

    /// <summary>
    /// Collision with central body → delegate removal to the coordinator.
    /// </summary>
    private void CheckCollisionWithEarth()
    {
        if (_bodyService == null)
            return;

        NBody earth = _bodyService.CentralBody;
        if (earth == null || earth == this)
            return;

        float distance = Vector3.Distance(transform.position, earth.transform.position);
        float collisionThreshold = cameraDistanceRadius + earth.radius;

        if (distance < collisionThreshold)
        {
            Debug.Log($"[NBODY]: [COLLISION] {name} collided with Earth");
            _bodyRuntimeCoordinator?.HandleCollision(this, earth);
        }
    }

    /// <summary>
    /// Exceeded sim boundary → treat as escape and delegate removal.
    /// </summary>
    private void CheckEscapeFromEarth()
    {
        if (_bodyService == null)
            return;

        NBody earth = _bodyService.CentralBody;
        if (earth == null || earth == this)
            return;

        float distance = Vector3.Distance(transform.position, earth.transform.position);
        if (distance > MaxDistanceFromEarth)
        {
            Debug.Log(
                $"[NBODY]: [ESCAPE] {name} exceeded {MaxDistanceFromEarth * 10f:N0} km and is removed."
            );

            _bodyRuntimeCoordinator?.HandleCollision(this, earth);
        }
    }

    /// <summary>
    /// Clean up line-visibility registration for this body.
    /// </summary>
    private void OnDestroy()
    {
        if (_lineVisibilityController != null)
        {
            _lineVisibilityController.DeregisterNBody(this);
        }
    }

    /// <summary>
    /// Asynchronously samples a forward trajectory (GPU) from a given state with external bodies.
    /// </summary>
    /// <param name="steps">Number of integration steps.</param>
    /// <param name="deltaTime">Step size.</param>
    /// <param name="onComplete">Callback with sampled positions.</param>
    /// <param name="overrideStartPosition">Optional start position override.</param>
    /// <param name="overrideStartVelocity">Optional start velocity override.</param>
    public void CalculatePredictedTrajectoryGPU_Async(
        int steps,
        float deltaTime,
        Action<List<Vector3>> onComplete,
        Vector3? overrideStartPosition = null,
        Vector3? overrideStartVelocity = null
    )
    {
        if (_relevantBodies == null || _relevantBodies.Count == 0)
            return;

        Vector3[] otherPositions = _relevantBodies.Select(b => b.transform.position).ToArray();
        float[] otherMasses = _relevantBodies.Select(b => (float)b.mass).ToArray();

        if (_tcc == null)
        {
            Debug.LogError(
                "[NBODY]: TrajectoryComputeController (_tcc) is null. " +
                "Ensure it is assigned before calling this method."
            );
            onComplete?.Invoke(null);
            return;
        }

        _tcc.CalculateTrajectoryGPU_Async(
            startPos: overrideStartPosition ?? state.position.ToVector3(),
            startVel: overrideStartVelocity ?? state.velocity.ToVector3(),
            bodyMass: mass,
            otherBodyPositions: otherPositions,
            otherBodyMasses: otherMasses,
            dt: deltaTime,
            steps: steps,
            onComplete: positionsArray =>
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

    private AttitudeController.PointingMode MapBurnTypeToAttitude(BurnType burnType)
    {
        switch (burnType)
        {
            case BurnType.Prograde:
                return AttitudeController.PointingMode.Velocity;

            case BurnType.Retrograde:
                return AttitudeController.PointingMode.Retrograde;

            case BurnType.RadialIn:
                return AttitudeController.PointingMode.Nadir;

            case BurnType.RadialOut:
                return AttitudeController.PointingMode.Zenith;

            case BurnType.Normal:
                return AttitudeController.PointingMode.Normal;

            case BurnType.AntiNormal:
                return AttitudeController.PointingMode.AntiNormal;

            default:
                return AttitudeController.PointingMode.Velocity;
        }
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
            double rUnits = math.length(state.position);
            return rUnits - EarthRadiusUnits;
        }
    }

    /// <summary>
    /// Lightweight orbital state used by the batch integrator.
    /// </summary>
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
            Vector3 force
        )
        {
            this.position = position;
            this.velocity = velocity;
            this.centralBodyMass = centralBodyMass > 0f ? centralBodyMass : 5.972e24f;
            this.mass = mass;
            this.radius = radius;
            this.dragCoefficient = dragCoefficient;
            this.force = force;
            crossSectionArea = Math.PI * radius * radius;
        }
    }


    public void ComputePredictionForNodes(
    int steps,
    float dt,
    System.Action<List<Vector3>, float, float> onComplete)
    {
        float startTime = _bodyRuntimeCoordinator != null
            ? _bodyRuntimeCoordinator.simulationTime
            : 0f;

        // This must mirror TrajectoryComputeController.CalculateTrajectoryGPU_Async.
        const int maxPoints = 2500;
        int lodFactor = Mathf.Max(1, steps / maxPoints);
        float sampleDt = dt * lodFactor;

        CalculatePredictedTrajectoryGPU_Async(
            steps,
            dt,
            positions =>
            {
                // positions.Length == outputCount (≈ steps / lodFactor)
                onComplete?.Invoke(
                    positions ?? new List<Vector3>(),
                    startTime,
                    sampleDt
                );
            });
    }

}
