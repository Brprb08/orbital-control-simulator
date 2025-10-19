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

    bool isFireHeld;

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

        // Ensure we have the craft's attitude controller
        if (!attitude) attitude = ship.GetComponent<AttitudeController>();

        // Body axes in world space
        Transform t = ship.transform;
        Vector3 fwd = t.forward;   // +Z in Unity
        Vector3 right = t.right;   // +X
        Vector3 up = t.up;         // +Y

        // Central body position
        Vector3 center = Vector3.zero;
        var svc = ctx?.BodyService;
        if (svc != null && svc.CentralBody)
            center = svc.CentralBody.transform.position;

        Vector3 r = ship.transform.position - center;
        Vector3 v = ship.velocity;

        Vector3 rHat = r.normalized;
        Vector3 vHat = v.normalized;

        // nominal orbit normal
        Vector3 nHat = Vector3.Cross(rHat, vHat);
        if (nHat.sqrMagnitude < 1e-12f)
        {
            // fallback normal using a stable world reference
            Vector3 refUp = (Mathf.Abs(Vector3.Dot(rHat, Vector3.up)) < 0.9f) ? Vector3.up : Vector3.forward;
            nHat = Vector3.Cross(rHat, refUp);
        }
        nHat.Normalize();


        bool isThrusting = false;
        bool lateralActive = false;

        // --- Thrust modes ---
        if (isForwardThrustActive)
        {
            ApplyThrust(ship, maxForwardThrustMagnitude, fwd);
            isThrusting = true;
        }

        // --- Control integrator behavior ---
        bool holdCurrent = attitude != null &&
                           attitude.mode == AttitudeController.PointingMode.HoldCurrent;

        // Only enable per-substep re-projection if we’re actually doing a lateral burn
        // AND not in HoldCurrent mode.
        ship.projectLateralPerSubstep = lateralActive && !holdCurrent;

        // --- Visuals / cleanup ---
        if (!isThrusting)
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
        isFireHeld = true;
    }
    public void StopForwardThrust()
    {
        isForwardThrustActive = false;
        isFireHeld = false;
        EventSystem.current.SetSelectedGameObject(null);
    }
}
