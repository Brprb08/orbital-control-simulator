using UnityEngine;
using Unity.Mathematics;

public class ManeuverPreviewController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private OrbitPreviewUI orbitPreviewUI;

    [Header("Preview Performance")]
    [SerializeField] private float previewRebuildDelay = 0.08f;
    [SerializeField] private float fastPreviewMinInterval = 0.15f;
    [SerializeField] private int fastPreviewSteps = 768;
    [SerializeField] private float fastPreviewDt = 6f;
    [SerializeField] private bool autoFitFinalPreviewToOrbit = true;
    [SerializeField] private int finalPreviewSteps = 6000;
    [SerializeField] private bool useFastPreviewWhileInteracting = true;
    [SerializeField, Min(1)] private int maxExactCoastSteps = 5000;
    [SerializeField, Min(1)] private int maxFinalizedExactNativeCoastSteps = 5000;
    [SerializeField, Min(60f)] private float maxFinalizedAnalyticCoastSeconds = 172800f;

    private BodyService bodyService;
    private BodyRuntimeCoordinator bodyRuntimeCoordinator;
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
    public event System.Action<bool> AccuratePreviewLimitExceededChanged;

    private readonly double3[] previewPosBuf = new double3[1];
    private readonly double3[] previewVelBuf = new double3[1];
    private readonly double[] previewMassBuf = new double[1];
    private readonly Vector3[] previewThrustBuf = new Vector3[1];
    private readonly float[] previewCdBuf = new float[1];
    private readonly float[] previewAreaBuf = new float[1];
    private readonly sbyte[] previewNormalSignBuf = new sbyte[1];
    private readonly byte[] previewIsThrustingBuf = new byte[1];
    private readonly sbyte[] previewLatchedParityBuf = new sbyte[1];
    private readonly double[] previewDeltaVBuf = new double[1];
    private readonly double[] previewDryMassBuf = new double[1];
    private readonly double[] previewFuelMassBuf = new double[1];
    private readonly double[] previewIspBuf = new double[1];
    private readonly byte[] previewFiniteFuelBuf = new byte[1];

    public void Initialize(
        BodyService bodyService,
        BodyRuntimeCoordinator bodyRuntimeCoordinator,
        TrajectoryRenderer trajectoryRenderer)
    {
        this.bodyService = bodyService;
        this.bodyRuntimeCoordinator = bodyRuntimeCoordinator;
        this.trajectoryRenderer = trajectoryRenderer;
    }

    private void LateUpdate()
    {
        if (pendingPreviewNode != null && orbitPreviewUI != null && bodyRuntimeCoordinator != null)
            orbitPreviewUI.UpdateTPlus(GetTimeToNode(pendingPreviewNode));

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
        SetAccuratePreviewLimitExceeded(false);
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

        float scheduleDt = BodyRuntimeCoordinator.BaseSimulationStep;
        float currentTime = bodyRuntimeCoordinator.simulationTime;
        float previewBurnTime = ResolvePreviewBurnTime(node);
        PreviewCoastMode coastMode = ResolvePreviewCoastMode(
            node,
            previewBurnTime,
            currentTime,
            scheduleDt,
            interactionActive
        );
        bool useSampledBurnStart = coastMode == PreviewCoastMode.Sampled;
        float scheduleBurnTime = useSampledBurnStart
            ? currentTime
            : previewBurnTime;
        BuildPreviewSchedule(node, scheduleBurnTime, currentTime, scheduleDt, out float burnStartTime, out float burnDuration);
        SetAccuratePreviewLimitExceeded(
            !interactionActive &&
            CoastExceedsAnalyticLimit(node, previewBurnTime, currentTime)
        );

        const double G_unity = PhysicsConstants.GDouble;
        double mu = G_unity * central.TotalMassKilograms;
        double3 posNow;
        double3 velNow;
        bool useExactPreview = !interactionActive && !useSampledBurnStart;

        if (useExactPreview)
        {
            posNow = body.state.position;
            velNow = body.state.velocity;
        }
        else
        {
            Vector3 burnStartPos;
            Vector3 burnStartVel;
            if (TrajectorySampler.TrySampleAtBurnTimeWrapped(node, out burnStartPos, out burnStartVel, out _))
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
        previewDryMassBuf[0] = body.DryMassKilograms;
        previewFuelMassBuf[0] = body.FuelMassKilograms;
        previewIspBuf[0] = body.SpecificImpulseSeconds;
        previewFiniteFuelBuf[0] = (byte)(body.UnlimitedPropellant ? 0 : 1);
        previewCdBuf[0] = useExactPreview ? body.dragCoefficient : 0f;
        previewAreaBuf[0] = useExactPreview ? (float)body.DragAreaSquareUnits : 0f;
        previewLatchedParityBuf[0] = 0;
        double propulsiveDeltaVWorld = 0.0;

        void IntegrateOneSegment(double segmentDt, Vector3 thrustWorld, sbyte normalSign)
        {
            if (segmentDt <= 0.0)
                return;

            previewPosBuf[0] = posNow;
            previewVelBuf[0] = velNow;
            previewThrustBuf[0] = thrustWorld;
            previewNormalSignBuf[0] = normalSign;
            previewIsThrustingBuf[0] = (byte)(thrustWorld.sqrMagnitude > 0f ? 1 : 0);

            const float dtMax = BodyRuntimeCoordinator.BaseSimulationStep;
            float totalDt = (float)segmentDt;
            int substeps = Mathf.Max(1, Mathf.CeilToInt(totalDt / dtMax));

            NativePhysics.BatchTwoBodyIntegrateMuExWithFuel(
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
                substeps,
                previewDeltaVBuf,
                previewDryMassBuf, previewFuelMassBuf, previewIspBuf, previewFiniteFuelBuf
            );
            previewMassBuf[0] = previewDryMassBuf[0] + previewFuelMassBuf[0];
            propulsiveDeltaVWorld += previewDeltaVBuf[0];

            posNow = previewPosBuf[0];
            velNow = previewVelBuf[0];
        }

        if (useExactPreview)
        {
            double coastSeconds = Mathf.Max(0f, burnStartTime - currentTime);
            if (coastMode == PreviewCoastMode.Analytic)
            {
                if (KeplerPropagator.TryPropagateUniversal(
                        posNow,
                        velNow,
                        mu,
                        coastSeconds,
                        out double3 propagatedPos,
                        out double3 propagatedVel))
                {
                    posNow = propagatedPos;
                    velNow = propagatedVel;
                }
                else
                {
                    IntegrateOneSegment(coastSeconds, Vector3.zero, 0);
                }
            }
            else
            {
                IntegrateOneSegment(coastSeconds, Vector3.zero, 0);
            }
        }

        Vector3 velPre = new Vector3((float)velNow.x, (float)velNow.y, (float)velNow.z);
        Vector3 center = new Vector3(
            (float)central.state.position.x,
            (float)central.state.position.y,
            (float)central.state.position.z
        );

        float effThrust = body.EffectiveThrustNewtons;

        float remainingBurn = burnDuration;
        while (remainingBurn > 1e-6f)
        {
            Vector3 burnPos = new Vector3((float)posNow.x, (float)posNow.y, (float)posNow.z);
            Vector3 burnVel = new Vector3((float)velNow.x, (float)velNow.y, (float)velNow.z);
            float burnChunkDt = Mathf.Min(scheduleDt, remainingBurn);

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
                IntegrateOneSegment(burnChunkDt, thrustForce, normalSign);
            }
            else
            {
                IntegrateOneSegment(burnChunkDt, Vector3.zero, 0);
            }

            remainingBurn -= burnChunkDt;
        }

        Vector3 posAfterBurn = new Vector3((float)posNow.x, (float)posNow.y, (float)posNow.z);
        Vector3 velAfterBurn = new Vector3((float)velNow.x, (float)velNow.y, (float)velNow.z);

        node.deltaV = velAfterBurn - velPre;
        node.predictedPropulsiveDeltaVMetersPerSecond = propulsiveDeltaVWorld * SimulationUnits.MetersPerUnit;
        node.predictedFuelUsedKg = body.FuelMassKilograms - previewFuelMassBuf[0];
        node.insufficientPropellant = !body.UnlimitedPropellant &&
            body.FuelFlowKilogramsPerSecond * burnDuration > body.FuelMassKilograms + 1e-9;

        double3 posD = new double3(posAfterBurn.x, posAfterBurn.y, posAfterBurn.z);
        double3 velD = new double3(velAfterBurn.x, velAfterBurn.y, velAfterBurn.z);

        previewOrbitParams = OrbitalCalculations.CalculateOrbitalParameters(
            central.TotalMassKilograms,
            central.state.position,
            posD,
            velD
        );

        if (orbitPreviewUI != null)
        {
            float timeToNode = GetTimeToNode(node);
            if (previewOrbitParams.isValid)
                orbitPreviewUI.Show(previewOrbitParams, central, timeToNode);
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
                bodyMass: (float)previewMassBuf[0],
                steps: previewSteps,
                dt: previewDt,
                singleOrbit: true
            );
        }
    }

    private static void BuildPreviewSchedule(
        ManeuverNode node,
        float burnTime,
        float currentTime,
        float scheduleDt,
        out float burnStartTime,
        out float burnDuration)
    {
        if (node != null && node.isFinalized)
        {
            float burnEndTime = ManeuverBurnMath.GetBurnEndTime(node);
            burnStartTime = Mathf.Max(currentTime, node.burnTime);
            burnDuration = Mathf.Max(0f, burnEndTime - burnStartTime);
            return;
        }

        burnStartTime = Mathf.Max(currentTime, burnTime);
        burnDuration = Mathf.Max(scheduleDt, node != null ? node.duration : scheduleDt);
    }

    private float ResolvePreviewBurnTime(ManeuverNode node)
    {
        if (node == null || bodyRuntimeCoordinator == null)
            return 0f;

        return ManeuverNodeTiming.TryGetBoundOrbitPeriod(bodyService, node.targetBody, out float orbitalPeriod)
            ? ManeuverNodeTiming.ResolveFutureBurnTime(
                node.burnTime,
                bodyRuntimeCoordinator.simulationTime,
                orbitalPeriod
            )
            : node.burnTime;
    }

    private enum PreviewCoastMode
    {
        ExactNative,
        Analytic,
        Sampled
    }

    private PreviewCoastMode ResolvePreviewCoastMode(
        ManeuverNode node,
        float resolvedBurnTime,
        float currentTime,
        float scheduleDt,
        bool interactionActive)
    {
        if (node == null)
            return PreviewCoastMode.ExactNative;

        float tolerance = Mathf.Max(0.001f, scheduleDt * 0.5f);
        if (resolvedBurnTime > node.burnTime + tolerance)
            return PreviewCoastMode.Sampled;

        float scheduledStartTime = node.isFinalized ? node.burnTime : resolvedBurnTime;
        int coastSteps = Mathf.CeilToInt(Mathf.Max(0f, scheduledStartTime - currentTime) / scheduleDt);
        if (coastSteps <= maxExactCoastSteps)
            return PreviewCoastMode.ExactNative;

        if (interactionActive)
            return PreviewCoastMode.Sampled;

        if (node.isFinalized && coastSteps <= maxFinalizedExactNativeCoastSteps)
            return PreviewCoastMode.ExactNative;

        float coastSeconds = Mathf.Max(0f, scheduledStartTime - currentTime);
        return coastSeconds <= maxFinalizedAnalyticCoastSeconds
            ? PreviewCoastMode.Analytic
            : PreviewCoastMode.Sampled;
    }

    private bool CoastExceedsAnalyticLimit(
        ManeuverNode node,
        float resolvedBurnTime,
        float currentTime)
    {
        if (node == null)
            return false;

        float scheduledStartTime = node.isFinalized ? node.burnTime : resolvedBurnTime;
        float coastSeconds = Mathf.Max(0f, scheduledStartTime - currentTime);
        return coastSeconds > maxFinalizedAnalyticCoastSeconds;
    }

    private void SetAccuratePreviewLimitExceeded(bool exceeded)
    {
        AccuratePreviewLimitExceededChanged?.Invoke(exceeded);
    }

    private float GetTimeToNode(ManeuverNode node)
    {
        if (node == null || bodyRuntimeCoordinator == null)
            return float.NaN;

        float simTime = bodyRuntimeCoordinator.simulationTime;
        if (node.isFinalized)
            return node.burnTime - simTime;

        return ManeuverNodeTiming.TryGetBoundOrbitPeriod(bodyService, node.targetBody, out float orbitalPeriod)
            ? ManeuverNodeTiming.GetTimeToNode(node.burnTime, simTime, orbitalPeriod)
            : node.burnTime - simTime;
    }

}
