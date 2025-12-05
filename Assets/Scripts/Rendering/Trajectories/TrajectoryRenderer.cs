using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class TrajectoryRenderer : MonoBehaviour
{
    // ---------------------- Constants ----------------------
    private const int MAX_STEPS = 100000;
    private const float DAY = 24f * 60f * 60f;
    private const float MAX_HORIZON_SECONDS = 10f * DAY;
    private const float MIN_HORIZON_SECONDS = 20000f;
    private const float MAX_FAST_HORIZON = 2f * DAY;
    private const int FAST_MIN_STEPS = 2000;
    private const int FAST_MAX_STEPS = 12000;
    private const float UI_INTERVAL_SECONDS = 0.5f;
    private const float FAST_TIMESCALE_THRESHOLD = 5f; // timeScale above which we consider "fast mode"
    private const float EARTH_RADIUS_UNITY = 637.8f;

    // ---------------------- Prediction Settings ----------------------
    [Header("Prediction")]
    [Min(1)] public int predictionSteps = 5000;
    [Min(0.0001f)] public float predictionDeltaTime = 7f;
    public bool orbitIsDirty = true;

    [Header("Debounce")]
    [Tooltip("Coalesce rapid orbitIsDirty toggles to avoid churn.")]
    [SerializeField, Range(0, 5)] private int dirtyDebounceFrames = 2;
    private int _dirtyDebounceCounter;

    // ---------------------- References ----------------------
    [Header("Refs")]
    // public TextMeshProUGUI apogeeText;
    // public TextMeshProUGUI perigeeText;
    public ThrustController thrustController;
    public CameraMovement cameraMovement;
    public BodyRuntimeCoordinator bodyRuntimeCoordinator;
    public BodyService bodyService;
    public NBody trackedBody;

    private CameraController cameraController;
    private UIManager ui;
    private Camera mainCamera;

    // ---------------------- Line Renderers ----------------------
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
    public Color apogeeColor = new Color32(0xFF, 0xB3, 0x00, 255);     // orange
    public Color perigeeColor = new Color32(0x00, 0xBF, 0xA5, 255);    // teal
    public Color burnColor = new Color32(0xFF, 0x3B, 0x30, 255);       // red

    [Tooltip("Hide lines when the camera is closer than this distance to the tracked body.")]
    public float lineDisableDistance = 20f;

    // ---------------------- State ----------------------
    [Header("State")]
    private bool isThrusting;
    private bool savedOriginalOrbit;
    private bool isComputingPrediction;
    private bool fullPassRequested;
    private bool wasThrusting;

    // ---------------------- Burn Trace Settings ----------------------
    [Header("Burn Trace State")]
    [SerializeField, Min(0.01f)] private float burnSampleInterval = 0.1f; // seconds, unscaled
    [SerializeField, Min(0f)] private float burnMinDistance = 0.05f;      // world units (~0.5 km if 1u=10km)
    [SerializeField, Min(128)] private int burnMaxPoints = 8192;

    [Header("Orbit UI Smoothing")]
    [SerializeField] private float orbitUISnapThresholdKm = 50f;   // snap if change > this
    [SerializeField, Range(0f, 1f)] private float orbitUISmoothAlpha = 0.4f; // 0.4 => settles in ~1–2s

    private float _smoothedAp_km;
    private float _smoothedPe_km;
    private bool _haveSmoothedOrbitUI;

    // new: external module instead of local lists
    private BurnTraceModule burnTrace;

    private float uiNextTick;

    // ---------------------- Outputs ----------------------
    [Header("Outputs")]
    public List<Vector3> latestPrediction = new();
    public float latestPredictionDeltaTime;
    public float latestPredictionStartTime;
    private List<Vector3> preManeuverSnapshot;

    // ---------------------- Preview ----------------------
    [Header("Preview")]
    public ProceduralLineRenderer previewLine;

    private TrajectoryPreviewModule previewModule;

    public NBody referenceOrbitBody;
    public event Action<NBody, NBody> TrackedBodyChanged;
    private SimContext ctx;

    // ---------------------- Single Orbit Clipping ----------------------
    [Header("Single Orbit Clipping")]
    [Tooltip("Enable clipping of rendered trajectories to a single revolution.")]
    [SerializeField] private bool clipToSingleOrbit = true;
    [Tooltip("How close (radians) to 2π before we consider a 'full turn' reached.")]
    [SerializeField, Range(0.001f, 0.5f)] private float fullTurnEpsilon = 0.00f;
    [Tooltip("Ignore very small steps when summing angles (prevents jitter).")]
    [SerializeField, Range(0f, 0.05f)] private float minStepAngleRad = 0.0015f;

    // ---------------------- Cached Central Body ----------------------
    private NBody centralBody;
    private Transform centralBodyTransform;
    private float centralBodyRadiusWorld;
    private bool centralBodyCached;

    // ---------------------- Small reusable buffers ----------------------
    private readonly Vector3[] _originLinePoints = new Vector3[2];
    private readonly Vector3[] _apogeeLinePoints = new Vector3[2];
    private readonly Vector3[] _perigeeLinePoints = new Vector3[2];

    // =====================================================================
    // Initialization / Lifecycle
    // =====================================================================

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
        centralBody = bodyService != null ? bodyService.CentralBody : null;
        RefreshCentralBodyCache();

        // tidy hierarchy
        Transform lineRoot = new GameObject("TrajectoryLines").transform;
        lineRoot.SetParent(transform, false);
        lineRoot.gameObject.layer = gameObject.layer;

        predictionLine = CreateProceduralLineRenderer("PredictionLine", predictionColor, lineRoot);
        originLine = CreateProceduralLineRenderer("OriginLine", originColor, lineRoot);
        apogeeLine = CreateProceduralLineRenderer("ApogeeLine", apogeeColor, lineRoot);
        perigeeLine = CreateProceduralLineRenderer("PerigeeLine", perigeeColor, lineRoot);
        preManeuverLine = CreateProceduralLineRenderer("PreManeuverLine", "#CCCCCC", lineRoot);
        previewLine = CreateProceduralLineRenderer("PreviewLine", "#FFD166", lineRoot);
        burnLine = CreateProceduralLineRenderer("BurnLine", burnColor, lineRoot);

        // modules
        burnTrace = new BurnTraceModule(
            burnLine,
            burnSampleInterval,
            burnMinDistance,
            burnMaxPoints
        );

        previewModule = new TrajectoryPreviewModule(
            owner: this,
            previewLine: previewLine,
            ctx: ctx,
            clipper: ClipTrajectorySphere
        );

        if (!bodyRuntimeCoordinator) Debug.LogError("[TrajectoryRenderer] Missing BodyRuntimeCoordinator");
        if (!cameraMovement) Debug.LogError("[TrajectoryRenderer] Missing CameraMovement");
        if (!thrustController) Debug.LogError("[TrajectoryRenderer] Missing ThrustController");
        if (!ui) Debug.LogError("[TrajectoryRenderer] Missing UIManager");

        if (!cameraController)
        {
            Debug.LogError("[TrajectoryRenderer] Missing CameraController");
            return;
        }

        cameraController.OnTrackedBodyChanged += HandleTrackedBodyChanged;

        var current = cameraController.CurrentBody;
        if (current != null && current != trackedBody)
            SetTrackedBody(current);

        if (ui != null && ui.removePreManeuverLineButton != null)
        {
            ui.removePreManeuverLineButton.onClick.RemoveListener(OnClearPreManeuverClicked); // safety
            ui.removePreManeuverLineButton.onClick.AddListener(OnClearPreManeuverClicked);
        }
    }

    private void OnDestroy()
    {
        if (cameraController != null)
            cameraController.OnTrackedBodyChanged -= HandleTrackedBodyChanged;

        if (ui != null && ui.removePreManeuverLineButton != null)
            ui.removePreManeuverLineButton.onClick.RemoveListener(OnClearPreManeuverClicked);
    }

    // =====================================================================
    // Main Update Loop
    // =====================================================================

    private void Update()
    {
        // debounce dirtiness
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
            burnLine?.Clear();
            ui?.ShowApogeePerigeePanel(false);

            burnTrace?.Reset();
            previewModule?.Reset();
            return;
        }

        // thrust state + pre-maneuver snapshot
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

        // detect thrust stop → long pass
        if (wasThrusting && !isThrusting)
            fullPassRequested = true;
        wasThrusting = isThrusting;

        // burn trace module
        burnTrace?.Update(
            thrusting: isThrusting,
            bodyTransform: trackedBody.transform,
            unscaledTime: Time.unscaledTime
        );

        // Δv UI
        if (trackedBody.cumulativeDeltaVUsed != 0f)
            ui?.UpdateDeltaV(trackedBody.cumulativeDeltaVUsed);

        // when idle on this body, do long horizon pass
        bool cameraOnTrackedBody =
            cameraMovement != null &&
            cameraMovement.targetBody == trackedBody;

        if (fullPassRequested && !isThrusting && !isComputingPrediction && cameraOnTrackedBody)
        {
            ComputeFinalLongPass(trackedBody);
        }

        if (ShouldComputePrediction(trackedBody))
            KickOrRefreshPrediction(trackedBody);

        // apsis lines + orbit UI at lower rate
        if (Time.unscaledTime >= uiNextTick)
        {
            var p = OrbitalCalculations.TryParams(trackedBody, bodyService);
            if (p.isValid) ShowApogeePerigeeLines(p);
            uiNextTick = Time.unscaledTime + UI_INTERVAL_SECONDS;
        }

        ToggleLinesByDistance();
        DrawOriginLine();
    }

    // =====================================================================
    // Prediction / Orbit Management
    // =====================================================================

    private bool ShouldComputePrediction(NBody body)
    {
        if (fullPassRequested && !isThrusting) return false;
        if (body == null) return false;
        if (cameraMovement == null || cameraMovement.targetBody != body) return false;

        bool dirtyReady = orbitIsDirty && (_dirtyDebounceCounter == 0);
        return isThrusting || dirtyReady;
    }

    /// <summary>
    /// Sets the tracked body and resets prediction + line state.
    /// Re-tracking the same body (e.g. Free → Track) will request a fresh
    /// orbit pass without nuking all state.
    /// </summary>
    public void SetTrackedBody(NBody body)
    {
        // Same-body re-track (ReturnToTracking)
        if (body == trackedBody && body != null)
        {
            RequestFullOrbitPass();
            orbitIsDirty = true;
            _dirtyDebounceCounter = dirtyDebounceFrames;
            return;
        }

        preManeuverSnapshot = null;
        ClearAllLines();

        var old = trackedBody;
        trackedBody = body;

        ResetOrbitUISmoothing();

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
        _dirtyDebounceCounter = dirtyDebounceFrames;
    }

    public void RequestFullOrbitPass() => fullPassRequested = true;

    private void KickOrRefreshPrediction(NBody body)
    {
        if (isComputingPrediction) return;

        var p = OrbitalCalculations.TryParams(body, bodyService);
        if (!p.isValid) return;

        bool fast = isThrusting || Time.timeScale > FAST_TIMESCALE_THRESHOLD;
        float horizonSeconds = ComputeHorizonSeconds(body, fast, p);

        float effectiveDt = predictionDeltaTime;
        int stepsNeeded = Mathf.CeilToInt(horizonSeconds / effectiveDt);

        if (fast)
        {
            stepsNeeded = Mathf.Clamp(stepsNeeded, FAST_MIN_STEPS, FAST_MAX_STEPS);
            effectiveDt = Mathf.Max(0.0001f, horizonSeconds / stepsNeeded);
        }
        else
        {
            stepsNeeded = Mathf.Clamp(stepsNeeded, 500, MAX_STEPS);
        }

        predictionSteps = stepsNeeded;
        isComputingPrediction = true;

        body.CalculatePredictedTrajectoryGPU_Async(
            steps: predictionSteps,
            deltaTime: effectiveDt,
            onComplete: resultList =>
            {
                if (!this || !gameObject) return;
                if (trackedBody != body) { isComputingPrediction = false; return; }
                if (predictionLine == null) { isComputingPrediction = false; return; }

                latestPrediction = resultList ?? new List<Vector3>();
                latestPredictionStartTime = bodyRuntimeCoordinator ? bodyRuntimeCoordinator.simulationTime : 0f;
                latestPredictionDeltaTime = effectiveDt;

                var pts = latestPrediction.ToArray();
                pts = ClipTrajectorySphere(pts);
                if (clipToSingleOrbit) pts = ClipToSingleOrbit(pts);
                predictionLine.UpdateLine(pts);

                orbitIsDirty = false;
                isComputingPrediction = false;
            }
        );
    }

    private void ComputeFinalLongPass(NBody body)
    {
        var p = OrbitalCalculations.TryParams(body, bodyService);
        if (!p.isValid) return;

        float horizonSeconds = ComputeHorizonSeconds(body, fast: false, p);

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
                pts = ClipTrajectorySphere(pts);
                if (clipToSingleOrbit) pts = ClipToSingleOrbit(pts);
                predictionLine.UpdateLine(pts);

                orbitIsDirty = false;
                isComputingPrediction = false;
            }
        );

        fullPassRequested = false;
    }

    private float ComputeHorizonSeconds(NBody body, bool fast, OrbitalParameters p)
    {
        if (!p.isValid)
            return Mathf.Clamp(30_000f, MIN_HORIZON_SECONDS, MAX_HORIZON_SECONDS);

        float mu = PhysicsConstants.G * body.state.centralBodyMass;
        bool bound = p.eccentricity < 1f && p.semiMajorAxis > 0f;

        float T = bound
            ? 2f * Mathf.PI * Mathf.Sqrt(Mathf.Pow(p.semiMajorAxis, 3) / mu)
            : 60000f; // fallback for hyperbolic / weird cases

        if (fast)
        {
            float h = Mathf.Clamp(T * 1.2f, 10_000f, MAX_FAST_HORIZON);
            return Mathf.Min(h, MAX_HORIZON_SECONDS);
        }

        float hFinal = Mathf.Clamp(T * 1.25f, MIN_HORIZON_SECONDS, MAX_HORIZON_SECONDS);
        return hFinal;
    }

    // =====================================================================
    // Line Visibility / Origin
    // =====================================================================

    private void DrawOriginLine()
    {
        if (originLine == null || trackedBody == null || !centralBodyCached) return;

        _originLinePoints[0] = trackedBody.transform.position;
        _originLinePoints[1] = centralBodyTransform.position;

        originLine.UpdateLine(_originLinePoints);
    }

    private void ToggleLinesByDistance()
    {
        if (trackedBody == null) return;

        bool cameraOnTrackedBody =
            cameraMovement != null &&
            cameraMovement.targetBody == trackedBody;

        if (!cameraOnTrackedBody)
        {
            predictionLine?.SetVisibility(false);
            originLine?.SetVisibility(false);
            apogeeLine?.SetVisibility(false);
            perigeeLine?.SetVisibility(false);
            preManeuverLine?.SetVisibility(false);

            // DO NOT TOUCH PREVIEW LINE HERE

            burnLine?.SetVisibility(false);
            return;
        }

        if (mainCamera == null) return;

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

    // =====================================================================
    // Line Creation
    // =====================================================================

    private ProceduralLineRenderer CreateProceduralLineRenderer(string name, Color color, Transform parent)
    {
        var go = new GameObject(name)
        {
            layer = gameObject.layer
        };

        go.transform.SetParent(parent, false);

        var lr = go.AddComponent<ProceduralLineRenderer>();
        string hex = ColorUtility.ToHtmlStringRGB(color);
        lr.SetLineColor("#" + hex);
        lr.SetLineWidth(0.1f);

        return lr;
    }

    private ProceduralLineRenderer CreateProceduralLineRenderer(string name, string hexColor, Transform parent)
    {
        if (!ColorUtility.TryParseHtmlString(hexColor, out var col))
            col = Color.white;

        return CreateProceduralLineRenderer(name, col, parent);
    }

    // =====================================================================
    // Pre-Maneuver Orbit
    // =====================================================================

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

        bool hasPreOrbit = preManeuverSnapshot != null;
        if (ui != null && ui.removePreManeuverLineButton != null)
            ui.removePreManeuverLineButton.gameObject.SetActive(hasPreOrbit);
    }

    private void OnClearPreManeuverClicked()
    {
        ClearPreManeuverOrbit();
        ClearBurnTrace();

        // optional: hide the button after use
        if (ui != null && ui.removePreManeuverLineButton != null)
            ui.removePreManeuverLineButton.gameObject.SetActive(false);
    }

    public void ClearPreManeuverOrbit()
    {
        preManeuverLine?.Clear();

        if (referenceOrbitBody != null)
        {
            Destroy(referenceOrbitBody.gameObject);
            referenceOrbitBody = null;
        }
    }

    public void ClearBurnTrace()
    {
        burnTrace?.Reset();
    }

    // =====================================================================
    // Central Body / Clipping
    // =====================================================================

    private void RefreshCentralBodyCache()
    {
        if (centralBody == null)
        {
            centralBodyCached = false;
            return;
        }

        centralBodyTransform = centralBody.transform;
        centralBodyRadiusWorld = GetCentralBodyRadiusWorld(centralBody);
        centralBodyCached = true;
    }

    private Vector3[] ClipTrajectorySphere(Vector3[] points)
    {
        if (points == null || points.Length < 2) return points;
        if (!centralBodyCached) return points;

        Vector3 center = centralBodyTransform.position;
        float radius = centralBodyRadiusWorld;
        float r2 = radius * radius;

        var clipped = new List<Vector3>(points.Length) { points[0] };

        for (int i = 1; i < points.Length; i++)
        {
            Vector3 a = points[i - 1];
            Vector3 b = points[i];
            Vector3 d = b - a;

            Vector3 m = a - center;
            float A = Vector3.Dot(d, d);
            float B = 2f * Vector3.Dot(m, d);
            float C = Vector3.Dot(m, m) - r2;

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
            return clipped.ToArray();
        }

        return clipped.ToArray();
    }

    private float GetCentralBodyRadiusWorld(NBody central)
    {
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
        catch
        {
            // ignore reflection issues
        }

        // Fallback for Earth-scale worlds at 1u=10km
        return EARTH_RADIUS_UNITY;
    }

    // =====================================================================
    // UI: Apogee / Perigee
    // =====================================================================

    private void ShowApogeePerigeeLines(OrbitalParameters op)
    {
        if (!apogeeLine || !perigeeLine) return;

        var apo = op.apogeePosition;
        var per = op.perigeePosition;

        float circularUnitsThreshold = 0.5f; // ~1 km / 10 km/u
        bool nearCircular = Mathf.Abs(apo.magnitude - per.magnitude) < circularUnitsThreshold;

        if (!nearCircular)
        {
            _apogeeLinePoints[0] = apo;
            _apogeeLinePoints[1] = Vector3.zero;
            _perigeeLinePoints[0] = per;
            _perigeeLinePoints[1] = Vector3.zero;

            apogeeLine.UpdateLine(_apogeeLinePoints);
            perigeeLine.UpdateLine(_perigeeLinePoints);
        }

        if (ui != null)
        {
            // Raw values from geometry
            double ap_km_raw = (apo.magnitude - EARTH_RADIUS_UNITY) * 10.0;
            double pe_km_raw = (per.magnitude - EARTH_RADIUS_UNITY) * 10.0;

            float ap_km = (float)ap_km_raw;
            float pe_km = (float)pe_km_raw;

            // --- Fast snap on big changes / first time ---
            if (!_haveSmoothedOrbitUI ||
                Mathf.Abs(ap_km - _smoothedAp_km) > orbitUISnapThresholdKm ||
                Mathf.Abs(pe_km - _smoothedPe_km) > orbitUISnapThresholdKm)
            {
                _smoothedAp_km = ap_km;
                _smoothedPe_km = pe_km;
                _haveSmoothedOrbitUI = true;
            }
            else
            {
                // --- Only smooth small jitter ---
                float a = orbitUISmoothAlpha;
                _smoothedAp_km = Mathf.Lerp(_smoothedAp_km, ap_km, a);
                _smoothedPe_km = Mathf.Lerp(_smoothedPe_km, pe_km, a);
            }

            ui.UpdateOrbitUI(
                _smoothedAp_km, _smoothedPe_km,
                op.semiMajorAxis, op.eccentricity, op.orbitalPeriod,
                op.inclination, op.RAAN, op.meanAnomaly, op.timeToPerigee, op.timeToApogee
            );
        }
    }

    private void ResetOrbitUISmoothing()
    {
        _haveSmoothedOrbitUI = false;
        _smoothedAp_km = 0f;
        _smoothedPe_km = 0f;
    }

    // =====================================================================
    // Bulk Control
    // =====================================================================

    public void ClearAllLines()
    {
        predictionLine?.Clear();
        originLine?.Clear();
        apogeeLine?.Clear();
        perigeeLine?.Clear();
        preManeuverLine?.Clear();
        previewLine?.Clear();
        burnLine?.Clear();

        burnTrace?.Reset();
        previewModule?.Reset();
        // trackedBody left; SetTrackedBody controls it
    }

    public void SetLineVisibility(bool showPrediction, bool showOrigin, bool showApogeePerigee)
    {
        SetVisible(predictionLine ? predictionLine.GetComponent<Renderer>() : null, showPrediction);
        SetVisible(originLine ? originLine.GetComponent<Renderer>() : null, showOrigin);
        SetVisible(apogeeLine ? apogeeLine.GetComponent<Renderer>() : null, showApogeePerigee);
        SetVisible(perigeeLine ? perigeeLine.GetComponent<Renderer>() : null, showApogeePerigee);
    }

    private static void SetVisible(Renderer r, bool visible)
    {
        if (!r) return;
#if UNITY_2021_2_OR_NEWER
        r.forceRenderingOff = !visible;
#else
        r.enabled = visible;
#endif
    }

    // =====================================================================
    // Preview API (VelocityDragManager) – forwards to module
    // =====================================================================

    public void QuickPreviewFromState(Vector3 startPos, Vector3 startVel, float bodyMass)
    {
        previewModule?.QuickPreviewFromState(startPos, startVel, bodyMass);
    }

    public void ClearPreview()
    {
        previewModule?.ClearPreview();
    }

    public void QuickPreviewOnceLong(
        Vector3 startPos,
        Vector3 startVel,
        float bodyMass,
        int steps = 8000,
        float dt = 2f,
        bool singleOrbit = true)
    {
        previewModule?.QuickPreviewOnceLong(
            startPos,
            startVel,
            bodyMass,
            steps,
            dt,
            singleOrbit && clipToSingleOrbit
        );
    }

    // =====================================================================
    // Orbit Clipping Helpers
    // =====================================================================

    private Vector3[] ClipToSingleOrbit(Vector3[] points)
    {
        if (!clipToSingleOrbit || points == null || points.Length < 3 || !centralBodyCached)
            return points;

        Vector3 center = centralBodyTransform.position;
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
                float target = Mathf.Sign(nextTotal) * threshold;
                float needed = target - cumulative;
                float frac = Mathf.Clamp01(needed / dTheta);

                Vector3 ra = points[i - 1] - center;
                Vector3 rb = points[i] - center;

                Vector3 na = ra.normalized;
                Vector3 nb = rb.normalized;
                float ang = SignedAngleDelta(na, nb, n);
                Quaternion q = Quaternion.AngleAxis((ang * frac) * Mathf.Rad2Deg, n);
                Vector3 dir = q * na;
                float rLen = Mathf.Lerp(ra.magnitude, rb.magnitude, Mathf.Clamp01(frac));

                Vector3 cutPos = center + dir * rLen;
                outPts.Add(cutPos);
                outPts.Add(outPts[0]); // close loop

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

    private float SignedAngleDelta(Vector3 a, Vector3 b, Vector3 n)
    {
        a.Normalize();
        b.Normalize();
        float sin = Vector3.Dot(n, Vector3.Cross(a, b));
        float cos = Mathf.Clamp(Vector3.Dot(a, b), -1f, 1f);
        return Mathf.Atan2(sin, cos);
    }

    // =====================================================================
    // Camera Body Change
    // =====================================================================

    private void HandleTrackedBodyChanged(NBody newBody)
    {
        SetTrackedBody(newBody);
    }
}