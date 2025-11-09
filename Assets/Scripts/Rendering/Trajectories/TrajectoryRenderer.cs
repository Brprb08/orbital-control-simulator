using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

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

    [Header("Debounce")]
    [Tooltip("Coalesce rapid orbitIsDirty toggles to avoid churn.")]
    [SerializeField, Range(0, 5)] private int dirtyDebounceFrames = 2;
    private int _dirtyDebounceCounter = 0;

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
    public ProceduralLineRenderer burnLine;

    [Header("Appearance")]
    public Color predictionColor = new Color32(0x29, 0x78, 0xFF, 255); // blue
    public Color originColor = Color.white;
    public Color apogeeColor = new Color32(0xFF, 0xB3, 0x00, 255);     // amber
    public Color perigeeColor = new Color32(0x00, 0xBF, 0xA5, 255);    // teal
    public Color previewColor = new Color32(0x29, 0x78, 0xFF, 255);
    public Color burnColor = new Color32(0xFF, 0x3B, 0x30, 255);       // red

    public float lineDisableDistance = 20f;

    [Header("State")]
    private bool isThrusting;
    private bool savedOriginalOrbit;
    private bool isComputingPrediction;
    private bool fullPassRequested;
    private bool wasThrusting;

    [Header("Burn Trace State")]
    [SerializeField, Min(0.01f)] private float burnSampleInterval = 0.1f; // seconds, unscaled
    [SerializeField, Min(0f)] private float burnMinDistance = 0.05f;      // world units (~0.5 km if 1u=10km)
    [SerializeField, Min(128)] private int burnMaxPoints = 8192;

    private readonly List<Vector3> burnPoints = new();
    private float burnNextSampleTime;
    private bool burnTracingActive;

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

    // -------- Single-orbit clipping tuning --------
    [Header("Single Orbit Clipping")]
    [Tooltip("Enable clipping of rendered trajectories to a single revolution.")]
    [SerializeField] private bool clipToSingleOrbit = true;
    [Tooltip("How close (radians) to 2π before we consider a 'full turn' reached.")]
    [SerializeField, Range(0.001f, 0.5f)] private float fullTurnEpsilon = 0.00f; // tight by default
    [Tooltip("Ignore very small steps when summing angles (prevents jitter).")]
    [SerializeField, Range(0f, 0.05f)] private float minStepAngleRad = 0.0015f;

    /// <summary>Sets up references from the simulation context, creates the line renderers, subscribes to camera events, and syncs to the current tracked body.</summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        bodyRuntimeCoordinator = ctx.BodyRuntimeCoordinator;
        cameraController = ctx.CameraController;
        cameraMovement = ctx.CameraMovement;
        thrustController = ctx.ThrustController;
        ui = ctx.UIManager;
        bodyService = ctx.BodyService;

        mainCamera = Camera.main;

        predictionLine = CreateProceduralLineRenderer("Prediction1Line", predictionColor);
        originLine = CreateProceduralLineRenderer("OriginLine", originColor);
        apogeeLine = CreateProceduralLineRenderer("ApogeeLine", apogeeColor);
        perigeeLine = CreateProceduralLineRenderer("PerigeeLine", perigeeColor);
        preManeuverLine = CreateProceduralLineRenderer("PreManeuverLine", "#CCCCCC");
        previewLine = CreateProceduralLineRenderer("PreviewLine", "#FFD166");
        burnLine = CreateProceduralLineRenderer("BurnLine", burnColor);

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

    /// <summary>Responds to camera-tracked body changes and updates internal state.</summary>
    private void HandleTrackedBodyChanged(NBody newBody)
    {
        if (newBody == trackedBody) return;
        SetTrackedBody(newBody);
    }

    /// <summary>Orchestrates prediction cadence, long-pass scheduling, UI updates, line visibility toggles, and the origin line.</summary>
    private void Update()
    {
        // Debounce state tick: only relevant if someone toggled orbitIsDirty
        if (orbitIsDirty && _dirtyDebounceCounter == 0)
            _dirtyDebounceCounter = dirtyDebounceFrames;
        else if (_dirtyDebounceCounter > 0)
            _dirtyDebounceCounter--;

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

        UpdateBurnTrace(isThrusting);

        if (trackedBody.cumulativeDeltaVUsed != 0f)
            ui?.UpdateDeltaV(trackedBody.cumulativeDeltaVUsed);

        // Prioritize long pass when idle
        if (fullPassRequested && !isThrusting && !isComputingPrediction
            && cameraMovement?.targetBody == trackedBody)
            ComputeFinalLongPass(trackedBody);

        // Otherwise run the responsive/fast pass (only when debounce window has elapsed)
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

    /// <summary>Determines if a prediction should be computed for the given body.</summary>
    private bool ShouldComputePrediction(NBody body)
    {
        if (fullPassRequested && !isThrusting) return false;
        if (body == null) return false;
        if (cameraMovement == null || cameraMovement.targetBody != body) return false;

        // Debounce: only treat dirty as actionable when the counter == 0
        bool dirtyReady = orbitIsDirty && (_dirtyDebounceCounter == 0);

        return isThrusting || dirtyReady;
    }

    private void OnDestroy()
    {
        if (cameraController != null)
            cameraController.OnTrackedBodyChanged -= HandleTrackedBodyChanged;
    }

    /// <summary>Sets the tracked body and resets prediction/line state.</summary>
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
        _dirtyDebounceCounter = dirtyDebounceFrames; // coalesce the immediate post-switch noise
    }

    /// <summary>Requests a full long-horizon prediction the next time conditions allow.</summary>
    public void RequestFullOrbitPass() => fullPassRequested = true;

    /// <summary>Starts or refreshes a fast, responsive prediction pass.</summary>
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
                if (!this || !gameObject) return; // destroyed
                if (trackedBody != body) { isComputingPrediction = false; return; }
                if (predictionLine == null) { isComputingPrediction = false; return; }

                latestPrediction = resultList ?? new List<Vector3>();
                latestPredictionStartTime = bodyRuntimeCoordinator ? bodyRuntimeCoordinator.simulationTime : 0f;
                latestPredictionDeltaTime = effectiveDt;

                var pts = latestPrediction.ToArray();
                pts = ClipTrajectorySphere(pts);        // <--- fast math clip
                if (clipToSingleOrbit) pts = ClipToSingleOrbit(pts);
                predictionLine.UpdateLine(pts);

                orbitIsDirty = false;
                isComputingPrediction = false;
            }
        );
    }

    /// <summary>Computes a single long-horizon pass, increasing dt as needed to stay within MAX_STEPS.</summary>
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

                var pts = latestPrediction.ToArray();
                pts = ClipTrajectorySphere(pts);        // <--- fast math clip
                if (clipToSingleOrbit) pts = ClipToSingleOrbit(pts);
                predictionLine.UpdateLine(pts);

                orbitIsDirty = false;
                isComputingPrediction = false;
            }
        );

        fullPassRequested = false;
    }

    /// <summary>Computes the time horizon (seconds) for fast or final passes based on current orbital parameters.</summary>
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

    /// <summary>Maintains a red trace of the spacecraft path while thrust is active.</summary>
    private void UpdateBurnTrace(bool thrusting)
    {
        if (!trackedBody || burnLine == null) return;

        // Rising edge: start a new segment
        if (!burnTracingActive && thrusting)
        {
            burnTracingActive = true;
            burnPoints.Clear();
            burnNextSampleTime = Time.unscaledTime; // sample immediately
            burnPoints.Add(trackedBody.transform.position);
            burnLine.UpdateLine(burnPoints.ToArray());
        }

        // While thrusting: sample at cadence and min spacing
        if (burnTracingActive && thrusting)
        {
            if (Time.unscaledTime >= burnNextSampleTime)
            {
                var pos = trackedBody.transform.position;

                bool farEnough =
                    burnPoints.Count == 0 ||
                    (pos - burnPoints[burnPoints.Count - 1]).sqrMagnitude >= burnMinDistance * burnMinDistance;

                if (farEnough)
                {
                    burnPoints.Add(pos);
                    if (burnPoints.Count > burnMaxPoints)
                        burnPoints.RemoveRange(0, burnPoints.Count - burnMaxPoints);

                    burnLine.UpdateLine(burnPoints.ToArray());
                }

                burnNextSampleTime = Time.unscaledTime + burnSampleInterval;
            }
        }

        // Falling edge: finalize the segment (leave it drawn)
        if (burnTracingActive && !thrusting)
        {
            var pos = trackedBody.transform.position;
            if (burnPoints.Count == 0 ||
                (pos - burnPoints[burnPoints.Count - 1]).sqrMagnitude >= burnMinDistance * burnMinDistance)
            {
                burnPoints.Add(pos);
                if (burnPoints.Count > burnMaxPoints)
                    burnPoints.RemoveRange(0, burnPoints.Count - burnMaxPoints);
                burnLine.UpdateLine(burnPoints.ToArray());
            }

            burnTracingActive = false;
        }
    }

    /// <summary>Draws a line from the tracked body to the central body (origin).</summary>
    private void DrawOriginLine()
    {
        if (originLine == null || trackedBody == null || ctx?.BodyService?.CentralBody == null) return;

        var center = ctx.BodyService.CentralBody.transform.position;
        originLine.UpdateLine(new[] { trackedBody.transform.position, center });
    }

    /// <summary>Shows or hides lines based on camera distance to the tracked body.</summary>
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
        burnLine?.SetVisibility(show);
    }

    /// <summary>Creates and configures a procedural line using a Unity Color.</summary>
    private ProceduralLineRenderer CreateProceduralLineRenderer(string name, Color color)
    {
        GameObject go = new GameObject(name);
        ProceduralLineRenderer lr = go.AddComponent<ProceduralLineRenderer>();

        string hex = ColorUtility.ToHtmlStringRGB(color);
        lr.SetLineColor("#" + hex);
        lr.SetLineWidth(0.1f);
        return lr;
    }

    /// <summary>Creates and configures a procedural line using a hex color string (#RRGGBB).</summary>
    private ProceduralLineRenderer CreateProceduralLineRenderer(string name, string hexColor)
    {
        if (!ColorUtility.TryParseHtmlString(hexColor, out var col))
            col = Color.white;
        return CreateProceduralLineRenderer(name, col);
    }

    /// <summary>Captures a deep copy of the latest prediction for pre-maneuver display.</summary>
    private void CapturePreManeuverFromLatest()
    {
        if (latestPrediction != null && latestPrediction.Count > 1)
        {
            preManeuverSnapshot = new List<Vector3>(latestPrediction);
            var clipped = ClipTrajectorySphere(preManeuverSnapshot.ToArray());
            if (clipToSingleOrbit) clipped = ClipToSingleOrbit(clipped);
            preManeuverLine.UpdateLine(clipped);
        }
        else
        {
            preManeuverLine.Clear();
            preManeuverSnapshot = null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (1) FAST MATH CLIP AGAINST CENTRAL SPHERE (replaces Physics.Raycast)
    // ─────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Clips a polyline trajectory against the central body sphere; stops at the first hit.
    /// Uses analytic segment–sphere intersection. Much faster than Physics.Raycast.
    /// </summary>
    private Vector3[] ClipTrajectorySphere(Vector3[] points)
    {
        if (points == null || points.Length < 2) return points;
        if (ctx?.BodyService?.CentralBody == null) return points;

        Vector3 center = ctx.BodyService.CentralBody.transform.position;
        float radius = GetCentralBodyRadiusWorld(ctx.BodyService.CentralBody);

        float r2 = radius * radius;
        var clipped = new List<Vector3>(points.Length) { points[0] };

        for (int i = 1; i < points.Length; i++)
        {
            Vector3 a = points[i - 1];
            Vector3 b = points[i];
            Vector3 d = b - a;

            // Segment-sphere intersection: ||a + t d - C||^2 = R^2, t in [0,1]
            Vector3 m = a - center;
            float A = Vector3.Dot(d, d);
            float B = 2f * Vector3.Dot(m, d);
            float C = Vector3.Dot(m, m) - r2;

            // If both endpoints are outside and moving away without intersection, just append b.
            float discr = B * B - 4f * A * C;
            if (discr < 0f)
            {
                clipped.Add(b);
                continue;
            }

            float sqrt = Mathf.Sqrt(discr);
            float inv2A = 0.5f / A;
            float t0 = (-B - sqrt) * inv2A;
            float t1 = (-B + sqrt) * inv2A;

            // We need the smallest t within [0,1]
            bool hit = false;
            float tHit = float.PositiveInfinity;
            if (t0 >= 0f && t0 <= 1f) { hit = true; tHit = Mathf.Min(tHit, t0); }
            if (t1 >= 0f && t1 <= 1f) { hit = true; tHit = Mathf.Min(tHit, t1); }

            if (!hit)
            {
                clipped.Add(b);
                continue;
            }

            Vector3 pHit = a + tHit * d;
            clipped.Add(pHit);
            return clipped.ToArray(); // stop at first impact
        }

        return clipped.ToArray();
    }

    /// <summary>
    /// Best-effort central body radius (world units). Tries SphereCollider first,
    /// then a 'radius' field on the central body component (if present),
    /// falls back to 637.8 (Earth ~6378 km at 1u = 10 km).
    /// </summary>
    private float GetCentralBodyRadiusWorld(NBody central)
    {
        // Try a sphere collider if present
        var sc = central.GetComponent<SphereCollider>();
        if (sc != null)
        {
            float maxScale = Mathf.Max(
                central.transform.lossyScale.x,
                central.transform.lossyScale.y,
                central.transform.lossyScale.z
            );
            return sc.radius * maxScale;
        }

        // Try a public field/property named "radius"
        try
        {
            var t = central.GetType();
            var f = t.GetField("radius");
            if (f != null && f.FieldType == typeof(float))
                return (float)f.GetValue(central);

            var p = t.GetProperty("radius");
            if (p != null && p.PropertyType == typeof(float))
                return (float)p.GetValue(central, null);
        }
        catch { /* ignore */ }

        // Fallback sane default for Earth-scale worlds at 1u=10km
        return 637.8f;
    }

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Draws apogee/perigee lines and updates orbit stats in the UI.</summary>
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

    /// <summary>Clears all line renderers and resets tracked state.</summary>
    public void ClearAllLines()
    {
        predictionLine.Clear();
        originLine.Clear();
        apogeeLine.Clear();
        perigeeLine.Clear();
        preManeuverLine.Clear();
        previewLine.Clear();
        burnLine.Clear();
        burnPoints.Clear();
        burnTracingActive = false;
        trackedBody = null; // (3) keep this exactly as requested
    }

    /// <summary>Bulk visibility toggle for key renderers.</summary>
    public void SetLineVisibility(bool showPrediction, bool showOrigin, bool showApogeePerigee)
    {
        SetVisible(predictionLine.GetComponent<Renderer>(), showPrediction);
        SetVisible(originLine.GetComponent<Renderer>(), showOrigin);
        SetVisible(apogeeLine.GetComponent<Renderer>(), showApogeePerigee);
        SetVisible(perigeeLine.GetComponent<Renderer>(), showApogeePerigee);
    }

    /// <summary>Sets renderer visibility without disabling component behaviour.</summary>
    private static void SetVisible(Renderer r, bool visible)
    {
        if (!r) return;
#if UNITY_2021_2_OR_NEWER
        r.forceRenderingOff = !visible;
#else
        r.enabled = visible;
#endif
    }

    // ---------------------- Preview APIs (used by VelocityDragManager) ----------------------

    /// <summary>Starts or refreshes a lightweight continuous trajectory preview from a given state.</summary>
    public void QuickPreviewFromState(Vector3 startPos, Vector3 startVel, float bodyMass)
    {
        previewPos = startPos;
        previewVel = startVel;
        previewMass = Mathf.Max(1f, bodyMass);
        previewDirty = true;

        if (previewCo == null) previewCo = StartCoroutine(QuickPreviewLoop());
    }

    /// <summary>Clears the preview line and stops the preview worker if running.</summary>
    public void ClearPreview()
    {
        previewDirty = false;
        previewLine.Clear();
        if (previewCo != null) { StopCoroutine(previewCo); previewCo = null; }
    }

    private IEnumerator QuickPreviewLoop()
    {
        const float tick = 0.1f;
        while (true)
        {
            if (!previewDirty) { yield return new WaitForSecondsRealtime(tick); continue; }
            previewDirty = false;

            var svc = ctx.BodyService;
            if (svc == null || svc.CentralBody == null)
            {
                previewLine.Clear();
                yield return new WaitForSecondsRealtime(tick);
                continue;
            }

            // Only the central body
            var cb = svc.CentralBody;
            Vector3[] attractorPos = { cb.transform.position };
            float[] attractorMass = { (float)cb.mass };

            ctx.TrajectoryComputeController.CalculateTrajectoryGPU_Async(
                previewPos, previewVel, previewMass,
                attractorPos, attractorMass,
                dt: 2f, steps: 1500,
                points =>
                {
                    if (points == null || points.Length < 2) { previewLine.Clear(); return; }
                    var clipped = ClipTrajectorySphere(points); // sphere collision-only
                    previewLine.UpdateLine(clipped);
                });

            yield return new WaitForSecondsRealtime(tick);
        }
    }

    /// <summary>Runs a one-off longer preview, then leaves the line until updated again.</summary>
    public void QuickPreviewOnceLong(Vector3 startPos, Vector3 startVel, float bodyMass,
                                     int steps = 8000, float dt = 2f, bool singleOrbit = true)
    {
        if (previewCo != null) { StopCoroutine(previewCo); previewCo = null; }

        var svc = ctx?.BodyService;
        if (svc == null || svc.CentralBody == null)
        {
            Debug.LogWarning("[QuickPreview] No BodyService or CentralBody.");
            previewLine?.Clear();
            return;
        }

        var cb = svc.CentralBody;
        Vector3[] attractorPos = { cb.transform.position };
        float[] attractorMass = { (float)cb.mass };

        ctx.TrajectoryComputeController.CalculateTrajectoryGPU_Async(
            startPos, startVel, Mathf.Max(1f, bodyMass),
            attractorPos, attractorMass,
            dt, steps,
            points =>
            {
                if (points == null || points.Length < 2) { previewLine?.Clear(); return; }

                var clipped = ClipTrajectorySphere(points); // collision only
                previewLine.UpdateLine(clipped);

                // do not restart the loop here; next QuickPreviewFromState will
                previewDirty = false;
            });
    }

    /// <summary>Clears the pre-maneuver line.</summary>
    public void ClearPreManeuverLine()
    {
        preManeuverLine.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Single-orbit clipping helpers
    // ─────────────────────────────────────────────────────────────────────────────
    private Vector3[] ClipToSingleOrbit(Vector3[] points)
    {
        if (!clipToSingleOrbit || points == null || points.Length < 3 || ctx?.BodyService?.CentralBody == null)
            return points;

        Vector3 center = ctx.BodyService.CentralBody.transform.position;
        Vector3 r0 = points[0] - center;
        if (r0.sqrMagnitude < 1e-8f) return points;

        if (!TryComputeOrbitNormal(points, center, out Vector3 n))
            return points;

        float threshold = Mathf.PI * 2f - fullTurnEpsilon;
        float cumulative = 0f;

        var outPts = new List<Vector3>(points.Length) { points[0] };
        Vector3 prev = r0;

        for (int i = 1; i < points.Length; i++)
        {
            Vector3 cur = points[i] - center;
            float dTheta = SignedAngleDelta(prev, cur, n);

            if (Mathf.Abs(dTheta) < minStepAngleRad)
            {
                outPts.Add(points[i]);
                prev = cur;
                continue;
            }

            float nextTotal = cumulative + dTheta;

            if (Mathf.Abs(nextTotal) >= threshold)
            {
                // cut exactly at the threshold
                float target = Mathf.Sign(nextTotal) * threshold;
                float needed = target - cumulative;          // signed
                float frac = Mathf.Clamp01(needed / dTheta); // signed ratio in [0,1]

                // angle-aware cut (slerp direction, lerp radius)
                Vector3 ra = points[i - 1] - center;
                Vector3 rb = points[i] - center;

                Vector3 na = ra.normalized;
                Vector3 nb = rb.normalized;
                float ang = SignedAngleDelta(na, nb, n);
                Quaternion q = Quaternion.AngleAxis((ang * frac) * Mathf.Rad2Deg, n);
                Vector3 dirCut = q * na;
                float rLen = Mathf.Lerp(ra.magnitude, rb.magnitude, Mathf.Clamp01(frac));

                Vector3 cutPos = center + dirCut * rLen;
                outPts.Add(cutPos);

                // close loop so the line has no tiny gap
                outPts.Add(outPts[0]);

                return outPts.ToArray();
            }

            cumulative = nextTotal;
            outPts.Add(points[i]);
            prev = cur;
        }

        return outPts.ToArray();
    }

    private bool TryComputeOrbitNormal(Vector3[] pts, Vector3 center, out Vector3 n)
    {
        n = Vector3.zero;
        Vector3? rPrev = null;
        for (int i = 1; i < pts.Length; i++)
        {
            Vector3 a = rPrev ?? (pts[i - 1] - center);
            Vector3 b = pts[i] - center;
            Vector3 c = Vector3.Cross(a, b);
            float mag = c.magnitude;
            if (mag > 1e-6f)
            {
                n = c / mag;
                return true;
            }
            rPrev = b;
        }
        return false;
    }

    /// <summary>Signed angle from 'a' to 'b' about normal 'n', in radians. Range (-π, π].</summary>
    private float SignedAngleDelta(Vector3 a, Vector3 b, Vector3 n)
    {
        a.Normalize(); b.Normalize();
        float sin = Vector3.Dot(n, Vector3.Cross(a, b));
        float cos = Mathf.Clamp(Vector3.Dot(a, b), -1f, 1f);
        return Mathf.Atan2(sin, cos);
    }
}
