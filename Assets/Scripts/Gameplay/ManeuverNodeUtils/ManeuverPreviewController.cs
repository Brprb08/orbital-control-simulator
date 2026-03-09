using UnityEngine;
using Unity.Mathematics;

public class ManeuverPreviewController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private OrbitPreviewUI orbitPreviewUI;

    [Header("Preview Performance")]
    [SerializeField] private float previewRebuildDelay = 0.08f;
    [SerializeField] private int fastPreviewSteps = 2000;
    [SerializeField] private float fastPreviewDt = 6f;
    [SerializeField] private int finalPreviewSteps = 6000;
    [SerializeField] private bool useFastPreviewWhileInteracting = true;

    private BodyService bodyService;
    private BodyRuntimeCoordinator bodyRuntimeCoordinator;
    private ThrustController thrustController;
    private TrajectoryRenderer trajectoryRenderer;

    private bool previewDirty;
    private float previewDirtyUntil;
    private ManeuverNode pendingPreviewNode;
    private bool previewInteractionActive;

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

            if (pendingPreviewNode != null && !pendingPreviewNode.isFinalized)
                RebuildPreviewNow(pendingPreviewNode, previewInteractionActive);

            previewInteractionActive = false;
        }
    }

    public void RequestPreview(ManeuverNode node, bool interactionActive)
    {
        if (node == null)
            return;

        pendingPreviewNode = node;
        previewDirty = true;
        previewInteractionActive = interactionActive;
        previewDirtyUntil = Time.unscaledTime + previewRebuildDelay;
    }

    public void Clear()
    {
        previewDirty = false;
        pendingPreviewNode = null;
        previewInteractionActive = false;
        previewVCcache = Vector3.right;
        previewHCache = Vector3.up;
        previewOrbitParams = new OrbitalParameters(false);
    }

    private void RebuildPreviewNow(ManeuverNode node, bool interactionActive)
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

        double3 posNow = body.state.position;
        double3 velNow = body.state.velocity;

        float simNow = bodyRuntimeCoordinator.simulationTime;
        float fixedDt = Time.fixedDeltaTime;

        float burnTimeRaw = Mathf.Max(node.burnTime, simNow);
        float burnStartQuantized = Mathf.Ceil(burnTimeRaw / fixedDt) * fixedDt;
        int burnFrames = Mathf.Max(1, Mathf.CeilToInt(node.duration / fixedDt));

        float coastDt = Mathf.Max(0f, burnStartQuantized - simNow);

        const double G_unity = 6.67430e-23;
        double mu = G_unity * central.mass;

        previewMassBuf[0] = body.state.mass;
        previewCdBuf[0] = 0f;
        previewAreaBuf[0] = 0f;
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

        IntegrateOneSegment(coastDt, Vector3.zero, 0);

        Vector3 velPre = new Vector3((float)velNow.x, (float)velNow.y, (float)velNow.z);
        Vector3 center = new Vector3(
            (float)central.state.position.x,
            (float)central.state.position.y,
            (float)central.state.position.z
        );

        float effThrust = thrustController != null
            ? thrustController.EffectiveForwardThrustMagnitude
            : 10f;

        float scaledMag = effThrust / 10f;

        sbyte normalSign =
            node.burnType == BurnType.Normal ? (sbyte)1 :
            node.burnType == BurnType.AntiNormal ? (sbyte)-1 :
            (sbyte)0;

        for (int i = 0; i < burnFrames; i++)
        {
            Vector3 burnPos = new Vector3((float)posNow.x, (float)posNow.y, (float)posNow.z);
            Vector3 burnVel = new Vector3((float)velNow.x, (float)velNow.y, (float)velNow.z);

            Vector3 burnDir = AttitudeMath.ComputeBurnDirection(
                node.burnType,
                burnPos,
                burnVel,
                center,
                ref previewVCcache,
                ref previewHCache
            );

            if (burnDir.sqrMagnitude < 1e-8f)
                burnDir = burnVel.sqrMagnitude > 1e-8f ? burnVel.normalized : Vector3.forward;
            else
                burnDir.Normalize();

            Vector3 thrustForce = burnDir * scaledMag;
            IntegrateOneSegment(fixedDt, thrustForce, normalSign);
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
            previewSteps = finalPreviewSteps;
            previewDt = trajectoryRenderer.predictionDeltaTime > 0f
                ? trajectoryRenderer.predictionDeltaTime
                : 0.5f;
        }

        trajectoryRenderer.QuickPreviewOnceLong(
            startPos: posAfterBurn,
            startVel: velAfterBurn,
            bodyMass: body.mass,
            steps: previewSteps,
            dt: previewDt,
            singleOrbit: true
        );
    }
}