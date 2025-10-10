using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using TMPro;

/// <summary>
/// Creates, previews, finalizes, and maintains a single maneuver node for the tracked body,
/// keeping UI widgets, trajectory snapshots, and world-space markers in sync.
/// </summary>
public class ManeuverNodeManager : MonoBehaviour
{
    [Header("Maneuver Nodes")]
    public List<ManeuverNode> nodes = new();

    [Header("Trajectory Rendering")]
    public ProceduralLineRenderer maneuverTrajectoryLine;
    public TrajectoryRenderer trajectoryRenderer;
    public TimeController timeController;
    private BodyService bodyService;

    [Header("References - UI")]
    public Slider maneuverTimeSlider;
    public TMP_Dropdown burnDropdown;
    public Button setupButton;
    public Slider adjustNodeSlider;
    public Button placeNodeButton;

    [Header("UI Controls")]
    public bool isSliderActive = false;

    [SerializeField] Material green;
    [SerializeField] Material red;

    public BodyRuntimeCoordinator bodyRuntimeCoordinator;
    private TutorialController tutorialController;
    private SimContext ctx;

    /// <summary>
    /// Wires context dependencies, seeds dropdown options, and subscribes to tracked-body changes.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.bodyRuntimeCoordinator = ctx.BodyRuntimeCoordinator;
        this.trajectoryRenderer = ctx.TrajectoryRenderer;
        this.timeController = ctx.TimeController;
        this.tutorialController = ctx.TutorialController;
        this.bodyService = ctx.BodyService;

        burnDropdown.ClearOptions();
        List<string> burnOptions = new() { "Prograde", "Retrograde", "Radial In", "Radial Out", "Normal", "Anti-Normal" };
        burnDropdown.AddOptions(burnOptions.Select(dir => new TMP_Dropdown.OptionData(dir)).ToList());

        adjustNodeSlider.interactable = false;
        placeNodeButton.interactable = false;

        if (trajectoryRenderer != null)
            trajectoryRenderer.TrackedBodyChanged += OnTrackedBodyChanged;
    }

    /// <summary>
    /// Enforces world-space pinning on finalized nodes to prevent drift or reparenting.
    /// </summary>
    void LateUpdate()
    {
        if (nodes.Count == 0) return;
        var node = nodes[0];

        if (node.isFinalized && node.isPinned && node.marker != null)
        {
            if (node.marker.transform.position != node.pinnedWorldPosition)
                node.marker.transform.position = node.pinnedWorldPosition;

            if (node.marker.transform.parent != null)
                node.marker.transform.SetParent(null, true);
        }
    }

    private void OnDestroy()
    {
        if (trajectoryRenderer != null)
            trajectoryRenderer.TrackedBodyChanged -= OnTrackedBodyChanged;
    }

    /// <summary>
    /// Enables/disables node adjustment when the tracked body changes, without mutating node data.
    /// </summary>
    private void OnTrackedBodyChanged(NBody oldBody, NBody newBody)
    {
        if (nodes.Count > 0 && nodes[0].targetBody != newBody)
        {
            isSliderActive = false;
            adjustNodeSlider.interactable = false;
        }
        else if (nodes.Count > 0 && nodes[0].targetBody == newBody)
        {
            isSliderActive = true;
            adjustNodeSlider.interactable = true;
        }
    }

    /// <summary>
    /// Creates a preview node using the latest trajectory prediction, initializes UI, and
    /// snapshots the path/time data so subsequent edits are stable against renderer changes.
    /// </summary>
    public void OnAddManeuverNode()
    {
        timeController.SetTimeScale(1f);
        timeController.timeSlider.value = Time.timeScale;

        var body = trajectoryRenderer.trackedBody;
        if (body == null) return;

        if (trajectoryRenderer.latestPrediction == null || trajectoryRenderer.latestPrediction.Count == 0)
        {
            Debug.LogError("No trajectory prediction available!");
            return;
        }

        float initialOffsetTime = 20f;
        float burnTime = bodyRuntimeCoordinator.simulationTime + initialOffsetTime;
        float dt = trajectoryRenderer.latestPredictionDeltaTime;

        float timeFromPredictionStart = burnTime - trajectoryRenderer.latestPredictionStartTime;
        if (timeFromPredictionStart < 0)
        {
            Debug.LogError("Burn time is before prediction start time. This is invalid.");
            return;
        }

        float floatIndex = timeFromPredictionStart / dt;
        int index = Mathf.Clamp(Mathf.FloorToInt(floatIndex), 0, trajectoryRenderer.latestPrediction.Count - 2);
        float t = floatIndex - index;

        Vector3 a = trajectoryRenderer.latestPrediction[index];
        Vector3 b = trajectoryRenderer.latestPrediction[index + 1];
        Vector3 burnPos = Vector3.Lerp(a, b, t);

        Vector3 deltaV = body.velocity.normalized * 1f;
        float burnDuration = 5f;

        CreatePreviewNode(burnPos, burnTime, deltaV, burnDuration);

        var node = nodes[0];
        SetupSlider(node.trajectorySnapshot, node.burnTime, node.snapshotDeltaTime);

        setupButton.interactable = false;
        adjustNodeSlider.interactable = true;
        placeNodeButton.interactable = true;

        if (tutorialController.inTutorialMode)
            tutorialController.hasSetupNode = true;
    }

    /// <summary>
    /// Locks the node, pins its world position, wraps burn time onto a bound orbit if needed,
    /// and disables adjustment UI.
    /// </summary>
    public void FinalizeManeuver()
    {
        if (nodes.Count == 0) return;

        var node = nodes[0];

        // Visual finalize
        node.isFinalized = true;
        node.marker.name = "ManeuverNode";
        var r = node.marker.GetComponent<Renderer>();
        r.material = new Material(red);
        r.material.SetColor("_BaseColor", red.GetColor("_BaseColor"));

        // Pin in world space
        node.marker.transform.SetParent(null, true);
        node.pinnedWorldPosition = node.marker.transform.position;
        node.isPinned = true;

        // Disable adjustments
        isSliderActive = false;
        maneuverTimeSlider?.onValueChanged.RemoveListener(OnSliderChanged);
        if (adjustNodeSlider != null) adjustNodeSlider.interactable = false;
        if (placeNodeButton != null) placeNodeButton.interactable = false;
        if (setupButton != null) setupButton.interactable = true;

        // Wrap burn time for bound orbits (elliptic only)
        var centralBody = bodyService.CentralBody;
        var trackedBodyForWrap = node.targetBody;
        var orbit = OrbitalCalculations.CalculateOrbitalParameters(
            centralBody.mass,
            centralBody.transform.position,
            trackedBodyForWrap.state.position,
            trackedBodyForWrap.state.velocity
        );

        if (orbit.isValid && orbit.eccentricity < 1f && orbit.orbitalPeriod > 0f)
        {
            float simTime = bodyRuntimeCoordinator.simulationTime;
            while (node.burnTime < simTime) node.burnTime += orbit.orbitalPeriod;
        }

        if (tutorialController.inTutorialMode) tutorialController.hasPlacedNode = true;
    }

    public void RemoveNode(ManeuverNode node)
    {
        if (node.marker != null) Destroy(node.marker);
        nodes.Remove(node);
        UpdateManeuverPrediction(node); // no-op if missing data
    }

    public void ClearAllNodes()
    {
        foreach (var node in nodes)
            if (node.marker != null) Destroy(node.marker);
        nodes.Clear();
    }

    /// <summary>
    /// Rebuilds the maneuver prediction based on the node’s snapshot timing and ΔV.
    /// Skips finalized nodes (they are pinned visuals only).
    /// </summary>
    public void UpdateManeuverPrediction(ManeuverNode node = null)
    {
        if (trajectoryRenderer == null) return;
        if (node == null) node = nodes.Count > 0 ? nodes[0] : null;
        if (node == null) return;
        if (node.isFinalized) return;

        var traj = node.trajectorySnapshot;
        if (traj == null || traj.Count < 2) return;

        int burnStep = Mathf.FloorToInt((node.burnTime - node.snapshotStartTime) / Mathf.Max(1e-5f, node.snapshotDeltaTime));
        if (burnStep < 0 || burnStep >= traj.Count) return;

        Vector3 burnPos = traj[Mathf.Clamp(burnStep, 0, traj.Count - 1)];
        Vector3 preVel = EstimateVelocity(traj, Mathf.Clamp(burnStep, 1, traj.Count - 2), node.snapshotDeltaTime);
        Vector3 newVel = preVel + node.deltaV;

        // Keep using node.snapshotDeltaTime for continuity if re-enabling GPU prediction:
        // trackedBody.CalculatePredictedTrajectoryGPU_Async(..., overrideStartPosition: burnPos, overrideStartVelocity: newVel);
    }

    /// <summary>
    /// Creates a preview node, snapshotting the renderer’s current prediction and timing to
    /// decouple subsequent edits from live trajectory updates.
    /// </summary>
    public void CreatePreviewNode(Vector3 position, float burnTime, Vector3 deltaV, float duration)
    {
        ClearAllNodes(); // single-node model

        var trackedBody = trajectoryRenderer.trackedBody;
        var snapshot = new List<Vector3>(trajectoryRenderer.latestPrediction);

        var node = new ManeuverNode
        {
            position = position,
            burnTime = burnTime,
            deltaV = deltaV,
            marker = GameObject.CreatePrimitive(PrimitiveType.Sphere),
            targetBody = trackedBody,
            duration = duration,
            isFinalized = false,
            burnType = GetBurnChoice(),
            trajectorySnapshot = snapshot,
            snapshotStartTime = trajectoryRenderer.latestPredictionStartTime,
            snapshotDeltaTime = Mathf.Max(1e-5f, trajectoryRenderer.latestPredictionDeltaTime),
        };

        node.marker.transform.position = position;
        node.marker.transform.localScale = Vector3.one * 5f;
        node.marker.name = "ManeuverNodePreview";
        node.marker.GetComponent<Renderer>().material = new Material(green);
        node.marker.GetComponent<Renderer>().material.SetColor("_BaseColor", green.GetColor("_BaseColor"));

        nodes.Add(node);
        UpdateManeuverPrediction(node);
    }

    public Vector3 EstimateVelocity(List<Vector3> trajectory, int step, float dt)
    {
        if (step <= 0 || step >= trajectory.Count - 1) return Vector3.zero;
        return (trajectory[step + 1] - trajectory[step - 1]) / (2f * dt);
    }

    /// <summary>
    /// Configures the time slider to operate on the node’s snapshot timeline.
    /// </summary>
    public void SetupSlider(List<Vector3> trajectory, float burnTime, float predictionDeltaTime)
    {
        if (trajectory == null || trajectory.Count == 0 || maneuverTimeSlider == null) return;

        isSliderActive = true;
        maneuverTimeSlider.wholeNumbers = false;
        maneuverTimeSlider.minValue = 0f;
        maneuverTimeSlider.maxValue = trajectory.Count - 1;

        var node = nodes[0];
        float floatIndex = (burnTime - node.snapshotStartTime) / node.snapshotDeltaTime;
        maneuverTimeSlider.value = Mathf.Clamp(floatIndex, 0f, maneuverTimeSlider.maxValue);

        maneuverTimeSlider.onValueChanged.RemoveAllListeners();
        maneuverTimeSlider.onValueChanged.AddListener(OnSliderChanged);
    }

    /// <summary>
    /// Moves the node along its snapshot path and updates predicted post-burn path.
    /// </summary>
    public void OnSliderChanged(float value)
    {
        if (!isSliderActive || nodes.Count == 0) return;
        var node = nodes[0];
        if (node.isFinalized) return;
        var traj = node.trajectorySnapshot;
        if (traj == null || traj.Count < 2) return;

        float floatIndex = value;
        int index = Mathf.Clamp(Mathf.FloorToInt(floatIndex), 0, traj.Count - 2);
        float t = floatIndex - index;

        Vector3 a = traj[index];
        Vector3 b = traj[index + 1];
        Vector3 interpolatedPos = Vector3.Lerp(a, b, t);

        float newBurnTime = node.snapshotStartTime + floatIndex * node.snapshotDeltaTime;

        node.burnTime = newBurnTime;
        node.position = interpolatedPos;
        node.marker.transform.position = interpolatedPos;

        UpdateManeuverPrediction(node);
    }

    /// <summary>
    /// Returns the burn direction corresponding to the current dropdown choice.
    /// Radial directions are computed relative to the central body if available.
    /// </summary>
    public Vector3 GetBurnDirectionFromDropdown(NBody targetBody)
    {
        if (trajectoryRenderer == null || targetBody == null)
            return targetBody != null ? targetBody.velocity.normalized : Vector3.forward;

        string selection = burnDropdown.options[burnDropdown.value].text;

        var central = bodyService != null ? bodyService.CentralBody : null;
        Vector3 r = central != null
            ? (targetBody.transform.position - central.transform.position)
            : targetBody.transform.position;

        Vector3 velocity = targetBody.velocity.normalized;
        Vector3 radialOut = r.normalized;
        Vector3 right = Vector3.Cross(radialOut, velocity).normalized;

        return selection switch
        {
            "Prograde" => velocity,
            "Retrograde" => -velocity,
            "Radial In" => -radialOut,
            "Radial Out" => radialOut,
            "Normal" => right,
            "Anti-Normal" => -right,
            _ => velocity
        };
    }

    public string GetBurnChoice() => burnDropdown.options[burnDropdown.value].text;
}
