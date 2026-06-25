using System;
using System.Collections.Generic;
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
    private const double EarthRadiusUnits = 637.8137;

    [Header("Flags")]
    public bool isReferenceOrbit = false;
    public bool projectLateralPerSubstep = false;

    [Header("Render Smoothing")]
    [SerializeField] private bool interpolateRenderedPosition = true;

    [Header("Telemetry")]
    public float cumulativeDeltaVUsed = 0f;

    // Caches & components
    private double[] _otherMassCache;
    private SimContext _ctx;
    private double3 _previousPhysicsPosition;
    private double3 _currentPhysicsPosition;
    private bool _hasPhysicsInterpolationState;

    public Vector3 RenderPosition => GetRenderPosition();

    /// <summary>
    /// Injects context dependencies used by integration, prediction, and UI systems.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        _ctx = ctx;

        _bodyRuntimeCoordinator = ctx.BodyRuntimeCoordinator;
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
    /// Applies state produced by the batch integrator to the Unity <see cref="Transform"/>,
    /// updates render interpolation state, and clears consumed force.
    /// Call this once per body after the manager’s native batch step completes.
    /// </summary>
    public void SyncAfterBatch()
    {
        SyncAfterBatch(state.position);
    }

    public void SyncAfterBatch(double3 previousPosition)
    {
        _previousPhysicsPosition = previousPosition;
        _currentPhysicsPosition = state.position;
        _hasPhysicsInterpolationState = true;

        transform.position = state.position.ToVector3();
        velocity = state.velocity.ToVector3();

        state.force = Vector3.zero;
    }

    private void LateUpdate()
    {
        if (!ShouldInterpolateRenderedPosition())
            return;

        transform.position = GetInterpolatedPhysicsPosition();
    }

    private bool ShouldInterpolateRenderedPosition()
    {
        if (!interpolateRenderedPosition)
            return false;

        if (isCentralBody || isReferenceOrbit)
            return false;

        if (!_hasPhysicsInterpolationState)
            return false;

        if (_ctx == null || _ctx.BodyService == null || !_ctx.BodyService.DrivePhysics)
            return false;

        return Application.isPlaying;
    }

    private Vector3 GetRenderPosition()
    {
        if (!ShouldInterpolateRenderedPosition())
            return transform.position;

        return GetInterpolatedPhysicsPosition();
    }

    private Vector3 GetInterpolatedPhysicsPosition()
    {
        float alpha = Time.fixedDeltaTime > 1e-6f
            ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime)
            : 1f;

        double3 position = math.lerp(_previousPhysicsPosition, _currentPhysicsPosition, alpha);
        return position.ToVector3();
    }

    public void ForceStopBurnEffects()
    {
        _rocketThrustAudio?.StopThrust();
        thrustController?.StopAllThrust();

        if (_attitudeController != null)
            _attitudeController.lockNormalParity = false;

        isThrusting = false;
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
        Action<Vector3[]> onComplete,
        Vector3? overrideStartPosition = null,
        Vector3? overrideStartVelocity = null
    )
    {
        if (_relevantBodies == null || _relevantBodies.Count == 0)
            return;

        int relevantBodyCount = _relevantBodies.Count;
        Vector3[] otherPositions = new Vector3[relevantBodyCount];
        float[] otherMasses = new float[relevantBodyCount];
        for (int i = 0; i < relevantBodyCount; i++)
        {
            NBody relevantBody = _relevantBodies[i];
            otherPositions[i] = relevantBody.transform.position;
            otherMasses[i] = (float)relevantBody.trueMass;
        }

        if (_tcc == null)
        {
            Debug.LogError(
                "[NBODY]: TrajectoryComputeController (_tcc) is null. " +
                "Ensure it is assigned before calling this method."
            );
            onComplete?.Invoke(Array.Empty<Vector3>());
            return;
        }

        _tcc.CalculateTrajectoryGPU_Async(
            startPos: overrideStartPosition ?? state.position.ToVector3(),
            startVel: overrideStartVelocity ?? state.velocity.ToVector3(),
            bodyMass: (float)state.mass,
            otherBodyPositions: otherPositions,
            otherBodyMasses: otherMasses,
            dt: deltaTime,
            steps: steps,
            onComplete: positionsArray =>
            {
                if (positionsArray == null)
                    onComplete?.Invoke(Array.Empty<Vector3>());
                else
                    onComplete?.Invoke(positionsArray);
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
            positionsArray =>
            {
                // positions.Length == outputCount (≈ steps / lodFactor)
                onComplete?.Invoke(
                    positionsArray != null ? new List<Vector3>(positionsArray) : new List<Vector3>(),
                    startTime,
                    sampleDt
                );
            });
    }

}
