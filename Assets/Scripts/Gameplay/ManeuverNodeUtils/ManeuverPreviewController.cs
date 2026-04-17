using UnityEngine;
using Unity.Mathematics;

public class ManeuverPreviewController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private OrbitPreviewUI orbitPreviewUI;

    [Header("Preview Performance")]
    [SerializeField] private float previewRebuildDelay = 0.08f;
    [SerializeField] private float fastPreviewMinInterval = 0.03f;
    [SerializeField] private int fastPreviewSteps = 2000;
    [SerializeField] private float fastPreviewDt = 6f;
    [SerializeField] private bool autoFitFinalPreviewToOrbit = true;
    [SerializeField] private int finalPreviewSteps = 6000;
    [SerializeField] private bool useFastPreviewWhileInteracting = true;

    private BodyService bodyService;
    private BodyRuntimeCoordinator bodyRuntimeCoordinator;
    private ThrustController thrustController;
    private TrajectoryRenderer trajectoryRenderer;

    private bool previewDirty;
    private float previewDirtyUntil;
    private ManeuverNode pendingPreviewNode;
    private float nextFastPreviewTime;
    private bool pendingRenderLine = true;

    private Vector3 previewVCcache = Vector3.right;
    private Vector3 previewHCache = Vector3.up;

    private OrbitalParameters previewOrbitParams = new OrbitalParameters(false);
    public OrbitalParameters PreviewOrbitParams => previewOrbitParams;

    private readonly double3[] previewPosBuf = new double3[1];
    private readonly double3[] previewVelBuf = new double3[1];
    private readonly double[] previewMassBuf = new double[1];
    private readonly Vector3[] previewThrustBuf = new Vector3[1];
    private readonly float[] previewCdBuf = new float[1];
    private readonly float[] previewAreaBuf = new float[1];
    private readonly sbyte[] previewNormalSignBuf = new sbyte[1];
    private readonly byte[] previewIsThrustingBuf = new byte[1];
    private readonly sbyte[] previewLatchedParityBuf = new sbyte[1];

    public void Initialize(
        BodyService bodyService,
        BodyRuntimeCoordinator bodyRuntimeCoordinator,
        ThrustController thrustController,
        TrajectoryRenderer trajectoryRenderer)
    {
        this.bodyService = bodyService;
        this.bodyRuntimeCoordinator = bodyRuntimeCoordinator;
        this.thrustController = thrustController;
        this.trajectoryRenderer = trajectoryRenderer;
    }

    private void LateUpdate()
    {
        if (previewDirty && Time.unscaledTime >= previewDirtyUntil)
        {
            previewDirty = false;

            if (pendingPreviewNode != null)
                RebuildPreviewNow(
                    pendingPreviewNode,
                    interactionActive: false,
                    renderLine: pendingRenderLine
                );
        }
    }

    public void RequestPreview(ManeuverNode node, bool interactionActive)
    {
        RequestRebuild(node, interactionActive, renderLine: true, immediate: false);
    }

    public void RequestReadoutRefresh(ManeuverNode node, bool immediate = false)
    {
        RequestRebuild(node, interactionActive: false, renderLine: false, immediate: immediate);
    }

    private void RequestRebuild(ManeuverNode node, bool interactionActive, bool renderLine, bool immediate)
    {
        if (node == null)
            return;

        pendingPreviewNode = node;
        pendingRenderLine = renderLine;

        float now = Time.unscaledTime;

        if (immediate)
        {
            previewDirty = false;
            RebuildPreviewNow(node, interactionActive, renderLine);
            return;
        }

        if (interactionActive && useFastPreviewWhileInteracting)
        {
            if (now >= nextFastPreviewTime)
            {
                RebuildPreviewNow(node, interactionActive: true, renderLine: true);
                nextFastPreviewTime = now + fastPreviewMinInterval;
            }

            previewDirty = true;
            previewDirtyUntil = now + previewRebuildDelay;
            return;
        }

        previewDirty = true;
        previewDirtyUntil = now + previewRebuildDelay;
    }

    public void Clear()
    {
        previewDirty = false;
        pendingPreviewNode = null;
        nextFastPreviewTime = 0f;
        pendingRenderLine = true;
        previewVCcache = Vector3.right;
        previewHCache = Vector3.up;
        previewOrbitParams = new OrbitalParameters(false);
        orbitPreviewUI?.ShowInvalid();
    }

    private void RebuildPreviewNow(ManeuverNode node, bool interactionActive, bool renderLine)
    {
        if (trajectoryRenderer == null || node == null)
            return;
        if (node.targetBody == null)
            return;
        if (bodyService == null || bodyService.CentralBody == null)
            return;
        if (bodyRuntimeCoordinator == null)
            return;

        var body = node.targetBody;
        var central = bodyService.CentralBody;

        float fixedDt = Time.fixedDeltaTime;
        int currentStep = bodyRuntimeCoordinator.simulationStep;
        BuildPreviewSchedule(node, currentStep, fixedDt, out int burnStartStep, out int burnFrames);

        const double G_unity = 6.67430e-23;
        double mu = G_unity * central.trueMass;
        double3 posNow;
        double3 velNow;
        bool useExactPreview = !interactionActive;

        if (useExactPreview)
        {
            posNow = body.state.position;
            velNow = body.state.velocity;
        }
        else
        {
            Vector3 burnStartPos;
            Vector3 burnStartVel;
            if (TrajectorySampler.TrySampleAtBurnTime(node, out burnStartPos, out burnStartVel, out _))
            {
                posNow = new double3(burnStartPos.x, burnStartPos.y, burnStartPos.z);
                velNow = new double3(burnStartVel.x, burnStartVel.y, burnStartVel.z);
            }
            else
            {
                posNow = body.state.position;
                velNow = body.state.velocity;
            }
        }

        previewMassBuf[0] = body.state.mass;
        previewCdBuf[0] = useExactPreview ? body.dragCoefficient : 0f;
        previewAreaBuf[0] = useExactPreview ? ResolveArea(body) : 0f;
        previewLatchedParityBuf[0] = 0;

        void IntegrateOneSegment(double segmentDt, Vector3 thrustWorld, sbyte normalSign)
        {
            if (segmentDt <= 0.0)
                return;

            previewPosBuf[0] = posNow;
            previewVelBuf[0] = velNow;
            previewThrustBuf[0] = thrustWorld;
            previewNormalSignBuf[0] = normalSign;
            previewIsThrustingBuf[0] = (byte)(thrustWorld.sqrMagnitude > 0f ? 1 : 0);

            const float dtMax = 0.02f;
            float totalDt = (float)segmentDt;
            int substeps = Mathf.Max(1, Mathf.CeilToInt(totalDt / dtMax));

            NativePhysics.BatchTwoBodyIntegrateMuEx(
                previewPosBuf,
                previewVelBuf,
                previewMassBuf,
                previewThrustBuf,
                previewCdBuf,
                previewAreaBuf,
                previewNormalSignBuf,
                previewIsThrustingBuf,
                previewLatchedParityBuf,
                1,
                mu,
                totalDt,
                substeps
            );

            posNow = previewPosBuf[0];
            velNow = previewVelBuf[0];
        }

        if (useExactPreview)
        {
            int coastSteps = Mathf.Max(0, burnStartStep - currentStep);
            for (int i = 0; i < coastSteps; i++)
                IntegrateOneSegment(fixedDt, Vector3.zero, 0);
        }

        Vector3 velPre = new Vector3((float)velNow.x, (float)velNow.y, (float)velNow.z);
        Vector3 center = new Vector3(
            (float)central.state.position.x,
            (float)central.state.position.y,
            (float)central.state.position.z
        );

        float effThrust = thrustController != null
            ? thrustController.EffectiveForwardThrustMagnitude
            : 10f;

        for (int i = 0; i < burnFrames; i++)
        {
            Vector3 burnPos = new Vector3((float)posNow.x, (float)posNow.y, (float)posNow.z);
            Vector3 burnVel = new Vector3((float)velNow.x, (float)velNow.y, (float)velNow.z);

            if (ManeuverBurnMath.TryBuildBurnCommand(
                    node.burnType,
                    burnPos,
                    burnVel,
                    center,
                    effThrust,
                    ref previewVCcache,
                    ref previewHCache,
                    out Vector3 thrustForce,
                    out sbyte normalSign))
            {
                IntegrateOneSegment(fixedDt, thrustForce, normalSign);
            }
            else
            {
                IntegrateOneSegment(fixedDt, Vector3.zero, 0);
            }
        }

        Vector3 posAfterBurn = new Vector3((float)posNow.x, (float)posNow.y, (float)posNow.z);
        Vector3 velAfterBurn = new Vector3((float)velNow.x, (float)velNow.y, (float)velNow.z);

        node.deltaV = velAfterBurn - velPre;

        double3 posD = new double3(posAfterBurn.x, posAfterBurn.y, posAfterBurn.z);
        double3 velD = new double3(velAfterBurn.x, velAfterBurn.y, velAfterBurn.z);

        previewOrbitParams = OrbitalCalculations.CalculateOrbitalParameters(
            central.trueMass,
            central.state.position,
            posD,
            velD
        );

        if (orbitPreviewUI != null)
        {
            if (previewOrbitParams.isValid)
                orbitPreviewUI.Show(previewOrbitParams, central);
            else
                orbitPreviewUI.ShowInvalid();
        }

        int previewSteps;
        float previewDt;

        if (useFastPreviewWhileInteracting && interactionActive)
        {
            previewSteps = fastPreviewSteps;
            previewDt = fastPreviewDt;
        }
        else
        {
            if (autoFitFinalPreviewToOrbit)
            {
                previewSteps = 0;
                previewDt = 0f;
            }
            else
            {
                previewSteps = finalPreviewSteps;
                previewDt = trajectoryRenderer.predictionDeltaTime > 0f
                    ? trajectoryRenderer.predictionDeltaTime
                    : 0.5f;
            }
        }

        if (renderLine)
        {
            trajectoryRenderer.QuickPreviewOnceLong(
                startPos: posAfterBurn,
                startVel: velAfterBurn,
                bodyMass: (float)body.state.mass,
                steps: previewSteps,
                dt: previewDt,
                singleOrbit: true
            );
        }
    }

    private static void BuildPreviewSchedule(
        ManeuverNode node,
        int currentStep,
        float fixedDt,
        out int burnStartStep,
        out int burnFrames)
    {
        if (node != null && node.isFinalized)
        {
            burnStartStep = Mathf.Max(currentStep, node.burnStartStep);
            int burnEndStep = node.burnStartStep + Mathf.Max(0, node.burnStepCount);
            burnFrames = Mathf.Max(0, burnEndStep - burnStartStep);
            return;
        }

        burnFrames = Mathf.Max(
            1,
            node != null && node.burnStepCount > 0
                ? node.burnStepCount
                : Mathf.CeilToInt((node != null ? node.duration : 0f) / fixedDt)
        );

        float burnTime = node != null ? node.burnTime : 0f;
        burnStartStep = Mathf.Max(currentStep, Mathf.CeilToInt(burnTime / fixedDt));
    }

    private static float ResolveArea(NBody body)
    {
        if (body == null)
            return 0f;

        float area = (float)body.state.crossSectionArea;
        if (area > 0f)
            return area;

        double radius = body.radius;
        return (float)(math.PI * radius * radius);
    }
}
