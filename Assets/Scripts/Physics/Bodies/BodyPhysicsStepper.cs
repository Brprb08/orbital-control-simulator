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
    private double3[] _startPosBuf;
    private double3[] _startVelBuf;
    private readonly List<SweptBounds> _sweeps = new();
    private readonly List<(int A, int B, double Fraction, double3 Position)> _contacts = new();
    private double integrationTime;
    // Geometric tolerance for subdividing one native integration step (0.1 mm).
    private const double ContactTolerance = 1e-8;

    private readonly struct SweptBounds
    {
        public readonly int Index;
        public readonly double3 Min, Max;
        public SweptBounds(int index, double3 min, double3 max)
        { Index = index; Min = min; Max = max; }
    }
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
                RotateCentralBodyVisual(body, stepDt);
        }

        // A large warp tick can cross more than one queued burn. End the current batch at
        // each burn end so lifecycle completion selects the next command before continuing.
        double remaining = stepDt;
        while (remaining > 0f)
        {
            ScheduledBurn burn = ctx?.ThrustController != null
                ? ctx.ThrustController.PrepareSimulationStep(_satCache, _satAttitudeCache, (float)remaining)
                : default;
            float chunk = (float)remaining;
            if (burn.IsValid && ctx?.ManeuverNodeManager != null && ctx.ManeuverNodeManager.HasRendezvousPlan)
            {
                double untilEnd = burn.EndTime - ctx.BodyRuntimeCoordinator.SimulationTimeSeconds;
                if (untilEnd > 0) chunk = (float)Math.Min(chunk, untilEnd);
            }
            StepAllBodiesBatch(chunk, burn);
            remaining -= chunk;
            if (_satCache.Count == 0) break;
        }
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

    private static void RotateCentralBodyVisual(NBody body, float stepDt)
    {
        float deltaAngle = -EarthRotationRate * stepDt;
        body.transform.Rotate(Vector3.up, deltaAngle);
    }

    private void EnsureArraysForN(int n)
    {
        void Alloc<T>(ref T[] arr, int len) { if (arr == null || arr.Length != len) arr = (len > 0) ? new T[len] : Array.Empty<T>(); }

        Alloc(ref _posBuf, n);
        Alloc(ref _velBuf, n);
        Alloc(ref _startPosBuf, n);
        Alloc(ref _startVelBuf, n);
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
        double simulationTime = runtime != null ? runtime.SimulationTimeSeconds : 0;

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

        integrationTime = simulationTime;

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

    private void CheckSatelliteCollisions(BodyRuntimeCoordinator runtime, double stepDt)
    {
        if (runtime == null || _satCache.Count < 2) return;
        _sweeps.Clear();
        _contacts.Clear();
        for (int i = 0; i < _satCache.Count; i++)
        {
            NBody body = _satCache[i];
            if (body == null || !body.isActiveAndEnabled || body.isReferenceOrbit ||
                runtime.IsPendingRemoval(body) || !(body.radius > 0) ||
                !math.all(math.isfinite(_startPosBuf[i])) || !math.all(math.isfinite(_posBuf[i])) ||
                !math.all(math.isfinite(_startVelBuf[i])) || !math.all(math.isfinite(_velBuf[i]))) continue;
            double3 p0 = _startPosBuf[i], p1 = _posBuf[i];
            double3 c1 = p0 + _startVelBuf[i] * (stepDt / 3);
            double3 c2 = p1 - _velBuf[i] * (stepDt / 3);
            double3 radius = new double3(body.radius);
            _sweeps.Add(new SweptBounds(i, math.min(math.min(p0, p1), math.min(c1, c2)) - radius,
                math.max(math.max(p0, p1), math.max(c1, c2)) + radius));
        }
        // Cubic Hermite motion lies inside these Bezier control-point bounds.
        // Sorting one axis avoids checking distant satellite pairs at each physics step.
        _sweeps.Sort((a, b) => a.Min.x.CompareTo(b.Min.x));
        for (int i = 0; i < _sweeps.Count; i++)
        {
            SweptBounds a = _sweeps[i];
            NBody bodyA = _satCache[a.Index];
            for (int j = i + 1; j < _sweeps.Count && _sweeps[j].Min.x <= a.Max.x; j++)
            {
                SweptBounds b = _sweeps[j];
                NBody bodyB = _satCache[b.Index];
                if (runtime.IsPendingRemoval(bodyB) ||
                    b.Min.y > a.Max.y || b.Max.y < a.Min.y ||
                    b.Min.z > a.Max.z || b.Max.z < a.Min.z) continue;
                double radius = bodyA.radius + bodyB.radius;
                if (TrySweptContact(a.Index, b.Index, stepDt, radius, out double fraction, out double3 position))
                    _contacts.Add((a.Index, b.Index, fraction, position));
            }
        }
        // A body can participate in several candidate pairs. Resolve its earliest
        // contact before deciding which later pairs still exist.
        _contacts.Sort((x, y) => {
            int time = x.Fraction.CompareTo(y.Fraction);
            if (time != 0) return time;
            int a = x.A.CompareTo(y.A);
            return a != 0 ? a : x.B.CompareTo(y.B);
        });
        foreach (var contact in _contacts)
            runtime.HandleSatelliteCollision(_satCache[contact.A], _satCache[contact.B], contact.Position);
    }

    private bool TrySweptContact(int a, int b, double duration, double radius,
        out double fraction, out double3 position)
    {
        double3 r0 = _startPosBuf[a] - _startPosBuf[b], r3 = _posBuf[a] - _posBuf[b];
        double3 r1 = r0 + (_startVelBuf[a] - _startVelBuf[b]) * (duration / 3);
        double3 r2 = r3 - (_velBuf[a] - _velBuf[b]) * (duration / 3);
        bool hit = ContactOnCurve(r0, r1, r2, r3, radius, 0, 1, out fraction);
        position = hit ? Hermite(_startPosBuf[a], _startVelBuf[a], _posBuf[a], _velBuf[a], duration, fraction) : default;
        return hit;
    }

    private static bool ContactOnCurve(double3 p0, double3 p1, double3 p2, double3 p3,
        double radius, double begin, double end, out double fraction)
    {
        fraction = 0;
        double3 min = math.min(math.min(p0, p1), math.min(p2, p3));
        double3 max = math.max(math.max(p0, p1), math.max(p2, p3));
        if (math.any(min > radius) || math.any(max < -radius)) return false;
        double error = Math.Max(math.distance(p1, math.lerp(p0, p3, 1.0 / 3)),
            math.distance(p2, math.lerp(p0, p3, 2.0 / 3)));
        if (error > ContactTolerance && end - begin > 1e-12)
        {
            double3 a = (p0 + p1) / 2, b = (p1 + p2) / 2, c = (p2 + p3) / 2;
            double3 d = (a + b) / 2, e = (b + c) / 2, middle = (d + e) / 2;
            double time = (begin + end) / 2;
            return ContactOnCurve(p0, a, d, middle, radius, begin, time, out fraction) ||
                ContactOnCurve(middle, e, c, p3, radius, time, end, out fraction);
        }
        // Entry root, not the time of closest approach. The padding bounds the
        // curve-to-chord error and is at most 0.1 mm per native step.
        double3 travel = p3 - p0;
        double c0 = math.lengthsq(p0) - (radius + error) * (radius + error);
        if (c0 <= 0) { fraction = begin; return true; }
        double a0 = math.lengthsq(travel), b0 = math.dot(p0, travel);
        double discriminant = b0 * b0 - a0 * c0;
        if (!(a0 > 0) || b0 >= 0 || discriminant < 0) return false;
        double t = c0 / (-b0 + Math.Sqrt(discriminant));
        if (t < 0 || t > 1) return false;
        fraction = begin + (end - begin) * t;
        return true;
    }

    // Conservative synchronous broad phase: gravity and thrust bound departure
    // from the straight relative path. Drag cannot accelerate a craft beyond
    // the atmospheric wind speed; include that displacement when reachable.
    private bool MayContact(double duration)
    {
        var runtime = ctx?.BodyRuntimeCoordinator;
        if (runtime == null || _satCache.Count < 2) return false;
        _sweeps.Clear();
        double earthRadius = _central != null ? _central.radius : PhysicsConstants.EarthRadiusUnits;
        double gravity = _muUnity / (earthRadius * earthRadius);
        for (int i = 0; i < _satCache.Count; i++)
        {
            var body = _satCache[i];
            if (body == null || !body.isActiveAndEnabled || body.isReferenceOrbit || runtime.IsPendingRemoval(body)) continue;
            double accel = gravity + (body.EffectiveThrustNewtons / 10000.0 + _baseThrustBuf[i].magnitude) /
                Math.Max(1e-9, body.DryMassKilograms);
            double padding = .5 * accel * duration * duration;
            if (math.length(_posBuf[i]) - math.length(_velBuf[i]) * duration - padding <= earthRadius + AtmosphericDragCutoffUnits)
                padding += (math.length(_velBuf[i]) + .1) * duration;
            double3 end = _posBuf[i] + _velBuf[i] * duration;
            double3 extent = new double3(body.radius + padding);
            _sweeps.Add(new SweptBounds(i, math.min(_posBuf[i], end) - extent, math.max(_posBuf[i], end) + extent));
        }
        _sweeps.Sort((a, b) => a.Min.x.CompareTo(b.Min.x));
        for (int i = 0; i < _sweeps.Count; i++)
        {
            var a = _sweeps[i];
            for (int j = i + 1; j < _sweeps.Count && _sweeps[j].Min.x <= a.Max.x; j++)
            {
                var b = _sweeps[j];
                if (b.Min.y <= a.Max.y && b.Max.y >= a.Min.y && b.Min.z <= a.Max.z && b.Max.z >= a.Min.z) return true;
            }
        }
        return false;
    }

    private static double3 Hermite(double3 p0, double3 v0, double3 p1, double3 v1,
        double duration, double t)
    {
        double t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * duration * v0 +
            (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * duration * v1;
    }

    private void IntegrateStepChunks(
        int n,
        ScheduledBurn burn,
        int nodeTargetIndex,
        Vector3 center,
        double stepStartTime,
        float stepDt)
    {
        double cursor = stepStartTime;
        double stepEnd = stepStartTime + stepDt;

        if (!burn.IsValid || nodeTargetIndex < 0 || nodeTargetIndex >= n)
        {
            IntegrateChunk(n, stepDt, default, -1, center);
            return;
        }

        double burnStart = Math.Clamp(burn.StartTime, stepStartTime, stepEnd);
        double burnEnd = Math.Clamp(burn.EndTime, stepStartTime, stepEnd);

        if (cursor < burnStart)
        {
            IntegrateChunk(n, (float)(burnStart - cursor), default, -1, center);
            cursor = burnStart;
        }

        if (burnEnd > cursor)
        {
            IntegrateBurnWindow(n, (float)(burnEnd - cursor), burn, nodeTargetIndex, center);
            cursor = burnEnd;
        }

        if (stepEnd > cursor)
            IntegrateChunk(n, (float)(stepEnd - cursor), default, -1, center);
    }

    private void IntegrateBurnWindow(
        int n,
        float burnDt,
        ScheduledBurn burn,
        int nodeTargetIndex,
        Vector3 center)
    {
        double remaining = burnDt;
        const float burnDirectionUpdateDt = BodyRuntimeCoordinator.BaseSimulationStep;

        while (remaining > 1e-6f)
        {
            float chunkDt = (float)Math.Min(burnDirectionUpdateDt, remaining);
            IntegrateChunk(n, chunkDt, burn, nodeTargetIndex, center);
            remaining -= chunkDt;
        }
    }

    private void IntegrateChunk(int n, float chunkDt, ScheduledBurn burn, int nodeTargetIndex, Vector3 center)
    {
        double remaining = chunkDt;
        var manager = ctx?.ManeuverNodeManager;
        while (remaining > 0)
        {
            manager?.ValidateRendezvousFlight(integrationTime, _satCache, _posBuf, _velBuf);
            double slice = remaining;
            if (manager != null && manager.HasRendezvousPlan)
            {
                slice = Math.Min(slice, 1.0);
                double toArrival = manager.RendezvousArrivalTime - integrationTime;
                if (toArrival > 0) slice = Math.Min(slice, toArrival);
            }
            bool contact = MayContact(slice);
            while (contact && slice > BodyRuntimeCoordinator.BaseSimulationStep)
            {
                slice = Math.Max(BodyRuntimeCoordinator.BaseSimulationStep, slice * .5);
                contact = MayContact(slice);
            }
            float dt = (float)Math.Min(slice, remaining);
            if (!(dt > 0)) break;
            Array.Copy(_posBuf, _startPosBuf, n);
            Array.Copy(_velBuf, _startVelBuf, n);
            // Cancellation during this batch must invalidate its cached command.
            var command = burn.IsValid && manager != null && manager.CurrentNode == burn.SourceNode ? burn : default;
            IntegrateSlice(n, dt, command, nodeTargetIndex, center);
            if (contact) CheckSatelliteCollisions(ctx?.BodyRuntimeCoordinator, dt);
            integrationTime += dt;
            remaining -= dt;
            manager?.ValidateRendezvousFlight(integrationTime, _satCache, _posBuf, _velBuf);
        }
    }

    private void IntegrateSlice(
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

        if (burn.IsValid && nodeTargetIndex >= 0 && _satCache[nodeTargetIndex].HasUsableThrust &&
            !ctx.BodyRuntimeCoordinator.IsPendingRemoval(_satCache[nodeTargetIndex]))
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
