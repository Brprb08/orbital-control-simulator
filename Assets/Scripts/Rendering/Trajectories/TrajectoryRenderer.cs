using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orchestrates tracked-body trajectory rendering. Line ownership, preview
/// rendering, burn traces, and prediction worker state live in helper classes;
/// this class decides when those helpers should refresh.
/// </summary>
public class TrajectoryRenderer : MonoBehaviour
{
    private const float UiIntervalSeconds = 0.5f;
    private const float NearCircularUnitsThreshold = 0.5f;

    [Header("Prediction")]
    [Min(1)] public int predictionSteps = 5000;
    [Min(0.0001f)] public float predictionDeltaTime = 7f;
    public bool orbitIsDirty = true;

    [Header("Debounce")]
    [Tooltip("Coalesce rapid orbitIsDirty toggles to avoid churn.")]
    [SerializeField, Range(0, 5)] private int dirtyDebounceFrames = 2;
    private int dirtyDebounceCounter;

    [Header("Continuous Refresh")]
    [SerializeField] private bool enableContinuousRefresh = true;
    [SerializeField, Min(0.001f)] private float continuousPositionDriftThreshold = 0.1f;
    [SerializeField, Min(0.0001f)] private float continuousVelocityDriftThreshold = 0.01f;
    [SerializeField, Min(0.01f)] private float minimumContinuousRefreshInterval = 0.05f;

    [Header("Continuous Quality")]
    [SerializeField, Min(32)] private int continuousCoarseMaxOutputPoints = 384;
    [SerializeField, Min(64)] private int continuousHighQualityMaxOutputPoints = 1600;
    [SerializeField, Min(0.1f)] private float continuousHighQualityInterval = 3f;

    [Header("Long Drag Transfer Refresh")]
    [SerializeField] private TrajectoryDragRefreshPolicy dragRefreshPolicy = new();

    [Header("Refs")]
    public ThrustController thrustController;
    public CameraMovement cameraMovement;
    public BodyRuntimeCoordinator bodyRuntimeCoordinator;
    public BodyService bodyService;
    public NBody trackedBody;

    private CameraController cameraController;
    private TrajectoryUI ui;
    private Camera mainCamera;

    [Header("Lines")]
    [NonSerialized] public ProceduralLineRenderer predictionLine;
    [NonSerialized] public ProceduralLineRenderer originLine;
    [NonSerialized] public ProceduralLineRenderer apogeeLine;
    [NonSerialized] public ProceduralLineRenderer perigeeLine;
    [NonSerialized] public ProceduralLineRenderer preManeuverLine;
    [NonSerialized] public ProceduralLineRenderer previewLine;
    [NonSerialized] public ProceduralLineRenderer previewApogeeLine;
    [NonSerialized] public ProceduralLineRenderer previewPerigeeLine;
    [NonSerialized] public ProceduralLineRenderer plannedManeuverLine;
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
    private readonly TrajectoryPredictionState predictionState = new();
    private readonly TrajectoryPredictionRunner predictionRunner = new();

    private List<Vector3> preManeuverSnapshot;
    private float uiNextTick;
    private bool forceFastSwitchPreview;
    private bool trackedPredictionOwnershipActive;

    public void Initialize(SimContext simContext)
    {
        UnbindListeners();
        DisposeLineSet();
        if (dragRefreshPolicy == null)
            dragRefreshPolicy = new TrajectoryDragRefreshPolicy();

        ctx = simContext;
        bodyRuntimeCoordinator = ctx.BodyRuntimeCoordinator;
        cameraController = ctx.CameraController;
        cameraMovement = ctx.CameraMovement;
        thrustController = ctx.ThrustController;
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
            previewApogeeLine: previewApogeeLine,
            previewPerigeeLine: previewPerigeeLine,
            ctx: ctx,
            clipper: ClipTrajectorySphere,
            singleOrbitClipper: ClipPreviewToSingleOrbit
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
        previewApogeeLine = null;
        previewPerigeeLine = null;
        plannedManeuverLine = null;
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
        previewApogeeLine = lines?.PreviewApogee;
        previewPerigeeLine = lines?.PreviewPerigee;
        plannedManeuverLine = lines?.PlannedManeuver;
        burnLine = lines?.Burn;
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
        ClearPreManeuverSnapshot();
        ClearAllLines();

        trackedBody = body;

        ResetTrackedBodyRuntimeState();

        TrackedBodyChanged?.Invoke(previousBody, trackedBody);

        if (trackedBody == null)
        {
            orbitIsDirty = false;
            forceFastSwitchPreview = false;
            HideTrackedOrbitUi();
            return;
        }

        forceFastSwitchPreview = true;
        ui?.SetApogeePerigeePanelVisible(true);
        RequestFullOrbitPass();
        MarkOrbitDirty();
    }

    public void RequestFullOrbitPass()
    {
        fullPassRequested = true;
    }

    public void RequestPredictionRefresh()
    {
        MarkOrbitDirty();
    }

    public void ClearPreManeuverOrbit()
    {
        ClearPreManeuverSnapshot();
        ClearBurnTrace();

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
        ResetRenderedTrajectoryState();
    }

    public void SetLineVisibility(bool showPrediction, bool showOrigin, bool showApogeePerigee)
    {
        showPredictionUser = showPrediction;
        showOriginUser = showOrigin;
        showApogeePerigeeUser = showApogeePerigee;

        ApplyEffectiveLineVisibility();
    }

    public void QuickPreviewFromState(Vector3 startPos, Vector3 startVel, float bodyMass)
    {
        EnsurePreviewLinesVisible();
        previewModule?.QuickPreviewFromState(startPos, startVel, bodyMass);
    }

    public void ClearPreview()
    {
        previewModule?.ClearPreview();
    }

    public void CommitPreviewAsPlannedManeuver()
    {
        lines?.CopyPreviewToPlannedManeuver();
    }

    public void ClearPlannedManeuver()
    {
        plannedManeuverLine?.Clear();
    }

    public void QuickPreviewOnceLong(
        Vector3 startPos,
        Vector3 startVel,
        float bodyMass,
        int steps = 8000,
        float dt = 2f,
        bool singleOrbit = true,
        bool smoothClosedLoop = false)
    {
        EnsurePreviewLinesVisible();
        previewModule?.QuickPreviewOnceLong(
            startPos,
            startVel,
            bodyMass,
            steps,
            dt,
            singleOrbit && clipToSingleOrbit,
            smoothClosedLoop
        );
    }

    private void EnsurePreviewLinesVisible()
    {
        if (lines == null)
            return;

        bool visible = IsManeuverOrbitRuntimeVisible();
        previewLine?.SetVisibility(visible);
        previewApogeeLine?.SetVisibility(visible);
        previewPerigeeLine?.SetVisibility(visible);
    }

    public bool HasFreshPredictionFor(NBody body)
    {
        if (body == null) return false;
        if (predictionRunner.IsComputing) return false;
        if (orbitIsDirty) return false;
        if (latestPredictionBody != body) return false;
        if (latestPrediction == null || latestPrediction.Count < 2) return false;

        Vector3 first = latestPrediction[0];
        float distanceSquared = (first - body.transform.position).sqrMagnitude;
        return distanceSquared <= 25f;
    }

    private void Update()
    {
        UpdateDirtyDebounce();
        PumpPredictionResults();

        if (trackedBody == null)
        {
            ClearWhenNoTrackedBody();
            return;
        }

        UpdateTrackedBodyFrameState();
        UpdateBurnTrace();
        UpdateDeltaVUi();

        bool cameraOnTrackedBody = IsCameraOnTrackedBody();
        UpdateTrackedPredictionOwnership(cameraOnTrackedBody);
        TryScheduleTrackedPrediction(cameraOnTrackedBody);

        RefreshTrackedVisuals();
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

    private void ResetTrackedBodyRuntimeState()
    {
        isThrusting = false;
        wasThrusting = false;
        savedOriginalOrbit = false;
        fullPassRequested = false;
        trackedPredictionOwnershipActive = false;
        dragRefreshPolicy.Reset();
    }

    private void ResetRenderedTrajectoryState()
    {
        burnTrace?.Reset();
        previewModule?.Reset();
        ResetContinuousRefreshState();
        ClearLatestPrediction();
        dragRefreshPolicy.Reset();
    }

    private void ClearLatestPrediction()
    {
        latestPrediction.Clear();
        latestPredictionBody = null;
        latestPredictionStartTime = 0f;
        latestPredictionDeltaTime = 0f;
    }

    private void HideTrackedOrbitUi()
    {
        ui?.SetApogeePerigeePanelVisible(false);
        SetPreManeuverButtonVisible(false);
    }

    private void ClearWhenNoTrackedBody()
    {
        InvalidatePredictionWork();

        if (IsManualVelocityPlacementActive())
        {
            HideTrackedOrbitUi();
            return;
        }

        ClearAllLines();
        HideTrackedOrbitUi();
    }

    private void TryScheduleTrackedPrediction(bool cameraOnTrackedBody)
    {
        bool dirtyReady = IsDirtyPredictionReady();
        if (cameraOnTrackedBody && (isThrusting || dirtyReady) && !predictionRunner.IsComputing)
            TryStartRealtimePrediction(trackedBody);
        else if (cameraOnTrackedBody && ShouldContinuouslyRefreshPrediction(trackedBody))
            TryStartContinuousPrediction(trackedBody);

        if (fullPassRequested &&
            !orbitIsDirty &&
            !isThrusting &&
            !predictionRunner.IsComputing &&
            cameraOnTrackedBody &&
            !dragRefreshPolicy.LongDragPassageRefreshActive)
        {
            TryStartFinalLongPass(trackedBody);
        }
    }

    private void RefreshTrackedVisuals()
    {
        RefreshOrbitUIIfNeeded();
        ApplyEffectiveLineVisibility();
        DrawOriginLine();
    }

    private void ApplyEffectiveLineVisibility()
    {
        if (lines == null || trackedBody == null)
            return;

        lines.ApplyEffectiveVisibility(
            IsTrackedOrbitRuntimeVisible(),
            IsManeuverOrbitRuntimeVisible(),
            showPredictionUser,
            showOriginUser,
            showApogeePerigeeUser
        );
    }

    private bool IsTrackedOrbitRuntimeVisible()
    {
        if (!IsCameraOnTrackedBody() || mainCamera == null || trackedBody == null)
            return false;

        float distance = Vector3.Distance(mainCamera.transform.position, trackedBody.transform.position);
        return distance > lineDisableDistance;
    }

    private bool IsManeuverOrbitRuntimeVisible()
    {
        if (IsManualVelocityPlacementActive())
        {
            GameObject pendingBody = GetPendingManualPlacementBody();
            if (mainCamera == null || pendingBody == null)
                return true;

            float pendingDistance = Vector3.Distance(mainCamera.transform.position, pendingBody.transform.position);
            return pendingDistance > lineDisableDistance;
        }

        if (!IsCameraOnTrackedBody() || mainCamera == null || trackedBody == null)
            return true;

        float distance = Vector3.Distance(mainCamera.transform.position, trackedBody.transform.position);
        return distance > lineDisableDistance;
    }

    private void UpdateTrackedBodyFrameState()
    {
        RefreshCentralBodyCache(force: false);
        UpdateThrustState();
        UpdateDragRefreshPolicy();
    }

    private void UpdateBurnTrace()
    {
        burnTrace?.Update(
            thrusting: isThrusting,
            bodyTransform: trackedBody.transform,
            unscaledTime: Time.unscaledTime
        );
    }

    private void UpdateDeltaVUi()
    {
        float deltaV = trackedBody != null ? trackedBody.cumulativeDeltaVUsed : 0f;
        ui?.UpdateDeltaV(deltaV);
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
            HandleThrustStopped();

        wasThrusting = isThrusting;
    }

    private void HandleThrustStopped()
    {
        InvalidatePredictionWork();
        ResetContinuousRefreshState();
        RequestFullOrbitPass();
        MarkOrbitDirty();
    }

    private void UpdateDragRefreshPolicy()
    {
        TrajectoryDragRefreshTransition transition = dragRefreshPolicy.Update(
            trackedBody,
            bodyService,
            isThrusting
        );

        if (transition == TrajectoryDragRefreshTransition.None)
            return;

        if (transition == TrajectoryDragRefreshTransition.EnteredDragPassage)
        {
            forceFastSwitchPreview = false;
            fullPassRequested = false;
            MarkOrbitDirty();
            return;
        }

        ResetContinuousRefreshState();
        RequestFullOrbitPass();
        MarkOrbitDirty();
    }

    private bool IsCameraOnTrackedBody()
    {
        return trackedBody != null &&
               cameraController != null &&
               cameraController.CurrentBody == trackedBody;
    }

    private bool IsManualVelocityPlacementActive()
    {
        return ctx?.PendingVelocityPlacementController != null &&
               ctx.PendingVelocityPlacementController.IsManualVelocityPlacementActive;
    }

    private GameObject GetPendingManualPlacementBody()
    {
        return ctx?.PendingVelocityPlacementController != null
            ? ctx.PendingVelocityPlacementController.planet
            : null;
    }

    private void UpdateTrackedPredictionOwnership(bool cameraOnTrackedBody)
    {
        if (trackedBody == null)
        {
            trackedPredictionOwnershipActive = false;
            return;
        }

        if (trackedPredictionOwnershipActive == cameraOnTrackedBody)
            return;

        trackedPredictionOwnershipActive = cameraOnTrackedBody;

        if (!cameraOnTrackedBody)
        {
            ExitTrackedPredictionOwnership();
            return;
        }

        EnterTrackedPredictionOwnership();
    }

    private void ExitTrackedPredictionOwnership()
    {
        // Trajectory rendering is owned by the active tracked-camera view.
        // If we leave that view, discard in-flight tracked predictions and
        // refresh state so stale results do not apply later.
        InvalidatePredictionWork();
        ResetContinuousRefreshState();
        ClearPreManeuverOrbit();
        SetPreManeuverButtonVisible(false);
    }

    private void EnterTrackedPredictionOwnership()
    {
        forceFastSwitchPreview = true;
        RequestFullOrbitPass();
        MarkOrbitDirty();
    }

    private void TryStartRealtimePrediction(NBody body)
    {
        if (predictionRunner.IsComputing || body == null)
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

        StartPrediction(body, PrepareRealtimeRequest(body, request));
    }

    private void TryStartFinalLongPass(NBody body)
    {
        if (predictionRunner.IsComputing || body == null)
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

        StartPrediction(body, PrepareHighQualityRequest(body, request));
        fullPassRequested = false;
    }

    private void TryStartContinuousPrediction(NBody body)
    {
        if (predictionRunner.IsComputing || body == null)
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

        StartPrediction(body, PrepareContinuousRequest(body, request));
    }

    private TrajectoryPredictionRequest PrepareRealtimeRequest(NBody body, TrajectoryPredictionRequest request)
    {
        request = dragRefreshPolicy.ResolveRequest(body, trackedBody, request);

        if (forceFastSwitchPreview && !dragRefreshPolicy.LongDragPassageRefreshActive)
        {
            forceFastSwitchPreview = false;

            if (request.Backend == TrajectoryPredictionBackend.NativeMatched)
                request = request.WithBackend(TrajectoryPredictionBackend.GpuGravity);
        }

        return ApplyHighQualitySettings(request);
    }

    private TrajectoryPredictionRequest PrepareHighQualityRequest(NBody body, TrajectoryPredictionRequest request)
    {
        request = dragRefreshPolicy.ResolveRequest(body, trackedBody, request);
        return ApplyHighQualitySettings(request);
    }

    private TrajectoryPredictionRequest ApplyHighQualitySettings(TrajectoryPredictionRequest request)
    {
        if (request.Backend != TrajectoryPredictionBackend.NativeMatched)
            return request;

        int maxPoints = Mathf.Max(request.MaxOutputPoints, continuousHighQualityMaxOutputPoints);
        predictionState.ScheduleNextHighQualityPass(Time.unscaledTime, continuousHighQualityInterval);
        return request.WithMaxOutputPoints(maxPoints);
    }

    private TrajectoryPredictionRequest PrepareContinuousRequest(NBody body, TrajectoryPredictionRequest request)
    {
        request = dragRefreshPolicy.ResolveRequest(body, trackedBody, request);

        if (request.Backend != TrajectoryPredictionBackend.NativeMatched)
            return request;

        bool useHighQualityPass = predictionState.IsHighQualityPassDue(Time.unscaledTime);
        int maxPoints = useHighQualityPass
            ? Mathf.Max(continuousHighQualityMaxOutputPoints, continuousCoarseMaxOutputPoints)
            : continuousCoarseMaxOutputPoints;

        if (useHighQualityPass)
            predictionState.ScheduleNextHighQualityPass(Time.unscaledTime, continuousHighQualityInterval);

        return request.WithMaxOutputPoints(maxPoints);
    }

    private void StartPrediction(NBody body, TrajectoryPredictionRequest request)
    {
        predictionSteps = request.Steps;
        BeginPrediction(body, request);
    }

    private void BeginPrediction(NBody body, TrajectoryPredictionRequest request)
    {
        predictionRunner.Begin(body, bodyService, request, () => this && gameObject);
    }

    private void InvalidatePredictionWork()
    {
        predictionRunner.Invalidate();
        forceFastSwitchPreview = false;
    }

    private void ApplyPredictionResult(
        NBody body,
        Vector3[] resultArray,
        TrajectoryPredictionRequest request,
        float sampleDeltaTime)
    {
        if (trackedBody != body)
            return;

        ReplaceLatestPrediction(resultArray);
        latestPredictionBody = body;

        latestPredictionStartTime = request.Epoch;
        latestPredictionDeltaTime = sampleDeltaTime;

        predictionLine?.UpdateLine(ClipTrackedPrediction(resultArray ?? Array.Empty<Vector3>()));

        CachePredictionSourceState(body, request);
        orbitIsDirty = false;
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

    private void CapturePreManeuverFromLatest()
    {
        if (latestPrediction != null && latestPrediction.Count > 1)
        {
            preManeuverSnapshot = new List<Vector3>(latestPrediction);

            preManeuverLine?.UpdateLine(ClipTrackedPrediction(preManeuverSnapshot.ToArray()));
        }
        else
        {
            ClearPreManeuverSnapshot();
        }

        SetPreManeuverButtonVisible(HasPreManeuverSnapshot);
    }

    private bool HasPreManeuverSnapshot => preManeuverSnapshot != null;

    private void ClearPreManeuverSnapshot()
    {
        preManeuverSnapshot = null;
        preManeuverLine?.Clear();
    }

    private void SetPreManeuverButtonVisible(bool visible)
    {
        ui?.SetRemovePreManeuverButtonVisible(visible);
    }

    private void OnClearPreManeuverClicked()
    {
        ClearPreManeuverOrbit();
        SetPreManeuverButtonVisible(false);
    }

    private Vector3[] ClipTrajectorySphere(Vector3[] points)
    {
        if (centralBodyCache == null)
            return points;

        return centralBodyCache.ClipTrajectorySphere(points);
    }

    private Vector3[] ClipTrackedPrediction(Vector3[] points)
    {
        points = ClipTrajectorySphere(points);

        if (clipToSingleOrbit && centralBodyCache != null)
            points = centralBodyCache.ClipToSingleOrbit(points, fullTurnEpsilon, minStepAngleRad);

        return points;
    }

    private Vector3[] ClipPreviewToSingleOrbit(Vector3[] points)
    {
        if (centralBodyCache == null || !clipToSingleOrbit)
            return points;

        return centralBodyCache.ClipToSingleOrbit(points, fullTurnEpsilon, minStepAngleRad);
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

    private bool IsDirtyPredictionReady()
    {
        return orbitIsDirty && dirtyDebounceCounter == 0;
    }

    private bool ShouldContinuouslyRefreshPrediction(NBody body)
    {
        if (!enableContinuousRefresh || body == null || predictionRunner.IsComputing)
            return false;

        if (dragRefreshPolicy.ShouldSuppressContinuousRefresh(body, trackedBody))
            return false;

        float simulationTime = bodyRuntimeCoordinator != null ? bodyRuntimeCoordinator.simulationTime : 0f;
        return predictionState.ShouldContinuouslyRefresh(
            body,
            latestPredictionBody,
            latestPrediction,
            Time.unscaledTime,
            simulationTime,
            minimumContinuousRefreshInterval,
            continuousPositionDriftThreshold,
            continuousVelocityDriftThreshold
        );
    }

    private void CachePredictionSourceState(NBody body, TrajectoryPredictionRequest request)
    {
        predictionState.CacheSourceState(body, request, Time.unscaledTime, minimumContinuousRefreshInterval);
    }

    private void ResetContinuousRefreshState()
    {
        predictionState.Reset();
    }

    private void PumpPredictionResults()
    {
        predictionRunner.PumpCompletedWork();
        ApplyCompletedPredictionResult();
    }

    private void ApplyCompletedPredictionResult()
    {
        if (!predictionRunner.TryTakeCompletedResult(out TrajectoryPredictionResult result))
            return;

        ApplyPredictionResult(result.Body, result.Points, result.Request, result.SampleDeltaTime);
    }

    private void ReplaceLatestPrediction(Vector3[] resultArray)
    {
        latestPrediction.Clear();

        if (resultArray == null || resultArray.Length == 0)
            return;

        if (latestPrediction.Capacity < resultArray.Length)
            latestPrediction.Capacity = resultArray.Length;

        for (int i = 0; i < resultArray.Length; i++)
            latestPrediction.Add(resultArray[i]);
    }

}
