using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

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
    [SerializeField, Min(0f)] private float longDragRefreshEnterAltitudeKm = 480f;
    [SerializeField, Min(0f)] private float longDragRefreshExitAltitudeKm = 520f;

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
    private float nextContinuousPredictionTime;
    private float nextContinuousHighQualityTime;
    private Vector3 lastPredictionSourcePosition;
    private Vector3 lastPredictionSourceVelocity;
    private float lastPredictionEpoch;
    private bool hasPredictionSourceState;
    private TrajectoryPredictionRequest lastPredictionRequest;
    private Task<TrajectoryMatchedPredictionResult> matchedPredictionTask;
    private uint matchedPredictionTaskGeneration;
    private NBody matchedPredictionTaskBody;
    private TrajectoryPredictionRequest matchedPredictionTaskRequest;
    private Vector3[] bufferedPredictionPoints;
    private float bufferedPredictionSampleDeltaTime;
    private NBody bufferedPredictionBody;
    private TrajectoryPredictionRequest bufferedPredictionRequest;
    private bool hasBufferedPredictionResult;
    private bool forceFastSwitchPreview;
    private bool trackedPredictionOwnershipActive;
    private bool dragRefreshOrbitActive;
    private bool longDragPassageRefreshActive;

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

    private void Update()
    {
        UpdateDirtyDebounce();
        PumpCompletedPredictionWork();
        ApplyBufferedPredictionResult();

        if (trackedBody == null)
        {
            ClearTrackedBodyState();
            return;
        }

        RefreshCentralBodyCache(force: false);
        UpdateThrustState();
        UpdateLongDragTransferRefreshState();

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
        UpdateTrackedPredictionOwnership(cameraOnTrackedBody);

        bool dirtyReady = IsDirtyPredictionReady();
        if (cameraOnTrackedBody && (isThrusting || dirtyReady) && !isComputingPrediction)
            TryStartRealtimePrediction(trackedBody);
        else if (cameraOnTrackedBody && ShouldContinuouslyRefreshPrediction(trackedBody))
            TryStartContinuousPrediction(trackedBody);

        if (fullPassRequested &&
            !orbitIsDirty &&
            !isThrusting &&
            !isComputingPrediction &&
            cameraOnTrackedBody &&
            !longDragPassageRefreshActive)
        {
            TryStartFinalLongPass(trackedBody);
        }

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
        trackedPredictionOwnershipActive = false;
        dragRefreshOrbitActive = false;
        longDragPassageRefreshActive = false;

        TrackedBodyChanged?.Invoke(previousBody, trackedBody);

        if (trackedBody == null)
        {
            orbitIsDirty = false;
            forceFastSwitchPreview = false;
            ui?.SetApogeePerigeePanelVisible(false);
            SetPreManeuverButtonVisible(false);
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

    public void ClearPreManeuverOrbit()
    {
        preManeuverSnapshot = null;
        preManeuverLine?.Clear();
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

        burnTrace?.Reset();
        previewModule?.Reset();
        ResetContinuousRefreshState();

        latestPrediction.Clear();
        latestPredictionBody = null;
        latestPredictionStartTime = 0f;
        latestPredictionDeltaTime = 0f;
        dragRefreshOrbitActive = false;
        longDragPassageRefreshActive = false;
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
        ResetContinuousRefreshState();
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

    private void UpdateLongDragTransferRefreshState()
    {
        bool wasPassageActive = longDragPassageRefreshActive;
        dragRefreshOrbitActive = false;

        if (trackedBody == null || bodyService == null || bodyService.CentralBody == null || isThrusting)
        {
            longDragPassageRefreshActive = false;
        }
        else
        {
            OrbitalParameters orbitalParameters = OrbitalCalculations.TryParams(trackedBody, bodyService);
            if (orbitalParameters.isValid)
                dragRefreshOrbitActive =
                    trackedBody.dragCoefficient > 0f &&
                    trackedBody.atmosphericDensity0 > 0f &&
                    (orbitalParameters.perigeeRadius - bodyService.CentralBody.radius) * 10f <=
                    TrajectoryPredictionPlanner.DragPeriapsisThresholdKm;

            if (!dragRefreshOrbitActive)
            {
                longDragPassageRefreshActive = false;
            }
            else
            {
                float currentAltitudeKm = (float)trackedBody.altitude * 10f;
                float thresholdKm = wasPassageActive
                    ? longDragRefreshExitAltitudeKm
                    : longDragRefreshEnterAltitudeKm;

                longDragPassageRefreshActive = currentAltitudeKm <= thresholdKm;
            }
        }

        if (longDragPassageRefreshActive == wasPassageActive)
            return;

        if (longDragPassageRefreshActive)
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
        if (trackedBody == null)
            return false;

        if (cameraController != null && cameraController.CurrentBody == trackedBody)
            return true;

        return cameraMovement != null && cameraMovement.targetBody == trackedBody;
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
            // Trajectory rendering is owned by the active tracked-camera view.
            // If we leave that view, discard in-flight tracked predictions and
            // refresh state so we do not apply stale results later.
            InvalidatePredictionWork();
            ResetContinuousRefreshState();
            return;
        }

        forceFastSwitchPreview = true;
        RequestFullOrbitPass();
        MarkOrbitDirty();
    }

    private void TryStartRealtimePrediction(NBody body)
    {
        if (isComputingPrediction || body == null)
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

        request = ResolveLongDragTransferRequest(body, request);

        if (forceFastSwitchPreview && !longDragPassageRefreshActive)
        {
            forceFastSwitchPreview = false;

            if (request.Backend == TrajectoryPredictionBackend.NativeMatched)
                request = request.WithBackend(TrajectoryPredictionBackend.GpuGravity);
        }

        if (request.Backend == TrajectoryPredictionBackend.NativeMatched)
        {
            int maxPoints = Mathf.Max(request.MaxOutputPoints, continuousHighQualityMaxOutputPoints);
            request = request.WithMaxOutputPoints(maxPoints);
            nextContinuousHighQualityTime = Time.unscaledTime + continuousHighQualityInterval;
        }

        predictionSteps = request.Steps;
        BeginPrediction(body, request);
    }

    private void TryStartFinalLongPass(NBody body)
    {
        if (isComputingPrediction || body == null)
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

        request = ResolveLongDragTransferRequest(body, request);

        if (request.Backend == TrajectoryPredictionBackend.NativeMatched)
        {
            int maxPoints = Mathf.Max(request.MaxOutputPoints, continuousHighQualityMaxOutputPoints);
            request = request.WithMaxOutputPoints(maxPoints);
            nextContinuousHighQualityTime = Time.unscaledTime + continuousHighQualityInterval;
        }

        predictionSteps = request.Steps;
        BeginPrediction(body, request);
        fullPassRequested = false;
    }

    private void TryStartContinuousPrediction(NBody body)
    {
        if (isComputingPrediction || body == null)
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

        request = ResolveLongDragTransferRequest(body, request);

        if (request.Backend == TrajectoryPredictionBackend.NativeMatched)
        {
            bool useHighQualityPass = Time.unscaledTime >= nextContinuousHighQualityTime;
            int maxPoints = useHighQualityPass
                ? Mathf.Max(continuousHighQualityMaxOutputPoints, continuousCoarseMaxOutputPoints)
                : continuousCoarseMaxOutputPoints;

            request = request.WithMaxOutputPoints(maxPoints);

            if (useHighQualityPass)
                nextContinuousHighQualityTime = Time.unscaledTime + continuousHighQualityInterval;
        }

        predictionSteps = request.Steps;
        BeginPrediction(body, request);
    }

    private TrajectoryPredictionRequest ResolveLongDragTransferRequest(
        NBody body,
        TrajectoryPredictionRequest request)
    {
        if (body == null || body != trackedBody || !dragRefreshOrbitActive || longDragPassageRefreshActive)
            return request;

        if (request.Backend == TrajectoryPredictionBackend.NativeMatched)
            return request.WithBackend(TrajectoryPredictionBackend.GpuGravity);

        return request;
    }

    private void BeginPrediction(NBody body, TrajectoryPredictionRequest request)
    {
        isComputingPrediction = true;
        uint requestGeneration = ++predictionGeneration;

        if (request.Backend == TrajectoryPredictionBackend.NativeMatched)
        {
            if (!TrajectoryMatchedPredictor.TryBuildWorkItem(body, bodyService, request, out TrajectoryMatchedPredictionWorkItem workItem))
            {
                isComputingPrediction = false;
                return;
            }

            matchedPredictionTaskGeneration = requestGeneration;
            matchedPredictionTaskBody = body;
            matchedPredictionTaskRequest = request;
            matchedPredictionTask = Task.Run(() => TrajectoryMatchedPredictor.Predict(workItem));
            return;
        }

        body.CalculatePredictedTrajectoryGPU_Async(
            steps: request.Steps,
            deltaTime: request.DeltaTime,
            onComplete: resultArray =>
            {
                if (!this || !gameObject)
                    return;

                if (requestGeneration != predictionGeneration)
                    return;

                QueuePredictionResult(
                    body,
                    resultArray,
                    request,
                    ResolveSampleDeltaTime(request, resultArray)
                );
            }
        );
    }

    private void InvalidatePredictionWork()
    {
        unchecked
        {
            predictionGeneration++;
        }

        matchedPredictionTask = null;
        matchedPredictionTaskBody = null;
        matchedPredictionTaskRequest = default;
        matchedPredictionTaskGeneration = 0;
        ClearBufferedPredictionResult();
        forceFastSwitchPreview = false;
        isComputingPrediction = false;
    }

    private void ApplyPredictionResult(
        NBody body,
        Vector3[] resultArray,
        TrajectoryPredictionRequest request,
        float sampleDeltaTime)
    {
        if (trackedBody != body)
        {
            isComputingPrediction = false;
            return;
        }

        ReplaceLatestPrediction(resultArray);
        latestPredictionBody = body;

        latestPredictionStartTime = request.Epoch;
        latestPredictionDeltaTime = sampleDeltaTime;

        Vector3[] points = resultArray ?? Array.Empty<Vector3>();
        points = ClipTrajectorySphere(points);

        if (clipToSingleOrbit && centralBodyCache != null)
            points = centralBodyCache.ClipToSingleOrbit(points, fullTurnEpsilon, minStepAngleRad);

        if (predictionLine != null)
            predictionLine.UpdateLine(points);

        CachePredictionSourceState(body, request);
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
        SetPreManeuverButtonVisible(false);
    }

    private Vector3[] ClipTrajectorySphere(Vector3[] points)
    {
        if (centralBodyCache == null)
            return points;

        return centralBodyCache.ClipTrajectorySphere(points);
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
        if (!enableContinuousRefresh || body == null || isComputingPrediction)
            return false;

        if (body == trackedBody && dragRefreshOrbitActive && !longDragPassageRefreshActive)
            return false;

        if (lastPredictionRequest.Backend != TrajectoryPredictionBackend.NativeMatched)
            return false;

        if (!TrajectoryPredictionPlanner.ShouldContinuouslyRefresh(lastPredictionRequest))
            return false;

        if (!hasPredictionSourceState || latestPredictionBody != body || latestPrediction == null || latestPrediction.Count < 2)
            return true;

        if (Time.unscaledTime < nextContinuousPredictionTime)
            return false;

        float positionThresholdSq = continuousPositionDriftThreshold * continuousPositionDriftThreshold;
        float velocityThresholdSq = continuousVelocityDriftThreshold * continuousVelocityDriftThreshold;
        bool positionDrifted = (body.transform.position - lastPredictionSourcePosition).sqrMagnitude >= positionThresholdSq;
        bool velocityDrifted = (body.velocity - lastPredictionSourceVelocity).sqrMagnitude >= velocityThresholdSq;

        float simulationTime = bodyRuntimeCoordinator != null ? bodyRuntimeCoordinator.simulationTime : 0f;
        float epochDrift = Mathf.Abs(simulationTime - lastPredictionEpoch);
        float refreshInterval = Mathf.Max(minimumContinuousRefreshInterval, lastPredictionRequest.RefreshInterval);

        return positionDrifted || velocityDrifted || epochDrift >= refreshInterval;
    }

    private void CachePredictionSourceState(NBody body, TrajectoryPredictionRequest request)
    {
        if (body == null)
        {
            ResetContinuousRefreshState();
            return;
        }

        lastPredictionSourcePosition = body.transform.position;
        lastPredictionSourceVelocity = body.velocity;
        lastPredictionEpoch = request.Epoch;
        lastPredictionRequest = request;
        hasPredictionSourceState = true;
        nextContinuousPredictionTime = Time.unscaledTime +
                                       Mathf.Max(minimumContinuousRefreshInterval, request.RefreshInterval);
    }

    private void ResetContinuousRefreshState()
    {
        nextContinuousPredictionTime = 0f;
        nextContinuousHighQualityTime = 0f;
        lastPredictionSourcePosition = Vector3.zero;
        lastPredictionSourceVelocity = Vector3.zero;
        lastPredictionEpoch = 0f;
        hasPredictionSourceState = false;
        lastPredictionRequest = default;
    }

    private void PumpCompletedPredictionWork()
    {
        if (matchedPredictionTask == null || !matchedPredictionTask.IsCompleted)
            return;

        Task<TrajectoryMatchedPredictionResult> completedTask = matchedPredictionTask;
        uint taskGeneration = matchedPredictionTaskGeneration;
        NBody taskBody = matchedPredictionTaskBody;
        TrajectoryPredictionRequest taskRequest = matchedPredictionTaskRequest;

        matchedPredictionTask = null;
        matchedPredictionTaskBody = null;
        matchedPredictionTaskRequest = default;
        matchedPredictionTaskGeneration = 0;

        if (taskGeneration != predictionGeneration)
        {
            isComputingPrediction = false;
            return;
        }

        if (completedTask.IsCanceled)
        {
            isComputingPrediction = false;
            return;
        }

        if (completedTask.IsFaulted)
        {
            Debug.LogException(completedTask.Exception);
            isComputingPrediction = false;
            return;
        }

        TrajectoryMatchedPredictionResult result = completedTask.Result;
        QueuePredictionResult(taskBody, result.Points, taskRequest, result.SampleDeltaTime);
    }

    private void QueuePredictionResult(
        NBody body,
        Vector3[] resultArray,
        TrajectoryPredictionRequest request,
        float sampleDeltaTime)
    {
        bufferedPredictionBody = body;
        bufferedPredictionRequest = request;
        bufferedPredictionPoints = resultArray ?? Array.Empty<Vector3>();
        bufferedPredictionSampleDeltaTime = sampleDeltaTime;
        hasBufferedPredictionResult = true;
    }

    private void ApplyBufferedPredictionResult()
    {
        if (!hasBufferedPredictionResult)
            return;

        NBody body = bufferedPredictionBody;
        TrajectoryPredictionRequest request = bufferedPredictionRequest;
        Vector3[] resultArray = bufferedPredictionPoints ?? Array.Empty<Vector3>();
        float sampleDeltaTime = bufferedPredictionSampleDeltaTime;

        ClearBufferedPredictionResult();
        ApplyPredictionResult(body, resultArray, request, sampleDeltaTime);
    }

    private void ClearBufferedPredictionResult()
    {
        bufferedPredictionBody = null;
        bufferedPredictionRequest = default;
        bufferedPredictionPoints = null;
        bufferedPredictionSampleDeltaTime = 0f;
        hasBufferedPredictionResult = false;
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

    private static float ResolveSampleDeltaTime(TrajectoryPredictionRequest request, Vector3[] resultArray)
    {
        int resultCount = resultArray != null ? resultArray.Length : 0;
        if (resultCount <= 0)
            return request.DeltaTime;

        int lodFactor = Mathf.Max(1, Mathf.CeilToInt((float)request.Steps / resultCount));
        return request.DeltaTime * lodFactor;
    }
}
