using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Rendezvous selection, relative telemetry, marker, and optional visual pair focus.
/// Never transfers spacecraft control. Owned and disposed by FlightUIController.
/// </summary>
public sealed class RendezvousPanelUIController : IDisposable
{
    private readonly UIReferences refs;
    private readonly ICameraTracker tracker;
    private readonly BodyService bodies;
    private readonly CameraMovement cameraMovement;
    private readonly TrajectoryRenderer trajectories;
    private readonly BodyRuntimeCoordinator runtime;
    private readonly ManeuverNodeManager maneuvers;
    private readonly Action modeChanged;
    private readonly RendezvousPlanUIController planPanel;
    private Task<TrajectoryMatchedPredictor.EncounterTimeline> encounterTask;
    private TrajectoryMatchedPredictor.EncounterTimeline encounterTimeline;
    private ManeuverNode observedNode;
    private float observedBurnTime, observedDuration;
    private BurnType observedBurnType;
    private bool observedUsesVector;
    private Vector3 observedVectorDirection;
    private bool observedFinalized;
    private double planStartEpoch;
    private bool encounterIncludesNode, taskIncludesNode;
    private double encounterSearchStart, taskSearchStart;
    private CancellationTokenSource encounterCancellation;
    private int encounterGeneration, taskGeneration;
    private double encounterEpoch, taskEpoch;
    private float nextEncounterRefresh;
    private bool hasEncounter;
    private TrajectoryMatchedPredictor.Encounter encounter;
    private string encounterMessage = "Select a target to predict closest approach.";
    private Graphic markerGraphic;
    private Canvas markerCanvas;
    private float nextMarkerTextTime;
    private int markerFrame = -1;
    private readonly HashSet<NBody> registered = new();
    private readonly List<NBody> options = new();
    private static readonly CultureInfo Numbers = CultureInfo.InvariantCulture;
    private NBody controlled;
    private NBody target;
    private bool open;
    private bool gameplayVisible = true;
    private bool optionsDirty = true;
    private float nextReadoutTime;

    public NBody ControlledBody => controlled;
    public NBody TargetBody => target;
    public bool IsModeActive => open && gameplayVisible && controlled != null;

    public RendezvousPanelUIController(UIReferences refs, ICameraTracker tracker, BodyService bodies,
        CameraMovement cameraMovement = null, TrajectoryRenderer trajectories = null,
        BodyRuntimeCoordinator runtime = null, ManeuverNodeManager maneuvers = null,
        Action modeChanged = null)
    {
        this.refs = refs;
        this.tracker = tracker;
        this.bodies = bodies;
        this.cameraMovement = cameraMovement;
        this.trajectories = trajectories;
        this.runtime = runtime;
        this.maneuvers = maneuvers;
        this.modeChanged = modeChanged;
        planPanel = new RendezvousPlanUIController(refs, bodies, runtime, maneuvers, trajectories,
            ShowPlanProposal, InvalidateEncounter);
    }

    public void Initialize()
    {
        planPanel.Initialize();
        InitializeEncounterMarker(refs.rendezvousControlledEncounterMarker);
        InitializeEncounterMarker(refs.rendezvousTargetEncounterMarker);
        if (refs.rendezvousViewTargetButton != null) refs.rendezvousViewTargetButton.onClick.AddListener(ViewTarget);
        if (refs.rendezvousTargetMarker != null)
        {
            markerCanvas = refs.rendezvousTargetMarker.GetComponentInParent<Canvas>();
            markerGraphic = refs.rendezvousTargetMarker.GetComponent<Graphic>();
            foreach (Graphic graphic in refs.rendezvousTargetMarker.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            refs.rendezvousTargetMarker.gameObject.SetActive(false);
        }
        Canvas.willRenderCanvases += UpdateMarker;
        if (refs.rendezvousPanelButton != null) refs.rendezvousPanelButton.onClick.AddListener(Toggle);
        if (refs.rendezvousCancelTargetButton != null) refs.rendezvousCancelTargetButton.onClick.AddListener(CancelTarget);
        if (refs.rendezvousTargetDropdown != null) refs.rendezvousTargetDropdown.onValueChanged.AddListener(SelectTarget);
        if (tracker != null)
        {
            tracker.OnModeChanged += OnModeChanged;
            tracker.OnTrackedBodyChanged += OnTrackedBodyChanged;
        }
        if (bodies != null) bodies.MembershipChanged += OnMembershipChanged;
        OnMembershipChanged();
    }

    public void Dispose()
    {
        planPanel.Dispose();
        InvalidateEncounter();
        if (target != null) target.FlightConfigurationChanged -= OnEncounterConfigurationChanged;
        if (controlled != null) controlled.FlightConfigurationChanged -= OnEncounterConfigurationChanged;
        if (encounterTask != null)
        {
            var cancellation = encounterCancellation;
            encounterTask.ContinueWith(completed => { _ = completed.Exception; cancellation.Dispose(); });
            encounterTask = null;
            encounterCancellation = null;
        }
        if (target != null) target.IsRendezvousRenderTarget = false;
        trajectories?.SetRendezvousOrbit(null, false, Color.clear);
        Canvas.willRenderCanvases -= UpdateMarker;
        cameraMovement?.ClearRendezvousFocus();
        SetMarkerVisible(false);
        if (refs.rendezvousViewTargetButton != null) refs.rendezvousViewTargetButton.onClick.RemoveListener(ViewTarget);
        if (refs.rendezvousPanelButton != null) refs.rendezvousPanelButton.onClick.RemoveListener(Toggle);
        if (refs.rendezvousCancelTargetButton != null) refs.rendezvousCancelTargetButton.onClick.RemoveListener(CancelTarget);
        if (refs.rendezvousTargetDropdown != null) refs.rendezvousTargetDropdown.onValueChanged.RemoveListener(SelectTarget);
        if (tracker != null)
        {
            tracker.OnModeChanged -= OnModeChanged;
            tracker.OnTrackedBodyChanged -= OnTrackedBodyChanged;
        }
        if (bodies != null) bodies.MembershipChanged -= OnMembershipChanged;
        controlled = target = null;
        open = false;
        ApplyVisibility();
    }

    private bool IsEligible(NBody body) => body != null && registered.Contains(body) &&
        body.isActiveAndEnabled && !body.isCentralBody && !body.isReferenceOrbit;

    private void OnMembershipChanged()
    {
        registered.Clear();
        if (bodies != null)
            foreach (NBody body in bodies.Bodies)
                if (body != null) registered.Add(body);
        optionsDirty = true;
        RefreshSelection();
    }

    private void OnModeChanged(CameraMode mode)
    {
        if (mode == CameraMode.Free)
        {
            ClearTarget("Rendezvous ended on entering Free Cam.");
            open = false;
        }
        RefreshSelection();
    }

    private void OnTrackedBodyChanged(NBody body) => RefreshSelection();

    public void RefreshSelection()
    {
        NBody selected = CameraVisibilityPolicy.SelectedBody(tracker);
        if (!IsEligible(selected)) selected = null;
        if (!ReferenceEquals(selected, controlled))
        {
            if (controlled != null) controlled.FlightConfigurationChanged -= OnEncounterConfigurationChanged;
            controlled = selected;
            if (controlled != null) controlled.FlightConfigurationChanged += OnEncounterConfigurationChanged;
            ClearTarget("Select another satellite to begin.");
        }
        if (!ReferenceEquals(target, null) && (!IsEligible(target) || target == controlled))
            ClearTarget("Target is no longer available. Select another satellite.");
        if (controlled == null) open = false;
        ApplyVisibility();
        if (open && gameplayVisible)
        {
            if (optionsDirty) RebuildOptions();
            RefreshReadouts();
        }
    }

    public void SetGameplayVisible(bool visible)
    {
        gameplayVisible = visible;
        ApplyVisibility();
        if (!visible) SetMarkerVisible(false);
    }

    public void Tick()
    {
        // Target validity and any scheduled plan continue in either UI mode.
        NBody selected = CameraVisibilityPolicy.SelectedBody(tracker);
        if (!ReferenceEquals(selected, controlled) ||
            (controlled != null && !IsEligible(controlled)) ||
            (!ReferenceEquals(target, null) && !IsEligible(target)))
            RefreshSelection();
        UpdateTargetOrbit();
        UpdateEncounter();
        // The UI mode is only a view; switching away must not discard a proposal.
        planPanel.Tick(controlled, target, gameplayVisible);
        if (planPanel.ConsumePlanOutcome(out string outcome, out bool succeeded))
        {
            if (succeeded) { ClearTarget(outcome, true); RefreshSelection(); return; }

        }
        if (!open || !gameplayVisible || Time.unscaledTime < nextReadoutTime) return;
        nextReadoutTime = Time.unscaledTime + 0.1f;
        if (optionsDirty) RebuildOptions();
        RefreshReadouts();
    }

    private void Toggle()
    {
        RefreshSelection();
        if (controlled == null || !gameplayVisible || refs.rendezvousPanel == null) return;
        if (open)
        {
            ClearTarget("Rendezvous canceled. Select another satellite to begin.");
            open = false;
        }
        else open = true;
        ApplyVisibility();
        if (open)
        {
            RebuildOptions();
            RefreshReadouts();
        }
        modeChanged?.Invoke();
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void ApplyVisibility()
    {
        if (!open || !gameplayVisible)
        {
            encounterCancellation?.Cancel();
            HideEncounterMarkers();
        }
        UpdateTargetOrbit();
        bool available = gameplayVisible && controlled != null;
        UIHelpers.SetActive(refs.rendezvousPanelButton != null ? refs.rendezvousPanelButton.gameObject : null, available);
        UIHelpers.SetActive(refs.rendezvousPanel, available && open);
        UpdateTargetActionVisibility();
        if (!available || !open) refs.rendezvousTargetDropdown?.Hide();
        if (refs.rendezvousPanelButton != null)
        {
            refs.rendezvousPanelButton.interactable = refs.rendezvousPanel != null;
            TMP_Text label = refs.rendezvousPanelButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = available && open ? "Normal Mode" : "Rendezvous Mode";
        }
    }

    private void RebuildOptions()
    {
        optionsDirty = false;
        options.Clear();
        var labels = new List<string> { "Select a target" };
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        if (bodies != null)
            foreach (NBody body in bodies.Bodies)
                if (IsEligible(body) && body != controlled)
                {
                    options.Add(body);
                    names.TryGetValue(body.name, out int count);
                    names[body.name] = count + 1;
                }
        int selectedIndex = 0;
        foreach (NBody body in options)
        {
            labels.Add(names[body.name] > 1 ? $"{body.name} (ID {body.GetInstanceID()})" : body.name);
            if (body == target) selectedIndex = labels.Count - 1;
        }
        if (refs.rendezvousTargetDropdown != null)
        {
            refs.rendezvousTargetDropdown.Hide();
            refs.rendezvousTargetDropdown.ClearOptions();
            refs.rendezvousTargetDropdown.AddOptions(labels);
            refs.rendezvousTargetDropdown.SetValueWithoutNotify(selectedIndex);
            refs.rendezvousTargetDropdown.interactable = controlled != null && options.Count > 0;
        }
    }

    private void SelectTarget(int index)
    {
        NBody previousControlled = controlled;
        NBody candidate = index > 0 && index <= options.Count ? options[index - 1] : null;
        RefreshSelection();
        if (!open || !gameplayVisible || controlled == null || controlled != previousControlled) return;
        if (index == 0) { CancelTarget(); return; }
        if (!IsEligible(candidate) || candidate == controlled)
        {
            optionsDirty = true;
            ClearTarget("Target is no longer available. Select another satellite.");
        }
        else
        {
            cameraMovement?.ClearRendezvousFocus();
            if (target != null)
            {
                target.IsRendezvousRenderTarget = false;
                target.FlightConfigurationChanged -= OnEncounterConfigurationChanged;
            }
            InvalidateEncounter();
            target = candidate;
            target.IsRendezvousRenderTarget = true;
            target.FlightConfigurationChanged += OnEncounterConfigurationChanged;
            nextMarkerTextTime = 0f;

        }
        RefreshSelection();
    }

    private void ClearTarget(string message, bool completedPlan = false)
    {
        if (completedPlan) planPanel.FinishCompletedPlan(message);
        else planPanel.CancelAll();
        InvalidateEncounter();
        if (target != null) target.FlightConfigurationChanged -= OnEncounterConfigurationChanged;
        if (target != null) target.IsRendezvousRenderTarget = false;
        trajectories?.SetRendezvousOrbit(null, false, Color.clear);
        cameraMovement?.ClearRendezvousFocus();
        SetMarkerVisible(false);
        target = null;

        optionsDirty = true;
        UpdateTargetActionVisibility();
        refs.rendezvousTargetDropdown?.Hide();
        refs.rendezvousTargetDropdown?.SetValueWithoutNotify(0);
    }

    public void CancelTarget()
    {
        ClearTarget("Target canceled. Select another satellite to begin.");
        RefreshSelection();
    }

    private void RefreshReadouts()
    {
        UpdateTargetActionVisibility();
        if (refs.rendezvousEncounterText != null) refs.rendezvousEncounterText.text = EncounterReadout();
        if (refs.rendezvousViewTargetButton != null)
        {
            refs.rendezvousViewTargetButton.interactable = controlled != null && target != null && cameraMovement != null;
            TMP_Text label = refs.rendezvousViewTargetButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = cameraMovement != null && cameraMovement.IsRendezvousView
                ? "Return to Satellite" : "View Target";
        }
        if (refs.rendezvousSelectionText != null)
            refs.rendezvousSelectionText.text = $"Controlled: {(controlled != null ? controlled.name : "None")}\nTarget: {(target != null ? target.name : "None")}";
        if (refs.rendezvousCancelTargetButton != null) refs.rendezvousCancelTargetButton.interactable = target != null;
        string readout = "Current separation: --\nDistance change: --";
        if (controlled != null && target != null)
        {
            if (TryMeasure(controlled.state.position, controlled.state.velocity, target.state.position,
                target.state.velocity, out double distanceMeters, out double rangeRate))
            {
                string change = distanceMeters < 1e-6 ? "undefined at contact"
                    : Math.Abs(rangeRate) < 0.0005 ? "steady"
                    : Math.Abs(rangeRate).ToString("F3", Numbers) +
                      (rangeRate < 0 ? " m/s closer" : " m/s farther");
                readout = "Right now\nSeparation: " + FormatDistance(distanceMeters) +
                    "\nDistance change: " + change;
                NBody central = bodies != null ? bodies.CentralBody : null;
                if (central != null && TryRelativeOffsets(controlled.state.position, target.state.position,
                    target.state.velocity, central.state.position, central.state.velocity, out double3 offsets))
                {
                    readout += "\n\nYour position in target's orbit" +
                        "\nAlong orbit: " + FormatOffset(offsets.y, "ahead", "behind") +
                        "\nRadial: " + FormatOffset(offsets.x, "outward", "inward") +
                        "\nCross-track: " + FormatOffset(offsets.z, "normal side", "anti-normal side");
                }
                else readout += "\nTarget's orbital directions unavailable.";
            }
            else readout = "Relative measurements unavailable: invalid physics state.";
        }
        if (refs.rendezvousReadoutText != null) refs.rendezvousReadoutText.text = readout;

    }

    private void UpdateTargetActionVisibility()
    {
        bool show = open && gameplayVisible && target != null;
        UIHelpers.SetActive(refs.rendezvousCancelTargetButton != null
            ? refs.rendezvousCancelTargetButton.gameObject : null, show);
        UIHelpers.SetActive(refs.rendezvousViewTargetButton != null
            ? refs.rendezvousViewTargetButton.gameObject : null, show);
    }

    private void OnEncounterConfigurationChanged(NBody body) => InvalidateEncounter();

    private void ShowPlanProposal(RendezvousPlanner.Result plan, double epoch)
    {
        InvalidateEncounter();
        encounterTimeline = plan.Prediction;
        encounterEpoch = epoch;
        encounterIncludesNode = true;
        encounterSearchStart = plan.ArrivalSeconds;
        hasEncounter = encounterTimeline.TryGetClosest(encounterSearchStart, out encounter);
    }

    private void InvalidateEncounter()
    {
        encounterGeneration++;
        encounterCancellation?.Cancel();
        hasEncounter = false;
        encounterTimeline = null;
        nextEncounterRefresh = 0;
        encounterMessage = "Waiting for encounter prediction...";
        HideEncounterMarkers();
    }

    private void UpdateEncounter()
    {
        bool available = open && gameplayVisible && controlled != null && target != null && runtime != null;
        if (available) ObserveEncounterPlan();
        bool burning = available && (controlled.isThrusting || target.isThrusting || runtime.IsNodeBurnInProgress);
        if (burning)
        {
            InvalidateEncounter();
            encounterMessage = "Prediction paused during actual thrust; it resumes after the burn.";
        }
        if (encounterTask != null && encounterTask.IsCompleted)
        {
            if (encounterTask.IsFaulted)
            {
                // Observe faults even for invalidated work; never show a result from an old selection.
                var error = encounterTask.Exception;
                if (taskGeneration == encounterGeneration)
                {
                    encounterMessage = "Encounter prediction unavailable. See the Console for details.";
                    Debug.LogException(error);
                }
            }
            else if (!encounterTask.IsCanceled && taskGeneration == encounterGeneration && available && !burning)
            {
                encounterTimeline = encounterTask.Result;
                encounterEpoch = taskEpoch;
                encounterIncludesNode = taskIncludesNode;
                encounterSearchStart = taskSearchStart;
                encounterMessage = encounterTimeline.Error;
            }
            encounterTask = null;
            encounterCancellation.Dispose();
            encounterCancellation = null;
            if (taskGeneration == encounterGeneration) nextEncounterRefresh = Time.unscaledTime + 5f;
        }
        if (!available || burning) return;
        // Give the planner sole use of the prediction worker budget while building a proposal.
        if (planPanel.IsCalculating)
        {
            encounterCancellation?.Cancel();
            return;
        }
        double age = runtime.SimulationTimeSeconds - encounterEpoch;
        if (encounterTimeline != null && encounterTimeline.IsValid)
        {
            if (encounterSearchStart > encounterTimeline.Horizon)
            {
                hasEncounter = false;
                encounterMessage = "No valid post-burn coverage. " + encounterTimeline.Notice;
                HideEncounterMarkers();
                return;
            }
            hasEncounter = encounterTimeline.TryGetClosest(Math.Max(age, encounterSearchStart), out encounter);
            if (hasEncounter && encounterTimeline.TrySample(age, out var expected))
            {
                // Compare with the predicted state, not wall-clock age. Warp alone is not invalidation.
                double positionError = Math.Max(math.distance(expected.A, controlled.state.position),
                    math.distance(expected.B, target.state.position));
                double velocityError = Math.Max(math.distance(expected.VA, controlled.state.velocity),
                    math.distance(expected.VB, target.state.velocity));
                if (positionError * SimulationUnits.MetersPerUnit > 100 ||
                    velocityError * SimulationUnits.MetersPerUnit > 0.5)
                {
                    InvalidateEncounter();
                    encounterMessage = "Flight state changed; updating encounter prediction...";
                }
            }
            if (!hasEncounter)
            {
                encounterMessage = "Prediction coverage exhausted or flight changed; updating...";
                nextEncounterRefresh = 0;
                HideEncounterMarkers();
            }
            // Extend coverage in the background while keeping the current future passes visible.
            else if (age < encounterTimeline.Horizon * 0.5) return;
        }
        // Keep at most one worker alive, including canceled work waiting to finish.
        if (encounterTask != null || Time.unscaledTime < nextEncounterRefresh) return;
        NBody central = bodies != null ? bodies.CentralBody : null;
        if (central == null || math.lengthsq(central.state.position) > 1e-12 ||
            math.lengthsq(central.state.velocity) > 1e-12)
        {
            hasEncounter = false;
            encounterMessage = "Coast prediction requires the fixed central-body frame.";
            nextEncounterRefresh = Time.unscaledTime + 5f;
            return;
        }
        var request = new TrajectoryPredictionRequest(1, 0.02f, runtime.simulationTime, 5, true,
            TrajectoryPredictionBackend.NativeMatched, 2);
        if (!TrajectoryMatchedPredictor.TryBuildWorkItem(controlled, bodies, request, out var a) ||
            !TrajectoryMatchedPredictor.TryBuildWorkItem(target, bodies, request, out var b)) return;
        // NativePhysics initializes through Unity APIs; ensure this happens on the main thread.
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(NativePhysics).TypeHandle);
        taskEpoch = runtime.SimulationTimeSeconds;
        taskGeneration = encounterGeneration;
        encounterCancellation = new CancellationTokenSource();
        CancellationToken token = encounterCancellation.Token;
        double horizon = ResolveEncounterHorizon();
        TrajectoryMatchedPredictor.EncounterBurn burn = default;
        taskSearchStart = 0;
        taskIncludesNode = observedNode != null;
        if (taskIncludesNode)
        {
            burn = observedUsesVector
                ? new TrajectoryMatchedPredictor.EncounterBurn(planStartEpoch - taskEpoch,
                    observedDuration, new double3(observedVectorDirection.x, observedVectorDirection.y,
                        observedVectorDirection.z), controlled.EffectiveThrustNewtons,
                    controlled.DryMassKilograms, controlled.FuelMassKilograms,
                    controlled.SpecificImpulseSeconds, !controlled.UnlimitedPropellant)
                : new TrajectoryMatchedPredictor.EncounterBurn(planStartEpoch - taskEpoch,
                    observedDuration, observedBurnType, controlled.EffectiveThrustNewtons,
                    controlled.DryMassKilograms, controlled.FuelMassKilograms,
                    controlled.SpecificImpulseSeconds, !controlled.UnlimitedPropellant);
            taskSearchStart = Math.Max(0, burn.End);
            if (burn.End >= horizon)
            {
                encounterMessage = "The maneuver ends beyond automatic prediction coverage.";
                encounterCancellation.Dispose(); encounterCancellation = null;
                nextEncounterRefresh = Time.unscaledTime + 5f;
                return;
            }
        }
        const bool autoWindow = true;
        TrajectoryMatchedPredictor.EncounterBurn[] sequence = null;
        if (maneuvers != null && maneuvers.HasRendezvousPlan)
        {
            var nodes = maneuvers.GetRendezvousSchedule();
            sequence = new TrajectoryMatchedPredictor.EncounterBurn[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
                sequence[i] = nodes[i].usesVectorDirection
                    ? new TrajectoryMatchedPredictor.EncounterBurn(nodes[i].burnTime-taskEpoch,
                        nodes[i].duration, new double3(nodes[i].vectorDirectionWorld.x,
                            nodes[i].vectorDirectionWorld.y, nodes[i].vectorDirectionWorld.z),
                        controlled.EffectiveThrustNewtons, controlled.DryMassKilograms,
                        controlled.FuelMassKilograms, controlled.SpecificImpulseSeconds,
                        !controlled.UnlimitedPropellant)
                    : new TrajectoryMatchedPredictor.EncounterBurn(nodes[i].burnTime-taskEpoch,
                        nodes[i].duration, nodes[i].burnType, controlled.EffectiveThrustNewtons,
                        controlled.DryMassKilograms, controlled.FuelMassKilograms,
                        controlled.SpecificImpulseSeconds, !controlled.UnlimitedPropellant);
            // The plan remains active while coasting to arrival after its last burn.
            // At that point the executable schedule is empty, but the encounter
            // prediction still needs to run from the current flight state.
            if (RendezvousFlightSchedule.TryGetLastBurnEnd(sequence, out double lastBurnEnd))
            {
                taskSearchStart = Math.Max(0, lastBurnEnd);
                horizon = Math.Min(TrajectoryMatchedPredictor.MaxEncounterHorizonSeconds,
                    Math.Max(horizon, taskSearchStart + 600));
            }
        }
        encounterTask = Task.Run(() => TrajectoryMatchedPredictor.PredictEncounterTimeline(a, b, horizon, token, burn, autoWindow, sequence), token);
        if (!hasEncounter) encounterMessage = "Calculating closest approach...";
    }

    private void ObserveEncounterPlan()
    {
        ManeuverNode node = maneuvers != null ? maneuvers.CurrentNode : null;
        if (node != null && node.targetBody != controlled) node = null;
        if (node != null && node.isFinalized && ManeuverBurnMath.GetBurnEndTime(node) <= runtime.simulationTime)
            node = null;
        double start = node != null ? node.burnTime : 0;
        if (node != null && !node.isFinalized &&
            ManeuverNodeTiming.TryGetBoundOrbitPeriod(bodies, controlled, out float period))
            start = ManeuverNodeTiming.ResolveFutureBurnTime(node.burnTime, runtime.simulationTime, period);
        bool changed = !ReferenceEquals(node, observedNode) || (node != null &&
            (node.burnTime != observedBurnTime || node.duration != observedDuration ||
             node.burnType != observedBurnType || node.usesVectorDirection != observedUsesVector ||
             node.vectorDirectionWorld != observedVectorDirection ||
             node.isFinalized != observedFinalized || Math.Abs(start - planStartEpoch) > 0.01));
        if (!changed) return;
        observedNode = node;
        observedBurnTime = node != null ? node.burnTime : 0;
        observedDuration = node != null ? node.duration : 0;
        observedBurnType = node != null ? node.burnType : default;
        observedUsesVector = node != null && node.usesVectorDirection;
        observedVectorDirection = node != null ? node.vectorDirectionWorld : Vector3.zero;
        observedFinalized = node != null && node.isFinalized;
        planStartEpoch = start;
        InvalidateEncounter();
        nextEncounterRefresh = Time.unscaledTime + 0.25f; // Debounce dragging/editing a node.
    }

    private double ResolveEncounterHorizon()
    {
        double horizon = 90 * 60;
        if (ManeuverNodeTiming.TryGetBoundOrbitPeriod(bodies, controlled, out float controlledPeriod))
            horizon = Math.Max(horizon, 2 * controlledPeriod);
        if (ManeuverNodeTiming.TryGetBoundOrbitPeriod(bodies, target, out float targetPeriod))
            horizon = Math.Max(horizon, 2 * targetPeriod);
        if (observedNode != null)
            horizon += Math.Max(0, planStartEpoch - runtime.simulationTime) + observedDuration;
        return Math.Min(horizon, TrajectoryMatchedPredictor.MaxEncounterHorizonSeconds);
    }

    private string EncounterReadout()
    {
        if (target == null) return "Select a target to predict closest approach.";
        if (runtime == null) return "Encounter prediction unavailable: simulation clock missing.";
        if (!hasEncounter) return encounterMessage ?? "Waiting for prediction...";
        double age = Math.Max(0, runtime.SimulationTimeSeconds - encounterEpoch);
        string timing = encounter.Seconds <= Math.Max(age, encounterSearchStart) + 0.01
            ? (encounterSearchStart > age ? "At maneuver end" : "Now")
            : encounter.Seconds >= encounterTimeline.Horizon - 0.01 ? "At automatic search limit"
            : "In " + UIHelpers.FormatCountdown(encounter.Seconds - age);
        return "Closest pass in search window\n" + timing +
            "\nMiss distance: " + FormatDistance(encounter.DistanceMeters) +
            "\nSpeed between craft: " + encounter.RelativeSpeedMetersPerSecond.ToString("F2", Numbers) + " m/s" +
            (encounterIncludesNode ? "\nIncludes planned burn" : "\nCoasting; no planned burn") +
            "\nWindow left: " + Math.Max(0, (encounterTimeline.Horizon - age) / 60).ToString("F1", Numbers) +
            " min (auto)" +
            (encounterTask != null ? "; extending" : "") +
            (string.IsNullOrEmpty(encounterTimeline.Notice) ? "" : "\n" + encounterTimeline.Notice);
    }

    private static void InitializeEncounterMarker(RectTransform marker)
    {
        if (marker == null) return;
        foreach (Graphic graphic in marker.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        UIHelpers.SetActive(marker.gameObject, false);
    }

    private void HideEncounterMarkers()
    {
        if (refs.rendezvousControlledEncounterMarker != null) UIHelpers.SetActive(refs.rendezvousControlledEncounterMarker.gameObject, false);
        if (refs.rendezvousTargetEncounterMarker != null) UIHelpers.SetActive(refs.rendezvousTargetEncounterMarker.gameObject, false);
    }

    private void DrawEncounterMarker(RectTransform marker, double3 position, Camera camera)
    {
        if (marker == null) return;
        Vector3 world = position.ToVector3();
        Canvas canvas = marker.GetComponentInParent<Canvas>();
        RectTransform parent = marker.parent as RectTransform;
        bool visible = open && gameplayVisible && hasEncounter && canvas != null && parent != null &&
            CameraVisibilityPolicy.IsInsideViewport(camera.WorldToViewportPoint(world), 0.01f) &&
            !CameraVisibilityPolicy.IsOccludedByCentralBody(camera.transform.position, world, bodies.CentralBody);
        if (visible)
        {
            Canvas root = canvas.rootCanvas;
            Camera uiCamera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            visible = RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, camera.WorldToScreenPoint(world),
                uiCamera, out Vector3 point);
            if (visible) marker.position = point;
        }
        UIHelpers.SetActive(marker.gameObject, visible);
    }

    private void UpdateTargetOrbit()
    {
        Color color = markerGraphic != null ? markerGraphic.color : new Color(1f, 0.65f, 0.1f);
        trajectories?.SetRendezvousOrbit(target,
            gameplayVisible && controlled != null && target != null &&
            tracker != null && tracker.Mode != CameraMode.Free, color);
    }

    private static string FormatOffset(double meters, string positive, string negative)
        => FormatDistance(Math.Abs(meters)) + " " + (meters >= 0 ? positive : negative);

    private static string FormatDistance(double meters) => meters >= 1000.0
        ? (meters / 1000.0).ToString("F3", Numbers) + " km" : meters.ToString("F3", Numbers) + " m";

    private void ViewTarget()
    {
        RefreshSelection();
        if (!open || !gameplayVisible || controlled == null || target == null || cameraMovement == null) return;
        if (cameraMovement.IsRendezvousView) cameraMovement.ClearRendezvousFocus();
        else
        {
            // Leaving Earth view selects the SAME controlled body, never the rendezvous target.
            if (tracker.IsEarthView) tracker.TrackBody(controlled);
            if (target != null) cameraMovement.FrameRendezvousTarget(controlled, target);
        }
        RefreshReadouts();
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void SetMarkerVisible(bool visible)
    {
        if (refs.rendezvousTargetMarker != null) UIHelpers.SetActive(refs.rendezvousTargetMarker.gameObject, visible);
    }

    // Canvas render callback runs after the camera's LateUpdate, avoiding a one-frame marker lag.
    private void UpdateMarker()
    {
        if (markerFrame == Time.frameCount) return;
        markerFrame = Time.frameCount;
        Camera camera = cameraMovement != null ? cameraMovement.MainCamera : null;
        if (camera != null && hasEncounter && open && gameplayVisible && target != null)
        {
            DrawEncounterMarker(refs.rendezvousControlledEncounterMarker, encounter.ControlledPosition, camera);
            DrawEncounterMarker(refs.rendezvousTargetEncounterMarker, encounter.TargetPosition, camera);
        }
        else HideEncounterMarkers();
        if (!gameplayVisible || tracker == null || tracker.Mode == CameraMode.Free ||
            !IsEligible(controlled) || !IsEligible(target) || camera == null || markerCanvas == null ||
            refs.rendezvousTargetMarker == null)
        {
            SetMarkerVisible(false);
            return;
        }
        Vector3 position = target.RenderPosition;
        Vector3 viewport = camera.WorldToViewportPoint(position);
        if (!CameraVisibilityPolicy.IsInsideViewport(viewport, 0.01f) ||
            CameraVisibilityPolicy.IsOccludedByCentralBody(camera.transform.position, position, bodies.CentralBody))
        {
            SetMarkerVisible(false);
            return;
        }
        RectTransform parent = refs.rendezvousTargetMarker.parent as RectTransform;
        Canvas root = markerCanvas.rootCanvas;
        Camera uiCamera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        if (parent == null || !RectTransformUtility.ScreenPointToWorldPointInRectangle(parent,
            camera.WorldToScreenPoint(position), uiCamera, out Vector3 worldPoint))
        {
            SetMarkerVisible(false);
            return;
        }
        refs.rendezvousTargetMarker.position = worldPoint;
        SetMarkerVisible(true);
        if (refs.rendezvousTargetMarkerText != null && Time.unscaledTime >= nextMarkerTextTime)
        {
            nextMarkerTextTime = Time.unscaledTime + 0.1f;
            double meters = math.length(target.state.position - controlled.state.position) * SimulationUnits.MetersPerUnit;
            refs.rendezvousTargetMarkerText.text = "Target: " + target.name + "\n" +
                (double.IsFinite(meters) ? FormatDistance(meters) : "Distance unavailable");
        }
    }

    /// <summary>Controlled ship offset in the target's radial/along-track/normal axes, in meters.
    /// Normal follows the project's ECI-to-Unity handedness convention (-cross(r,v)).</summary>
    public static bool TryRelativeOffsets(double3 controlledPosition, double3 targetPosition,
        double3 targetVelocity, double3 centralPosition, double3 centralVelocity, out double3 meters)
    {
        meters = double3.zero;
        double3 r = targetPosition - centralPosition;
        double3 v = targetVelocity - centralVelocity;
        double3 separation = controlledPosition - targetPosition;
        if (!math.all(math.isfinite(r)) || !math.all(math.isfinite(v)) ||
            !math.all(math.isfinite(separation)) || math.lengthsq(r) < 1e-20) return false;
        double3 radial = math.normalize(r);
        double3 tangent = v - radial * math.dot(v, radial);
        if (math.lengthsq(tangent) <= Math.Max(1e-30, math.lengthsq(v) * 1e-20)) return false;
        double3 along = math.normalize(tangent);
        double3 normal = -math.normalize(math.cross(radial, along));
        meters = new double3(math.dot(separation, radial), math.dot(separation, along),
            math.dot(separation, normal)) * SimulationUnits.MetersPerUnit;
        return math.all(math.isfinite(meters));
    }

    /// <summary>Same-epoch physics states in world units. Positive range rate means separating.
    /// This is line-of-sight range rate, not total relative speed or rotating-frame velocity.</summary>
    public static bool TryMeasure(double3 controlledPosition, double3 controlledVelocity,
        double3 targetPosition, double3 targetVelocity, out double distanceMeters, out double rangeRateMetersPerSecond)
    {
        double3 separation = targetPosition - controlledPosition;
        double3 relativeVelocity = targetVelocity - controlledVelocity;
        distanceMeters = math.length(separation) * SimulationUnits.MetersPerUnit;
        rangeRateMetersPerSecond = 0.0;
        if (!math.all(math.isfinite(separation)) || !math.all(math.isfinite(relativeVelocity)) ||
            !double.IsFinite(distanceMeters)) return false;
        if (distanceMeters < 1e-6) return true;
        rangeRateMetersPerSecond = math.dot(separation / (distanceMeters / SimulationUnits.MetersPerUnit),
            relativeVelocity) * SimulationUnits.MetersPerUnit;
        return double.IsFinite(rangeRateMetersPerSecond);
    }
}
