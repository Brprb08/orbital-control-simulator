using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

/// <summary>Owns batched integration, native buffers, and physics-to-render synchronization.</summary>
internal sealed class BodyPhysicsStepper : IDisposable
{
    private readonly BodyService bodyService;
    private readonly SimContext ctx;
    private readonly List<NBody> _satCache = new();
    private readonly List<AttitudeController> _satAttitudeCache = new();
    private double3[] _posBuf;
    private double3[] _velBuf;
    private double[] _massBuf;
    private double[] _dryMassBuf;
    private double[] _fuelMassBuf;
    private double[] _ispBuf;
    private byte[] _finiteFuelBuf;
    private Vector3[] _thrustBuf;
    private Vector3[] _baseThrustBuf;
    private float[] _cdBuf;
    private float[] _areaBuf;
    private double[] _deltaVBuf;

    private NBody _central => bodyService.CentralBody;
    private double _muUnity; // cached μ (= G*M) in Unity units
    private const double G_unity = PhysicsConstants.GDouble; // 1u = 10 km

    private byte[] _isThrustingBuf;
    private sbyte[] _normalSignBuf;
    private sbyte[] _baseNormalSignBuf;
    private Vector3 _nodeBurnVCache = Vector3.right;
    private Vector3 _nodeBurnHCache = Vector3.up;
    private sbyte[] _latchedParityBuf; // −1/0/+1

    private const float EarthRotationRate = 360f / 86164f; // deg/sec, sidereal
    private const float AtmosphericDragCutoffUnits = PhysicsConstants.AtmosphericDragCutoffAltitudeKm / SimulationUnits.KilometersPerUnit;


    public BodyPhysicsStepper(BodyService bodyService, SimContext ctx)
    {
        this.bodyService = bodyService;
        this.ctx = ctx;
        bodyService.MembershipChanged += RebuildSatelliteCache;
        RebuildSatelliteCache();
    }

    public void Dispose() => bodyService.MembershipChanged -= RebuildSatelliteCache;

    public void Step(float stepDt)
    {
        foreach (NBody body in bodyService.Bodies)
        {
            if (body == null) continue;
            ReportNaNPosition(body);
            if (body.isCentralBody)
                RotateCentralBodyVisual(body);
        }

        ScheduledBurn burn = ctx?.ThrustController != null
            ? ctx.ThrustController.PrepareSimulationStep(_satCache, _satAttitudeCache, stepDt)
            : default;
        StepAllBodiesBatch(stepDt, burn);
    }

    private void ReportNaNPosition(NBody body)
    {
        Vector3 pos = body.transform.position;
        if (!float.IsNaN(pos.x) && !float.IsNaN(pos.y) && !float.IsNaN(pos.z))
            return;

        Debug.LogError(
            $"[NBODY]: {body.name} has NaN transform.position! " +
            $"velocity={body.velocity}, force={body.state.force}"
        );
    }

    private static void RotateCentralBodyVisual(NBody body)
    {
        float deltaAngle = -EarthRotationRate * Time.deltaTime;
        body.transform.Rotate(Vector3.up, deltaAngle);
    }

    private void EnsureArraysForN(int n)
    {
        void Alloc<T>(ref T[] arr, int len) { if (arr == null || arr.Length != len) arr = (len > 0) ? new T[len] : Array.Empty<T>(); }

        Alloc(ref _posBuf, n);
        Alloc(ref _velBuf, n);
        Alloc(ref _massBuf, n);
        Alloc(ref _dryMassBuf, n);
        Alloc(ref _fuelMassBuf, n);
        Alloc(ref _ispBuf, n);
        Alloc(ref _finiteFuelBuf, n);
        Alloc(ref _thrustBuf, n);
        Alloc(ref _baseThrustBuf, n);
        Alloc(ref _cdBuf, n);
        Alloc(ref _areaBuf, n);
        Alloc(ref _deltaVBuf, n);

        if (_isThrustingBuf == null || _isThrustingBuf.Length != n)
            _isThrustingBuf = new byte[n]; // zeroed

        if (_latchedParityBuf == null || _latchedParityBuf.Length != n)
            _latchedParityBuf = new sbyte[n]; // zeroed 

        if (_normalSignBuf == null || _normalSignBuf.Length != n)
            _normalSignBuf = new sbyte[n];

        if (_baseNormalSignBuf == null || _baseNormalSignBuf.Length != n)
            _baseNormalSignBuf = new sbyte[n];
    }

    private void StepAllBodiesBatch(float stepDt, ScheduledBurn burn)
    {
        if (_satCache == null || _satCache.Count == 0) return;
        int n = _satCache.Count;
        if (stepDt <= 0f) return;

        EnsureArraysForN(n);
        UpdateMu();
        Array.Clear(_normalSignBuf, 0, n);
        Array.Clear(_baseNormalSignBuf, 0, n);
        BodyRuntimeCoordinator runtime = ctx?.BodyRuntimeCoordinator;
        float simulationTime = runtime != null ? runtime.simulationTime : 0f;

        Vector3 center = _central != null
            ? _central.state.position.ToVector3()
            : Vector3.zero;
        double3 centerD = _central != null ? _central.state.position : double3.zero;
        double dragCutoffRadius = (_central != null ? _central.radius : (float)PhysicsConstants.EarthRadiusUnits) + AtmosphericDragCutoffUnits;
        double dragCutoffRadiusSq = dragCutoffRadius * dragCutoffRadius;
        int nodeTargetIndex = -1;

        for (int i = 0; i < n; i++)
        {
            var b = _satCache[i];
            if (b == null)
            {
                _posBuf[i] = default; _velBuf[i] = default; _massBuf[i] = 0.0;
                _finiteFuelBuf[i] = 0;
                _thrustBuf[i] = Vector3.zero; _baseThrustBuf[i] = Vector3.zero; _cdBuf[i] = 0f; _areaBuf[i] = 0f;
                _normalSignBuf[i] = 0;
                _baseNormalSignBuf[i] = 0;
                _isThrustingBuf[i] = 0;              // not thrusting
                                                     // _latchedParityBuf[i] stays as-is (but native will clear when not thrusting)
                continue;
            }

            _posBuf[i] = b.state.position;
            _velBuf[i] = b.state.velocity;
            _massBuf[i] = b.state.mass;
            _dryMassBuf[i] = b.DryMassKilograms;
            _fuelMassBuf[i] = b.FuelMassKilograms;
            _ispBuf[i] = b.SpecificImpulseSeconds;
            _finiteFuelBuf[i] = (byte)(b.UnlimitedPropellant ? 0 : 1);

            // Raw commanded thrust from AttitudeController / ThrustController
            _baseThrustBuf[i] = b.state.force;

            var att = i < _satAttitudeCache.Count ? _satAttitudeCache[i] : null;

            if (burn.TargetBody == b)
                nodeTargetIndex = i;

            if (att != null && att.mode == AttitudeController.PointingMode.Normal)
            {
                _baseNormalSignBuf[i] = +1;   // Normal
            }
            else if (att != null && att.mode == AttitudeController.PointingMode.AntiNormal)
            {
                _baseNormalSignBuf[i] = -1;   // AntiNormal
            }
            else
            {
                _baseNormalSignBuf[i] = 0;    // Free thrust: native uses thrust vector as-is
            }

            // any nonzero thrust
            bool thrusting = _baseThrustBuf[i].sqrMagnitude > 1e-12f;
            _isThrustingBuf[i] = thrusting ? (byte)1 : (byte)0;

            if (thrusting)
                ctx?.ConstellationRegistry?.MarkDepartedIdeal(b);

            // going to ignore latched parity in native now, keep it cleared.
            if (_latchedParityBuf != null)
                _latchedParityBuf[i] = 0;

            // Drag inputs
            double3 relativeToCentral = b.state.position - centerD;
            _cdBuf[i] = math.lengthsq(relativeToCentral) > dragCutoffRadiusSq
                ? 0f
                : (float)b.dragCoefficient;

            _areaBuf[i] = (float)b.DragAreaSquareUnits;
        }

        IntegrateStepChunks(
            n,
            burn,
            nodeTargetIndex,
            center,
            simulationTime,
            stepDt
        );

        // Write back & sync
        for (int i = 0; i < n; i++)
        {
            var b = _satCache[i];
            if (b == null) continue;
            double3 previousPosition = b.state.position;
            b.state.position = _posBuf[i];
            b.state.velocity = _velBuf[i];

            b.SyncAfterBatch(previousPosition);
            runtime?.CheckPostStepRemoval(b);
        }

        ctx?.BodyRuntimeCoordinator?.AdvanceSimulation(stepDt);
        ctx?.BodyRuntimeCoordinator?.FlushPendingRemovals();
    }

    private void IntegrateStepChunks(
        int n,
        ScheduledBurn burn,
        int nodeTargetIndex,
        Vector3 center,
        float stepStartTime,
        float stepDt)
    {
        float cursor = stepStartTime;
        float stepEnd = stepStartTime + stepDt;

        if (!burn.IsValid || nodeTargetIndex < 0 || nodeTargetIndex >= n)
        {
            IntegrateChunk(n, stepDt, default, -1, center);
            return;
        }

        float burnStart = Mathf.Clamp(burn.StartTime, stepStartTime, stepEnd);
        float burnEnd = Mathf.Clamp(burn.EndTime, stepStartTime, stepEnd);

        if (cursor < burnStart)
        {
            IntegrateChunk(n, burnStart - cursor, default, -1, center);
            cursor = burnStart;
        }

        if (burnEnd > cursor)
        {
            IntegrateBurnWindow(n, burnEnd - cursor, burn, nodeTargetIndex, center);
            cursor = burnEnd;
        }

        if (stepEnd > cursor)
            IntegrateChunk(n, stepEnd - cursor, default, -1, center);
    }

    private void IntegrateBurnWindow(
        int n,
        float burnDt,
        ScheduledBurn burn,
        int nodeTargetIndex,
        Vector3 center)
    {
        float remaining = burnDt;
        const float burnDirectionUpdateDt = BodyRuntimeCoordinator.BaseSimulationStep;

        while (remaining > 1e-6f)
        {
            float chunkDt = Mathf.Min(burnDirectionUpdateDt, remaining);
            IntegrateChunk(n, chunkDt, burn, nodeTargetIndex, center);
            remaining -= chunkDt;
        }
    }

    private void IntegrateChunk(
        int n,
        float chunkDt,
        ScheduledBurn burn,
        int nodeTargetIndex,
        Vector3 center)
    {
        if (chunkDt <= 0f)
            return;

        Array.Copy(_baseThrustBuf, _thrustBuf, n);
        Array.Copy(_baseNormalSignBuf, _normalSignBuf, n);

        if (burn.IsValid && nodeTargetIndex >= 0 && _satCache[nodeTargetIndex].HasUsableThrust)
        {
            Vector3 burnPos = _posBuf[nodeTargetIndex].ToVector3();
            Vector3 burnVel = _velBuf[nodeTargetIndex].ToVector3();

            if (burn.TryBuildCommand(
                    burnPos,
                    burnVel,
                    center,
                    ref _nodeBurnVCache,
                    ref _nodeBurnHCache,
                    out Vector3 nodeBurnForce,
                    out sbyte nodeBurnNormalSign))
            {
                _thrustBuf[nodeTargetIndex] += nodeBurnForce;
                _normalSignBuf[nodeTargetIndex] = nodeBurnNormalSign;
                ctx?.ConstellationRegistry?.MarkDepartedIdeal(_satCache[nodeTargetIndex]);
            }
        }

        for (int i = 0; i < n; i++)
            _isThrustingBuf[i] = _thrustBuf[i].sqrMagnitude > 1e-12f ? (byte)1 : (byte)0;

        const float dtMax = BodyRuntimeCoordinator.BaseSimulationStep;
        int substeps = Mathf.Max(1, Mathf.CeilToInt(chunkDt / dtMax));

        NativePhysics.BatchTwoBodyIntegrateMuExWithFuel(
            _posBuf, _velBuf, _massBuf, _thrustBuf,
            _cdBuf, _areaBuf, _normalSignBuf,
            _isThrustingBuf, _latchedParityBuf,
            n, _muUnity, chunkDt, substeps, _deltaVBuf,
            _dryMassBuf, _fuelMassBuf, _ispBuf, _finiteFuelBuf
        );

        // Native telemetry integrates only applied thrust acceleration (world units/s).
        // Keep the existing UI contract of km/s, with double precision across long runs.
        for (int i = 0; i < n; i++)
        {
            NBody body = _satCache[i];
            if (body != null)
            {
                body.CommitFuelFromPhysics(_fuelMassBuf[i]);
                _massBuf[i] = body.TotalMassKilograms;
                if (!body.UnlimitedPropellant && body.FuelMassKilograms <= 0.0)
                    ctx?.ThrustController?.StopThrustForBody(body);
            }
            double deltaV = _deltaVBuf[i];
            if (body != null && deltaV > 0.0 && double.IsFinite(deltaV))
                body.cumulativeDeltaVUsed += deltaV * (double)SimulationUnits.KilometersPerUnit;
        }
    }

    private void RebuildSatelliteCache()
    {
        _satCache.Clear();
        _satAttitudeCache.Clear();

        for (int i = 0; i < bodyService.Bodies.Count; i++)
        {
            var b = bodyService.Bodies[i];
            if (b && !b.isCentralBody)
            {
                _satCache.Add(b);
                _satAttitudeCache.Add(b.GetComponent<AttitudeController>());
            }
        }

        EnsureArraysForN(_satCache.Count);
    }

    private void UpdateMu()
    {
        double m = (_central != null) ? _central.TotalMassKilograms : 5.972e24; // fallback Earth
        _muUnity = G_unity * m;
    }
}
