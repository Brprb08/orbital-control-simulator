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
    [Range(0f, 5f)]
    public float thrustPowerScale = 1f;

    /// <summary>
    /// Effective thrust magnitude after scaling; use this instead of maxForwardThrustMagnitude.
    /// </summary>
    public float EffectiveForwardThrustMagnitude => maxForwardThrustMagnitude * thrustPowerScale;

    [Header("Visual Feedback")]
    public ParticleSystem thrustParticles;

    [Header("Thrust Flags")]
    public bool isForwardThrustActive = false;

    [SerializeField] float backOffset = 0.6f;

    [Header("References - Scripts")]
    public CameraController cameraController;
    public CameraMovement cameraMovement;
    public TrajectoryRenderer trajectoryRenderer;
    public BodyRuntimeCoordinator bodyRuntimeCoordinator;
    private TutorialController tutorialController;

    private AttitudeController attitude;

    private bool nodeBurnActive;
    private BurnType activeBurnType;
    private NBody activeBurnBody;

    private Vector3 burnVCache = Vector3.right;
    private Vector3 burnHCache = Vector3.up;

    [Header("Thrust Configs")]
    private bool thrustStopped = false;

    private SimContext ctx;

    /// <summary>
    /// True if any thrust flag is active.
    /// </summary>
    public bool IsThrusting =>
        isForwardThrustActive;

    public bool IsNodeBurnActive => nodeBurnActive;

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
        NBody ship = ResolveActiveShip();
        if (ship == null) return;

        if (!attitude) attitude = ship.GetComponent<AttitudeController>();

        bool isThrustingNow = false;

        if (isForwardThrustActive)
        {
            Vector3 burnDir = ResolveBurnDirection(ship);
            if (nodeBurnActive)
            {
                UpdateThrustParticleSystem(ship, burnDir);
            }
            else
            {
                ApplyThrust(ship, EffectiveForwardThrustMagnitude, burnDir);
            }
            isThrustingNow = true;
        }

        bool holdCurrent = attitude != null &&
                           attitude.mode == AttitudeController.PointingMode.HoldCurrent;

        ship.projectLateralPerSubstep = IsLateralBurnActive() && !holdCurrent;

        if (!isThrustingNow)
        {
            StopThrustVisuals();
        }
    }

    private NBody ResolveActiveShip()
    {
        return nodeBurnActive
            ? activeBurnBody
            : (cameraController != null ? cameraController.CurrentBody : null);
    }

    private Vector3 ResolveBurnDirection(NBody ship)
    {
        if (ship == null)
            return Vector3.forward;

        if (!nodeBurnActive)
            return ship.transform.forward;

        var bodyService = ctx != null ? ctx.BodyService : null;
        var central = bodyService != null ? bodyService.CentralBody : null;
        Vector3 center = central != null ? central.transform.position : Vector3.zero;

        Vector3 pos = ship.state.position.ToVector3();
        Vector3 vel = ship.state.velocity.ToVector3();

        return AttitudeMath.ComputeBurnDirection(
            activeBurnType,
            pos,
            vel,
            center,
            ref burnVCache,
            ref burnHCache
        );
    }

    private bool IsLateralBurnActive()
    {
        if (nodeBurnActive)
            return activeBurnType == BurnType.Normal || activeBurnType == BurnType.AntiNormal;

        return attitude != null &&
               (attitude.mode == AttitudeController.PointingMode.Normal ||
                attitude.mode == AttitudeController.PointingMode.AntiNormal);
    }

    private void StopThrustVisuals()
    {
        if (!thrustParticles) return;

        thrustParticles.Stop();
        thrustStopped = true;
    }

    public void ApplyThrust(
        NBody targetBody,
        float magnitude,
        Vector3 thrustDirection,
        float rampedThrustFactor = 1f
    )
    {
        if (targetBody == null) return;

        Vector3 adjustedThrustDirection = thrustDirection.normalized;
        if (float.IsNaN(adjustedThrustDirection.x) || adjustedThrustDirection == Vector3.zero)
        {
            Debug.LogWarning($"[ThrustController] Invalid thrust direction: {thrustDirection}");
            return;
        }

        // Scale (world is 1 unit = 10 km)
        float scaledMagnitude = (magnitude * rampedThrustFactor) / 10f;

        Vector3 F = adjustedThrustDirection * scaledMagnitude;

        targetBody.AddForce(F);

        UpdateThrustParticleSystem(targetBody, adjustedThrustDirection);
        trajectoryRenderer.orbitIsDirty = true;

        if (tutorialController != null && tutorialController.inTutorialMode)
        {
            tutorialController.hasAppliedThrust = true;
        }
    }


    /// <summary>
    /// Positions/orients the thrust particle system and plays it when thrust starts.
    /// </summary>
    private void UpdateThrustParticleSystem(NBody targetBody, Vector3 thrustDirection)
    {
        if (!thrustParticles) return;

        var rot = Quaternion.LookRotation(-thrustDirection.normalized, targetBody.transform.up);

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
    /// Sets a multiplier on top of maxForwardThrustMagnitude (1 = default behavior).
    /// </summary>
    public void SetThrustPowerScale(float scale)
    {
        thrustPowerScale = Mathf.Max(0f, scale);
    }

    /// <summary>
    /// Directly sets the base forward thrust magnitude in "game units".
    /// </summary>
    public void SetForwardThrustMagnitude(float magnitude)
    {
        maxForwardThrustMagnitude = Mathf.Max(0f, magnitude);
    }


    /// <summary>
    /// Activates a single thrust mode by name; used by node-driven burns.
    /// </summary>
    // public void SetDirectionalThrust()
    // {
    //     isForwardThrustActive = true;
    // }

    public void StartNodeBurn(ManeuverNode node)
    {
        if (node == null || node.targetBody == null) return;

        activeBurnBody = node.targetBody;
        activeBurnType = node.burnType;
        nodeBurnActive = true;
        isForwardThrustActive = true;
        ctx?.UIRoot?.RefreshAllUi();
    }

    public void StopNodeBurn()
    {
        nodeBurnActive = false;
        activeBurnBody = null;
        isForwardThrustActive = false;
        StopThrustVisuals();
        ctx?.UIRoot?.RefreshAllUi();
    }

    /// <summary>Clears all thrust flags.</summary>
    public void StopAllThrust()
    {
        isForwardThrustActive = false;
        nodeBurnActive = false;
        activeBurnBody = null;
        StopThrustVisuals();
        ctx?.UIRoot?.RefreshAllUi();
    }

    /// <summary>
    /// UI Button Handlers
    /// </summary>
    public void StartForwardThrust()
    {
        if (!CanStartManualThrust())
        {
            EventSystem.current?.SetSelectedGameObject(null);
            return;
        }

        isForwardThrustActive = true;
    }
    public void StopForwardThrust()
    {
        if (nodeBurnActive)
        {
            EventSystem.current?.SetSelectedGameObject(null);
            return;
        }

        isForwardThrustActive = false;
        EventSystem.current.SetSelectedGameObject(null);
    }

    private bool CanStartManualThrust()
    {
        if (nodeBurnActive)
            return false;

        ManeuverNode node = ctx != null && ctx.ManeuverNodeManager != null
            ? ctx.ManeuverNodeManager.CurrentNode
            : null;

        if (node == null || !node.isFinalized)
            return true;

        NBody activeShip = ResolveActiveShip();
        return node.targetBody == null || node.targetBody != activeShip;
    }
}
