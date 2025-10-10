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
    public float maxReverseThrustMagnitude = 10f;
    public float maxLateralThrustMagnitude = 10f;
    public float maxRadialThrustMagnitude = 10f;
    // public float thrustRampUpTime = 2f;

    [Header("Visual Feedback")]
    public ParticleSystem thrustParticles;

    [Header("Thrust Flags")]
    public bool isForwardThrustActive = false;
    public bool isReverseThrustActive = false;
    public bool isLeftThrustActive = false;
    public bool isRightThrustActive = false;
    public bool isRadialInThrustActive = false;
    public bool isRadialOutThrustActive = false;

    [Header("References - Scripts")]
    public CameraController cameraController;
    public CameraMovement cameraMovement;
    public TrajectoryRenderer trajectoryRenderer;
    public BodyRuntimeCoordinator bodyRuntimeCoordinator;
    private TutorialController tutorialController;

    [Header("Thrust Configs")]
    private bool thrustStopped = false;

    private SimContext ctx;

    /// <summary>
    /// True if any thrust flag is active.
    /// </summary>
    public bool IsThrusting =>
        isForwardThrustActive
        || isReverseThrustActive
        || isLeftThrustActive
        || isRightThrustActive
        || isRadialInThrustActive
        || isRadialOutThrustActive;

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

    /// <summary>
    /// Applies thrust each physics tick based on active flags and current orbital basis.
    /// </summary>
    void FixedUpdate()
    {
        if (cameraMovement == null) return;

        NBody ship = cameraMovement.targetBody;
        if (ship == null) return;

        // Orbital basis (radial, prograde, normal) in the same inertial frame
        Vector3 r = ship.transform.position - Vector3.zero;
        Vector3 v = ship.velocity - Vector3.zero;

        Vector3 rHat = r.normalized;                         // radial-out
        Vector3 vHat = v.normalized;                         // prograde
        Vector3 nHat = Vector3.Cross(rHat, vHat).normalized; // orbit normal

        bool isThrusting = false;

        if (isForwardThrustActive)
        {
            ApplyThrust(ship, maxForwardThrustMagnitude, vHat);
            isThrusting = true;
        }
        else if (isReverseThrustActive)
        {
            ApplyThrust(ship, maxReverseThrustMagnitude, -vHat);
            isThrusting = true;
        }
        else if (isRightThrustActive)   // Normal
        {
            ApplyThrust(ship, maxLateralThrustMagnitude, nHat);
            isThrusting = true;
        }
        else if (isLeftThrustActive)    // Anti-Normal
        {
            ApplyThrust(ship, maxLateralThrustMagnitude, -nHat);
            isThrusting = true;
        }
        else if (isRadialInThrustActive)
        {
            ApplyThrust(ship, maxRadialThrustMagnitude, -rHat);
            isThrusting = true;
        }
        else if (isRadialOutThrustActive)
        {
            ApplyThrust(ship, maxRadialThrustMagnitude, rHat);
            isThrusting = true;
        }

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

    /// <summary>
    /// Positions/orients the thrust particle system and plays it when thrust starts.
    /// </summary>
    private void UpdateThrustParticleSystem(NBody targetBody, Vector3 thrustDirection)
    {
        if (thrustParticles == null)
        {
            Debug.LogError("ThrustController: thrustParticles is null! Ensure the particle system is assigned.");
            return;
        }
        if (!thrustParticles) return;

        thrustParticles.transform.position = targetBody.transform.position;
        thrustParticles.transform.rotation = Quaternion.LookRotation(-thrustDirection, targetBody.transform.up);

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
        isReverseThrustActive = false;
        isLeftThrustActive = false;
        isRightThrustActive = false;
        isRadialInThrustActive = false;
        isRadialOutThrustActive = false;

        switch (burnDirection)
        {
            case "Prograde":
                isForwardThrustActive = true; break;
            case "Retrograde":
                isReverseThrustActive = true; break;
            case "Radial In":
                isRadialInThrustActive = true; break;
            case "Radial Out":
                isRadialOutThrustActive = true; break;
            case "Normal":
                isRightThrustActive = true; break;
            case "Anti-Normal":
                isLeftThrustActive = true; break;
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
        isReverseThrustActive = false;
        isLeftThrustActive = false;
        isRightThrustActive = false;
        isRadialInThrustActive = false;
        isRadialOutThrustActive = false;
    }

    // UI Button Handlers
    public void StartForwardThrust() => isForwardThrustActive = true;
    public void StopForwardThrust()
    {
        isForwardThrustActive = false;
        EventSystem.current.SetSelectedGameObject(null);
    }

    public void StartReverseThrust() => isReverseThrustActive = true;
    public void StopReverseThrust()
    {
        isReverseThrustActive = false;
        EventSystem.current.SetSelectedGameObject(null);
    }

    public void StartLeftThrust() => isLeftThrustActive = true;
    public void StopLeftThrust()
    {
        isLeftThrustActive = false;
        EventSystem.current.SetSelectedGameObject(null);
    }

    public void StartRightThrust() => isRightThrustActive = true;
    public void StopRightThrust()
    {
        isRightThrustActive = false;
        EventSystem.current.SetSelectedGameObject(null);
    }

    public void StartRadialInThrust() => isRadialInThrustActive = true;
    public void StopRadialInThrust()
    {
        isRadialInThrustActive = false;
        EventSystem.current.SetSelectedGameObject(null);
    }

    public void StartRadialOutThrust() => isRadialOutThrustActive = true;
    public void StopRadialOutThrust()
    {
        isRadialOutThrustActive = false;
        EventSystem.current.SetSelectedGameObject(null);
    }
}
