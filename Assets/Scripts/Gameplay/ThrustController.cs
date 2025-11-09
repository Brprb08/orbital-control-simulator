using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Applies directional thrust to the tracked craft and keeps visual thrust effects in sync.
/// Integrates with UI inputs, tutorial flags, and trajectory updates.
/// </summary>
public class ThrustController : MonoBehaviour
{
    [Header("Thrust Settings")]
    public float maxForwardThrustMagnitude = 10f;

    [Header("Visual Feedback")]
    public ParticleSystem thrustParticles;

    [Header("Thrust Flags")]
    public bool isForwardThrustActive = false;

    [Header("References - Scripts")]
    public CameraController cameraController;
    public CameraMovement cameraMovement;
    public TrajectoryRenderer trajectoryRenderer;
    public BodyRuntimeCoordinator bodyRuntimeCoordinator;
    private TutorialController tutorialController;

    private AttitudeController attitude;

    [Header("Thrust Parity Sync")]
    [SerializeField] private int thrustGraceFrames = 6; // ~0.1s @60fps
    private int _graceCounter = 0;
    private bool _prevThrusting = false;
    private sbyte _latchedParity = 0;

    [Header("Thrust Configs")]
    private bool thrustStopped = false;

    private SimContext ctx;

    /// <summary>
    /// True if any thrust flag is active.
    /// </summary>
    public bool IsThrusting =>
        isForwardThrustActive;

    /// <summary>
    /// Injects context, resolves dependencies, and initializes thrust VFX.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.bodyRuntimeCoordinator = ctx.BodyRuntimeCoordinator;
        this.cameraController = ctx.CameraController;
        this.cameraMovement = ctx.CameraMovement;
        this.trajectoryRenderer = ctx.TrajectoryRenderer;
        this.tutorialController = ctx.TutorialController;

        if (thrustParticles == null)
        {
            thrustParticles = GameObject.Find("Particle System").GetComponent<ParticleSystem>();

            if (thrustParticles == null)
            {
                Debug.LogError("ThrustController: No Particle System found in the scene!");
            }
        }

        var main = thrustParticles.main;
        main.useUnscaledTime = true;

        thrustParticles.Stop();
        thrustParticles.Clear();

    }

    void FixedUpdate()
    {
        if (cameraMovement == null) return;

        NBody ship = cameraMovement.targetBody;
        if (ship == null) return;

        if (!attitude) attitude = ship.GetComponent<AttitudeController>();

        Transform t = ship.transform;
        Vector3 fwd = t.forward;

        // Central body
        Vector3 center = Vector3.zero;
        var svc = ctx?.BodyService;
        if (svc != null && svc.CentralBody)
            center = svc.CentralBody.transform.position;

        // Local orbital state
        Vector3 r = ship.transform.position - center;
        Vector3 v = ship.velocity;

        // Safe norms
        Vector3 rHat = (r.sqrMagnitude > 1e-12f) ? r.normalized : Vector3.up;
        Vector3 vHat = (v.sqrMagnitude > 1e-12f) ? v.normalized : Vector3.right;

        // Orbit angular momentum
        Vector3 h = Vector3.Cross(r, v);
        // Live parity from current side of 90°: +1 if h.y < 0, else -1 (to match your AttitudeController)
        sbyte liveParity = (h.y < 0f) ? (sbyte)+1 : (sbyte)-1;

        bool isThrustingNow = false;
        bool lateralActive = false;

        // --- Thrust modes (yours only has forward right now) ---
        if (isForwardThrustActive)
        {
            ApplyThrust(ship, maxForwardThrustMagnitude, fwd);
            isThrustingNow = true;
        }

        // NEW: parity latch with grace window
        if (isThrustingNow && !_prevThrusting)
        {
            // rising edge: capture current side (+1 / -1)
            _latchedParity = liveParity;
            _graceCounter = thrustGraceFrames; // start/refresh grace
        }

        if (isThrustingNow)
        {
            // while thrusting, keep extending the grace
            _graceCounter = thrustGraceFrames;
        }
        else if (_prevThrusting)
        {
            // falling edge: don't clear latch immediately; grace handles it
            // (no-op here; we decrement below)
        }

        // tick grace once per frame
        if (_graceCounter > 0) _graceCounter--;

        // Treat "within grace" as thrusting for attitude purposes
        bool thrustingForAttitude = isThrustingNow || (_graceCounter > 0);

        // If we’re within grace, send the latched parity; otherwise send 0 (no latch)
        sbyte parityForAttitude = thrustingForAttitude ? _latchedParity : (sbyte)0;

        // >>> This is the key line: keep AttitudeController from flipping during a burn <<<
        if (attitude)
            attitude.SyncThrustParity(thrustingForAttitude, parityForAttitude);

        _prevThrusting = isThrustingNow;

        // --- Your existing integrator + VFX cleanup ---
        bool holdCurrent = attitude != null &&
                           attitude.mode == AttitudeController.PointingMode.HoldCurrent;

        ship.projectLateralPerSubstep = lateralActive && !holdCurrent;

        if (!isThrustingNow)
        {
            thrustParticles.Stop();
            thrustStopped = true;
        }
    }

    /// <summary>
    /// Applies a world-space thrust force to the target body and updates visuals/trajectory.
    /// </summary>
    public void ApplyThrust(NBody targetBody, float magnitude, Vector3 thrustDirection, float rampedThrustFactor = 1f)
    {
        if (targetBody == null) return;

        Vector3 adjustedThrustDirection = thrustDirection.normalized;
        if (float.IsNaN(adjustedThrustDirection.x) || adjustedThrustDirection == Vector3.zero)
        {
            Debug.LogWarning($"[ThrustController] Invalid thrust direction: {thrustDirection}");
            return;
        }

        // Scale (world is 1 unit = 10 km)
        float scaledMagnitude = magnitude / 10f;

        Vector3 F = adjustedThrustDirection * scaledMagnitude;

        // Optional diagnostics in orbital basis
        Vector3 r = targetBody.transform.position - Vector3.zero;
        Vector3 v = targetBody.velocity - Vector3.zero;

        Vector3 rHat = r.normalized;
        Vector3 vHat = v.normalized;
        Vector3 nHat = Vector3.Cross(rHat, vHat).normalized;

        float Fr = Vector3.Dot(F, rHat);
        float Fp = Vector3.Dot(F, vHat);
        float Fn = Vector3.Dot(F, nHat);
        float cos_n_v = Vector3.Dot(nHat, vHat); // ~0 for a clean basis
        float power = Vector3.Dot(v, F);         // ≈ 0 for pure normal

        targetBody.AddForce(F);

        UpdateThrustParticleSystem(targetBody, adjustedThrustDirection);
        trajectoryRenderer.orbitIsDirty = true;

        if (tutorialController.inTutorialMode)
        {
            tutorialController.hasAppliedThrust = true;
        }
    }

    [SerializeField] float backOffset = 0.6f;
    /// <summary>
    /// Positions/orients the thrust particle system and plays it when thrust starts.
    /// </summary>
    private void UpdateThrustParticleSystem(NBody targetBody, Vector3 thrustDirection)
    {
        if (!thrustParticles) return;

        // Build rotation first
        var rot = Quaternion.LookRotation(-thrustDirection.normalized, targetBody.transform.up);

        // Offset "back" relative to the particle system's forward (which is -thrustDirection)
        // rot * (Vector3.forward) == +thrustDirection (toward the craft)
        Vector3 pos = targetBody.transform.position + rot * Vector3.forward * backOffset;

        thrustParticles.transform.SetPositionAndRotation(pos, rot);

        if (!thrustParticles.isPlaying || thrustStopped)
        {
            thrustParticles.Clear();
            thrustParticles.Play();
            thrustStopped = false;
        }
    }

    /// <summary>
    /// Activates a single thrust mode by name; used by node-driven burns.
    /// </summary>
    public void SetDirectionalThrust(string burnDirection)
    {
        // Reset all
        isForwardThrustActive = false;


        switch (burnDirection)
        {
            case "Prograde":
                isForwardThrustActive = true; break;
            default:
                isForwardThrustActive = true;
                Debug.LogWarning($"Unknown burn direction: {burnDirection}. Defaulting to Prograde.");
                break;
        }
    }

    /// <summary>Clears all thrust flags.</summary>
    public void StopAllThrust()
    {
        isForwardThrustActive = false;
    }

    // UI Button Handlers
    public void StartForwardThrust()
    {
        isForwardThrustActive = true;
    }
    public void StopForwardThrust()
    {
        isForwardThrustActive = false;
        EventSystem.current.SetSelectedGameObject(null);
    }
}
