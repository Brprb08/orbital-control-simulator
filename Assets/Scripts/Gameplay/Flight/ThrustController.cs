using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Applies directional thrust to the tracked craft and keeps visual thrust effects in sync.
/// Integrates with UI inputs, tutorial flags, and trajectory updates.
/// </summary>
[DefaultExecutionOrder(-100)]
public class ThrustController : MonoBehaviour
{
    [Header("Thrust Settings")]
    public float maxForwardThrustMagnitude = 10f;
    [Range(0f, 5f)]
    public float thrustPowerScale = 1f;
    [SerializeField, Min(0.01f)] private float maxThrustTimeScale = 30f;
    [SerializeField] private string thrustTimeScaleLimitMessage =
        "Thrust can only be performed at {0}x timescale and below.";

    /// <summary>
    /// Effective thrust magnitude after scaling; use this instead of maxForwardThrustMagnitude.
    /// </summary>
    public float EffectiveForwardThrustMagnitude => maxForwardThrustMagnitude * thrustPowerScale;

    [Header("Visual Feedback")]
    public ParticleSystem thrustParticles;

    [Header("Thrust Flags")]
    public bool isForwardThrustActive = false;

    [SerializeField, Min(0f)] float backOffset = 0.1f;

    [Header("References - Scripts")]
    public CameraController cameraController;
    public CameraMovement cameraMovement;
    public TrajectoryRenderer trajectoryRenderer;
    public BodyRuntimeCoordinator bodyRuntimeCoordinator;
    private TutorialController tutorialController;
    private TimeController timeController;

    private AttitudeController attitude;

    private bool nodeBurnActive;
    private BurnType activeBurnType;
    private NBody activeBurnBody;

    private Vector3 burnVCache = Vector3.right;
    private Vector3 burnHCache = Vector3.up;

    [Header("Thrust Configs")]
    private bool thrustStopped = false;
    private NBody thrustParticlesParentBody;
    private Vector3 thrustParticlesBaseScale = Vector3.one;
    private bool thrustTimeScaleLimitHeld;

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
        this.timeController = ctx.TimeController;

        if (thrustParticles == null)
        {
            GameObject thrustParticleObject = GameObject.Find("Particle System");
            thrustParticles = thrustParticleObject != null
                ? thrustParticleObject.GetComponent<ParticleSystem>()
                : null;

            if (thrustParticles == null)
            {
                Debug.LogError("ThrustController: No Particle System found in the scene!");
                return;
            }
        }

        thrustParticles.transform.SetParent(null, true);
        thrustParticlesBaseScale = thrustParticles.transform.localScale;

        var main = thrustParticles.main;
        main.useUnscaledTime = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        thrustParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        thrustParticles.Clear(true);
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

        thrustParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        thrustParticles.Clear(true);
        thrustStopped = true;
        thrustParticlesParentBody = null;
        thrustParticles.transform.SetParent(null, true);
        thrustParticles.transform.localScale = thrustParticlesBaseScale;
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
        trajectoryRenderer?.RequestPredictionRefresh();

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
        if (!thrustParticles || targetBody == null) return;

        Vector3 exhaustDirection = -thrustDirection.normalized;
        if (float.IsNaN(exhaustDirection.x) || exhaustDirection == Vector3.zero)
            return;

        Vector3 worldPosition = targetBody.transform.position + exhaustDirection * backOffset;
        Vector3 worldUp = Mathf.Abs(Vector3.Dot(exhaustDirection, targetBody.transform.up)) > 0.98f
            ? Vector3.forward
            : targetBody.transform.up;
        Quaternion worldRotation = Quaternion.LookRotation(exhaustDirection, worldUp);

        if (thrustParticlesParentBody != targetBody)
        {
            thrustParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            thrustParticles.Clear(true);
            thrustParticles.transform.SetParent(targetBody.transform, true);
            thrustParticles.transform.localScale = thrustParticlesBaseScale;
            thrustParticlesParentBody = targetBody;
            thrustStopped = true;
        }

        thrustParticles.transform.SetPositionAndRotation(worldPosition, worldRotation);

        if (!thrustParticles.isPlaying || thrustStopped)
        {
            thrustParticles.Clear(true);
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

        EnsureThrustTimeScaleLimit(showNodeFeedback: true);
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
        ReleaseThrustTimeScaleLimit();
        StopThrustVisuals();
        ctx?.UIRoot?.RefreshAllUi();
    }

    /// <summary>Clears all thrust flags.</summary>
    public void StopAllThrust()
    {
        isForwardThrustActive = false;
        nodeBurnActive = false;
        activeBurnBody = null;
        ReleaseThrustTimeScaleLimit();
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

        EnsureThrustTimeScaleLimit(showNodeFeedback: false);
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
        ReleaseThrustTimeScaleLimit();
        EventSystem.current?.SetSelectedGameObject(null);
    }

    public void EnsureThrustTimeScaleLimit(bool showNodeFeedback)
    {
        if (thrustTimeScaleLimitHeld)
            return;

        bool reduced = timeController != null &&
                       timeController.BeginTemporaryMaxTimeScale(maxThrustTimeScale);

        thrustTimeScaleLimitHeld = true;

        if (reduced)
            ShowThrustTimeScaleLimitFeedback(showNodeFeedback);
    }

    private void ReleaseThrustTimeScaleLimit()
    {
        if (!thrustTimeScaleLimitHeld)
            return;

        thrustTimeScaleLimitHeld = false;
        timeController?.EndTemporaryMaxTimeScale();
    }

    private void ShowThrustTimeScaleLimitFeedback(bool showNodeFeedback)
    {
        string message = string.Format(thrustTimeScaleLimitMessage, FormatTimeScale(maxThrustTimeScale));

        if (showNodeFeedback)
            ctx?.ManeuverNodeManager?.uiController?.ShowThrustTimeScaleLimitFeedback(message);

        var feedbackText = ctx?.UIRoot?.References?.feedbackText;
        if (feedbackText != null)
        {
            feedbackText.text = message;
            feedbackText.gameObject.SetActive(true);
        }
    }

    private static string FormatTimeScale(float value)
    {
        return Mathf.Approximately(value, Mathf.Round(value))
            ? Mathf.RoundToInt(value).ToString()
            : value.ToString("0.##");
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
