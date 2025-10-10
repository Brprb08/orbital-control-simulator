using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Renders predicted and previewed orbital trajectories for the currently tracked body,
/// including apogee/perigee markers, origin line, and pre-maneuver snapshots. Supports
/// both fast interactive passes and longer, higher-horizon passes.
/// </summary>
public class TrajectoryRenderer : MonoBehaviour
{
    private const int MAX_STEPS = 100_000;                 // GPU per-dispatch step cap
    private const float DAY = 24f * 60f * 60f;
    private const float MAX_HORIZON_SECONDS = 10f * DAY;   // Long-pass cap
    private const float MIN_HORIZON_SECONDS = 20_000f;     // Minimum horizon
    private const float MAX_FAST_HORIZON = 2f * DAY;       // Fast-pass cap
    private const float uiInterval = 0.5f;

    [Header("Prediction")]
    [Min(1)] public int predictionSteps = 5000;
    [Min(0.0001f)] public float predictionDeltaTime = 7f;
    public bool orbitIsDirty = true;

    [Header("Refs")]
    public TextMeshProUGUI apogeeText;
    public TextMeshProUGUI perigeeText;
    public ThrustController thrustController;
    public CameraMovement cameraMovement;
    public BodyRuntimeCoordinator bodyRuntimeCoordinator;
    public BodyService bodyService;
    public NBody trackedBody;

    private CameraController cameraController;
    private UIManager ui;
    private Camera mainCamera;

    [Header("Lines")]
    public ProceduralLineRenderer predictionLine;
    public ProceduralLineRenderer originLine;
    public ProceduralLineRenderer apogeeLine;
    public ProceduralLineRenderer perigeeLine;
    public ProceduralLineRenderer preManeuverLine;

    [Header("Appearance")]
    public Color predictionColor = new Color32(0x29, 0x78, 0xFF, 255);
    public Color originColor = Color.white;
    public Color apogeeColor = new Color32(0xC0, 0x39, 0x2B, 255);
    public Color perigeeColor = new Color32(0x00, 0x9B, 0x4D, 255);
    public Color previewColor = new Color32(0xFF, 0xD1, 0x66, 255);
    public float lineDisableDistance = 20f;

    [Header("State")]
    private bool isThrusting;
    private bool savedOriginalOrbit;
    private bool isComputingPrediction;
    private bool fullPassRequested;
    private bool wasThrusting;

    private Coroutine predictionCo;
    private float uiNextTick;

    [Header("Outputs")]
    public List<Vector3> latestPrediction = new();
    public float latestPredictionDeltaTime;
    public float latestPredictionStartTime;
    private List<Vector3> preManeuverSnapshot;

    [Header("Preview")]
    public ProceduralLineRenderer previewLine;

    private Coroutine previewCo;
    private Vector3 previewPos, previewVel;
    private float previewMass;
    private bool previewDirty;

    public event Action<NBody, NBody> TrackedBodyChanged;

    private SimContext ctx;

    /// <summary>
    /// Sets up references from the simulation context, creates the line renderers,
    /// subscribes to camera events, and syncs to the current tracked body.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        bodyRuntimeCoordinator = ctx.BodyRuntimeCoordinator;
        cameraController = ctx.CameraController;
        cameraMovement = ctx.CameraMovement;
        thrustController = ctx.ThrustController;
        ui = ctx.UIManager;

        mainCamera = Camera.main;

        predictionLine = CreateProceduralLineRenderer("Prediction1Line", predictionColor);
        originLine = CreateProceduralLineRenderer("OriginLine", originColor);
        apogeeLine = CreateProceduralLineRenderer("ApogeeLine", apogeeColor);
        perigeeLine = CreateProceduralLineRenderer("PerigeeLine", perigeeColor);
        preManeuverLine = CreateProceduralLineRenderer("PreManeuverLine", "#CCCCCC");
        previewLine = CreateProceduralLineRenderer("PreviewLine", "#FFD166");

        if (!bodyRuntimeCoordinator) Debug.LogError("[TrajectoryRenderer] missing BodyRuntimeCoordinator");
        if (!cameraMovement) Debug.LogError("[TrajectoryRenderer] missing CameraMovement");
        if (!thrustController) Debug.LogError("[TrajectoryRenderer] missing ThrustController");
        if (!ui) Debug.LogError("[TrajectoryRenderer] missing UIManager");

        if (!cameraController)
        {
            Debug.LogError("[TrajectoryRenderer] missing CameraController");
            return;
        }

        cameraController.OnTrackedBodyChanged += HandleTrackedBodyChanged;

        var current = cameraController.CurrentBody;
        if (current != null && current != trackedBody)
            SetTrackedBody(current);
    }

    /// <summary>
    /// Responds to camera-tracked body changes and updates internal state.
    /// </summary>
    private void HandleTrackedBodyChanged(NBody newBody)
    {
        if (newBody == trackedBody) return;
        SetTrackedBody(newBody);
    }

    /// <summary>
    /// Orchestrates prediction cadence, long-pass scheduling, UI updates,
    /// line visibility toggles, and the origin line.
    /// </summary>
    private void Update()
    {
        if (!trackedBody)
        {
            predictionLine?.Clear();
            originLine?.Clear();
            apogeeLine?.Clear();
            perigeeLine?.Clear();
            preManeuverLine?.Clear();
            // Keep previewLine as-is; do not clear here.
            ui?.ShowApogeePerigeePanel(false);
            return;
        }

        // Track thrust state and capture pre-maneuver snapshot on rising edge
        if (thrustController)
        {
            bool nowThrusting = thrustController.IsThrusting;
            if (nowThrusting && !savedOriginalOrbit)
            {
                CapturePreManeuverFromLatest();
                savedOriginalOrbit = true;
            }
            else if (!nowThrusting)
            {
                savedOriginalOrbit = false;
            }
            isThrusting = nowThrusting;
        }

        // If thrust just stopped, schedule a full pass
        if (wasThrusting && !isThrusting) fullPassRequested = true;
        wasThrusting = isThrusting;

        if (trackedBody.cumulativeDeltaVUsed != 0f)
            ui?.UpdateDeltaV(trackedBody.cumulativeDeltaVUsed);

        // Prioritize long pass when idle
        if (fullPassRequested && !isThrusting && !isComputingPrediction
            && cameraMovement?.targetBody == trackedBody)
            ComputeFinalLongPass(trackedBody);

        // Otherwise run the responsive/fast pass
        if (ShouldComputePrediction(trackedBody))
            KickOrRefreshPrediction(trackedBody);

        // UI + apogee/perigee (tick-limited)
        if (Time.unscaledTime >= uiNextTick)
        {
            var p = OrbitalCalculations.TryParams(trackedBody, bodyService);
            if (p.isValid) ShowApogeePerigeeLines(p);
            uiNextTick = Time.unscaledTime + uiInterval;
        }

        ToggleLinesByDistance();
        DrawOriginLine();
    }

    /// <summary>
    /// Determines if a prediction should be computed for the given body.
    /// </summary>
    private bool ShouldComputePrediction(NBody body)
    {
        if (fullPassRequested && !isThrusting) return false;
        if (body == null) return false;
        if (cameraMovement == null || cameraMovement.targetBody != body) return false;
        return isThrusting || orbitIsDirty;
    }

    /// <summary>
    /// Sets the tracked body and resets prediction/line state.
    /// </summary>
    public void SetTrackedBody(NBody body)
    {
        if (predictionCo != null)
        {
            StopCoroutine(predictionCo);
            predictionCo = null;
        }

        preManeuverSnapshot = null;
        ClearAllLines();
        var old = trackedBody;
        trackedBody = body;

        TrackedBodyChanged?.Invoke(old, trackedBody);

        if (!trackedBody)
        {
            ui?.ShowApogeePerigeePanel(false);
            orbitIsDirty = false;
            isComputingPrediction = false;
            return;
        }

        RequestFullOrbitPass();
        ui?.ShowApogeePerigeePanel(true);
        orbitIsDirty = true;
    }

    /// <summary>
    /// Requests a full long-horizon prediction the next time conditions allow.
    /// </summary>
    public void RequestFullOrbitPass() => fullPassRequested = true;

    /// <summary>
    /// Starts or refreshes a fast, responsive prediction pass.
    /// </summary>
    private void KickOrRefreshPrediction(NBody body)
    {
        if (isComputingPrediction) return;
        var p = OrbitalCalculations.TryParams(body, bodyService);
        if (!p.isValid) return;

        bool fast = isThrusting || Time.timeScale > 5f;
        float horizonSeconds = ComputeHorizonSeconds(body, fast);

        float effectiveDt = predictionDeltaTime;
        int stepsNeeded = Mathf.CeilToInt(horizonSeconds / effectiveDt);

        predictionSteps = Mathf.Clamp(stepsNeeded, 500, MAX_STEPS);

        isComputingPrediction = true;
        body.CalculatePredictedTrajectoryGPU_Async(
            steps: predictionSteps,
            deltaTime: effectiveDt,
            onComplete: resultList =>
            {
                if (trackedBody != body) { isComputingPrediction = false; return; }

                latestPrediction = resultList ?? new List<Vector3>();
                latestPredictionStartTime = bodyRuntimeCoordinator ? bodyRuntimeCoordinator.simulationTime : 0f;
                latestPredictionDeltaTime = effectiveDt;

                predictionLine.UpdateLine(ClipTrajectory(latestPrediction.ToArray()));

                orbitIsDirty = false;
                isComputingPrediction = false;
            }
        );
    }

    /// <summary>
    /// Computes a single long-horizon pass, increasing dt as needed to stay within MAX_STEPS.
    /// </summary>
    private void ComputeFinalLongPass(NBody body)
    {
        var p = OrbitalCalculations.TryParams(body, bodyService);
        if (!p.isValid) return;

        float horizonSeconds = ComputeHorizonSeconds(body, fast: false);

        float effectiveDt = predictionDeltaTime;
        int stepsNeeded = Mathf.CeilToInt(horizonSeconds / effectiveDt);
        if (stepsNeeded > MAX_STEPS)
        {
            effectiveDt = horizonSeconds / MAX_STEPS;
            stepsNeeded = MAX_STEPS;
        }

        stepsNeeded = Mathf.Clamp(stepsNeeded + 8, 1500, MAX_STEPS);

        isComputingPrediction = true;
        body.CalculatePredictedTrajectoryGPU_Async(
            steps: stepsNeeded,
            deltaTime: effectiveDt,
            onComplete: resultList =>
            {
                if (trackedBody != body) { isComputingPrediction = false; return; }

                latestPrediction = resultList ?? new List<Vector3>();
                latestPredictionStartTime = bodyRuntimeCoordinator ? bodyRuntimeCoordinator.simulationTime : 0f;
                latestPredictionDeltaTime = effectiveDt;

                predictionLine.UpdateLine(ClipTrajectory(latestPrediction.ToArray()));

                orbitIsDirty = false;
                isComputingPrediction = false;
            }
        );

        fullPassRequested = false;
    }

    /// <summary>
    /// Computes the time horizon (seconds) for fast or final passes based on current orbital parameters.
    /// </summary>
    private float ComputeHorizonSeconds(NBody body, bool fast)
    {
        var p = OrbitalCalculations.TryParams(body, bodyService);
        if (!p.isValid)
            return Mathf.Clamp(30_000f, MIN_HORIZON_SECONDS, MAX_HORIZON_SECONDS);

        float mu = PhysicsConstants.G * body.state.centralBodyMass;
        bool bound = p.eccentricity < 1f && p.semiMajorAxis > 0f;

        float T = bound
            ? 2f * Mathf.PI * Mathf.Sqrt(Mathf.Pow(p.semiMajorAxis, 3) / mu)
            : 60_000f; // Fallback for hyperbolic

        if (fast)
        {
            float h = Mathf.Clamp(T * 0.4f, 8_000f, MAX_FAST_HORIZON);
            return Mathf.Min(h, MAX_HORIZON_SECONDS);
        }
        else
        {
            float h = Mathf.Clamp(T * 1.25f, MIN_HORIZON_SECONDS, MAX_HORIZON_SECONDS);
            return h;
        }
    }

    /// <summary>
    /// Draws a line from the tracked body to the central body (origin).
    /// </summary>
    private void DrawOriginLine()
    {
        if (originLine == null || trackedBody == null) return;

        var center = ctx.BodyService.CentralBody.transform.position;
        originLine.UpdateLine(new[] { trackedBody.transform.position, center });
    }

    /// <summary>
    /// Shows or hides lines based on camera distance to the tracked body.
    /// </summary>
    private void ToggleLinesByDistance()
    {
        if (mainCamera == null || trackedBody == null) return;

        float d = Vector3.Distance(mainCamera.transform.position, trackedBody.transform.position);
        bool show = d > lineDisableDistance;

        predictionLine?.SetVisibility(show);
        originLine?.SetVisibility(show);
        apogeeLine?.SetVisibility(show);
        perigeeLine?.SetVisibility(show);
        preManeuverLine?.SetVisibility(show);
        previewLine?.SetVisibility(show);
    }

    /// <summary>
    /// Creates and configures a procedural line using a Unity Color.
    /// </summary>
    private ProceduralLineRenderer CreateProceduralLineRenderer(string name, Color color)
    {
        GameObject go = new GameObject(name);
        ProceduralLineRenderer lr = go.AddComponent<ProceduralLineRenderer>();

        string hex = ColorUtility.ToHtmlStringRGB(color);
        lr.SetLineColor("#" + hex);
        lr.SetLineWidth(0.1f);
        return lr;
    }

    /// <summary>
    /// Creates and configures a procedural line using a hex color string (#RRGGBB).
    /// </summary>
    private ProceduralLineRenderer CreateProceduralLineRenderer(string name, string hexColor)
    {
        if (!ColorUtility.TryParseHtmlString(hexColor, out var col))
            col = Color.white;
        return CreateProceduralLineRenderer(name, col);
    }

    /// <summary>
    /// Captures a deep copy of the latest prediction for pre-maneuver display.
    /// </summary>
    private void CapturePreManeuverFromLatest()
    {
        if (latestPrediction != null && latestPrediction.Count > 1)
        {
            preManeuverSnapshot = new List<Vector3>(latestPrediction);
            var clipped = ClipTrajectory(preManeuverSnapshot.ToArray());
            preManeuverLine.UpdateLine(clipped);
        }
        else
        {
            preManeuverLine.Clear();
            preManeuverSnapshot = null;
        }
    }

    /// <summary>
    /// Clips a polyline trajectory against the central body; stops at the first hit.
    /// </summary>
    private Vector3[] ClipTrajectory(Vector3[] points)
    {
        if (points == null || points.Length < 2) return points;

        var clipped = new List<Vector3>(points.Length) { points[0] };

        for (int i = 1; i < points.Length; i++)
        {
            Vector3 a = points[i - 1];
            Vector3 b = points[i];
            var dir = b - a;
            float dist = dir.magnitude;

            if (Physics.Raycast(a, dir.normalized, out var hit, dist)
                && hit.collider.CompareTag("CentralBody"))
            {
                clipped.Add(hit.point);
                break;
            }
            clipped.Add(b);
        }
        return clipped.ToArray();
    }

    /// <summary>
    /// Draws apogee/perigee lines and updates orbit stats in the UI.
    /// Hides lines when the orbit is near circular.
    /// </summary>
    private void ShowApogeePerigeeLines(OrbitalParameters op)
    {
        if (!apogeeLine || !perigeeLine) return;

        var apo = op.apogeePosition;
        var per = op.perigeePosition;

        float circularUnitsThreshold = 0.1f; // 1 km / 10 km/u
        bool nearCircular = Mathf.Abs(apo.magnitude - per.magnitude) < circularUnitsThreshold;

        if (!nearCircular)
        {
            apogeeLine.UpdateLine(new[] { apo, Vector3.zero });
            perigeeLine.UpdateLine(new[] { per, Vector3.zero });
        }

        if (ui != null)
        {
            double ap_km = (apo.magnitude - 637.8) * 10.0;
            double pe_km = (per.magnitude - 637.8) * 10.0;

            ui.UpdateOrbitUI(
                (float)ap_km, (float)pe_km,
                op.semiMajorAxis, op.eccentricity, op.orbitalPeriod,
                op.inclination, op.RAAN
            );
        }
    }

    /// <summary>
    /// Clears all line renderers and resets tracked state.
    /// </summary>
    public void ClearAllLines()
    {
        predictionLine.Clear();
        originLine.Clear();
        apogeeLine.Clear();
        perigeeLine.Clear();
        preManeuverLine.Clear();
        previewLine.Clear();
        trackedBody = null;
    }

    /// <summary>
    /// Bulk visibility toggle for key renderers.
    /// </summary>
    public void SetLineVisibility(bool showPrediction, bool showOrigin, bool showApogeePerigee)
    {
        SetVisible(predictionLine.GetComponent<Renderer>(), showPrediction);
        SetVisible(originLine.GetComponent<Renderer>(), showOrigin);
        SetVisible(apogeeLine.GetComponent<Renderer>(), showApogeePerigee);
        SetVisible(perigeeLine.GetComponent<Renderer>(), showApogeePerigee);
    }

    /// <summary>
    /// Sets renderer visibility without disabling component behaviour.
    /// </summary>
    private static void SetVisible(Renderer r, bool visible)
    {
        if (!r) return;
        // Keeps updates active but stops drawing.
#if UNITY_2021_2_OR_NEWER
        r.forceRenderingOff = !visible;
#else
        r.enabled = visible;
#endif
    }

    // Preview APIs (used by VelocityDragManager)

    /// <summary>
    /// Starts or refreshes a lightweight continuous trajectory preview from a given state.
    /// </summary>
    public void QuickPreviewFromState(Vector3 startPos, Vector3 startVel, float bodyMass)
    {
        previewPos = startPos;
        previewVel = startVel;
        previewMass = Mathf.Max(1f, bodyMass);
        previewDirty = true;

        if (previewCo == null) previewCo = StartCoroutine(QuickPreviewLoop());
    }

    /// <summary>
    /// Clears the preview line and stops the preview worker if running.
    /// </summary>
    public void ClearPreview()
    {
        previewDirty = false;
        previewLine.Clear();
        if (previewCo != null) { StopCoroutine(previewCo); previewCo = null; }
    }

    /// <summary>
    /// Worker for the lightweight continuous preview path.
    /// </summary>
    private IEnumerator QuickPreviewLoop()
    {
        const float tick = 0.1f;
        while (true)
        {
            if (!previewDirty) { yield return new WaitForSecondsRealtime(tick); continue; }
            previewDirty = false;

            var svc = ctx.BodyService;
            if (svc == null || svc.Bodies == null || svc.Bodies.Count == 0)
            {
                previewLine.Clear();
                yield return new WaitForSecondsRealtime(tick);
                continue;
            }

            var pos = new List<Vector3>(svc.Bodies.Count);
            var mass = new List<float>(svc.Bodies.Count);
            for (int i = 0; i < svc.Bodies.Count; i++)
            {
                var body = svc.Bodies[i];
                if (body == null) continue;
                pos.Add(body.transform.position);
                mass.Add(body.mass);
            }

            ctx.TrajectoryComputeController.CalculateTrajectoryGPU_Async(
                previewPos, previewVel, previewMass,
                pos.ToArray(), mass.ToArray(),
                dt: 2f, steps: 1500,
                points =>
                {
                    if (points == null || points.Length < 2) { previewLine.Clear(); return; }
                    previewLine.UpdateLine(ClipTrajectory(points));
                });

            yield return new WaitForSecondsRealtime(tick);
        }
    }

    /// <summary>
    /// Runs a one-off longer preview, then resumes the lightweight loop.
    /// </summary>
    public void QuickPreviewOnceLong(Vector3 startPos, Vector3 startVel, float bodyMass, int steps = 8000, float dt = 2f)
    {
        if (previewCo != null) { StopCoroutine(previewCo); previewCo = null; }

        var svc = ctx?.BodyService;
        if (svc == null || svc.Bodies == null || svc.Bodies.Count == 0)
        {
            previewLine.Clear();
            return;
        }

        var pos = new List<Vector3>(svc.Bodies.Count);
        var mass = new List<float>(svc.Bodies.Count);
        for (int i = 0; i < svc.Bodies.Count; i++)
        {
            var b = svc.Bodies[i];
            if (b == null) continue;
            pos.Add(b.transform.position);
            mass.Add((float)b.mass);
        }

        ctx.TrajectoryComputeController.CalculateTrajectoryGPU_Async(
            startPos, startVel, Mathf.Max(1f, bodyMass),
            pos.ToArray(), mass.ToArray(),
            dt, steps,
            points =>
            {
                if (points == null || points.Length < 2) { previewLine?.Clear(); return; }
                var clipped = ClipTrajectory(points);
                previewLine.UpdateLine(clipped);

                if (previewCo == null) previewCo = StartCoroutine(QuickPreviewLoop());
            });
    }

    /// <summary>
    /// Clears the pre-maneuver line.
    /// </summary>
    public void ClearPreManeuverLine()
    {
        preManeuverLine.Clear();
    }
}
