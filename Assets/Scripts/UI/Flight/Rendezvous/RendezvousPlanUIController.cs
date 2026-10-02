using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using Unity.Mathematics;
using UnityEngine;

/// <summary>Owns proposal jobs and review UI; scheduling remains in ManeuverNodeManager.</summary>
public sealed class RendezvousPlanUIController : IDisposable
{
    private readonly UIReferences refs;
    private readonly BodyService bodies;
    private readonly BodyRuntimeCoordinator runtime;
    private readonly ManeuverNodeManager nodes;
    private readonly TrajectoryRenderer renderer;
    private readonly Action<RendezvousPlanner.Result, double> showProposal;
    private readonly Action clearProposal;
    private NBody controlled, target, requestedBody, requestedTarget;
    private Task<RendezvousPlanner.Result> task;
    private System.Diagnostics.Stopwatch planningClock;
    private RendezvousPlanner.Progress progress;
    private RendezvousPlanner.Settings activePlannerSettings;
    private TMP_Dropdown arrivalModeDropdown, waypointDirectionDropdown;
    private TMP_InputField waypointDistanceInput;
    private bool holdsSimulation;
    private CancellationTokenSource cancellation;
    private RendezvousPlanner.Result proposal;
    private RendezvousPlanner.Result[] choices;
    private RendezvousPlanner.Result activePlan;
    private string[] proposalBurnReadouts, activeBurnReadouts;
    private double epoch, activeEpoch;
    private int generation, jobGeneration;
    private bool visible;
    private string pendingOutcome;
    private bool pendingSuccess;
    public bool IsCalculating => task != null;
    private float nextReadoutTime;
    private string configuration;
    private string message = "Select a target, then build a burn plan.";
    private static readonly CultureInfo Numbers = CultureInfo.InvariantCulture;

    public RendezvousPlanUIController(UIReferences refs, BodyService bodies, BodyRuntimeCoordinator runtime,
        ManeuverNodeManager nodes, TrajectoryRenderer renderer,
        Action<RendezvousPlanner.Result, double> showProposal, Action clearProposal)
    {
        this.refs = refs; this.bodies = bodies; this.runtime = runtime; this.nodes = nodes; this.renderer = renderer;
        this.showProposal = showProposal; this.clearProposal = clearProposal;
    }

    public void Initialize()
    {
        if (nodes != null) nodes.RendezvousEnded += OnRendezvousEnded;
        InitializeArrivalControls();
        if (refs.rendezvousBurnCountDropdown != null)
        {
            refs.rendezvousBurnCountDropdown.ClearOptions();
            refs.rendezvousBurnCountDropdown.AddOptions(new System.Collections.Generic.List<string>
                { "Auto (2–4 burns)", "2 burns", "3 burns", "4 burns" });
            refs.rendezvousBurnCountDropdown.SetValueWithoutNotify(0);
            refs.rendezvousBurnCountDropdown.onValueChanged.AddListener(ChangeBurnCount);
        }
        if (refs.rendezvousTimingPreferenceDropdown != null)
        {
            refs.rendezvousTimingPreferenceDropdown.ClearOptions();
            refs.rendezvousTimingPreferenceDropdown.AddOptions(new System.Collections.Generic.List<string>
                { "Save fuel", "Balanced", "Start sooner" });
            refs.rendezvousTimingPreferenceDropdown.SetValueWithoutNotify(0);
            refs.rendezvousTimingPreferenceDropdown.onValueChanged.AddListener(ChangeTimingPreference);
        }
        if (refs.rendezvousPlanChoiceDropdown != null)
        {
            refs.rendezvousPlanChoiceDropdown.ClearOptions();
            refs.rendezvousPlanChoiceDropdown.interactable = false;
            refs.rendezvousPlanChoiceDropdown.gameObject.SetActive(false);
            refs.rendezvousPlanChoiceDropdown.onValueChanged.AddListener(SelectPlan);
        }
        refs.rendezvousBuildPlanButton?.onClick.AddListener(Build);
        refs.rendezvousSchedulePlanButton?.onClick.AddListener(Schedule);
        refs.rendezvousCancelPlanButton?.onClick.AddListener(CancelRequested);
    }

    private void InitializeArrivalControls()
    {
        arrivalModeDropdown = refs.rendezvousArrivalModeDropdown;
        waypointDirectionDropdown = refs.rendezvousWaypointDirectionDropdown;
        waypointDistanceInput = refs.rendezvousWaypointDistanceInput;
        if (arrivalModeDropdown != null)
        {
            arrivalModeDropdown.ClearOptions();
            arrivalModeDropdown.AddOptions(new System.Collections.Generic.List<string>
                { "Safe rendezvous", "Waypoint", "Intercept" });
            arrivalModeDropdown.SetValueWithoutNotify(0);
            arrivalModeDropdown.onValueChanged.AddListener(ChangeArrivalMode);
        }
        if (waypointDirectionDropdown != null)
        {
            waypointDirectionDropdown.ClearOptions();
            waypointDirectionDropdown.AddOptions(new System.Collections.Generic.List<string>
                { "Ahead (prograde)", "Behind (retrograde)", "Radial out", "Radial in", "Normal", "Anti-normal" });
            waypointDirectionDropdown.SetValueWithoutNotify(0);
            waypointDirectionDropdown.onValueChanged.AddListener(ChangeWaypointDirection);
        }
        if (waypointDistanceInput != null)
        {
            if (waypointDistanceInput.placeholder is TMP_Text placeholder)
                placeholder.text = "Distance from center (m)";
            waypointDistanceInput.onEndEdit.AddListener(ChangeWaypointDistance);
        }
        UpdateArrivalControlVisibility();
    }

    private RendezvousPlanner.ArrivalMode SelectedArrivalMode => arrivalModeDropdown?.value switch
    {
        1 => RendezvousPlanner.ArrivalMode.Waypoint,
        2 => RendezvousPlanner.ArrivalMode.Intercept,
        _ => RendezvousPlanner.ArrivalMode.Safe
    };

    private void UpdateArrivalControlVisibility()
    {
        bool waypoint = SelectedArrivalMode == RendezvousPlanner.ArrivalMode.Waypoint;
        if (waypointDirectionDropdown != null) waypointDirectionDropdown.gameObject.SetActive(waypoint);
        if (waypointDistanceInput != null) waypointDistanceInput.gameObject.SetActive(waypoint);
    }

    private void ChangeArrivalMode(int _)
    {
        UpdateArrivalControlVisibility();
        if (refs.rendezvousBurnCountDropdown != null)
        {
            refs.rendezvousBurnCountDropdown.ClearOptions();
            refs.rendezvousBurnCountDropdown.AddOptions(SelectedArrivalMode == RendezvousPlanner.ArrivalMode.Intercept
                ? new System.Collections.Generic.List<string> { "Auto (1–4 burns)", "1 burn", "2 burns", "3 burns", "4 burns" }
                : new System.Collections.Generic.List<string> { "Auto (2–4 burns)", "2 burns", "3 burns", "4 burns" });
            refs.rendezvousBurnCountDropdown.SetValueWithoutNotify(0);
        }
        CancelDraft("Arrival mode changed; build a new proposal.");
        Refresh();
    }

    private void ChangeWaypointDirection(int _)
    {
        CancelDraft("Waypoint direction changed; build a new proposal.");
        Refresh();
    }

    private void ChangeWaypointDistance(string _)
    {
        CancelDraft("Waypoint distance changed; build a new proposal.");
        Refresh();
    }

    private bool TryArrivalGoal(out RendezvousPlanner.ArrivalGoal goal)
    {
        goal = default;
        if (controlled == null || target == null) return false;
        double contact = (Math.Max(0, controlled.radius) + Math.Max(0, target.radius)) * 10000;
        var mode = SelectedArrivalMode;
        double distance = mode == RendezvousPlanner.ArrivalMode.Safe ? contact + 100 : 0;
        if (mode == RendezvousPlanner.ArrivalMode.Waypoint &&
            (!double.TryParse(waypointDistanceInput?.text, NumberStyles.Float, Numbers, out distance) ||
             !double.IsFinite(distance) || distance < contact + 1 || distance > 100000))
            return false;
        var direction = mode == RendezvousPlanner.ArrivalMode.Safe
            ? RendezvousPlanner.WaypointDirection.Retrograde
            : (RendezvousPlanner.WaypointDirection)(waypointDirectionDropdown?.value ?? 0);
        goal = new RendezvousPlanner.ArrivalGoal(mode, direction, distance, contact);
        return true;
    }

    public void Tick(NBody controlled, NBody target, bool visible)
    {
        if (this.controlled != controlled || this.target != target)
        {
            CancelAll(); this.controlled = controlled; this.target = target;
        }
        this.visible = visible;
        bool scheduled = nodes != null && nodes.HasRendezvousPlan;
        if (!scheduled) { activePlan = null; activeBurnReadouts = null; }
        if ((!visible || controlled == null || target == null) && (proposal != null || task != null && jobGeneration == generation))
            CancelDraft("Plan review closed; build a new proposal when ready.");
        if ((proposal != null || task != null && jobGeneration == generation) &&
            (controlled == null || target == null || controlled.isThrusting || target.isThrusting ||
             nodes != null && nodes.HasNode && !nodes.HasRendezvousPlan || Configuration() != configuration))
            CancelDraft("Flight or maneuver configuration changed; rebuild the plan.");

        if (task != null && task.IsCompleted)
        {
            if (task.IsFaulted)
            {
                var error = task.Exception;
                if (jobGeneration == generation) { Debug.LogException(error); message = "Planner failed; see the Console."; }
            }
            else if (jobGeneration == generation)
            {
                if (task.IsCanceled) message = "Planning canceled.";
                else
                {
                    proposal = task.Result; message = proposal.Message;
                    if (!proposal.Success) proposal = null;
                    else if (!ProposalCurrent()) CancelDraft("Flight state changed while calculating; rebuild the plan.");
                    else
                    {
                        choices = proposal.Choices ?? new[] { proposal };
                        PopulateChoices();
                        PreviewSelectedPlan();
                    }
                }
            }
            task = null; cancellation.Dispose(); cancellation = null;
            planningClock?.Stop();
            ReleasePlanningHold();
        }
        if (proposal != null && !ProposalCurrent())
        {
            int preference = refs.rendezvousTimingPreferenceDropdown != null
                ? refs.rendezvousTimingPreferenceDropdown.value : 0;
            if (RerankAvailablePlans(preference))
                message = "Previous departure expired; previewing another valid choice.";
            else CancelDraft("Proposals expired or flight state changed; rebuild the plan.");
        }
        if (Time.unscaledTime >= nextReadoutTime)
        { nextReadoutTime = Time.unscaledTime + 0.1f; Refresh(); }
    }

    private string Configuration() => controlled == null || target == null ? "" :
        FormattableString.Invariant($"{controlled.radius:R}/{target.radius:R}/{controlled.TotalMassKilograms:R}/{controlled.EffectiveThrustNewtons:R}/{controlled.DryMassKilograms:R}/{controlled.FuelMassKilograms:R}/{controlled.SpecificImpulseSeconds:R}/{controlled.UnlimitedPropellant}/{controlled.dragCoefficient:R}/{controlled.DragAreaSquareUnits:R}/{target.TotalMassKilograms:R}/{target.dragCoefficient:R}/{target.DragAreaSquareUnits:R}/{bodies?.CentralBody?.TotalMassKilograms:R}/{SelectedArrivalMode}/{waypointDirectionDropdown?.value}/{waypointDistanceInput?.text}");

    private bool ProposalCurrent()
    {
        bool blockingNode = nodes != null && nodes.HasNode && !nodes.HasRendezvousPlan;
        if (proposal == null || runtime == null || controlled != requestedBody || target != requestedTarget ||
            controlled == null || target == null || nodes == null || blockingNode || Configuration() != configuration) return false;
        double age = runtime.SimulationTimeSeconds - epoch;
        if (age < 0 || age >= proposal.Burns[0].Start - 5 || !proposal.CoastReference.TrySample(age, out var expected)) return false;
        return proposal.MatchesFlight(expected, controlled.state.position, controlled.state.velocity,
            target.state.position, target.state.velocity);
    }

    private bool AlreadyRendezvoused()
    {
        if (!TryArrivalGoal(out var goal) || bodies?.CentralBody == null) return false;
        var settings = refs.rendezvousPlannerSettings != null
            ? refs.rendezvousPlannerSettings.Snapshot() : RendezvousPlanner.Settings.Default;
        double mu = PhysicsConstants.GDouble * bodies.CentralBody.TotalMassKilograms;
        // Preserve the close-and-slow Safe-mode block regardless of approach side.
        // Larger spacecraft also use their size-aware goal check below.
        if (goal.Mode == RendezvousPlanner.ArrivalMode.Safe &&
            math.distance(controlled.state.position, target.state.position) * 10000 <= settings.ValidationDistance &&
            math.distance(controlled.state.velocity, target.state.velocity) * 10000 <= settings.ValidationSpeed)
            return true;
        return goal.IsSatisfied(controlled.state.position, controlled.state.velocity,
            target.state.position, target.state.velocity, mu, settings.ValidationDistance, settings.ValidationSpeed);
    }

    private void Build()
    {
        bool replacingScheduledPlan = nodes != null && nodes.HasRendezvousPlan;
        if (!visible || task != null || controlled == null || target == null || nodes == null ||
            nodes.HasNode && !replacingScheduledPlan ||
            runtime == null || controlled.isThrusting || target.isThrusting) return;
        if (AlreadyRendezvoused())
        { message = "Already within rendezvous distance and relative-speed limits."; Refresh(); return; }
        if (!TryArrivalGoal(out var goal))
        { message = "Waypoint distance must be at least 1 m beyond the combined satellite radii (maximum 100 km)."; Refresh(); return; }
        CancelDraft("");
        if (nodes.timeController == null)
        { message = "Connect the maneuver manager's Time Controller before planning."; Refresh(); return; }
        if (bodies?.CentralBody == null || math.lengthsq(bodies.CentralBody.state.position) > 1e-12 ||
            math.lengthsq(bodies.CentralBody.state.velocity) > 1e-12)
        { message = "Planning requires a fixed central body at the origin."; Refresh(); return; }
        nodes.timeController?.SetTimeScale(1f);
        var request = new TrajectoryPredictionRequest(1, .02f, runtime.simulationTime, 5, true, TrajectoryPredictionBackend.NativeMatched, 2);
        if (!TrajectoryMatchedPredictor.TryBuildWorkItem(controlled, bodies, request, out var a) ||
            !TrajectoryMatchedPredictor.TryBuildWorkItem(target, bodies, request, out var b)) return;
        var engine = new RendezvousPlanner.Propulsion(controlled.EffectiveThrustNewtons, controlled.DryMassKilograms,
            controlled.FuelMassKilograms, controlled.SpecificImpulseSeconds, !controlled.UnlimitedPropellant);
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(NativePhysics).TypeHandle);
        epoch = runtime.SimulationTimeSeconds; requestedBody = controlled; requestedTarget = target; configuration = Configuration();
        jobGeneration = ++generation;
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        double requestEpoch = epoch;
        progress = new RendezvousPlanner.Progress();
        var jobProgress = progress;
        planningClock = System.Diagnostics.Stopwatch.StartNew();
        int countChoice = refs.rendezvousBurnCountDropdown != null ? refs.rendezvousBurnCountDropdown.value : 0;
        int burnCount = countChoice == 0 ? 0 : countChoice + (SelectedArrivalMode == RendezvousPlanner.ArrivalMode.Intercept ? 0 : 1);
        int timingChoice = refs.rendezvousTimingPreferenceDropdown != null ?
            refs.rendezvousTimingPreferenceDropdown.value : 0;
        var timingPreference = (RendezvousPlanner.TimingPreference)Math.Clamp(timingChoice, 0, 2);
        var plannerSettings = refs.rendezvousPlannerSettings != null
            ? refs.rendezvousPlannerSettings.Snapshot() : RendezvousPlanner.Settings.Default;
        plannerSettings = plannerSettings.WithGoal(goal);
        activePlannerSettings = plannerSettings;
        nodes.timeController?.BeginPlanningHold();
        holdsSimulation = nodes.timeController != null;
        try
        {
            task = Task.Run(() => RendezvousPlanner.Solve(a, b, engine, token, requestEpoch, jobProgress,
                burnCount, timingPreference, plannerSettings), token);
        }
        catch
        {
            ReleasePlanningHold();
            cancellation.Dispose(); cancellation = null;
            throw;
        }
        message = replacingScheduledPlan
            ? "Calculating a replacement with simulation time held; the existing schedule is preserved."
            : "Calculating and validating the full plan... (simulation time held)";
        Refresh();
    }

    private void Schedule()
    {
        if (AlreadyRendezvoused())
        { message = "Already within rendezvous distance and relative-speed limits."; Refresh(); return; }
        if (!visible || !ProposalCurrent()) { CancelDraft("Proposal is stale; rebuild it before scheduling."); Refresh(); return; }
        var accepted = proposal;
        // Clear the draft before manager UI callbacks run, so they cannot invalidate the accepted schedule.
        proposal = null;
        if (refs.rendezvousSchedulePlanButton != null)
            refs.rendezvousSchedulePlanButton.interactable = false;
        if (nodes.TryScheduleRendezvousPlan(controlled, target, accepted, epoch))
        {
            message = $"Plan scheduled. All {accepted.Burns.Length} burns execute automatically.";
            pendingOutcome = null; activePlan = accepted; activeEpoch = epoch;
            if (accepted.Candidates != null)
                foreach (var candidate in accepted.Candidates)
                { candidate.Candidates = null; candidate.Choices = null; }
            activeBurnReadouts = proposalBurnReadouts; proposalBurnReadouts = null; choices = null;
            refs.rendezvousPlanChoiceDropdown?.ClearOptions();
            if (refs.rendezvousPlanChoiceDropdown != null)
                refs.rendezvousPlanChoiceDropdown.gameObject.SetActive(false);
        }
        else { proposal = accepted; message = "Could not schedule; check for an existing node or expired departure."; }
        Refresh();
    }

    private void ChangeBurnCount(int _)
    {
        if (proposal != null || IsCalculating) CancelDraft("Burn count changed; build a new proposal.");
        Refresh();
    }

    private bool RerankAvailablePlans(int preference)
    {
        if (proposal == null || runtime == null) return false;
        double age = runtime.SimulationTimeSeconds - epoch;
        var available = Array.FindAll(proposal.Candidates ?? new[] { proposal },
            candidate => age < candidate.Burns[0].Start - 5);
        if (available.Length == 0) return false;
        proposal = RendezvousPlanner.RankPlans(available,
            (RendezvousPlanner.TimingPreference)Math.Clamp(preference, 0, 2), activePlannerSettings);
        if (!ProposalCurrent()) return false;
        choices = proposal.Choices;
        PopulateChoices();
        PreviewSelectedPlan();
        message = proposal.Message;
        return true;
    }

    private void ChangeTimingPreference(int preference)
    {
        if (proposal != null && !RerankAvailablePlans(preference))
            CancelDraft("Proposals expired or flight state changed; rebuild the plan.");
        Refresh();
    }

    private void PopulateChoices()
    {
        if (refs.rendezvousPlanChoiceDropdown == null) return;
        var labels = new System.Collections.Generic.List<string>();
        for (int i = 0; i < choices.Length; i++)
        {
            var choice = choices[i];
            labels.Add($"Plan {i + 1}: {choice.DeltaV:F0} m/s dV, {choice.Burns.Length} burns");
        }
        refs.rendezvousPlanChoiceDropdown.ClearOptions();
        refs.rendezvousPlanChoiceDropdown.AddOptions(labels);
        refs.rendezvousPlanChoiceDropdown.SetValueWithoutNotify(0);
        refs.rendezvousPlanChoiceDropdown.interactable = choices.Length > 1;
        refs.rendezvousPlanChoiceDropdown.gameObject.SetActive(true);
    }

    private void SelectPlan(int index)
    {
        if (choices == null || index < 0 || index >= choices.Length) return;
        var previous = proposal;
        proposal = choices[index];
        if (!ProposalCurrent())
        {
            proposal = previous;
            refs.rendezvousPlanChoiceDropdown?.SetValueWithoutNotify(Array.IndexOf(choices, previous));
            message = "That departure has passed. Choose another plan or build again.";
            Refresh();
            return;
        }
        PreviewSelectedPlan();
        Refresh();
    }

    private void PreviewSelectedPlan()
    {
        proposalBurnReadouts = BuildBurnReadouts(proposal);
        showProposal?.Invoke(proposal, epoch);
        renderer?.ShowRendezvousPlan(proposal.Prediction, proposal.Burns[0].Start,
            proposal.ArrivalSeconds);
    }

    private void ReleasePlanningHold()
    {
        if (!holdsSimulation) return;
        holdsSimulation = false;
        nodes?.timeController?.EndPlanningHold();
    }

    private void CancelDraft(string reason)
    {
        bool hadProposal = proposal != null;
        ReleasePlanningHold();
        generation++; cancellation?.Cancel(); proposal = null; choices = null; proposalBurnReadouts = null;
        if (refs.rendezvousPlanChoiceDropdown != null)
        {
            refs.rendezvousPlanChoiceDropdown.ClearOptions();
            refs.rendezvousPlanChoiceDropdown.interactable = false;
            refs.rendezvousPlanChoiceDropdown.gameObject.SetActive(false);
        }
        message = reason;
        if (hadProposal) { if (nodes == null || !nodes.HasNode) renderer?.ClearPlannedManeuver(); clearProposal?.Invoke(); }
    }

    public void CancelAll()
    {
        CancelDraft("Plan canceled. Build a new proposal when ready.");
        if (nodes != null && nodes.HasRendezvousPlan) nodes.ClearNode();
        activePlan = null;
        activeBurnReadouts = null;
        Refresh();
    }

    private void CancelRequested()
    {
        if (nodes != null && nodes.HasRendezvousPlan && (proposal != null || task != null))
        {
            CancelDraft("Replacement discarded; the scheduled plan is still active.");
            Refresh();
            return;
        }
        CancelAll();
    }

    private void OnRendezvousEnded(ManeuverNodeManager.RendezvousOutcome outcome, string reason)
    {
        pendingOutcome = reason;
        pendingSuccess = outcome == ManeuverNodeManager.RendezvousOutcome.Succeeded;
        message = reason;
    }

    public bool ConsumePlanOutcome(out string reason, out bool succeeded)
    {
        reason = pendingOutcome; succeeded = pendingSuccess;
        if (reason == null) return false;
        pendingOutcome = null;
        message = reason;
        Refresh();
        return true;
    }

    public void FinishCompletedPlan(string reason)
    {
        CancelDraft(reason);
        activePlan = null;
        activeBurnReadouts = null;
        Refresh();
    }

    private void Refresh()
    {
        bool scheduled = nodes != null && nodes.HasRendezvousPlan;
        bool alreadyRendezvoused = AlreadyRendezvoused();
        bool validGoal = controlled != null && target != null && TryArrivalGoal(out _);
        if (arrivalModeDropdown != null) arrivalModeDropdown.interactable = !IsCalculating;
        if (waypointDirectionDropdown != null) waypointDirectionDropdown.interactable = !IsCalculating;
        if (waypointDistanceInput != null) waypointDistanceInput.interactable = !IsCalculating;
        if (refs.rendezvousBurnCountDropdown != null) refs.rendezvousBurnCountDropdown.interactable = !IsCalculating;
        if (refs.rendezvousTimingPreferenceDropdown != null)
            refs.rendezvousTimingPreferenceDropdown.interactable = !IsCalculating;
        if (refs.rendezvousPlanChoiceDropdown != null)
        {
            refs.rendezvousPlanChoiceDropdown.interactable = proposal != null && choices?.Length > 1;
            refs.rendezvousPlanChoiceDropdown.gameObject.SetActive(proposal != null);
        }
        bool canReplaceScheduledPlan = scheduled && runtime != null && !runtime.IsNodeBurnInProgress;
        if (refs.rendezvousBuildPlanButton != null) refs.rendezvousBuildPlanButton.interactable = visible && validGoal && task == null && nodes != null && (!nodes.HasNode || canReplaceScheduledPlan) && !controlled.isThrusting && !target.isThrusting && !alreadyRendezvoused;
        if (refs.rendezvousSchedulePlanButton != null) refs.rendezvousSchedulePlanButton.interactable = visible && proposal != null && task == null &&
            (!scheduled || runtime != null && !runtime.IsNodeBurnInProgress) && !alreadyRendezvoused;
        if (refs.rendezvousCancelPlanButton != null) refs.rendezvousCancelPlanButton.interactable = proposal != null || task != null || scheduled;
        if (refs.rendezvousPlanText == null) return;
        if (task != null && jobGeneration == generation)
        {
            bool replacement = scheduled;
            refs.rendezvousPlanText.text = "Building plan: " + planningClock.Elapsed.TotalSeconds.ToString("F0", Numbers) +
                " s (simulation time held)\n" + progress.Stage + "\nCandidate " +
                progress.Seed + ", refinement " + progress.Iteration +
                "\nPhysics checks: " + progress.Evaluations +
                (replacement ? "\nCurrent schedule is preserved. Cancel discards only the replacement."
                    : "\nNo burns are scheduled yet. You can cancel.");
            return;
        }
        if (scheduled && proposal == null)
        {
            var text = new StringBuilder("Scheduled: " + nodes.RendezvousBurnsRemaining + " burns left\n");
            if (nodes.RendezvousBurnsRemaining == 0 && activePlan != null)
                text.AppendLine("Coasting to " + activePlan.Goal.Mode.ToString().ToLowerInvariant() + " arrival: " +
                    UIHelpers.FormatCountdown(activeEpoch + activePlan.ArrivalSeconds - runtime.SimulationTimeSeconds));
            int burnIndex = activePlan == null ? 0 : activePlan.Burns.Length - nodes.RendezvousBurnsRemaining;
            foreach (var node in nodes.GetRendezvousSchedule())
            {
                text.Append((burnIndex + 1) + ". in " +
                    UIHelpers.FormatCountdown(node.burnTime - runtime.SimulationTimeSeconds) +
                    " | " + node.duration.ToString("F1", Numbers) + " s");
                if (activePlan != null && burnIndex < activePlan.BurnDeltaVs.Length)
                    text.Append(" | " + activePlan.BurnDeltaVs[burnIndex].ToString("F0", Numbers) + " m/s");
                if (activeBurnReadouts != null && burnIndex >= 0 && burnIndex < activeBurnReadouts.Length &&
                    !string.IsNullOrEmpty(activeBurnReadouts[burnIndex]))
                    text.Append(" | " + activeBurnReadouts[burnIndex]);
                text.AppendLine();
                burnIndex++;
            }
            refs.rendezvousPlanText.text = text.ToString(); return;
        }
        if (proposal == null)
        {
            refs.rendezvousPlanText.text = !validGoal && controlled != null && target != null
                ? "Waypoint distance must be at least 1 m beyond the combined satellite radii (maximum 100 km)."
                : alreadyRendezvoused && !scheduled
                ? "Already within rendezvous distance and relative-speed limits. No new plan is needed."
                : nodes != null && nodes.HasNode && !scheduled
                    ? "Clear the existing maneuver node before building a rendezvous plan." : message;
            return;
        }
        var sb = new StringBuilder("Proposed plan - review before scheduling\n");
        sb.AppendLine("Arrival: " + proposal.Goal.Mode +
            (proposal.Goal.Mode == RendezvousPlanner.ArrivalMode.Waypoint
                ? " | " + proposal.Goal.DistanceMeters.ToString("F0", Numbers) + " m " + proposal.Goal.Direction
                : ""));
        int preference = refs.rendezvousTimingPreferenceDropdown != null ?
            refs.rendezvousTimingPreferenceDropdown.value : 0;
        sb.AppendLine("Priority: " + (preference == 2 ? "Start sooner" :
            preference == 1 ? "Balanced" : "Save fuel") +
            " | Transfer: " + UIHelpers.FormatCountdown(
                proposal.ArrivalSeconds - proposal.Burns[0].Start));
        if (choices != null && choices.Length > 1)
            sb.AppendLine(choices.Length + " validated choices; select a plan above.");
        sb.AppendLine("Burns (direction | starts in | delta-v | duration):");
        for (int i = 0; i < proposal.Burns.Length; i++)
        {
            var burn = proposal.Burns[i];
            sb.AppendLine((i + 1) + ". " +
                (proposalBurnReadouts?[i] ?? burn.Type.ToString()) + " | " +
                UIHelpers.FormatCountdown(epoch + burn.Start - runtime.SimulationTimeSeconds) +
                " / " + proposal.BurnDeltaVs[i].ToString("F0", Numbers) + " m/s" +
                " / " + burn.Duration.ToString("F1", Numbers) + " s");

        }
        sb.AppendLine("Total dV: " + proposal.DeltaV.ToString("F1", Numbers) + " m/s");
        if (double.IsFinite(proposal.HohmannOrbitChangeDeltaV))
        {
            sb.AppendLine("Hohmann orbit-only ref: " +
                proposal.HohmannOrbitChangeDeltaV.ToString("F0", Numbers) + " m/s; plan " +
                (proposal.DeltaV - proposal.HohmannOrbitChangeDeltaV)
                    .ToString("+0;-0;0", Numbers) + " m/s");
            sb.AppendLine("Reference ignores target timing, finite burns, and drag.");
        }
        sb.AppendLine("Arrival in " + UIHelpers.FormatCountdown(
            epoch + proposal.ArrivalSeconds - runtime.SimulationTimeSeconds) +
            " | miss " + proposal.SeparationMeters.ToString("F1", Numbers) + " m");
        sb.AppendLine("Relative speed: " + proposal.RelativeSpeed.ToString("F2", Numbers) +
            " m/s | fuel: " + proposal.FuelKg.ToString("F2", Numbers) + " kg");
        sb.Append(proposal.Goal.Mode == RendezvousPlanner.ArrivalMode.Intercept
            ? "Intercept attempts physical contact; Schedule Plan to execute."
            : "Approach only; Schedule Plan to execute.");
        refs.rendezvousPlanText.text = sb.ToString();
    }

    private string[] BuildBurnReadouts(RendezvousPlanner.Result plan)
    {
        var readouts = new string[plan.Burns.Length];
        if (plan.Prediction == null) return readouts;
        for (int i = 0; i < plan.Burns.Length; i++)
        {
            var burn = plan.Burns[i];
            if (!plan.Prediction.TrySample(burn.Start, out var before)) continue;
            double3 radial = math.normalizesafe(before.A);
            double3 angularMomentum = math.cross(before.A, before.VA);
            if (math.lengthsq(radial) < 1e-12 || math.lengthsq(angularMomentum) < 1e-12) continue;
            double3 orbitNormal = math.normalize(angularMomentum);
            double3 alongTrack = math.normalize(math.cross(orbitNormal, radial));
            // Normal is -r x v in the simulator's attitude convention.
            double3 simulatorNormal = -orbitNormal;
            double3 velocityDirection = math.normalizesafe(before.VA, alongTrack);
            double3 direction = burn.UsesVectorDirection ? burn.VectorDirectionWorld : burn.Type switch
            {
                BurnType.Prograde => velocityDirection,
                BurnType.Retrograde => -velocityDirection,
                BurnType.RadialOut => radial,
                BurnType.RadialIn => -radial,
                BurnType.Normal => simulatorNormal,
                BurnType.AntiNormal => -simulatorNormal,
                _ => velocityDirection
            };
            double along = math.dot(direction, alongTrack);
            double outward = math.dot(direction, radial);
            double normal = math.dot(direction, simulatorNormal);
            // Show meaningful thrust directions without a second row of vector components.
            var labels = new System.Collections.Generic.List<string>();
            if (Math.Abs(along) >= 0.25) labels.Add(along >= 0 ? "Prograde" : "Retrograde");
            if (Math.Abs(outward) >= 0.25) labels.Add(outward >= 0 ? "Radial out" : "Radial in");
            if (Math.Abs(normal) >= 0.25) labels.Add(normal >= 0 ? "Normal" : "Anti-normal");
            readouts[i] = string.Join(" + ", labels);

        }
        return readouts;
    }

    public void Dispose()
    {
        if (nodes != null) nodes.RendezvousEnded -= OnRendezvousEnded;
        CancelAll();
        refs.rendezvousBurnCountDropdown?.onValueChanged.RemoveListener(ChangeBurnCount);
        refs.rendezvousTimingPreferenceDropdown?.onValueChanged.RemoveListener(ChangeTimingPreference);
        refs.rendezvousPlanChoiceDropdown?.onValueChanged.RemoveListener(SelectPlan);
        refs.rendezvousBuildPlanButton?.onClick.RemoveListener(Build);
        refs.rendezvousSchedulePlanButton?.onClick.RemoveListener(Schedule);
        refs.rendezvousCancelPlanButton?.onClick.RemoveListener(CancelRequested);
        arrivalModeDropdown?.onValueChanged.RemoveListener(ChangeArrivalMode);
        waypointDirectionDropdown?.onValueChanged.RemoveListener(ChangeWaypointDirection);
        waypointDistanceInput?.onEndEdit.RemoveListener(ChangeWaypointDistance);
        if (task != null) { var source = cancellation; task.ContinueWith(t => { _ = t.Exception; source.Dispose(); }); }
    }
}

