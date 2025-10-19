using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using TMPro;

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
    public Slider maneuverTimeSlider;  // time-adjust slider
    public TMP_Dropdown burnDropdown;
    public Button setupButton;
    public Slider adjustNodeSlider;    // legacy / optional; we just toggle interactable
    public Button placeNodeButton;

    [Header("Materials")]
    [SerializeField] Material green;
    [SerializeField] Material red;

    [Header("UX Settings")]
    [SerializeField] float sliderUpdateMinInterval = 0.02f; // debounce
    [SerializeField] float nodeVisualScale = 1f;
    [SerializeField] bool allowSlider = true;               // keep slider visible/usable
    private float _nextSliderAllowed;
    public bool isSliderActive = false;

    // gating "Place" until user actually adjusts
    private bool nodeAdjusted = false;

    public BodyRuntimeCoordinator bodyRuntimeCoordinator;
    private TutorialController tutorialController;
    private SimContext ctx;

    // ------------------------------------------------------------

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        bodyRuntimeCoordinator = ctx.BodyRuntimeCoordinator;
        trajectoryRenderer = ctx.TrajectoryRenderer;
        timeController = ctx.TimeController;
        tutorialController = ctx.TutorialController;
        bodyService = ctx.BodyService;

        burnDropdown.ClearOptions();
        List<string> burnOptions = new() { "Prograde", "Retrograde", "Radial In", "Radial Out", "Normal", "Anti-Normal" };
        burnDropdown.AddOptions(burnOptions.Select(dir => new TMP_Dropdown.OptionData(dir)).ToList());

        if (trajectoryRenderer != null)
            trajectoryRenderer.TrackedBodyChanged += OnTrackedBodyChanged;

        if (!allowSlider && maneuverTimeSlider != null)
            maneuverTimeSlider.gameObject.SetActive(false);

        if (placeNodeButton != null) placeNodeButton.interactable = false; // always gated
        if (adjustNodeSlider != null) adjustNodeSlider.interactable = false;
    }

    void LateUpdate()
    {
        if (nodes.Count == 0) return;
        var node = nodes[0];

        // Keep finalized nodes pinned
        if (node.isFinalized && node.isPinned && node.marker != null)
        {
            if (node.marker.transform.position != node.pinnedWorldPosition)
                node.marker.transform.position = node.pinnedWorldPosition;
            if (node.marker.transform.parent != null)
                node.marker.transform.SetParent(null, true);
        }

        // Update T± label
        if (node.marker != null && bodyRuntimeCoordinator != null)
        {
            float tMinus = node.burnTime - bodyRuntimeCoordinator.simulationTime;
            var giz = node.marker.GetComponent<NodeGizmo>();
            if (giz) giz.SetTimeToNode(tMinus);
        }
    }

    private void OnDestroy()
    {
        if (trajectoryRenderer != null)
            trajectoryRenderer.TrackedBodyChanged -= OnTrackedBodyChanged;

        if (maneuverTimeSlider != null)
            maneuverTimeSlider.onValueChanged.RemoveListener(OnSliderChanged);
    }

    private void OnTrackedBodyChanged(NBody oldBody, NBody newBody)
    {
        bool active = nodes.Count > 0 && nodes[0].targetBody == newBody;
        isSliderActive = active;
        if (adjustNodeSlider != null) adjustNodeSlider.interactable = active;
    }

    // ------------------------------------------------------------
    // Node Creation / Finalization
    // ------------------------------------------------------------

    public void OnAddManeuverNode()
    {
        timeController.SetTimeScale(1f);
        timeController.timeSlider.value = Time.timeScale;

        var body = trajectoryRenderer.trackedBody;
        if (body == null || trajectoryRenderer.latestPrediction == null || trajectoryRenderer.latestPrediction.Count == 0)
            return;

        // initial placement ~20s ahead of current sim time
        float initialOffsetTime = 20f;
        float desiredBurnTime = bodyRuntimeCoordinator.simulationTime + initialOffsetTime;
        float dt = trajectoryRenderer.latestPredictionDeltaTime;

        float timeFromPredictionStart = desiredBurnTime - trajectoryRenderer.latestPredictionStartTime;
        if (timeFromPredictionStart < 0) timeFromPredictionStart = 0;

        float floatIndex = timeFromPredictionStart / dt;
        int index = Mathf.Clamp(Mathf.FloorToInt(floatIndex), 0, trajectoryRenderer.latestPrediction.Count - 2);
        float t = floatIndex - index;

        Vector3 a = trajectoryRenderer.latestPrediction[index];
        Vector3 b = trajectoryRenderer.latestPrediction[index + 1];
        Vector3 burnPos = Vector3.Lerp(a, b, t);

        Vector3 deltaV = body.velocity.normalized * 1f;
        float burnDuration = 5f;

        CreatePreviewNode(burnPos, desiredBurnTime, deltaV, burnDuration);

        // Reset gating — user must adjust before "Place" becomes available
        nodeAdjusted = false;
        if (placeNodeButton != null) placeNodeButton.interactable = false;

        // Keep slider as an option
        var node = nodes[0];
        SetupSlider(node.trajectorySnapshot, node.burnTime, node.snapshotDeltaTime);

        // allow time adjustments
        isSliderActive = true;
        if (adjustNodeSlider != null) adjustNodeSlider.interactable = true;

        if (tutorialController != null && tutorialController.inTutorialMode)
            tutorialController.hasSetupNode = true;
    }

    public void FinalizeManeuver()
    {
        if (nodes.Count == 0) return;
        var node = nodes[0];

        node.isFinalized = true;
        node.marker.name = "ManeuverNode";
        var r = node.marker.GetComponent<Renderer>();
        r.material = new Material(red);
        CopyColorIfPresent(red, r.material);

        node.marker.transform.SetParent(null, true);
        node.pinnedWorldPosition = node.marker.transform.position;
        node.isPinned = true;

        // Calm the pulse
        var giz = node.marker.GetComponent<NodeGizmo>();
        if (giz) giz.SetPulse(false);

        // Disable adjustments
        isSliderActive = false;
        maneuverTimeSlider?.onValueChanged.RemoveListener(OnSliderChanged);
        if (adjustNodeSlider != null) adjustNodeSlider.interactable = false;
        if (placeNodeButton != null) placeNodeButton.interactable = false; // placed

        // Wrap burn time to future orbit if needed
        var central = bodyService.CentralBody;
        var orbit = OrbitalCalculations.CalculateOrbitalParameters(
            central.mass,
            central.transform.position,
            node.targetBody.state.position,
            node.targetBody.state.velocity
        );

        if (orbit.isValid && orbit.eccentricity < 1f && orbit.orbitalPeriod > 0f)
        {
            float simTime = bodyRuntimeCoordinator.simulationTime;
            while (node.burnTime < simTime) node.burnTime += orbit.orbitalPeriod;
        }

        if (tutorialController != null && tutorialController.inTutorialMode)
            tutorialController.hasPlacedNode = true;
    }

    // ------------------------------------------------------------
    // Node Creation Helpers
    // ------------------------------------------------------------

    public void CreatePreviewNode(Vector3 position, float burnTime, Vector3 deltaV, float duration)
    {
        ClearAllNodes();
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
            snapshotDeltaTime = Mathf.Max(1e-5f, trajectoryRenderer.latestPredictionDeltaTime)
        };

        node.marker.name = "ManeuverNodePreview";
        node.marker.transform.position = position;
        node.marker.transform.localScale = Vector3.one * (5f * nodeVisualScale);

        var rend = node.marker.GetComponent<Renderer>();
        rend.material = new Material(green);
        CopyColorIfPresent(green, rend.material);
        rend.material.renderQueue = 5000;

        var col = node.marker.GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.9f;

        // Billboard/label
        var giz = node.marker.AddComponent<NodeGizmo>();
        if (green.HasProperty("_BaseColor"))
            giz.baseColor = green.GetColor("_BaseColor");

        // Drag handle (screen-space picking)
        var drag = node.marker.AddComponent<NodeDragHandle>();
        drag.Init(this);

        nodes.Add(node);
        UpdateManeuverPrediction(node);

        FocusCameraOn(node.marker.transform.position);
    }

    public void ClearAllNodes()
    {
        foreach (var n in nodes)
            if (n.marker != null) Destroy(n.marker);
        nodes.Clear();
    }

    // ------------------------------------------------------------
    // Core Updates
    // ------------------------------------------------------------

    public void UpdateManeuverPrediction(ManeuverNode node = null)
    {
        if (trajectoryRenderer == null) return;
        node ??= nodes.Count > 0 ? nodes[0] : null;
        if (node == null || node.isFinalized) return;

        var traj = node.trajectorySnapshot;
        if (traj == null || traj.Count < 2) return;

        int burnStep = Mathf.FloorToInt((node.burnTime - node.snapshotStartTime) / Mathf.Max(1e-5f, node.snapshotDeltaTime));
        burnStep = Mathf.Clamp(burnStep, 0, traj.Count - 1);

        // keep node.position consistent with timing to avoid "fires at different point"
        node.position = traj[burnStep];
    }

    public Vector3 EstimateVelocity(List<Vector3> trajectory, int step, float dt)
    {
        if (step <= 0 || step >= trajectory.Count - 1) return Vector3.zero;
        return (trajectory[step + 1] - trajectory[step - 1]) / (2f * dt);
    }

    // ------------------------------------------------------------
    // Slider support (kept and synced)
    // ------------------------------------------------------------

    public void SetupSlider(List<Vector3> trajectory, float burnTime, float predictionDeltaTime)
    {
        if (!allowSlider || trajectory == null || trajectory.Count == 0 || maneuverTimeSlider == null) return;

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

    public void OnSliderChanged(float value)
    {
        if (Time.unscaledTime < _nextSliderAllowed) return;
        _nextSliderAllowed = Time.unscaledTime + sliderUpdateMinInterval;
        SetNodeAtFloatIndex(value);
        MarkAdjusted(); // enable "Place" after first user move
    }

    // ------------------------------------------------------------
    // Shared movement helper (used by slider & drag)
    // ------------------------------------------------------------

    public void SetNodeAtFloatIndex(float floatIndex)
    {
        if (nodes.Count == 0) return;
        var node = nodes[0];
        if (node.isFinalized) return;
        var traj = node.trajectorySnapshot;
        if (traj == null || traj.Count < 2) return;

        floatIndex = Mathf.Clamp(floatIndex, 0f, traj.Count - 1.0001f);
        int idx = Mathf.Clamp(Mathf.FloorToInt(floatIndex), 0, traj.Count - 2);
        float t = Mathf.Clamp01(floatIndex - idx);

        Vector3 p = Vector3.Lerp(traj[idx], traj[idx + 1], t);
        float newBurnTime = node.snapshotStartTime + floatIndex * node.snapshotDeltaTime;

        node.burnTime = newBurnTime;
        node.position = p;
        if (node.marker) node.marker.transform.position = p;

        UpdateManeuverPrediction(node);

        if (allowSlider && maneuverTimeSlider != null && isSliderActive)
            maneuverTimeSlider.SetValueWithoutNotify(floatIndex);
    }

    void MarkAdjusted()
    {
        if (!nodeAdjusted)
        {
            nodeAdjusted = true;
            if (placeNodeButton != null) placeNodeButton.interactable = true;
        }
    }

    // Call this from your Δv UI if you have one, to also enable Place:
    public void OnDeltaVChanged(float newDv)
    {
        // clamp defensively & snap UI elsewhere (where your slider lives)
        if (!float.IsFinite(newDv)) return;
        MarkAdjusted();
    }

    // ------------------------------------------------------------
    // Utilities
    // ------------------------------------------------------------

    public Vector3 GetBurnDirectionFromDropdown(NBody targetBody)
    {
        if (targetBody == null) return Vector3.forward;
        if (trajectoryRenderer == null) return targetBody.velocity.normalized;

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

    void FocusCameraOn(Vector3 worldPos)
    {
        var cam = Camera.main;
        if (!cam) return;
        var dir = (cam.transform.position - worldPos).normalized;
        var targetPos = worldPos + dir * 30f;
        cam.transform.position = Vector3.Lerp(cam.transform.position, targetPos, 0.25f);
    }

    static void CopyColorIfPresent(Material src, Material dst)
    {
        if (src == null || dst == null) return;
        if (src.HasProperty("_BaseColor") && dst.HasProperty("_BaseColor"))
            dst.SetColor("_BaseColor", src.GetColor("_BaseColor"));
        else if (src.HasProperty("_Color") && dst.HasProperty("_Color"))
            dst.SetColor("_Color", src.GetColor("_Color"));
    }

    // ------------------------------------------------------------
    // Inline screen-space drag handle (smooth, no teleport)
    // ------------------------------------------------------------

    [RequireComponent(typeof(Collider))]
    private class NodeDragHandle : MonoBehaviour
    {
        ManeuverNodeManager mgr;
        Camera cam;
        bool dragging;

        public void Init(ManeuverNodeManager manager)
        {
            mgr = manager;
            cam = Camera.main;
        }

        void OnMouseDown()
        {
            if (mgr == null || mgr.nodes.Count == 0) return;
            if (mgr.nodes[0].isFinalized) return;
            dragging = true;
        }

        void OnMouseUp() { dragging = false; }

        void Update()
        {
            if (!dragging || mgr == null) return;
            var node = mgr.nodes.Count > 0 ? mgr.nodes[0] : null;
            if (node == null || node.isFinalized) return;

            var traj = node.trajectorySnapshot;
            if (traj == null || traj.Count < 2) return;

            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            Vector2 mouse = Input.mousePosition;

            // Find the closest screen-space segment
            int bestSeg = 0; float bestT = 0f; float bestD2 = float.PositiveInfinity;
            for (int i = 0; i < traj.Count - 1; i++)
            {
                Vector3 wa = traj[i];
                Vector3 wb = traj[i + 1];

                Vector3 sa3 = cam.WorldToScreenPoint(wa);
                Vector3 sb3 = cam.WorldToScreenPoint(wb);

                // Skip if behind camera (z <= 0)
                if (sa3.z <= 0f && sb3.z <= 0f) continue;

                Vector2 sa = new Vector2(sa3.x, sa3.y);
                Vector2 sb = new Vector2(sb3.x, sb3.y);

                float t = ClosestParamOnSegment2D(sa, sb, mouse);
                Vector2 sp = Vector2.Lerp(sa, sb, t);
                float d2 = (sp - mouse).sqrMagnitude;
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    bestSeg = i;
                    bestT = t;
                }
            }

            // Move node along trajectory by the best segment param
            mgr.SetNodeAtFloatIndex(bestSeg + bestT);
            mgr.MarkAdjusted();
        }

        static float ClosestParamOnSegment2D(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            float len2 = Vector2.Dot(ab, ab);
            if (len2 < 1e-8f) return 0f;
            float t = Vector2.Dot(p - a, ab) / len2;
            return Mathf.Clamp01(t);
        }
    }

    public void RemoveNode(ManeuverNode node)
    {
        if (node.marker != null) Destroy(node.marker);
        nodes.Remove(node);
        UpdateManeuverPrediction(node); // no-op if missing data
    }
}
