using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;

public class ManeuverNodeManager : MonoBehaviour
{
    public const int MaxManualNodes = 4;
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
    public float LegacyDefaultThrustScale => thrustPowerScale;

    [Header("Preview Snapshot Refresh")]
    [SerializeField, Min(0.1f)] private float previewNodeSnapshotRefreshInterval = 0.5f;

    [Header("Node Orbit Sampling")]
    [SerializeField, Min(128)] private int minNodeOrbitSamples = 1024;
    [SerializeField, Min(256)] private int maxNodeOrbitSamples = 6000;

    private BodyService bodyService;
    private TutorialController tutorialController;
    private UIRoot uiRoot;
    private NBody configurationBody;

    public ManeuverNode CurrentNode { get; private set; }
    public bool HasNode => CurrentNode != null || draftNode != null;
    private readonly Queue<ManeuverNode> manualQueue = new();
    private ManeuverNode draftNode;
    public ManeuverNode EditingNode => draftNode ?? CurrentNode;
    public int ManualNodeCount => HasRendezvousPlan ? 0 : manualQueue.Count +
        (CurrentNode != null && CurrentNode.isFinalized ? 1 : 0) + (draftNode != null ? 1 : 0);
    private readonly Queue<ManeuverNode> rendezvousQueue = new();
    public bool HasRendezvousPlan { get; private set; }
    private RendezvousPlanner.Result rendezvousPlan;
    private double rendezvousEpoch;
    private NBody rendezvousBody, rendezvousTarget;
    public enum RendezvousOutcome { Succeeded, Failed, Canceled }
    public event System.Action<RendezvousOutcome, string> RendezvousEnded;

    private void FinishRendezvous(RendezvousOutcome outcome, string message)
    {
        if (!HasRendezvousPlan) return;
        HasRendezvousPlan = false;
        ClearNode();
        RendezvousEnded?.Invoke(outcome, message);
    }

    private void OnSatelliteCollision(NBody a, NBody b, Unity.Mathematics.double3 position)
    {
        if (!HasRendezvousPlan) return;
        bool pair = a == rendezvousBody && b == rendezvousTarget || b == rendezvousBody && a == rendezvousTarget;
        if (pair && rendezvousPlan.Goal.Mode == RendezvousPlanner.ArrivalMode.Intercept)
            FinishRendezvous(RendezvousOutcome.Succeeded, "Intercept impact confirmed. Target cleared.");
        else if (a == rendezvousBody || b == rendezvousBody || a == rendezvousTarget || b == rendezvousTarget)
            FinishRendezvous(RendezvousOutcome.Failed, "Rendezvous failed: a satellite was lost in a collision.");
    }

    // Called on the physics clock, including powered flight and the final coast.
    internal void ValidateRendezvousFlight(double time, List<NBody> bodies,
        Unity.Mathematics.double3[] positions, Unity.Mathematics.double3[] velocities)
    {
        if (!HasRendezvousPlan) return;
        int a = bodies.IndexOf(rendezvousBody), b = bodies.IndexOf(rendezvousTarget);
        if (rendezvousBody == null || rendezvousTarget == null || a < 0 || b < 0 ||
            bodyRuntimeCoordinator.IsPendingRemoval(rendezvousBody) || bodyRuntimeCoordinator.IsPendingRemoval(rendezvousTarget))
        { FinishRendezvous(RendezvousOutcome.Failed, "Rendezvous failed: a satellite is no longer available."); return; }
        double age = time - rendezvousEpoch;
        var plan = rendezvousPlan;
        double contact = (System.Math.Max(0, rendezvousBody.radius) + System.Math.Max(0, rendezvousTarget.radius)) * 10000;
        if (System.Math.Abs(contact - plan.Goal.ContactDistanceMeters) > 1e-5 ||
            bodyService?.CentralBody == null ||
            PhysicsConstants.GDouble * bodyService.CentralBody.TotalMassKilograms != plan.Mu)
        { FinishRendezvous(RendezvousOutcome.Failed, "Flight configuration changed; rebuild the rendezvous plan."); return; }
        if (age >= plan.ArrivalSeconds)
        {
            bool arrived = plan.Goal.IsSatisfied(positions[a], velocities[a], positions[b], velocities[b],
                plan.Mu, plan.ArrivalDistanceTolerance, plan.ArrivalSpeedTolerance);
            FinishRendezvous(arrived ? RendezvousOutcome.Succeeded : RendezvousOutcome.Failed,
                arrived ? "Rendezvous arrival verified. Target cleared." : "Rendezvous failed: arrival requirements were not met.");
            return;
        }
        if (!plan.Prediction.TrySample(age, out var expected) ||
            !plan.MatchesFlight(expected, positions[a], velocities[a], positions[b], velocities[b]))
            FinishRendezvous(RendezvousOutcome.Failed,
                "Flight diverged from the validated approach; remaining burns canceled. Build a fresh plan.");
    }

    internal double RendezvousArrivalTime => HasRendezvousPlan ? rendezvousEpoch + rendezvousPlan.ArrivalSeconds : double.PositiveInfinity;
    public int RendezvousBurnsRemaining => HasRendezvousPlan ? rendezvousQueue.Count + (HasNode ? 1 : 0) : 0;

    public ManeuverNode[] GetRendezvousSchedule()
    {
        if (!HasRendezvousPlan || CurrentNode == null) return System.Array.Empty<ManeuverNode>();
        var nodes = new List<ManeuverNode> { CurrentNode };
        nodes.AddRange(rendezvousQueue);
        return nodes.ToArray();
    }

    public bool TryScheduleRendezvousPlan(NBody body, NBody target, RendezvousPlanner.Result plan, double epoch)
    {
        bool replacingPlan = HasRendezvousPlan && bodyRuntimeCoordinator != null &&
            !bodyRuntimeCoordinator.IsNodeBurnInProgress;
        if (HasNode && !replacingPlan || body == null || bodyRuntimeCoordinator == null ||
            trajectoryRenderer == null || trajectoryRenderer.trackedBody != body ||
            target == null || target == body || plan == null || !plan.Success ||
            (plan.Burns.Length < (plan.Goal.Mode == RendezvousPlanner.ArrivalMode.Intercept ? 1 : 2) || plan.Burns.Length > 4) || body.isThrusting ||
            epoch + plan.Burns[0].Start <= bodyRuntimeCoordinator.simulationTime + 5) return false;
        if (!plan.CoastReference.TrySample(bodyRuntimeCoordinator.SimulationTimeSeconds - epoch, out var current) ||
            !plan.MatchesFlight(current, body.state.position, body.state.velocity, target.state.position, target.state.velocity)) return false;
        var nodes = new List<ManeuverNode>();
        foreach (var burn in plan.Burns)
        {
            if (!plan.Prediction.TrySample(burn.Start, out var sample)) return false;
            nodes.Add(new ManeuverNode {
                targetBody=body, burnTime=(float)(epoch+burn.Start), duration=(float)burn.Duration,
                burnType=burn.Type, isFinalized=true, position=sample.A.ToVector3(),
                usesVectorDirection=burn.UsesVectorDirection,
                vectorDirectionWorld=burn.VectorDirectionWorld.ToVector3(),
                pinnedWorldPosition=sample.A.ToVector3(), isPinned=true,
                trajectorySnapshot=new List<Vector3>()
            });
        }
        // Keep the old queue intact until the replacement has been completely
        // validated and converted to executable nodes.
        if (replacingPlan)
            ClearNode();
        thrustController?.StopForwardThrust();
        HasRendezvousPlan = true;
        rendezvousPlan = plan; rendezvousEpoch = epoch;
        rendezvousBody = body; rendezvousTarget = target;
        rendezvousBody.FlightConfigurationChanged += OnRendezvousConfigurationChanged;
        rendezvousTarget.FlightConfigurationChanged += OnRendezvousConfigurationChanged;
        foreach (var node in nodes) rendezvousQueue.Enqueue(node);
        AdvanceRendezvousPlan();
        return true;
    }

    private void AdvanceRendezvousPlan()
    {
        if (CurrentNode != null) visualController?.DestroyVisual(CurrentNode);
        CurrentNode = rendezvousQueue.Count > 0 ? rendezvousQueue.Dequeue() : null;
        if (CurrentNode == null)
        {
            // Finishing thrust is not arrival. Keep the plan alive through the coast.
            uiController?.ClearManeuverFeedback();
            RefreshSetupNodeButtonState();
            return;
        }
        // The next burn's preview starts at that burn. The completed transfer coast
        // is now represented by the actual orbit and must not remain as a duplicate.
        trajectoryRenderer?.ClearPreview();
        previewController?.Clear();
        trajectoryRenderer?.ShowRendezvousPlan(rendezvousPlan.Prediction,
            (double)CurrentNode.burnTime - rendezvousEpoch, rendezvousPlan.ArrivalSeconds);
        visualController?.SetupNodeVisuals(CurrentNode, isPreview:false, manager:this);
        uiController?.SetEditingEnabled(false);
        RefreshSetupNodeButtonState();
        // Rendezvous owns the queued burns and reports their state in its plan readout.
        // The ordinary single-node instruction is misleading for this sequence.
        uiController?.ClearManeuverFeedback();
        uiRoot?.RefreshAllUi();
    }

    // Completion advances the queue; explicit removal/cancellation always clears the whole plan.
    public void CompleteNode(ManeuverNode node)
    {
        if (node != CurrentNode) return;
        if (HasRendezvousPlan) { AdvanceRendezvousPlan(); return; }
        visualController?.DestroyVisual(CurrentNode);
        CurrentNode = manualQueue.Count > 0 ? manualQueue.Dequeue() : null;
        if (CurrentNode == null && draftNode == null) ClearNode();
        else
        {
            RefreshSetupNodeButtonState();
            uiRoot?.RefreshAllUi();
        }
    }

    public OrbitalParameters PreviewOrbitParams =>
        previewController != null ? previewController.PreviewOrbitParams : new OrbitalParameters(false);

    private bool _initialized;
    private bool _previewNodeSnapshotRefreshInFlight;
    private float _nextPreviewNodeSnapshotRefreshTime;

    public void Initialize(SimContext ctx)
    {
        if (_initialized)
            return;

        _initialized = true;

        bodyRuntimeCoordinator = ctx.BodyRuntimeCoordinator;
        if (bodyRuntimeCoordinator != null) bodyRuntimeCoordinator.SatellitesCollided += OnSatelliteCollision;
        trajectoryRenderer = ctx.TrajectoryRenderer;
        timeController = ctx.TimeController;
        tutorialController = ctx.TutorialController;
        thrustController = ctx.ThrustController;
        uiRoot = ctx.UIRoot;
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
            RefreshSetupNodeButtonState();
        }

        if (previewController != null)
        {
            previewController.Initialize(
                bodyService: bodyService,
                bodyRuntimeCoordinator: bodyRuntimeCoordinator,
                trajectoryRenderer: trajectoryRenderer
            );
            previewController.AccuratePreviewLimitExceededChanged += OnAccuratePreviewLimitExceededChanged;
        }

        if (trajectoryRenderer != null)
            trajectoryRenderer.TrackedBodyChanged += OnTrackedBodyChanged;
        ObserveFlightConfiguration(ctx.CameraTracker?.CurrentBody);
    }

    private void LateUpdate()
    {
        uiController?.SetInsufficientFuelWarning(EditingNode != null && EditingNode.insufficientPropellant);
        UpdatePinnedNodeVisuals();
    }

    private void OnDestroy()
    {
        if (rendezvousBody != null) rendezvousBody.FlightConfigurationChanged -= OnRendezvousConfigurationChanged;
        if (rendezvousTarget != null) rendezvousTarget.FlightConfigurationChanged -= OnRendezvousConfigurationChanged;
        if (bodyRuntimeCoordinator != null) bodyRuntimeCoordinator.SatellitesCollided -= OnSatelliteCollision;
        ObserveFlightConfiguration(null);
        if (trajectoryRenderer != null)
            trajectoryRenderer.TrackedBodyChanged -= OnTrackedBodyChanged;

        if (uiController != null)
        {
            uiController.NodeTimeSliderChanged -= SetNodeAtFloatIndex;
            uiController.BurnDurationChanged -= OnBurnDurationChangedFromUI;
            uiController.ThrustScaleChanged -= OnThrustScaleChangedFromUI;
            uiController.Dispose();
        }

        if (previewController != null)
            previewController.AccuratePreviewLimitExceededChanged -= OnAccuratePreviewLimitExceededChanged;
    }

    private void UpdatePinnedNodeVisuals()
    {
        if (!HasNode && draftNode == null) return;
        var node = EditingNode;

        if (node.isFinalized && node.isPinned && node.marker != null)
        {
            if (node.marker.transform.position != node.pinnedWorldPosition)
                node.marker.transform.position = node.pinnedWorldPosition;

            if (node.marker.transform.parent != null)
                node.marker.transform.SetParent(null, true);
        }

    }

    private void OnTrackedBodyChanged(NBody oldBody, NBody newBody)
    {
        ObserveFlightConfiguration(newBody);
        if ((HasNode || HasRendezvousPlan) && oldBody != newBody)
        {
            ClearNode();
            return;
        }

        bool active = EditingNode != null && EditingNode.targetBody == newBody &&
                      !EditingNode.isFinalized;

        uiController?.SetNodeTimeSliderInteractable(active);
    }

    public void OnAddManeuverNode()
    {
        if (HasRendezvousPlan) return;
        if (ManualNodeCount >= MaxManualNodes)
        {
            uiController?.ShowManualNodeLimitFeedback(MaxManualNodes);
            return;
        }
        bool addingToSequence = CurrentNode != null && CurrentNode.isFinalized;
        if (draftNode != null) RemoveDraftNode();
        else if (HasNode && !addingToSequence)
            ClearNode();

        if (bodyRuntimeCoordinator != null && bodyRuntimeCoordinator.IsNodeBurnInProgress)
        {
            uiController?.ShowBurnInProgressFeedback();
            return;
        }

        if (timeController != null)
        {
            timeController.SetTimeScale(1f);
        }

        var body = trajectoryRenderer != null ? trajectoryRenderer.trackedBody : null;
        if (body == null)
        {
            uiController?.ShowNoTrackedBodyFeedback();
            return;
        }

        ManeuverNode precedingNode = null;
        if (addingToSequence)
        {
            precedingNode = CurrentNode;
            foreach (var queued in manualQueue) precedingNode = queued;
            previewController?.RequestReadoutRefresh(precedingNode, immediate: true);
            if (!precedingNode.hasPredictedPostBurnState)
            {
                uiController?.ShowNodeSnapshotUnavailableFeedback();
                return;
            }
        }

        System.Action<List<Vector3>, float, float> onSnapshot = (traj, startTime, usedDt) =>
        {
            if (!this || (addingToSequence && (CurrentNode == null || !CurrentNode.isFinalized))) return;
            if (traj == null || traj.Count < 2)
            {
                uiController?.ShowNodeSnapshotUnavailableFeedback();
                return;
            }

            float simTime = bodyRuntimeCoordinator != null ? bodyRuntimeCoordinator.simulationTime : 0f;
            float initialOffsetTime = 20f;
            float desiredBurnTime = addingToSequence
                ? Mathf.Max(simTime + initialOffsetTime, GetLastManualBurnEnd() + initialOffsetTime)
                : simTime + initialOffsetTime;
            if (desiredBurnTime > startTime + (traj.Count - 1) * usedDt)
            {
                uiController?.ShowNodeSnapshotUnavailableFeedback();
                return;
            }

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

            if (precedingNode != null)
            {
                node.hasPredictionSeed = true;
                node.predictionSeedTime = precedingNode.predictedBurnEndTime;
                node.predictionSeedPosition = precedingNode.predictedPostBurnPosition;
                node.predictionSeedVelocity = precedingNode.predictedPostBurnVelocity;
                node.predictionSeedMassKg = precedingNode.predictedPostBurnMassKg;
                node.predictionSeedFuelKg = precedingNode.predictedPostBurnFuelKg;
            }

            ActivatePreviewNode(node, focusCamera: false);

            if (tutorialController != null && tutorialController.inTutorialMode)
                tutorialController.hasSetupNode = true;
        };
        if (precedingNode != null)
            RequestPostBurnNodeSnapshot(precedingNode, onSnapshot);
        else
            RequestSingleOrbitNodeSnapshot(body, onSnapshot);
    }

    public void FinalizeManeuver()
    {
        if (HasRendezvousPlan) return;
        if (EditingNode == null)
            return;

        var node = EditingNode;
        if (node == null || node.marker == null)
            return;

        ResolveNodeBurnTimeIntoFuture(node);
        if (draftNode == node && node.burnTime < GetLastManualBurnEnd() + 1f)
            node.burnTime = GetLastManualBurnEnd() + 1f;
        BuildNodeFixedStepSchedule(node);

        node.isFinalized = true;
        node.marker.transform.SetParent(null, true);
        node.pinnedWorldPosition = node.marker.transform.position;
        node.isPinned = true;

        if (draftNode == node)
        {
            if (CurrentNode == null) CurrentNode = node;
            else manualQueue.Enqueue(node);
            draftNode = null;
        }

        thrustController?.StopForwardThrust();
        visualController?.SetupNodeVisuals(node, isPreview: false, manager: this);
        uiController?.SetEditingEnabled(false);

        if (tutorialController != null && tutorialController.inTutorialMode)
            tutorialController.hasPlacedNode = true;

        trajectoryRenderer?.CommitPreviewAsPlannedManeuver();
        trajectoryRenderer?.ClearPreview();
        previewController?.RequestReadoutRefresh(node, immediate: true);

        RefreshSetupNodeButtonState();
        uiController?.ShowFinalizedManeuverFeedback(ManualNodeCount);
        uiRoot?.RefreshAllUi();
    }

    private void BuildNodeFixedStepSchedule(ManeuverNode node)
    {
        if (node == null || bodyRuntimeCoordinator == null)
            return;

        float scheduleDt = BodyRuntimeCoordinator.BaseSimulationStep;
        if (scheduleDt <= 0f)
            return;

        float currentTime = bodyRuntimeCoordinator.simulationTime;

        int startStep = Mathf.Max(
            Mathf.CeilToInt(currentTime / scheduleDt),
            Mathf.CeilToInt(node.burnTime / scheduleDt)
        );

        int burnSteps = Mathf.Max(1, Mathf.CeilToInt(node.duration / scheduleDt));

        node.burnStartStep = startStep;
        node.burnStepCount = burnSteps;

        node.burnTime = node.burnStartStep * scheduleDt;
        node.duration = node.burnStepCount * scheduleDt;
    }

    public void CreatePreviewNode(Vector3 position, float burnTime, Vector3 deltaV, float duration)
    {
        if (HasRendezvousPlan) return;
        if (ManualNodeCount >= MaxManualNodes)
        {
            uiController?.ShowManualNodeLimitFeedback(MaxManualNodes);
            return;
        }
        var trackedBody = trajectoryRenderer != null ? trajectoryRenderer.trackedBody : null;

        var node = new ManeuverNode
        {
            position = position,
            burnTime = burnTime,
            deltaV = deltaV,
            targetBody = trackedBody,
            duration = duration,
            isFinalized = false,
            burnType = GetBurnChoice(),
            trajectorySnapshot = new List<Vector3>(),
            snapshotStartTime = bodyRuntimeCoordinator != null ? bodyRuntimeCoordinator.simulationTime : 0f,
            snapshotDeltaTime = trajectoryRenderer != null ? Mathf.Max(1e-5f, trajectoryRenderer.predictionDeltaTime) : 1f
        };

        ActivatePreviewNode(node, focusCamera: true);

        if (trackedBody != null)
        {
            RequestSingleOrbitNodeSnapshot(
                trackedBody,
                (traj, startTime, usedDt) =>
                {
                    if (!this || EditingNode != node || node.isFinalized)
                        return;

                    if (traj == null || traj.Count < 2)
                        return;

                    ApplySnapshotToNode(node, traj, startTime, usedDt, rebuildPreview: true);
                });
        }
    }

    private void ActivatePreviewNode(ManeuverNode node, bool focusCamera)
    {
        if (node == null)
            return;

        if (CurrentNode != null && CurrentNode.isFinalized)
        {
            RemoveDraftNode();
            draftNode = node;
        }
        else
        {
            ClearNode();
            CurrentNode = node;
        }

        visualController?.SetupNodeVisuals(node, isPreview: true, manager: this);

        UpdateManeuverPrediction(node);

        if (focusCamera && node.marker != null)
            visualController?.FocusCameraOn(node.marker.transform.position);

        previewController?.RequestPreview(node, interactionActive: false);
        _previewNodeSnapshotRefreshInFlight = false;
        _nextPreviewNodeSnapshotRefreshTime = Time.unscaledTime + previewNodeSnapshotRefreshInterval;

        uiController?.SetEditingEnabled(true);
        uiController?.SetupNodeSlider(node);
        RefreshSetupNodeButtonState();
        uiController?.ShowPreviewManeuverFeedback(draftNode != null);
    }

    public void ClearNode()
    {
        bool canceledRendezvous = HasRendezvousPlan;
        if (rendezvousBody != null) rendezvousBody.FlightConfigurationChanged -= OnRendezvousConfigurationChanged;
        if (rendezvousTarget != null) rendezvousTarget.FlightConfigurationChanged -= OnRendezvousConfigurationChanged;
        rendezvousQueue.Clear();
        foreach (var queued in manualQueue) visualController?.DestroyVisual(queued);
        manualQueue.Clear();
        RemoveDraftNode();
        rendezvousPlan = null;
        rendezvousBody = rendezvousTarget = null;
        HasRendezvousPlan = false;
        if (thrustController != null && thrustController.IsNodeBurnActive)
            thrustController.StopNodeBurn();

        if (CurrentNode != null)
            visualController?.DestroyVisual(CurrentNode);

        CurrentNode = null;
        _previewNodeSnapshotRefreshInFlight = false;
        _nextPreviewNodeSnapshotRefreshTime = 0f;

        trajectoryRenderer?.ClearPreview();
        trajectoryRenderer?.ClearPlannedManeuver();
        previewController?.Clear();
        uiController?.ResetEditingUI();
        uiController?.SetInsufficientFuelWarning(false);
        uiController?.ClearManeuverFeedback();
        RefreshSetupNodeButtonState();
        uiRoot?.RefreshAllUi();
        if (canceledRendezvous) RendezvousEnded?.Invoke(RendezvousOutcome.Canceled, "Rendezvous canceled.");
    }

    private float GetLastManualBurnEnd()
    {
        float end = CurrentNode != null && CurrentNode.isFinalized
            ? ManeuverBurnMath.GetBurnEndTime(CurrentNode) : 0f;
        foreach (var node in manualQueue)
            end = Mathf.Max(end, ManeuverBurnMath.GetBurnEndTime(node));
        return end;
    }

    private void RemoveDraftNode()
    {
        if (draftNode == null) return;
        visualController?.DestroyVisual(draftNode);
        draftNode = null;
        previewController?.Clear();
        trajectoryRenderer?.ClearPreview();
        uiController?.ResetEditingUI();
        RefreshSetupNodeButtonState();
    }

    private void RefreshPreviewNodeSnapshotIfNeeded()
    {
        if (EditingNode == null || trajectoryRenderer == null)
            return;

        var node = EditingNode;
        if (node == null || node.isFinalized || node.targetBody == null)
            return;

        if (trajectoryRenderer.trackedBody != node.targetBody)
            return;

        if (_previewNodeSnapshotRefreshInFlight || Time.unscaledTime < _nextPreviewNodeSnapshotRefreshTime)
            return;

        _nextPreviewNodeSnapshotRefreshTime = Time.unscaledTime + previewNodeSnapshotRefreshInterval;

        RequestPreviewNodeSnapshotRefresh(node);
    }

    private void RequestPreviewNodeSnapshotRefresh(ManeuverNode node)
    {
        if (node == null || node.targetBody == null)
            return;

        _previewNodeSnapshotRefreshInFlight = true;

        RequestSingleOrbitNodeSnapshot(
            node.targetBody,
            (traj, startTime, usedDt) =>
            {
                _previewNodeSnapshotRefreshInFlight = false;

                if (!this || EditingNode != node || node.isFinalized)
                    return;

                if (traj == null || traj.Count < 2)
                    return;

                ApplySnapshotToNode(node, traj, startTime, usedDt, rebuildPreview: true);
            });
    }

    private void RequestSingleOrbitNodeSnapshot(
        NBody body,
        System.Action<List<Vector3>, float, float> onComplete)
    {
        if (body == null)
        {
            onComplete?.Invoke(new List<Vector3>(), 0f, 1f);
            return;
        }

        ResolveSingleOrbitNodePredictionSettings(body, out int steps, out float dt);
        body.ComputePredictionForNodes(steps, dt, onComplete);
    }

    private void RequestPostBurnNodeSnapshot(
        ManeuverNode precedingNode,
        System.Action<List<Vector3>, float, float> onComplete)
    {
        var central = bodyService != null ? bodyService.CentralBody : null;
        if (precedingNode == null || !precedingNode.hasPredictedPostBurnState || central == null)
        {
            onComplete?.Invoke(new List<Vector3>(), 0f, 1f);
            return;
        }

        double3 startPos = precedingNode.predictedPostBurnPosition;
        double3 startVel = precedingNode.predictedPostBurnVelocity;
        double mu = PhysicsConstants.GDouble * central.TotalMassKilograms;
        OrbitalParameters orbit = OrbitalCalculations.CalculateOrbitalParameters(
            central.TotalMassKilograms, central.state.position, startPos, startVel);
        float baseDt = trajectoryRenderer != null && trajectoryRenderer.predictionDeltaTime > 0f
            ? trajectoryRenderer.predictionDeltaTime : 2f;
        float horizon = orbit.isValid && orbit.eccentricity < 1f && orbit.orbitalPeriod > 0f
            ? Mathf.Min(orbit.orbitalPeriod, 12000f) : 12000f;
        int steps = Mathf.Clamp(Mathf.CeilToInt(horizon / baseDt),
            minNodeOrbitSamples, maxNodeOrbitSamples);
        float dt = horizon / steps;
        var points = new List<Vector3>(steps + 1);
        for (int i = 0; i <= steps; i++)
        {
            if (!KeplerPropagator.TryPropagateUniversal(startPos, startVel, mu,
                i * (double)dt, out double3 position, out _))
            {
                onComplete?.Invoke(new List<Vector3>(), 0f, 1f);
                return;
            }
            points.Add(position.ToVector3());
        }
        onComplete?.Invoke(points, precedingNode.predictedBurnEndTime, dt);
    }

    private void ResolveSingleOrbitNodePredictionSettings(NBody body, out int steps, out float dt)
    {
        dt = trajectoryRenderer != null && trajectoryRenderer.predictionDeltaTime > 0f
            ? trajectoryRenderer.predictionDeltaTime
            : 2f;
        steps = Mathf.Max(maxNodeOrbitSamples, minNodeOrbitSamples);

        if (body == null || bodyService == null || bodyService.CentralBody == null)
            return;

        NBody central = bodyService.CentralBody;
        OrbitalParameters orbit = OrbitalCalculations.CalculateOrbitalParameters(
            central.TotalMassKilograms,
            central.state.position,
            body.state.position,
            body.state.velocity
        );

        if (!orbit.isValid || orbit.eccentricity >= 1f || orbit.orbitalPeriod <= 0f)
            return;

        int targetSamples = Mathf.CeilToInt(orbit.orbitalPeriod / Mathf.Max(1e-5f, dt));
        targetSamples = Mathf.Clamp(targetSamples, minNodeOrbitSamples, maxNodeOrbitSamples);

        steps = Mathf.Max(2, targetSamples);
        dt = orbit.orbitalPeriod / steps;
    }

    private void ApplySnapshotToNode(
        ManeuverNode node,
        List<Vector3> snapshot,
        float startTime,
        float sampleDt,
        bool rebuildPreview)
    {
        if (node == null || snapshot == null || snapshot.Count < 2)
            return;

        node.trajectorySnapshot = new List<Vector3>(snapshot);
        node.snapshotStartTime = startTime;
        node.snapshotDeltaTime = Mathf.Max(1e-5f, sampleDt);
        ResolveNodeBurnTimeIntoFuture(node);
        node.burnTime = Mathf.Max(node.burnTime, node.snapshotStartTime);
        if (node == draftNode)
            node.burnTime = Mathf.Max(node.burnTime, GetLastManualBurnEnd() + 1f);

        UpdateManeuverPrediction(node);

        if (node.marker != null)
            node.marker.transform.position = node.position;

        uiController?.SetupNodeSlider(node);

        if (rebuildPreview)
            previewController?.RequestPreview(node, interactionActive: false);
    }

    public void RemoveNode(ManeuverNode node)
    {
        if (node == draftNode) RemoveDraftNode();
        else if (node == CurrentNode) ClearNode();
    }

    public void DragNodeToFloatIndex(float floatIndex)
    {
        SetNodeAtFloatIndex(floatIndex);
    }

    public bool TryGetCurrentNodeIndex(out float currentFloatIndex)
    {
        currentFloatIndex = 0f;

        if (EditingNode == null)
            return false;

        var node = EditingNode;
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

        node ??= EditingNode;
        if (node == null || node.isFinalized)
            return;

        if (!TrajectorySampler.TrySampleAtBurnTime(node, out var pos, out _, out _))
            return;

        node.position = pos;
    }

    public void SetNodeAtFloatIndex(float floatIndex)
    {
        if (EditingNode == null)
            return;

        var node = EditingNode;
        if (node.isFinalized)
            return;

        var traj = node.trajectorySnapshot;
        if (traj == null || traj.Count < 2)
            return;

        int count = traj.Count;
        if (node == draftNode)
        {
            float earliest = (GetLastManualBurnEnd() + 1f - node.snapshotStartTime) /
                Mathf.Max(1e-5f, node.snapshotDeltaTime);
            floatIndex = Mathf.Max(floatIndex, earliest);
        }
        floatIndex = Mathf.Clamp(floatIndex, 0f, count - 1.0001f);

        float sampleDt = Mathf.Max(1e-5f, node.snapshotDeltaTime);
        float newBurnTime = node.snapshotStartTime + floatIndex * sampleDt;

        Vector3 p = TrajectorySampler.SampleAtIndex(traj, floatIndex);

        if (Mathf.Abs(newBurnTime - node.burnTime) <= 1e-4f &&
            (p - node.position).sqrMagnitude <= 1e-6f)
        {
            return;
        }

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

        if (EditingNode != null && !EditingNode.isFinalized)
        {
            EditingNode.duration = burnDuration;
            previewController?.RequestPreview(EditingNode, interactionActive: true);
            MarkAdjusted();
        }
    }

    private void OnThrustScaleChangedFromUI(float newScale)
    {
        if (configurationBody != null)
        {
            configurationBody.TryConfigurePropulsion(configurationBody.EngineThrustNewtons,
                configurationBody.SpecificImpulseSeconds, newScale);
            uiController?.SetThrustScaleWithoutNotify(configurationBody.ThrustScale);
        }
    }

    private void ObserveFlightConfiguration(NBody body)
    {
        if (configurationBody != null)
            configurationBody.FlightConfigurationChanged -= OnFlightConfigurationChanged;
        configurationBody = body;
        if (configurationBody == null) return;
        configurationBody.FlightConfigurationChanged += OnFlightConfigurationChanged;
        uiController?.SetThrustScaleWithoutNotify(configurationBody.ThrustScale);
    }

    private void OnFlightConfigurationChanged(NBody body)
    {
        uiController?.SetThrustScaleWithoutNotify(body.ThrustScale);
        trajectoryRenderer?.RequestPredictionRefresh();
        if (EditingNode != null && EditingNode.targetBody == body && !EditingNode.isFinalized)
        {
            previewController?.RequestPreview(EditingNode, interactionActive: false);
            MarkAdjusted();
        }
    }

    private void OnRendezvousConfigurationChanged(NBody body) =>
        FinishRendezvous(RendezvousOutcome.Failed, "Flight configuration changed; rebuild the rendezvous plan.");

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

    public void SetSetupNodeButtonInteractable(bool interactable)
    {
        bool blockedByExistingNode = HasRendezvousPlan || ManualNodeCount >= MaxManualNodes;
        uiController?.SetSetupNodeButtonState(
            interactable && !blockedByExistingNode,
            blockedByExistingNode
        );
    }

    private void RefreshSetupNodeButtonState()
    {
        bool nodeBurnActive = bodyRuntimeCoordinator != null && bodyRuntimeCoordinator.IsNodeBurnInProgress;
        SetSetupNodeButtonInteractable(!nodeBurnActive);
        uiController?.SetManualSequenceState(ManualNodeCount, draftNode != null);
    }

    private void OnAccuratePreviewLimitExceededChanged(bool exceeded)
    {
        if (exceeded)
            uiController?.ShowPreviewHorizonLimitFeedback();
        else if (EditingNode != null && !EditingNode.isFinalized)
            uiController?.ShowPreviewManeuverFeedback(draftNode != null);
    }

    private float GetTimeToNode(ManeuverNode node)
    {
        if (node == null || bodyRuntimeCoordinator == null)
            return float.NaN;

        float simTime = bodyRuntimeCoordinator.simulationTime;
        if (node.isFinalized)
            return node.burnTime - simTime;

        if (node.hasPredictionSeed)
            return node.burnTime - simTime;

        return ManeuverNodeTiming.TryGetBoundOrbitPeriod(bodyService, node.targetBody, out float orbitalPeriod)
            ? ManeuverNodeTiming.GetTimeToNode(node.burnTime, simTime, orbitalPeriod)
            : node.burnTime - simTime;
    }

    private void ResolveNodeBurnTimeIntoFuture(ManeuverNode node)
    {
        if (node == null || bodyRuntimeCoordinator == null)
            return;

        if (node.hasPredictionSeed)
            return;

        if (!ManeuverNodeTiming.TryGetBoundOrbitPeriod(bodyService, node.targetBody, out float orbitalPeriod))
            return;

        node.burnTime = ManeuverNodeTiming.ResolveFutureBurnTime(
            node.burnTime,
            bodyRuntimeCoordinator.simulationTime,
            orbitalPeriod
        );
    }

}
