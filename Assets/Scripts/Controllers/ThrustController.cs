using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.EventSystems;

/// <summary>
/// Manages the thrust system for a spacecraft or object, allowing for various directional thrusts 
/// including forward, reverse, lateral, and radial directions. It provides visual feedback through
/// particle systems and manages thrust force to the NBody object.
/// 
/// Also handles UI button input to toggle thrust activation and tracks the duration 
/// for any thrust that is applied.
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
    public GravityManager gravityManager;
    private TutorialController tutorialController;

    [Header("Thrust Configs")]
    private bool thrustStopped = false;

    private SimContext ctx;

    /// <summary>
    /// Returns true if any thrust is currently active.
    /// </summary>
    public bool IsThrusting
    {
        get
        {
            return isForwardThrustActive
                || isReverseThrustActive
                || isLeftThrustActive
                || isRightThrustActive
                || isRadialInThrustActive
                || isRadialOutThrustActive;
        }
    }

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.gravityManager = ctx.GravityManager;
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

        // Correct orbital basis (all in SAME inertial frame)
        Vector3 r = ship.transform.position - Vector3.zero;
        Vector3 v = ship.velocity - Vector3.zero;

        Vector3 rHat = r.normalized;                             // radial-out
        Vector3 vHat = v.normalized;                             // (prograde)
        Vector3 nHat = Vector3.Cross(rHat, vHat).normalized;     // Normal (orbit normal)

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

    public void ApplyThrust(NBody targetBody, float magnitude, Vector3 thrustDirection, float rampedThrustFactor = 1f)
    {
        if (targetBody == null) return;

        Vector3 adjustedThrustDirection = thrustDirection.normalized;
        if (float.IsNaN(adjustedThrustDirection.x) || adjustedThrustDirection == Vector3.zero)
        {
            Debug.LogWarning($"[ThrustController] Invalid thrust direction: {thrustDirection}");
            return;
        }

        // scale (world is 1 unit = 10 km)
        float scaledMagnitude = magnitude / 10f;

        // build force in world frame
        Vector3 F = adjustedThrustDirection * scaledMagnitude;

        Vector3 r = targetBody.transform.position - Vector3.zero;
        Vector3 v = targetBody.velocity - Vector3.zero;

        // unit basis
        Vector3 rHat = r.normalized;
        Vector3 vHat = v.normalized;
        Vector3 nHat = Vector3.Cross(rHat, vHat).normalized;

        float Fr = Vector3.Dot(F, rHat);
        float Fp = Vector3.Dot(F, vHat);
        float Fn = Vector3.Dot(F, nHat);

        // (for a pure Normal burn want Fn ≈ |F|, Fr ≈ 0, Fp ≈ 0, power ≈ 0)
        float cos_n_v = Vector3.Dot(nHat, vHat);              // should be ~0
        float power = Vector3.Dot(v, F);                   // work rate; ~0 for pure normal

        // apply force
        targetBody.AddForce(F);

        UpdateThrustParticleSystem(targetBody, adjustedThrustDirection);
        trajectoryRenderer.orbitIsDirty = true;

        if (tutorialController.inTutorialMode)
        {
            tutorialController.hasAppliedThrust = true;
        }
    }


    /// <summary>
    /// Updates the particle system position and rotation to match the thrust direction.
    /// </summary>
    /// <param name="targetBody">The body the particles should follow.</param>
    /// <param name="thrustDirection">The direction of applied thrust.</param>
    private void UpdateThrustParticleSystem(NBody targetBody, Vector3 thrustDirection)
    {
        if (thrustParticles == null)
        {
            Debug.LogError("ThrustController: thrustParticles is null! Ensure the particle system is assigned.");
            return;
        }
        if (!thrustParticles) return;

        // Set the position of the particle system to the target bodys position
        thrustParticles.transform.position = targetBody.transform.position;

        // Rotate the particle system to align with the opposite of the thrust direction
        thrustParticles.transform.rotation = Quaternion.LookRotation(-thrustDirection, targetBody.transform.up);

        if (!thrustParticles.isPlaying || thrustStopped)
        {
            thrustParticles.Clear();
            thrustParticles.Play();
            thrustStopped = false;
        }
    }

    /// <summary>
    /// Turns on active thrust for particles and correct thrust direction. Used in NBody for nodes.
    /// </summary>
    /// <param name="burnDirection"></param>
    public void SetDirectionalThrust(string burnDirection)
    {
        // Reset all first
        isForwardThrustActive = false;
        isReverseThrustActive = false;
        isLeftThrustActive = false;
        isRightThrustActive = false;
        isRadialInThrustActive = false;
        isRadialOutThrustActive = false;

        switch (burnDirection)
        {
            case "Prograde":
                isForwardThrustActive = true;
                break;
            case "Retrograde":
                isReverseThrustActive = true;
                break;
            case "Radial In":
                isRadialInThrustActive = true;
                break;
            case "Radial Out":
                isRadialOutThrustActive = true;
                break;
            case "Normal":
                isRightThrustActive = true;
                break;
            case "Anti-Normal":
                isLeftThrustActive = true;
                break;
            default:
                isForwardThrustActive = true;
                Debug.LogWarning($"Unknown burn direction: {burnDirection}. Defaulting to Prograde.");
                break;
        }
    }

    /// <summary>
    /// Turns off all active thrust flags.
    /// </summary>
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