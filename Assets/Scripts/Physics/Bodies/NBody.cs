using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

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
    // Keep the existing serialized trueMass value as dry mass.
    // The float alias preserves callers without a second editable Inspector value.
    public float mass { get => (float)trueMass; set => trueMass = value; }
    [FormerlySerializedAs("trueMass"), SerializeField]
    [InspectorName("Dry Mass (kg)")]
    [Tooltip("Starting dry mass. Set before Play; during Play use the engine panel to keep physics and maneuver validation synchronized.")]
    private double massKilograms = 5.0e21;
    [SerializeField, InspectorName("Fuel Mass (kg)")]
    [Tooltip("Starting fuel mass, added to dry mass for physics. Consumed only in Finite mode. Edit through the engine panel during Play.")]
    private double fuelMassKg;
    public double trueMass
    {
        get => massKilograms;
        set
        {
            if (!TrySetMassKilograms(value))
                throw new ArgumentException("Mass must be finite, positive, and editable (no active burn or finalized node).", nameof(value));
        }
    }
    public float radius = (float)PhysicsConstants.EarthRadiusUnits;
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
    private BodyService _bodyService;
    private AttitudeController _attitudeController;

    [Header("References - Relevant Bodies")]
    private List<NBody> _relevantBodies;

    [Header("Atmosphere & Drag")]
    // Legacy serialized field. Density belongs to the native reference atmosphere;
    // this value is retained only for scene/source compatibility, not as a control.
    [HideInInspector]
    public float atmosphericDensity0 = 1.225e9f;

    [Tooltip("Dimensionless drag coefficient")]
    public float dragCoefficient = 2.2f;

    [SerializeField, Min(0f), InspectorName("Drag Area Override (m²)")]
    [Tooltip("Effective projected drag area, independent of radius. 0 = automatic: 2 m² at 500 kg dry mass, scaled by (dry mass / 500)^(2/3). Set before Play; fuel consumption does not change area.")]
    private float dragAreaSquareMeters;

    public double DragAreaSquareMeters => float.IsFinite(dragAreaSquareMeters) && dragAreaSquareMeters > 0f
        ? dragAreaSquareMeters
        : DefaultDragAreaSquareMeters(DryMassKilograms);

    public double DragAreaSquareUnits => DragAreaSquareMeters /
        ((double)SimulationUnits.MetersPerUnit * SimulationUnits.MetersPerUnit);

    // A simulator default for geometrically similar spacecraft, not a measured spacecraft area.
    // Use dry mass so carrying or consuming propellant does not alter geometry.
    public static double DefaultDragAreaSquareMeters(double dryMassKg)
        => double.IsFinite(dryMassKg) && dryMassKg > 0.0
            ? 2.0 * Math.Pow(dryMassKg / 500.0, 2.0 / 3.0)
            : 2.0;

    public bool TrySetDragAreaSquareMeters(float areaOrZeroForAutomatic)
    {
        if (isCentralBody || !CanEditFlightConfiguration ||
            !float.IsFinite(areaOrZeroForAutomatic) || areaOrZeroForAutomatic < 0f)
            return false;
        if (dragAreaSquareMeters == areaOrZeroForAutomatic) return true;
        dragAreaSquareMeters = areaOrZeroForAutomatic;
        FlightConfigurationChanged?.Invoke(this);
        return true;
    }

    [Header("Thrust State")]
    public bool isThrusting = false;

    // Serialized per-body data; changes go through the validated API below.
    // Unlimited mode keeps mass constant. Finite mode consumes fuel in the shared native integrator.
    [SerializeField, HideInInspector] private bool propulsionInitialized;
    [SerializeField, HideInInspector] private float engineThrustNewtons = 10000f;
    [SerializeField, HideInInspector] private float specificImpulseSeconds = 300f;
    [SerializeField, HideInInspector] private float thrustScale = 1f;

    public float EngineThrustNewtons => engineThrustNewtons;
    public float SpecificImpulseSeconds => specificImpulseSeconds;
    public float ThrustScale => thrustScale;
    public float EffectiveThrustNewtons => engineThrustNewtons * thrustScale;
    [SerializeField, HideInInspector] private bool finitePropellant;
    public bool UnlimitedPropellant => !finitePropellant;
    public bool HasUsableThrust => EffectiveThrustNewtons > 0f && (UnlimitedPropellant || fuelMassKg > 0.0);
    public const double StandardGravityMetersPerSecondSquared = 9.80665;
    public double FuelFlowKilogramsPerSecond => EffectiveThrustNewtons / (specificImpulseSeconds * StandardGravityMetersPerSecondSquared);
    public double AvailableDeltaVMetersPerSecond => UnlimitedPropellant ? double.PositiveInfinity :
        specificImpulseSeconds * StandardGravityMetersPerSecondSquared * Math.Log(1.0 + fuelMassKg / massKilograms);
    public double MassKilograms => massKilograms;
    public double DryMassKilograms => massKilograms;
    public double FuelMassKilograms => fuelMassKg;
    public double TotalMassKilograms => massKilograms + fuelMassKg;
    public double FullThrustAccelerationMetersPerSecondSquared => EffectiveThrustNewtons / TotalMassKilograms;
    public double DeliveredDeltaVMetersPerSecond => cumulativeDeltaVUsed * SimulationUnits.MetersPerKilometer;
    public event Action<NBody> FlightConfigurationChanged;

    public bool CanEditFlightConfiguration
    {
        get
        {
            var node = _ctx?.ManeuverNodeManager != null ? _ctx.ManeuverNodeManager.CurrentNode : null;
            return !isThrusting && !(thrustController != null && thrustController.IsThrusting &&
                _ctx?.CameraTracker?.CurrentBody == this) &&
                !(node != null && node.isFinalized && node.targetBody == this);
        }
    }

    public bool TrySetMassKilograms(double kilograms)
        => TrySetMassesKilograms(kilograms, fuelMassKg);

    public bool TrySetFuelMassKilograms(double kilograms)
        => TrySetMassesKilograms(massKilograms, kilograms);

    public bool TrySetUnlimitedPropellant(bool unlimited)
    {
        if (!CanEditFlightConfiguration || isCentralBody) return false;
        if (finitePropellant == !unlimited) return true;
        finitePropellant = !unlimited;
        FlightConfigurationChanged?.Invoke(this);
        return true;
    }

    // Physics writes consumption without firing configuration-edit events every substep.
    internal void CommitFuelFromPhysics(double remainingKg)
    {
        if (UnlimitedPropellant) return;
        if (!double.IsFinite(remainingKg) || remainingKg < 0.0 || remainingKg > fuelMassKg)
            throw new ArgumentOutOfRangeException(nameof(remainingKg));
        fuelMassKg = remainingKg;
        state.mass = TotalMassKilograms;
    }

    // Native integration excludes masses <= 1e-6 kg; prediction buffers also use floats.
    public static bool IsValidMassComposition(double dryKg, double fuelKg)
        => double.IsFinite(dryKg) && dryKg > 1e-6 && double.IsFinite(fuelKg) && fuelKg >= 0.0 &&
           double.IsFinite(dryKg + fuelKg) && dryKg + fuelKg <= float.MaxValue;

    public bool TrySetMassesKilograms(double dryKg, double fuelKg)
    {
        if (!IsValidMassComposition(dryKg, fuelKg) || !CanEditFlightConfiguration)
            return false;
        bool changed = massKilograms != dryKg || fuelMassKg != fuelKg;
        massKilograms = dryKg;
        fuelMassKg = fuelKg;
        state.mass = TotalMassKilograms;
        if (changed) FlightConfigurationChanged?.Invoke(this);
        return true;
    }

    public bool TryConfigurePropulsion(float thrustNewtons, float ispSeconds, float scale = 1f)
    {
        if (isCentralBody || !CanEditFlightConfiguration ||
            !float.IsFinite(thrustNewtons) || thrustNewtons < 0f ||
            !float.IsFinite(ispSeconds) || ispSeconds <= 0f ||
            !float.IsFinite(scale) || scale < 0f || !float.IsFinite(thrustNewtons * scale))
            return false;
        bool changed = engineThrustNewtons != thrustNewtons || specificImpulseSeconds != ispSeconds || thrustScale != scale;
        engineThrustNewtons = thrustNewtons;
        specificImpulseSeconds = ispSeconds;
        thrustScale = scale;
        propulsionInitialized = true;
        if (changed) FlightConfigurationChanged?.Invoke(this);
        return true;
    }

    [Header("Constants")]
    private const double EarthRadiusUnits = PhysicsConstants.EarthRadiusUnits;

    [Header("Flags")]
    public bool isReferenceOrbit = false;

    [Header("Render Smoothing")]
    [SerializeField] private bool interpolateRenderedPosition = true;

    [Header("Telemetry")]
    [Tooltip("Accumulated thrust delta-v in km/s; excludes gravity and drag.")]
    public double cumulativeDeltaVUsed = 0.0;

    // Caches & components
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
        _bodyService = ctx.BodyService;

        // Migrate the old scene-wide settings once; later edits belong to this body.
        if (!propulsionInitialized && !isCentralBody)
        {
            float defaultThrust = thrustController != null ? thrustController.LegacyDefaultThrustNewtons : 10000f;
            float defaultScale = ctx.ManeuverNodeManager != null
                ? ctx.ManeuverNodeManager.LegacyDefaultThrustScale
                : (thrustController != null ? thrustController.LegacyDefaultThrustScale : 1f);
            TryConfigurePropulsion(defaultThrust, specificImpulseSeconds, defaultScale);
        }
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
            TotalMassKilograms,
            radius,
            dragCoefficient,
            Vector3.zero
        );

        _attitudeController = GetComponent<AttitudeController>();

        // Build the list of attractors used by GPU prediction.
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

        // Interpolation is visually important for the vehicle under the camera, but
        // dispatching one LateUpdate and transform write per constellation member is
        // not. Distant satellites remain on their fixed-step transform instead.
        if (_ctx.CameraTracker != null && _ctx.CameraTracker.CurrentBody != this)
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
        thrustController?.StopThrustForBody(this);

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
        Vector3? overrideStartVelocity = null,
        string coalesceKey = "TrackedOrbitPrediction"
    )
    {
        _relevantBodies?.RemoveAll(body => body == null);
        if (_relevantBodies == null || _relevantBodies.Count == 0)
        {
            onComplete?.Invoke(Array.Empty<Vector3>());
            return;
        }

        int relevantBodyCount = _relevantBodies.Count;
        Vector3[] otherPositions = new Vector3[relevantBodyCount];
        float[] otherMasses = new float[relevantBodyCount];
        for (int i = 0; i < relevantBodyCount; i++)
        {
            NBody relevantBody = _relevantBodies[i];
            otherPositions[i] = relevantBody.transform.position;
            otherMasses[i] = (float)relevantBody.TotalMassKilograms;
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
            coalesceKey: coalesceKey,
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
                },
            coalesceKey: "ManeuverNodeSnapshot");
    }

}
