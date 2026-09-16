using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using UnityEngine.Serialization;

/// <summary>
/// Owns manual/node burn state, scheduling, and the temporary time-scale limit.
/// Physics consumes burn commands; audio and particles follow the active burn.
/// </summary>
[DefaultExecutionOrder(-100)]
public class ThrustController : MonoBehaviour
{
    [Header("Thrust Settings")]
    [FormerlySerializedAs("maxForwardThrustMagnitude"), SerializeField, HideInInspector]
    private float legacyThrustKilonewtons = 10f;
    [FormerlySerializedAs("thrustPowerScale"), SerializeField, HideInInspector]
    private float legacyThrustScale = 1f;
    [SerializeField, Min(0.01f)] private float maxThrustTimeScale = 30f;
    [SerializeField] private string thrustTimeScaleLimitMessage =
        "Thrust can only be performed at {0}x timescale and below.";

    /// <summary>
    /// Migration defaults only. Runtime burns read the target body's propulsion settings.
    /// </summary>
    public float LegacyDefaultThrustNewtons => legacyThrustKilonewtons * SimulationUnits.MetersPerKilometer;
    public float LegacyDefaultThrustScale => legacyThrustScale;

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

    private const float AttitudeLeadTime = 20f;
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

    private void OnDisable() => StopAllThrust();

    void FixedUpdate()
    {
        NBody ship = ResolveActiveShip();
        if (ship == null)
        {
            if (isForwardThrustActive || activeBurnBody != null)
                StopAllThrust();
            return;
        }

        if (!ship.HasUsableThrust)
        {
            StopThrustForBody(ship);
            return;
        }

        if (!nodeBurnActive && isForwardThrustActive)
            SetActiveBurnBody(ship);

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
                ApplyThrust(ship, burnDir);
            }
            isThrustingNow = true;
        }

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
        Vector3 thrustDirection,
        float rampedThrustFactor = 1f
    )
    {
        if (targetBody == null || targetBody.isCentralBody || !targetBody.HasUsableThrust ||
            !float.IsFinite(rampedThrustFactor) || rampedThrustFactor <= 0f) return;

        Vector3 adjustedThrustDirection = thrustDirection.normalized;
        if (!float.IsFinite(thrustDirection.x) || !float.IsFinite(thrustDirection.y) ||
            !float.IsFinite(thrustDirection.z) || adjustedThrustDirection == Vector3.zero)
        {
            Debug.LogWarning($"[ThrustController] Invalid thrust direction: {thrustDirection}");
            return;
        }

        float scaledMagnitude = SimulationUnits.ForceNewtonsToWorld(targetBody.EffectiveThrustNewtons * rampedThrustFactor);
        if (!float.IsFinite(scaledMagnitude) || scaledMagnitude <= 0f) return;

        Vector3 F = adjustedThrustDirection * scaledMagnitude;

        targetBody.AddForce(F);
        ctx?.ConstellationRegistry?.MarkDepartedIdeal(targetBody);

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

    public void StartNodeBurn(ManeuverNode node)
    {
        if (node == null || node.targetBody == null || !node.targetBody.HasUsableThrust) return;

        bool changed = !nodeBurnActive || activeBurnBody != node.targetBody || activeBurnType != node.burnType;
        EnsureThrustTimeScaleLimit(showNodeFeedback: true);
        SetActiveBurnBody(node.targetBody);
        activeBurnType = node.burnType;
        nodeBurnActive = true;
        isForwardThrustActive = true;
        ctx?.RocketThrustAudio?.SetThrustActive(true);
        if (changed)
            ctx?.UIRoot?.RefreshAllUi();
    }

    public void StopNodeBurn() => StopAllThrust();

    /// <summary>Clears all thrust flags.</summary>
    public void StopAllThrust()
    {
        bool changed = isForwardThrustActive || nodeBurnActive || activeBurnBody != null;
        isForwardThrustActive = false;
        nodeBurnActive = false;
        SetActiveBurnBody(null);
        ReleaseThrustTimeScaleLimit();
        StopThrustVisuals();
        ctx?.RocketThrustAudio?.SetThrustActive(false);
        if (changed)
            ctx?.UIRoot?.RefreshAllUi();
    }

    private void SetActiveBurnBody(NBody body)
    {
        if (activeBurnBody != body && activeBurnBody != null)
        {
            activeBurnBody.isThrusting = false;
            if (activeBurnBody.TryGetComponent(out AttitudeController attitude))
                attitude.lockNormalParity = false;
        }

        activeBurnBody = body;
        if (body != null)
            body.isThrusting = true;
    }

    /// <summary>Stops shared thrust resources only when they belong to this body.</summary>
    public void StopThrustForBody(NBody body)
    {
        if (body == null) return;
        if (activeBurnBody == body || (isForwardThrustActive && ResolveActiveShip() == body))
            StopAllThrust();
        else
            body.isThrusting = false;
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

        NBody ship = ResolveActiveShip();
        if (ship == null || !ship.HasUsableThrust) return;
        EnsureThrustTimeScaleLimit(showNodeFeedback: false);
        SetActiveBurnBody(ship);
        isForwardThrustActive = true;
        ctx?.RocketThrustAudio?.SetThrustActive(true);
    }
    public void StopForwardThrust()
    {
        if (nodeBurnActive)
        {
            EventSystem.current?.SetSelectedGameObject(null);
            return;
        }

        StopAllThrust();
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

    // Called once immediately before integration, after manual force has been queued.
    internal ScheduledBurn PrepareSimulationStep(
        IReadOnlyList<NBody> bodies, IReadOnlyList<AttitudeController> attitudes, float stepDt)
    {
        ManeuverNode node = ctx?.ManeuverNodeManager != null ? ctx.ManeuverNodeManager.CurrentNode : null;
        NBody target = node != null && node.isFinalized ? node.targetBody : null;
        AttitudeController targetAttitude = null;
        bool targetRegistered = false;

        for (int i = 0; i < bodies.Count; i++)
        {
            NBody body = bodies[i];
            AttitudeController attitude = i < attitudes.Count ? attitudes[i] : null;
            if (body != null && body == target)
            {
                targetRegistered = true;
                targetAttitude = attitude;
                continue;
            }

            if (attitude != null)
                attitude.lockNormalParity = false;
            if (body != null && body != activeBurnBody)
                body.isThrusting = false;
        }

        if (nodeBurnActive && (target == null || !targetRegistered || activeBurnBody != target))
            StopAllThrust();

        if (target == null || !targetRegistered)
            return default;

        if (!target.isReferenceOrbit)
            UpdateNodeBurnLifecycle(target, targetAttitude, node, stepDt);

        // Completion may remove the node. Never hand the integrator a stale command.
        if (ctx?.ManeuverNodeManager == null || ctx.ManeuverNodeManager.CurrentNode != node)
            return default;

        return new ScheduledBurn(node, target.EffectiveThrustNewtons);
    }

    private void UpdateNodeBurnLifecycle(NBody body, AttitudeController attitude, ManeuverNode node, float stepDt)
    {
        BodyRuntimeCoordinator runtime = ctx?.BodyRuntimeCoordinator;
        if (body == null || node == null || runtime == null)
            return;

        float simTime = runtime.simulationTime;
        float stepEndTime = simTime + Mathf.Max(0f, stepDt);
        UpdateNodeBurnAttitude(attitude, node, simTime);

        if (simTime >= ManeuverBurnMath.GetBurnEndTime(node))
        {
            StopThrustForBody(body);
            ctx?.ManeuverNodeManager?.RemoveNode(node);
        }
        else if (ManeuverBurnMath.DoesBurnOverlap(node, body, simTime, stepEndTime))
        {
            StartNodeBurn(node);
        }
        else if (body.isThrusting)
        {
            StopThrustForBody(body);
        }
    }

    private static void UpdateNodeBurnAttitude(
        AttitudeController attitude,
        ManeuverNode node,
        float simTime)
    {
        if (attitude == null || node == null)
            return;

        bool inBurnPhase =
            simTime >= node.burnTime - AttitudeLeadTime &&
            simTime < ManeuverBurnMath.GetBurnEndTime(node);

        if (inBurnPhase)
        {
            AttitudeController.PointingMode desiredMode = MapBurnTypeToAttitude(node.burnType);
            if (attitude.mode != desiredMode)
                attitude.SetMode(desiredMode);

            attitude.lockNormalParity = true;
            return;
        }

        attitude.lockNormalParity = false;
    }

    private static AttitudeController.PointingMode MapBurnTypeToAttitude(BurnType burnType)
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
