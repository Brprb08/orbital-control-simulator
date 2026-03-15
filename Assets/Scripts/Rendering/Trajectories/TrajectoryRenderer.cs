using System;
using System.Collections.Generic;
using UnityEngine;

public class TrajectoryRenderer : MonoBehaviour
{
    private const float UiIntervalSeconds = 0.5f;
    private const float NearCircularUnitsThreshold = 0.5f;
    private const float PredictionLodMaxPoints = 2500f;

    [Header("Prediction")]
    [Min(1)] public int predictionSteps = 5000;
    [Min(0.0001f)] public float predictionDeltaTime = 7f;
    public bool orbitIsDirty = true;

    [Header("Debounce")]
    [Tooltip("Coalesce rapid orbitIsDirty toggles to avoid churn.")]
    [SerializeField, Range(0, 5)] private int dirtyDebounceFrames = 2;
    private int dirtyDebounceCounter;

    [Header("Refs")]
    public ThrustController thrustController;
    public CameraMovement cameraMovement;
    public BodyRuntimeCoordinator bodyRuntimeCoordinator;
    public BodyService bodyService;
    public NBody trackedBody;

    private CameraController cameraController;
    // private UIManager ui;
    private TrajectoryUI ui;
    private Camera mainCamera;

    [Header("Lines")]
    [NonSerialized] public ProceduralLineRenderer predictionLine;
    [NonSerialized] public ProceduralLineRenderer originLine;
    [NonSerialized] public ProceduralLineRenderer apogeeLine;
    [NonSerialized] public ProceduralLineRenderer perigeeLine;
    [NonSerialized] public ProceduralLineRenderer preManeuverLine;
    [NonSerialized] public ProceduralLineRenderer previewLine;
    [NonSerialized] public ProceduralLineRenderer burnLine;

    [Header("Appearance")]
    public Color predictionColor = new Color32(0x29, 0x78, 0xFF, 255);
    public Color originColor = Color.white;
    public Color apogeeColor = new Color32(0xFF, 0xB3, 0x00, 255);
    public Color perigeeColor = new Color32(0x00, 0xBF, 0xA5, 255);
    public Color burnColor = new Color32(0xFF, 0x3B, 0x30, 255);

    [Tooltip("Hide lines when the camera is closer than this distance to the tracked body.")]
    public float lineDisableDistance = 20f;

    [Header("State")]
    private bool isThrusting;
    private bool savedOriginalOrbit;
    private bool isComputingPrediction;
    private bool fullPassRequested;
    private bool wasThrusting;

    private bool showPredictionUser = true;
    private bool showOriginUser = true;
    private bool showApogeePerigeeUser = true;

    [Header("Burn Trace State")]
    [SerializeField, Min(0.01f)] private float burnSampleInterval = 0.1f;
    [SerializeField, Min(0f)] private float burnMinDistance = 0.05f;
    [SerializeField, Min(128)] private int burnMaxPoints = 8192;

    [Header("Outputs")]
    public List<Vector3> latestPrediction = new();
    public float latestPredictionDeltaTime;
    public float latestPredictionStartTime;
    public NBody latestPredictionBody;

    [Header("Preview")]
    public NBody referenceOrbitBody;

    [Header("Single Orbit Clipping")]
    [SerializeField] private bool clipToSingleOrbit = true;
    [SerializeField, Range(0.001f, 0.5f)] private float fullTurnEpsilon = 0.001f;
    [SerializeField, Range(0f, 0.05f)] private float minStepAngleRad = 0.0015f;

    public event Action<NBody, NBody> TrackedBodyChanged;

    private SimContext ctx;
    private BurnTraceModule burnTrace;
    private TrajectoryPreviewModule previewModule;
    private TrajectoryLineSet lines;
    private TrajectoryCentralBodyCache centralBodyCache;

    private List<Vector3> preManeuverSnapshot;
    private float uiNextTick;
    private uint predictionGeneration;

    public void Initialize(SimContext simContext)
    {
        UnbindListeners();
        DisposeLineSet();

        ctx = simContext;
        bodyRuntimeCoordinator = ctx.BodyRuntimeCoordinator;
        cameraController = ctx.CameraController;
        cameraMovement = ctx.CameraMovement;
        thrustController = ctx.ThrustController;
        // ui = ctx.UIManager;
        ui = ctx.UIRoot != null ? ctx.UIRoot.TrajectoryUI : null;
        bodyService = ctx.BodyService;

        mainCamera = Camera.main;
        RefreshCentralBodyCache(force: true);

        lines = TrajectoryLineSet.Create(
            transform,
            gameObject.layer,
            predictionColor,
            originColor,
            apogeeColor,
            perigeeColor,
            "#CCCCCC",
            "#FFD166",
            burnColor
        );

        BindLineRefs();

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

        ValidateReferences();
        BindListeners();

        if (cameraController != null)
        {
            NBody current = cameraController.CurrentBody;
            if (current != null && current != trackedBody)
                SetTrackedBody(current);
        }
    }

    private void OnDestroy()
    {
        UnbindListeners();
        DisposeLineSet();
    }

    private void Update()
    {
        UpdateDirtyDebounce();

        if (trackedBody == null)
        {
            ClearTrackedBodyState();
            return;
        }

        RefreshCentralBodyCache(force: false);
        UpdateThrustState();

        burnTrace?.Update(
            thrusting: isThrusting,
            bodyTransform: trackedBody.transform,
            unscaledTime: Time.unscaledTime
        );

        if (trackedBody.cumulativeDeltaVUsed != 0f)
            ui?.UpdateDeltaV(trackedBody.cumulativeDeltaVUsed);
        else
            ui?.UpdateDeltaV(0f);

        bool cameraOnTrackedBody = IsCameraOnTrackedBody();

        if (fullPassRequested && !isThrusting && !isComputingPrediction && cameraOnTrackedBody)
            TryStartFinalLongPass(trackedBody);

        if (ShouldComputePrediction(trackedBody))
            TryStartRealtimePrediction(trackedBody);

        RefreshOrbitUIIfNeeded();
        ToggleLinesByDistance(cameraOnTrackedBody);
        DrawOriginLine();
    }

    public void SetTrackedBody(NBody body)
    {
        if (body == trackedBody && body != null)
        {
            RequestFullOrbitPass();
            MarkOrbitDirty();
            return;
        }

        NBody previousBody = trackedBody;

        InvalidatePredictionWork();
        preManeuverSnapshot = null;
        ClearAllLines();

        trackedBody = body;

        isThrusting = false;
        wasThrusting = false;
        savedOriginalOrbit = false;
        fullPassRequested = false;

        TrackedBodyChanged?.Invoke(previousBody, trackedBody);

        if (trackedBody == null)
        {
            orbitIsDirty = false;
            ui?.SetApogeePerigeePanelVisible(false);
            SetPreManeuverButtonVisible(false);
            return;
        }

        ui?.SetApogeePerigeePanelVisible(true);
        RequestFullOrbitPass();
        MarkOrbitDirty();
    }

    public void RequestFullOrbitPass()
    {
        fullPassRequested = true;
    }

    public void ClearPreManeuverOrbit()
    {
        preManeuverSnapshot = null;
        preManeuverLine?.Clear();

        if (referenceOrbitBody != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(referenceOrbitBody.gameObject);
            else
                Destroy(referenceOrbitBody.gameObject);
#else
            Destroy(referenceOrbitBody.gameObject);
#endif
            referenceOrbitBody = null;
        }
    }

    public void ClearBurnTrace()
    {
        burnTrace?.Reset();
    }

    public void ClearAllLines()
    {
        lines?.ClearAll();

        burnTrace?.Reset();
        previewModule?.Reset();

        latestPrediction.Clear();
        latestPredictionBody = null;
        latestPredictionStartTime = 0f;
        latestPredictionDeltaTime = 0f;
    }

    public void SetLineVisibility(bool showPrediction, bool showOrigin, bool showApogeePerigee)
    {
        showPredictionUser = showPrediction;
        showOriginUser = showOrigin;
        showApogeePerigeeUser = showApogeePerigee;

        ApplyEffectiveLineVisibility();
    }

    private void ApplyEffectiveLineVisibility()
    {
        if (lines == null || trackedBody == null)
            return;

        bool runtimeVisible = false;

        bool cameraOnTrackedBody = IsCameraOnTrackedBody();
        if (cameraOnTrackedBody && mainCamera != null)
        {
            float distance = Vector3.Distance(mainCamera.transform.position, trackedBody.transform.position);
            runtimeVisible = distance > lineDisableDistance;
        }

        predictionLine?.SetVisibility(runtimeVisible && showPredictionUser);
        originLine?.SetVisibility(runtimeVisible && showOriginUser);
        apogeeLine?.SetVisibility(runtimeVisible && showApogeePerigeeUser);
        perigeeLine?.SetVisibility(runtimeVisible && showApogeePerigeeUser);

        preManeuverLine?.SetVisibility(runtimeVisible);
        previewLine?.SetVisibility(true);
        burnLine?.SetVisibility(runtimeVisible);
    }

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

    public bool HasFreshPredictionFor(NBody body)
    {
        if (body == null) return false;
        if (isComputingPrediction) return false;
        if (orbitIsDirty) return false;
        if (latestPredictionBody != body) return false;
        if (latestPrediction == null || latestPrediction.Count < 2) return false;

        Vector3 first = latestPrediction[0];
        float distanceSquared = (first - body.transform.position).sqrMagnitude;
        return distanceSquared <= 25f;
    }

    private void ValidateReferences()
    {
        if (!bodyRuntimeCoordinator) Debug.LogError("[TrajectoryRenderer] Missing BodyRuntimeCoordinator");
        if (!cameraMovement) Debug.LogError("[TrajectoryRenderer] Missing CameraMovement");
        if (!thrustController) Debug.LogError("[TrajectoryRenderer] Missing ThrustController");
        if (ui == null) Debug.LogError("[TrajectoryRenderer] Missing TrajectoryUI");
        if (!cameraController) Debug.LogError("[TrajectoryRenderer] Missing CameraController");
    }

    private void BindListeners()
    {
        if (cameraController != null)
        {
            cameraController.OnTrackedBodyChanged -= HandleTrackedBodyChanged;
            cameraController.OnTrackedBodyChanged += HandleTrackedBodyChanged;
        }

        if (ui != null)
        {
            ui.ClearPreManeuverClicked -= OnClearPreManeuverClicked;
            ui.ClearPreManeuverClicked += OnClearPreManeuverClicked;
        }
    }

    private void UnbindListeners()
    {
        if (cameraController != null)
            cameraController.OnTrackedBodyChanged -= HandleTrackedBodyChanged;

        if (ui != null)
            ui.ClearPreManeuverClicked -= OnClearPreManeuverClicked;
    }

    private void DisposeLineSet()
    {
        if (lines == null)
            return;

        lines.Dispose();
        lines = null;

        predictionLine = null;
        originLine = null;
        apogeeLine = null;
        perigeeLine = null;
        preManeuverLine = null;
        previewLine = null;
        burnLine = null;
    }

    private void BindLineRefs()
    {
        predictionLine = lines?.Prediction;
        originLine = lines?.Origin;
        apogeeLine = lines?.Apogee;
        perigeeLine = lines?.Perigee;
        preManeuverLine = lines?.PreManeuver;
        previewLine = lines?.Preview;
        burnLine = lines?.Burn;
    }

    private void UpdateDirtyDebounce()
    {
        if (orbitIsDirty && dirtyDebounceCounter == 0)
            dirtyDebounceCounter = dirtyDebounceFrames;
        else if (dirtyDebounceCounter > 0)
            dirtyDebounceCounter--;
    }

    private void MarkOrbitDirty()
    {
        orbitIsDirty = true;
        dirtyDebounceCounter = dirtyDebounceFrames;
    }

    private void ClearTrackedBodyState()
    {
        InvalidatePredictionWork();
        ClearAllLines();
        ui?.SetApogeePerigeePanelVisible(false);
        SetPreManeuverButtonVisible(false);
    }

    private void RefreshCentralBodyCache(bool force)
    {
        NBody currentCentralBody = bodyService != null ? bodyService.CentralBody : null;

        if (!force && centralBodyCache != null && centralBodyCache.CentralBody == currentCentralBody)
            return;

        if (centralBodyCache == null)
            centralBodyCache = new TrajectoryCentralBodyCache(currentCentralBody);
        else
            centralBodyCache.Refresh(currentCentralBody);
    }

    private void UpdateThrustState()
    {
        if (!thrustController)
            return;

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

        if (wasThrusting && !isThrusting)
            fullPassRequested = true;

        wasThrusting = isThrusting;
    }

    private bool IsCameraOnTrackedBody()
    {
        return cameraMovement != null && cameraMovement.targetBody == trackedBody;
    }

    private bool ShouldComputePrediction(NBody body)
    {
        if (fullPassRequested && !isThrusting) return false;
        if (body == null) return false;
        if (!IsCameraOnTrackedBody()) return false;

        bool dirtyReady = orbitIsDirty && dirtyDebounceCounter == 0;
        return isThrusting || dirtyReady;
    }

    private void TryStartRealtimePrediction(NBody body)
    {
        if (isComputingPrediction)
            return;

        if (!TrajectoryPredictionPlanner.TryBuildRealtimeRequest(
                body,
                bodyService,
                bodyRuntimeCoordinator,
                predictionDeltaTime,
                isThrusting,
                Time.timeScale,
                out TrajectoryPredictionRequest request))
        {
            return;
        }

        predictionSteps = request.Steps;
        BeginPrediction(body, request);
    }

    private void TryStartFinalLongPass(NBody body)
    {
        if (isComputingPrediction)
            return;

        if (!TrajectoryPredictionPlanner.TryBuildFinalPassRequest(
                body,
                bodyService,
                bodyRuntimeCoordinator,
                predictionDeltaTime,
                out TrajectoryPredictionRequest request))
        {
            return;
        }

        predictionSteps = request.Steps;
        BeginPrediction(body, request);
        fullPassRequested = false;
    }

    private void BeginPrediction(NBody body, TrajectoryPredictionRequest request)
    {
        isComputingPrediction = true;
        uint requestGeneration = ++predictionGeneration;

        body.CalculatePredictedTrajectoryGPU_Async(
            steps: request.Steps,
            deltaTime: request.DeltaTime,
            onComplete: resultList =>
            {
                if (!this || !gameObject)
                    return;

                if (requestGeneration != predictionGeneration)
                    return;

                if (predictionLine == null)
                {
                    isComputingPrediction = false;
                    return;
                }

                ApplyPredictionResult(body, resultList, request);
            }
        );
    }

    private void InvalidatePredictionWork()
    {
        unchecked
        {
            predictionGeneration++;
        }

        isComputingPrediction = false;
    }

    private void ApplyPredictionResult(
        NBody body,
        List<Vector3> resultList,
        TrajectoryPredictionRequest request)
    {
        if (trackedBody != body)
        {
            isComputingPrediction = false;
            return;
        }

        latestPrediction = resultList ?? new List<Vector3>();
        latestPredictionBody = body;

        int lodFactor = Mathf.Max(1, request.Steps / (int)PredictionLodMaxPoints);

        latestPredictionStartTime = request.Epoch;
        latestPredictionDeltaTime = request.DeltaTime * lodFactor;

        Vector3[] points = latestPrediction.ToArray();
        points = ClipTrajectorySphere(points);

        if (clipToSingleOrbit && centralBodyCache != null)
            points = centralBodyCache.ClipToSingleOrbit(points, fullTurnEpsilon, minStepAngleRad);

        predictionLine.UpdateLine(points);

        orbitIsDirty = false;
        isComputingPrediction = false;
    }

    private void RefreshOrbitUIIfNeeded()
    {
        if (Time.unscaledTime < uiNextTick)
            return;

        OrbitalParameters orbitalParameters = OrbitalCalculations.TryParams(trackedBody, bodyService);

        if (orbitalParameters.isValid)
            ShowApogeePerigeeLines(orbitalParameters);
        else
            lines?.ClearApsides();

        uiNextTick = Time.unscaledTime + UiIntervalSeconds;
    }

    private void DrawOriginLine()
    {
        if (trackedBody == null || lines == null || centralBodyCache == null || !centralBodyCache.IsReady)
            return;

        lines.DrawOrigin(trackedBody.transform.position, centralBodyCache.CenterPosition);
    }

    private void ToggleLinesByDistance(bool cameraOnTrackedBody)
    {
        if (trackedBody == null || lines == null)
            return;

        ApplyEffectiveLineVisibility();
    }

    private void CapturePreManeuverFromLatest()
    {
        if (latestPrediction != null && latestPrediction.Count > 1)
        {
            preManeuverSnapshot = new List<Vector3>(latestPrediction);

            Vector3[] clipped = ClipTrajectorySphere(preManeuverSnapshot.ToArray());
            if (clipToSingleOrbit && centralBodyCache != null)
                clipped = centralBodyCache.ClipToSingleOrbit(clipped, fullTurnEpsilon, minStepAngleRad);

            preManeuverLine?.UpdateLine(clipped);
        }
        else
        {
            preManeuverSnapshot = null;
            preManeuverLine?.Clear();
        }

        SetPreManeuverButtonVisible(preManeuverSnapshot != null);
    }

    private void SetPreManeuverButtonVisible(bool visible)
    {
        ui?.SetRemovePreManeuverButtonVisible(visible);
    }

    private void OnClearPreManeuverClicked()
    {
        ClearPreManeuverOrbit();
        ClearBurnTrace();
        SetPreManeuverButtonVisible(false);
    }

    private Vector3[] ClipTrajectorySphere(Vector3[] points)
    {
        if (centralBodyCache == null)
            return points;

        return centralBodyCache.ClipTrajectorySphere(points);
    }

    private void ShowApogeePerigeeLines(OrbitalParameters orbitalParameters)
    {
        if (lines == null)
            return;

        bool nearCircular =
            Mathf.Abs(orbitalParameters.apogeeRadius - orbitalParameters.perigeeRadius) < NearCircularUnitsThreshold;

        Vector3 center = centralBodyCache != null && centralBodyCache.IsReady
            ? centralBodyCache.CenterPosition
            : Vector3.zero;

        lines.DrawApsides(
            orbitalParameters.apogeePosition,
            orbitalParameters.perigeePosition,
            center,
            !nearCircular
        );

        if (ui != null)
        {
            float apogeeKm = (orbitalParameters.apogeeRadius - TrajectoryCentralBodyCache.DefaultEarthRadiusUnity) * 10f;
            float perigeeKm = (orbitalParameters.perigeeRadius - TrajectoryCentralBodyCache.DefaultEarthRadiusUnity) * 10f;

            ui?.UpdateOrbitUI(
                apogeeKm,
                perigeeKm,
                orbitalParameters.semiMajorAxis,
                orbitalParameters.eccentricity,
                orbitalParameters.orbitalPeriod,
                orbitalParameters.inclination,
                orbitalParameters.RAAN,
                orbitalParameters.meanAnomaly,
                orbitalParameters.timeToPerigee,
                orbitalParameters.timeToApogee
            );
        }
    }

    private void HandleTrackedBodyChanged(NBody newBody)
    {
        SetTrackedBody(newBody);
    }
}