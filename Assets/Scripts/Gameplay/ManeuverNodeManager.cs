using UnityEngine;
using System.Collections.Generic;

public class ManeuverNodeManager : MonoBehaviour
{
    [Header("Trajectory Rendering")]
    public TrajectoryRenderer trajectoryRenderer;
    public TimeController timeController;
    public ThrustController thrustController;

    [Header("Modular Controllers")]
    public ManeuverNodeUIController uiController;
    public ManeuverNodeVisualController visualController;
    public ManeuverPreviewController previewController;

    [Header("References")]
    public BodyRuntimeCoordinator bodyRuntimeCoordinator;

    [Header("UX Settings")]
    [SerializeField] private bool allowNodeSlider = true;

    [Header("Burn Tuning")]
    [SerializeField] private float burnDuration = 20f;
    [SerializeField] private float thrustPowerScale = 1f;

    private BodyService bodyService;
    private TutorialController tutorialController;

    public ManeuverNode CurrentNode { get; private set; }
    public bool HasNode => CurrentNode != null;

    public OrbitalParameters PreviewOrbitParams =>
        previewController != null ? previewController.PreviewOrbitParams : new OrbitalParameters(false);

    private bool _initialized;

    public void Initialize(SimContext ctx)
    {
        if (_initialized)
            return;

        _initialized = true;

        bodyRuntimeCoordinator = ctx.BodyRuntimeCoordinator;
        trajectoryRenderer = ctx.TrajectoryRenderer;
        timeController = ctx.TimeController;
        tutorialController = ctx.TutorialController;
        thrustController = ctx.ThrustController;
        if (thrustController != null)
            thrustController.SetThrustPowerScale(thrustPowerScale);
        bodyService = ctx.BodyService;

        if (uiController != null)
        {
            uiController.Initialize(
                defaultBurnDuration: burnDuration,
                defaultThrustScale: thrustPowerScale,
                allowNodeSlider: allowNodeSlider
            );

            uiController.NodeTimeSliderChanged += SetNodeAtFloatIndex;
            uiController.BurnDurationChanged += OnBurnDurationChangedFromUI;
            uiController.ThrustScaleChanged += OnThrustScaleChangedFromUI;
        }

        if (previewController != null)
        {
            previewController.Initialize(
                bodyService: bodyService,
                bodyRuntimeCoordinator: bodyRuntimeCoordinator,
                thrustController: thrustController,
                trajectoryRenderer: trajectoryRenderer
            );
        }

        if (trajectoryRenderer != null)
            trajectoryRenderer.TrackedBodyChanged += OnTrackedBodyChanged;
    }

    private void LateUpdate()
    {
        UpdatePinnedNodeVisuals();
    }

    private void OnDestroy()
    {
        if (trajectoryRenderer != null)
            trajectoryRenderer.TrackedBodyChanged -= OnTrackedBodyChanged;

        if (uiController != null)
        {
            uiController.NodeTimeSliderChanged -= SetNodeAtFloatIndex;
            uiController.BurnDurationChanged -= OnBurnDurationChangedFromUI;
            uiController.ThrustScaleChanged -= OnThrustScaleChangedFromUI;
            uiController.Dispose();
        }
    }

    private void UpdatePinnedNodeVisuals()
    {
        if (!HasNode)
            return;

        var node = CurrentNode;
        if (node == null)
            return;

        if (node.isFinalized && node.isPinned && node.marker != null)
        {
            if (node.marker.transform.position != node.pinnedWorldPosition)
                node.marker.transform.position = node.pinnedWorldPosition;

            if (node.marker.transform.parent != null)
                node.marker.transform.SetParent(null, true);
        }

        if (node.marker != null && bodyRuntimeCoordinator != null)
        {
            float tMinus = node.burnTime - bodyRuntimeCoordinator.simulationTime;
            var giz = node.marker.GetComponent<NodeGizmo>();
            if (giz != null)
                giz.SetTimeToNode(node.burnType.ToDisplayName(), tMinus);
        }
    }

    private void OnTrackedBodyChanged(NBody oldBody, NBody newBody)
    {
        bool active = HasNode &&
                      CurrentNode.targetBody == newBody &&
                      !CurrentNode.isFinalized;

        uiController?.SetNodeTimeSliderInteractable(active);
    }

    public void OnAddManeuverNode()
    {
        if (timeController != null)
        {
            timeController.SetTimeScale(1f);
        }

        var body = trajectoryRenderer != null ? trajectoryRenderer.trackedBody : null;
        if (body == null)
            return;

        int steps = 6000;
        float dt = trajectoryRenderer != null ? trajectoryRenderer.predictionDeltaTime : 2f;

        body.ComputePredictionForNodes(steps, dt, (traj, startTime, usedDt) =>
        {
            if (traj == null || traj.Count < 2)
                return;

            float simTime = bodyRuntimeCoordinator != null ? bodyRuntimeCoordinator.simulationTime : 0f;
            float initialOffsetTime = 20f;
            float desiredBurnTime = simTime + initialOffsetTime;

            float timeFromPredictionStart = desiredBurnTime - startTime;
            if (timeFromPredictionStart < 0f)
                timeFromPredictionStart = 0f;

            float floatIndex = timeFromPredictionStart / usedDt;
            int index = Mathf.Clamp(Mathf.FloorToInt(floatIndex), 0, traj.Count - 2);
            float t = floatIndex - index;

            Vector3 a = traj[index];
            Vector3 b = traj[index + 1];
            Vector3 burnPos = Vector3.Lerp(a, b, t);

            var node = new ManeuverNode
            {
                position = burnPos,
                burnTime = desiredBurnTime,
                deltaV = Vector3.zero,
                targetBody = body,
                duration = burnDuration,
                isFinalized = false,
                burnType = GetBurnChoice(),
                trajectorySnapshot = new List<Vector3>(traj),
                snapshotStartTime = startTime,
                snapshotDeltaTime = usedDt
            };

            ActivatePreviewNode(node, focusCamera: false);

            if (tutorialController != null && tutorialController.inTutorialMode)
                tutorialController.hasSetupNode = true;
        });
    }

    public void FinalizeManeuver()
    {
        if (!HasNode)
            return;

        var node = CurrentNode;
        if (node == null || node.marker == null)
            return;

        BuildNodeFixedStepSchedule(node);

        node.isFinalized = true;
        node.marker.transform.SetParent(null, true);
        node.pinnedWorldPosition = node.marker.transform.position;
        node.isPinned = true;

        visualController?.SetupNodeVisuals(node, isPreview: false, manager: this);
        uiController?.SetEditingEnabled(false);

        if (bodyService != null && bodyService.CentralBody != null && trajectoryRenderer != null)
        {
            var central = bodyService.CentralBody;
            var tracked = trajectoryRenderer.trackedBody;

            if (tracked != null)
            {
                var orbit = OrbitalCalculations.CalculateOrbitalParameters(
                    central.trueMass,
                    central.state.position,
                    tracked.state.position,
                    tracked.state.velocity
                );

                if (orbit.isValid && orbit.eccentricity < 1f && orbit.orbitalPeriod > 0f)
                {
                    float simTime = bodyRuntimeCoordinator != null ? bodyRuntimeCoordinator.simulationTime : 0f;
                    while (node.burnTime < simTime)
                        node.burnTime += orbit.orbitalPeriod;

                    BuildNodeFixedStepSchedule(node);
                }
            }
        }

        if (tutorialController != null && tutorialController.inTutorialMode)
            tutorialController.hasPlacedNode = true;

        trajectoryRenderer?.ClearPreview();
    }

    private void BuildNodeFixedStepSchedule(ManeuverNode node)
    {
        if (node == null || bodyRuntimeCoordinator == null)
            return;

        float fixedDt = Time.fixedDeltaTime;
        if (fixedDt <= 0f)
            return;

        int currentStep = bodyRuntimeCoordinator.simulationStep;

        int startStep = Mathf.Max(
            currentStep,
            Mathf.CeilToInt(node.burnTime / fixedDt)
        );

        int burnSteps = Mathf.Max(1, Mathf.CeilToInt(node.duration / fixedDt));

        node.burnStartStep = startStep;
        node.burnStepCount = burnSteps;

        node.burnTime = node.burnStartStep * fixedDt;
        node.duration = node.burnStepCount * fixedDt;
    }

    public void CreatePreviewNode(Vector3 position, float burnTime, Vector3 deltaV, float duration)
    {
        var trackedBody = trajectoryRenderer != null ? trajectoryRenderer.trackedBody : null;
        var latestPrediction = trajectoryRenderer != null ? trajectoryRenderer.latestPrediction : null;
        var snapshot = latestPrediction != null
            ? new List<Vector3>(latestPrediction)
            : new List<Vector3>();

        var node = new ManeuverNode
        {
            position = position,
            burnTime = burnTime,
            deltaV = deltaV,
            targetBody = trackedBody,
            duration = duration,
            isFinalized = false,
            burnType = GetBurnChoice(),
            trajectorySnapshot = snapshot,
            snapshotStartTime = trajectoryRenderer != null ? trajectoryRenderer.latestPredictionStartTime : 0f,
            snapshotDeltaTime = trajectoryRenderer != null ? Mathf.Max(1e-5f, trajectoryRenderer.latestPredictionDeltaTime) : 1f
        };

        ActivatePreviewNode(node, focusCamera: true);
    }

    private void ActivatePreviewNode(ManeuverNode node, bool focusCamera)
    {
        if (node == null)
            return;

        ClearNode();

        CurrentNode = node;

        visualController?.SetupNodeVisuals(node, isPreview: true, manager: this);

        UpdateManeuverPrediction(node);

        if (focusCamera && node.marker != null)
            visualController?.FocusCameraOn(node.marker.transform.position);

        previewController?.RequestPreview(node, interactionActive: false);

        uiController?.SetEditingEnabled(true);
        uiController?.SetupNodeSlider(node);
    }

    public void ClearNode()
    {
        if (CurrentNode != null)
            visualController?.DestroyVisual(CurrentNode);

        CurrentNode = null;

        trajectoryRenderer?.ClearPreview();
        previewController?.Clear();
        uiController?.ResetEditingUI();
    }

    public void RemoveNode(ManeuverNode node)
    {
        if (node == null || node != CurrentNode)
            return;

        ClearNode();
    }

    public void DragNodeToFloatIndex(float floatIndex)
    {
        SetNodeAtFloatIndex(floatIndex);
    }

    public bool TryGetCurrentNodeIndex(out float currentFloatIndex)
    {
        currentFloatIndex = 0f;

        if (!HasNode)
            return false;

        var node = CurrentNode;
        if (node == null || node.trajectorySnapshot == null || node.trajectorySnapshot.Count < 2)
            return false;

        float dt = Mathf.Max(1e-5f, node.snapshotDeltaTime);
        currentFloatIndex = (node.burnTime - node.snapshotStartTime) / dt;
        currentFloatIndex = Mathf.Clamp(currentFloatIndex, 0f, node.trajectorySnapshot.Count - 1.0001f);
        return true;
    }

    public void OnDeltaVChanged(float newDv)
    {
        if (!float.IsFinite(newDv))
            return;

        MarkAdjusted();
    }

    public void UpdateManeuverPrediction(ManeuverNode node = null)
    {
        if (trajectoryRenderer == null)
            return;

        node ??= CurrentNode;
        if (node == null || node.isFinalized)
            return;

        if (!TrajectorySampler.TrySampleAtBurnTime(node, out var pos, out _, out _))
            return;

        node.position = pos;
    }

    public void SetNodeAtFloatIndex(float floatIndex)
    {
        if (!HasNode)
            return;

        var node = CurrentNode;
        if (node.isFinalized)
            return;

        var traj = node.trajectorySnapshot;
        if (traj == null || traj.Count < 2)
            return;

        int count = traj.Count;
        floatIndex = Mathf.Clamp(floatIndex, 0f, count - 1.0001f);

        float sampleDt = Mathf.Max(1e-5f, node.snapshotDeltaTime);
        float newBurnTime = node.snapshotStartTime + floatIndex * sampleDt;

        Vector3 p = TrajectorySampler.SampleAtIndex(traj, floatIndex);

        node.burnTime = newBurnTime;
        node.position = p;

        if (node.marker != null)
            node.marker.transform.position = p;

        UpdateManeuverPrediction(node);
        previewController?.RequestPreview(node, interactionActive: true);

        if (allowNodeSlider)
            uiController?.SetNodeSliderValueWithoutNotify(floatIndex);

        MarkAdjusted();
    }

    private void OnBurnDurationChangedFromUI(float newDuration)
    {
        burnDuration = newDuration;

        if (HasNode && !CurrentNode.isFinalized)
        {
            CurrentNode.duration = burnDuration;
            previewController?.RequestPreview(CurrentNode, interactionActive: true);
            MarkAdjusted();
        }
    }

    private void OnThrustScaleChangedFromUI(float newScale)
    {
        thrustPowerScale = newScale;

        if (thrustController != null)
            thrustController.SetThrustPowerScale(thrustPowerScale);

        if (HasNode && !CurrentNode.isFinalized)
        {
            previewController?.RequestPreview(CurrentNode, interactionActive: true);
            MarkAdjusted();
        }
    }

    private void MarkAdjusted()
    {
        uiController?.SetPlaceButtonInteractable(true);
    }

    public Vector3 GetBurnDirectionFromDropdown(NBody targetBody)
    {
        if (targetBody == null)
            return Vector3.forward;

        if (trajectoryRenderer == null)
            return targetBody.velocity.normalized;

        var central = bodyService != null ? bodyService.CentralBody : null;
        Vector3 r = central != null
            ? (targetBody.transform.position - central.transform.position)
            : targetBody.transform.position;

        Vector3 velocity = targetBody.state.velocity.ToVector3().normalized;
        Vector3 radialOut = r.normalized;
        Vector3 right = Vector3.Cross(radialOut, velocity).normalized;

        BurnType burnType = GetBurnChoice();

        return burnType switch
        {
            BurnType.Prograde => velocity,
            BurnType.Retrograde => -velocity,
            BurnType.RadialIn => -radialOut,
            BurnType.RadialOut => radialOut,
            BurnType.Normal => right,
            BurnType.AntiNormal => -right,
            _ => velocity
        };
    }

    public BurnType GetBurnChoice()
    {
        return uiController != null
            ? uiController.GetBurnChoice()
            : BurnType.Prograde;
    }
}