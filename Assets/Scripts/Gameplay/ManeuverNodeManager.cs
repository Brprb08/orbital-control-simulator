using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using Unity.Mathematics;
using System;

public class ManeuverNodeManager : MonoBehaviour
{
    // ------------------------------------------------------------
    // Fields / Inspector
    // ------------------------------------------------------------

    [Header("Maneuver Nodes")]
    public List<ManeuverNode> nodes = new();

    [Header("Trajectory Rendering")]
    public ProceduralLineRenderer maneuverTrajectoryLine;
    public TrajectoryRenderer trajectoryRenderer;
    public TimeController timeController;
    private BodyService bodyService;
    public ThrustController thrustController;
    public TrajectoryComputeController trajectoryComputeController;

    [Header("References - UI")]
    public Slider maneuverTimeSlider;
    public TMP_Dropdown burnDropdown;
    public Button setupButton;
    public Slider adjustNodeSlider;
    public Button placeNodeButton;
    public Button removeNodeButton;

    [Header("Materials")]
    [SerializeField] Material green;
    [SerializeField] Material red;

    [Header("UX Settings")]
    [SerializeField] float sliderUpdateMinInterval = 0.02f;
    [SerializeField] float nodeVisualScale = 1f;
    [SerializeField] bool allowSlider = true;

    public OrbitPreviewUI orbitPreviewUI;

    public bool isSliderActive = false;

    private float _nextSliderAllowed;
    private Vector3 previewVCcache = Vector3.right;
    private Vector3 previewHCache = Vector3.up;
    private int previewLastPolarSign = +1;

    [Header("Burn Tuning")]
    float burnDuration = 20f;
    private float thrustPowerScale = 1f;
    public BurnTuningController burnTuningController;

    private bool nodeAdjusted = false;

    private OrbitalParameters _previewOrbitParams = new OrbitalParameters(false);
    public OrbitalParameters PreviewOrbitParams => _previewOrbitParams;

    public BodyRuntimeCoordinator bodyRuntimeCoordinator;
    private TutorialController tutorialController;
    private SimContext ctx;

    // ------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        bodyRuntimeCoordinator = ctx.BodyRuntimeCoordinator;
        trajectoryRenderer = ctx.TrajectoryRenderer;
        timeController = ctx.TimeController;
        tutorialController = ctx.TutorialController;
        thrustController = ctx.ThrustController;
        trajectoryComputeController = ctx.TrajectoryComputeController;
        bodyService = ctx.BodyService;

        if (burnTuningController != null)
        {
            burnTuningController.defaultBurnDuration = burnDuration;
            burnTuningController.defaultThrustScale = thrustPowerScale;

            burnTuningController.BurnDurationChanged += OnBurnDurationChangedFromUI;
            burnTuningController.ThrustScaleChanged += OnThrustScaleChangedFromUI;

            burnTuningController.SetSlidersInteractable(false);
        }

        burnDropdown.ClearOptions();
        var burnOptions = Enum.GetValues(typeof(BurnType));
        foreach (BurnType t in burnOptions)
        {
            burnDropdown.options.Add(new TMP_Dropdown.OptionData(t.ToDisplayName()));
        }

        if (trajectoryRenderer != null)
            trajectoryRenderer.TrackedBodyChanged += OnTrackedBodyChanged;

        if (maneuverTimeSlider != null)
        {
            maneuverTimeSlider.interactable = false;
            maneuverTimeSlider.onValueChanged.RemoveAllListeners();
        }

        if (placeNodeButton != null) placeNodeButton.interactable = false;
        if (adjustNodeSlider != null) adjustNodeSlider.interactable = false;
        if (removeNodeButton != null) removeNodeButton.interactable = false;

        isSliderActive = false;
    }

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

        if (node.marker != null && bodyRuntimeCoordinator != null)
        {
            float tMinus = node.burnTime - bodyRuntimeCoordinator.simulationTime;
            var giz = node.marker.GetComponent<NodeGizmo>();
            if (giz) giz.SetTimeToNode(node.burnType.ToDisplayName(), tMinus);
        }
    }

    private void OnDestroy()
    {
        if (trajectoryRenderer != null)
            trajectoryRenderer.TrackedBodyChanged -= OnTrackedBodyChanged;

        if (maneuverTimeSlider != null)
            maneuverTimeSlider.onValueChanged.RemoveListener(OnSliderChanged);

        if (burnTuningController != null)
        {
            burnTuningController.BurnDurationChanged -= OnBurnDurationChangedFromUI;
            burnTuningController.ThrustScaleChanged -= OnThrustScaleChangedFromUI;
        }
    }

    private void OnTrackedBodyChanged(NBody oldBody, NBody newBody)
    {
        bool active = nodes.Count > 0 && nodes[0].targetBody == newBody;
        isSliderActive = active;
        if (adjustNodeSlider != null) adjustNodeSlider.interactable = active;
    }

    /// <summary>
    /// Creates a maneuver node ~20 seconds ahead along the tracked body's trajectory.
    /// </summary>
    public void OnAddManeuverNode()
    {
        timeController.SetTimeScale(1f);
        timeController.timeSlider.value = Time.timeScale;

        var body = trajectoryRenderer.trackedBody;
        if (body == null)
            return;

        int steps = 6000;
        float dt = trajectoryRenderer != null
            ? trajectoryRenderer.predictionDeltaTime
            : 2f;

        body.ComputePredictionForNodes(steps, dt, (traj, startTime, usedDt) =>
        {
            if (traj == null || traj.Count < 2)
                return;

            float initialOffsetTime = 20f;
            float desiredBurnTime = bodyRuntimeCoordinator.simulationTime + initialOffsetTime;

            float timeFromPredictionStart = desiredBurnTime - startTime;
            if (timeFromPredictionStart < 0f)
                timeFromPredictionStart = 0f;

            float floatIndex = timeFromPredictionStart / usedDt;
            int index = Mathf.Clamp(Mathf.FloorToInt(floatIndex), 0, traj.Count - 2);
            float t = floatIndex - index;

            Vector3 a = traj[index];
            Vector3 b = traj[index + 1];
            Vector3 burnPos = Vector3.Lerp(a, b, t);

            Vector3 deltaV = body.velocity.normalized * 1f;

            ClearAllNodes();

            var node = new ManeuverNode
            {
                position = burnPos,
                burnTime = desiredBurnTime,
                deltaV = deltaV,
                targetBody = body,
                duration = burnDuration,
                isFinalized = false,
                burnType = GetBurnChoice(),
                trajectorySnapshot = new List<Vector3>(traj),
                snapshotStartTime = startTime,
                snapshotDeltaTime = usedDt
            };

            SetupNodeVisuals(node, isPreview: true);
            nodes.Add(node);

            UpdateManeuverPrediction(node);
            UpdatePreviewOrbit(node);

            if (placeNodeButton != null)
                placeNodeButton.interactable = true;
            if (removeNodeButton != null)
                removeNodeButton.interactable = true;

            SetupSlider(node.trajectorySnapshot, node.burnTime, node.snapshotDeltaTime);

            isSliderActive = true;
            if (adjustNodeSlider != null)
                adjustNodeSlider.interactable = true;
            if (maneuverTimeSlider != null)
                maneuverTimeSlider.interactable = true;

            if (burnTuningController != null)
                burnTuningController.SetSlidersInteractable(true);

            if (tutorialController != null && tutorialController.inTutorialMode)
                tutorialController.hasSetupNode = true;
        });
    }

    /// <summary>
    /// Finalizes the current maneuver node, pins it, and disables further interaction.
    /// </summary>
    public void FinalizeManeuver()
    {
        if (nodes.Count == 0) return;
        var node = nodes[0];
        if (node == null || node.marker == null) return;

        node.isFinalized = true;

        node.marker.transform.SetParent(null, true);
        node.pinnedWorldPosition = node.marker.transform.position;
        node.isPinned = true;

        SetupNodeVisuals(node, isPreview: false);

        isSliderActive = false;
        maneuverTimeSlider?.onValueChanged.RemoveListener(OnSliderChanged);

        if (maneuverTimeSlider != null)
            maneuverTimeSlider.interactable = false;
        if (adjustNodeSlider != null)
            adjustNodeSlider.interactable = false;
        if (placeNodeButton != null)
            placeNodeButton.interactable = false;

        if (burnTuningController != null)
            burnTuningController.SetSlidersInteractable(false);

        if (bodyService != null && bodyService.CentralBody != null)
        {
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
                while (node.burnTime < simTime)
                    node.burnTime += orbit.orbitalPeriod;
            }
        }

        if (tutorialController != null && tutorialController.inTutorialMode)
            tutorialController.hasPlacedNode = true;

        if (trajectoryRenderer != null)
            trajectoryRenderer.ClearPreview();
    }

    /// <summary>
    /// Creates a preview node using the latest trajectory prediction.
    /// </summary>
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
            targetBody = trackedBody,
            duration = duration,
            isFinalized = false,
            burnType = GetBurnChoice(),
            trajectorySnapshot = snapshot,
            snapshotStartTime = trajectoryRenderer.latestPredictionStartTime,
            snapshotDeltaTime = Mathf.Max(1e-5f, trajectoryRenderer.latestPredictionDeltaTime)
        };

        SetupNodeVisuals(node, isPreview: true);
        nodes.Add(node);

        UpdateManeuverPrediction(node);

        FocusCameraOn(node.marker.transform.position);
        UpdatePreviewOrbit(node);
    }

    private void SetupNodeVisuals(ManeuverNode node, bool isPreview)
    {
        if (node.marker == null)
            node.marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);

        node.marker.name = isPreview ? "ManeuverNodePreview" : "ManeuverNode";
        node.marker.transform.position = node.position;
        node.marker.transform.localScale = Vector3.one * (5f * nodeVisualScale);

        var rend = node.marker.GetComponent<Renderer>();
        if (isPreview)
        {
            rend.material = new Material(green);
            CopyColorIfPresent(green, rend.material);
        }
        else
        {
            rend.material = new Material(red);
            CopyColorIfPresent(red, rend.material);
        }
        rend.material.renderQueue = 5000;

        var col = node.marker.GetComponent<SphereCollider>();
        if (col != null)
        {
            col.isTrigger = isPreview;
            col.radius = 0.9f;
        }

        var giz = node.marker.GetComponent<NodeGizmo>();
        if (giz == null)
            giz = node.marker.AddComponent<NodeGizmo>();

        if (isPreview && green.HasProperty("_BaseColor"))
            giz.baseColor = green.GetColor("_BaseColor");

        if (isPreview)
        {
            var drag = node.marker.GetComponent<NodeDragHandle>();
            if (drag == null)
            {
                drag = node.marker.AddComponent<NodeDragHandle>();
                drag.Init(this);
            }
        }
        else
        {
            giz.SetPulse(false);

            var drag = node.marker.GetComponent<NodeDragHandle>();
            if (drag != null)
                Destroy(drag);

            if (col != null)
                col.enabled = false;
        }
    }

    public void ClearAllNodes()
    {
        foreach (var n in nodes)
            if (n.marker != null) Destroy(n.marker);
        nodes.Clear();
        trajectoryRenderer?.ClearPreview();

        isSliderActive = false;

        if (maneuverTimeSlider != null)
        {
            maneuverTimeSlider.interactable = false;
            maneuverTimeSlider.onValueChanged.RemoveListener(OnSliderChanged);
        }

        if (adjustNodeSlider != null)
            adjustNodeSlider.interactable = false;

        if (placeNodeButton != null)
            placeNodeButton.interactable = false;
        if (removeNodeButton != null)
            removeNodeButton.interactable = false;

        if (burnTuningController != null)
            burnTuningController.SetSlidersInteractable(false);
    }

    public void RemoveNode(ManeuverNode node)
    {
        if (node.marker != null) Destroy(node.marker);
        nodes.Remove(node);
        UpdateManeuverPrediction(node);
        if (nodes.Count == 0)
        {
            trajectoryRenderer?.ClearPreview();

            isSliderActive = false;

            if (maneuverTimeSlider != null)
            {
                maneuverTimeSlider.interactable = false;
                maneuverTimeSlider.onValueChanged.RemoveListener(OnSliderChanged);
            }

            if (adjustNodeSlider != null)
                adjustNodeSlider.interactable = false;
            if (placeNodeButton != null)
                placeNodeButton.interactable = false;
            if (removeNodeButton != null)
                removeNodeButton.interactable = false;

            if (burnTuningController != null)
                burnTuningController.SetSlidersInteractable(false);
        }
    }

    /// <summary>
    /// Used by drag handle to move the node along its trajectory.
    /// </summary>
    public void DragNodeToFloatIndex(float floatIndex)
    {
        SetNodeAtFloatIndex(floatIndex);
    }

    public bool TryGetCurrentNodeIndex(out float currentFloatIndex)
    {
        currentFloatIndex = 0f;

        if (nodes.Count == 0) return false;
        var node = nodes[0];
        if (node == null || node.trajectorySnapshot == null || node.trajectorySnapshot.Count < 2)
            return false;

        float dt = Mathf.Max(1e-5f, node.snapshotDeltaTime);
        currentFloatIndex = (node.burnTime - node.snapshotStartTime) / dt;
        currentFloatIndex = Mathf.Clamp(currentFloatIndex, 0f, node.trajectorySnapshot.Count - 1.0001f);
        return true;
    }

    public void OnDeltaVChanged(float newDv)
    {
        if (!float.IsFinite(newDv)) return;
        MarkAdjusted();
    }

    public void UpdateManeuverPrediction(ManeuverNode node = null)
    {
        if (trajectoryRenderer == null) return;
        node ??= nodes.Count > 0 ? nodes[0] : null;
        if (node == null || node.isFinalized) return;

        if (!TrajectorySampler.TrySampleAtBurnTime(node, out var pos, out _, out _))
            return;

        node.position = pos;
    }

    private void UpdatePreviewOrbit(ManeuverNode node)
    {
        if (trajectoryRenderer == null || node == null) return;
        if (node.trajectorySnapshot == null || node.trajectorySnapshot.Count < 3) return;
        if (node.targetBody == null) return;

        var traj = node.trajectorySnapshot;
        float sampleDt = Mathf.Max(1e-5f, node.snapshotDeltaTime);

        if (!TrajectorySampler.TrySampleAtBurnTime(node, out var burnPos, out var velAtBurn, out var floatIndex))
            return;

        var central = bodyService != null ? bodyService.CentralBody : null;
        Vector3 center = central != null ? central.transform.position : Vector3.zero;
        double mu = (central != null) ? PhysicsConstants.G * central.mass : 0.0;

        const float EPS = 1e-8f;

        Vector3 r = burnPos - center;
        Vector3 v = velAtBurn;
        Vector3 rHat = r.sqrMagnitude > EPS ? r.normalized : Vector3.up;
        Vector3 vHat = v.sqrMagnitude > EPS ? v.normalized : node.targetBody.velocity.normalized;

        Vector3 h = Vector3.Cross(r, v);
        Vector3 hHat = (h.sqrMagnitude > EPS) ? h.normalized : Vector3.up;

        Vector3 burnDirRaw = AttitudeMath.ComputeBurnDirection(
            node.burnType,
            burnPos,
            velAtBurn,
            center,
            ref previewVCcache,
            ref previewHCache,
            ref previewLastPolarSign
        );

        if (burnDirRaw.sqrMagnitude < EPS)
            burnDirRaw = vHat;
        burnDirRaw.Normalize();

        Vector3 burnDirConstant = burnDirRaw;

        float normalSign = 1f;
        if (node.burnType == BurnType.Normal || node.burnType == BurnType.AntiNormal)
        {
            float dot = Vector3.Dot(burnDirRaw, hHat);
            if (dot < 0f) normalSign = -1f;
        }

        float mass = node.targetBody.mass;

        float thrustMag = 10f;
        if (thrustController != null)
            thrustMag = thrustController.EffectiveForwardThrustMagnitude;

        float F = thrustMag / 10f;
        float aThrust = (mass > 0f) ? F / mass : 0f;

        float burnDuration = Mathf.Max(0f, node.duration > 0f ? node.duration : this.burnDuration);

        float dtBurn = Mathf.Min(0.25f, sampleDt);
        int burnSteps = Mathf.Max(1, Mathf.CeilToInt(burnDuration / dtBurn));

        Vector3 pos = burnPos;
        Vector3 vel = velAtBurn;

        for (int i = 0; i < burnSteps; i++)
        {
            Vector3 rStep = pos - center;
            float rMag = rStep.magnitude;
            Vector3 aGrav = Vector3.zero;

            if (mu > 0.0 && rMag > 1e-3f)
            {
                float invR3 = 1.0f / (rMag * rMag * rMag);
                aGrav = (float)(-mu) * invR3 * rStep;
            }

            Vector3 burnDirStep;

            if (node.burnType == BurnType.Normal || node.burnType == BurnType.AntiNormal)
            {
                Vector3 rHatStep = rStep.sqrMagnitude > EPS ? rStep.normalized : rHat;
                Vector3 vHatStep = vel.sqrMagnitude > EPS ? vel.normalized : vHat;

                Vector3 hStep = Vector3.Cross(rStep, vel);
                Vector3 nHatStep = (hStep.sqrMagnitude > EPS)
                    ? hStep.normalized
                    : hHat;

                Vector3 nSigned = normalSign > 0f ? nHatStep : -nHatStep;

                Vector3 lateral = nSigned - Vector3.Dot(nSigned, vHatStep) * vHatStep;

                if (lateral.sqrMagnitude < 1e-6f)
                {
                    burnDirStep = nSigned;
                }
                else
                {
                    burnDirStep = lateral.normalized;
                }
            }
            else
            {
                burnDirStep = burnDirConstant;
            }

            burnDirStep.Normalize();

            Vector3 aTotal = aGrav + burnDirStep * aThrust;

            vel += aTotal * dtBurn;
            pos += vel * dtBurn;
        }

        _previewOrbitParams = new OrbitalParameters(false);

        if (central != null)
        {
            double3 posD = new double3(pos.x, pos.y, pos.z);
            double3 velD = new double3(vel.x, vel.y, vel.z);

            _previewOrbitParams = OrbitalCalculations.CalculateOrbitalParameters(
                central.mass,
                central.transform.position,
                posD,
                velD
            );

            if (orbitPreviewUI != null)
            {
                if (_previewOrbitParams.isValid)
                    orbitPreviewUI.Show(_previewOrbitParams, central);
                else
                    orbitPreviewUI.ShowInvalid();
            }
        }
        else
        {
            orbitPreviewUI?.ShowInvalid();
        }

        trajectoryRenderer.QuickPreviewOnceLong(
            startPos: pos,
            startVel: vel,
            bodyMass: mass,
            steps: 6000,
            dt: trajectoryRenderer.predictionDeltaTime,
            singleOrbit: true
        );
    }

    public void SetupSlider(List<Vector3> trajectory, float burnTime, float predictionDeltaTime)
    {
        if (!allowSlider || trajectory == null || trajectory.Count == 0 || maneuverTimeSlider == null) return;

        isSliderActive = true;
        maneuverTimeSlider.wholeNumbers = false;
        maneuverTimeSlider.minValue = 0f;
        maneuverTimeSlider.maxValue = trajectory.Count - 1;

        var node = nodes[0];
        float dt = Mathf.Max(1e-5f, node.snapshotDeltaTime);
        float floatIndex = (burnTime - node.snapshotStartTime) / dt;
        maneuverTimeSlider.value = Mathf.Clamp(floatIndex, 0f, maneuverTimeSlider.maxValue);

        maneuverTimeSlider.onValueChanged.RemoveAllListeners();
        maneuverTimeSlider.onValueChanged.AddListener(OnSliderChanged);
    }

    public void OnSliderChanged(float value)
    {
        if (Time.unscaledTime < _nextSliderAllowed) return;
        _nextSliderAllowed = Time.unscaledTime + sliderUpdateMinInterval;
        SetNodeAtFloatIndex(value);
        MarkAdjusted();
    }

    public void SetNodeAtFloatIndex(float floatIndex)
    {
        if (nodes.Count == 0) return;
        var node = nodes[0];
        if (node.isFinalized) return;
        var traj = node.trajectorySnapshot;
        if (traj == null || traj.Count < 2) return;

        int count = traj.Count;
        floatIndex = Mathf.Clamp(floatIndex, 0f, count - 1.0001f);

        float sampleDt = Mathf.Max(1e-5f, node.snapshotDeltaTime);
        float newBurnTime = node.snapshotStartTime + floatIndex * sampleDt;

        Vector3 p = TrajectorySampler.SampleAtIndex(traj, floatIndex);

        node.burnTime = newBurnTime;
        node.position = p;
        if (node.marker) node.marker.transform.position = p;

        UpdateManeuverPrediction(node);
        UpdatePreviewOrbit(node);

        if (allowSlider && maneuverTimeSlider != null && isSliderActive)
            maneuverTimeSlider.SetValueWithoutNotify(floatIndex);
    }

    private void OnBurnDurationChangedFromUI(float newDuration)
    {
        burnDuration = newDuration;

        if (nodes.Count > 0)
        {
            var node = nodes[0];
            if (!node.isFinalized)
            {
                node.duration = burnDuration;
                UpdatePreviewOrbit(node);
                MarkAdjusted();
            }
        }
    }

    private void OnThrustScaleChangedFromUI(float newScale)
    {
        thrustPowerScale = newScale;

        if (thrustController != null)
            thrustController.SetThrustPowerScale(thrustPowerScale);

        if (nodes.Count > 0)
        {
            var node = nodes[0];
            if (!node.isFinalized)
            {
                UpdatePreviewOrbit(node);
                MarkAdjusted();
            }
        }
    }

    void MarkAdjusted()
    {
        if (!nodeAdjusted)
        {
            nodeAdjusted = true;
            if (placeNodeButton != null) placeNodeButton.interactable = true;
        }
    }

    public Vector3 GetBurnDirectionFromDropdown(NBody targetBody)
    {
        if (targetBody == null) return Vector3.forward;
        if (trajectoryRenderer == null) return targetBody.velocity.normalized;

        var central = bodyService != null ? bodyService.CentralBody : null;
        Vector3 r = central != null
            ? (targetBody.transform.position - central.transform.position)
            : targetBody.transform.position;

        Vector3 velocity = targetBody.velocity.normalized;
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
        return BurnTypeExtensions.FromDropdownIndex(burnDropdown.value);
    }

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
}
