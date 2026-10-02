using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

public readonly struct TrajectoryMatchedPredictionWorkItem
{
    public double3 StartPosition { get; }
    public double3 StartVelocity { get; }
    public double Mass { get; }
    public float DragCoefficient { get; }
    public float CrossSectionArea { get; }
    public double Mu { get; }
    public int Steps { get; }
    public float DeltaTime { get; }
    public int MaxOutputPoints { get; }

    public TrajectoryMatchedPredictionWorkItem(
        double3 startPosition,
        double3 startVelocity,
        double mass,
        float dragCoefficient,
        float crossSectionArea,
        double mu,
        int steps,
        float deltaTime,
        int maxOutputPoints)
    {
        StartPosition = startPosition;
        StartVelocity = startVelocity;
        Mass = mass;
        DragCoefficient = dragCoefficient;
        CrossSectionArea = crossSectionArea;
        Mu = mu;
        Steps = Mathf.Max(1, steps);
        DeltaTime = Mathf.Max(1e-5f, deltaTime);
        MaxOutputPoints = Mathf.Max(2, maxOutputPoints);
    }
}

public readonly struct TrajectoryMatchedPredictionResult
{
    public Vector3[] Points { get; }
    public float SampleDeltaTime { get; }

    public TrajectoryMatchedPredictionResult(Vector3[] points, float sampleDeltaTime)
    {
        Points = points ?? Array.Empty<Vector3>();
        SampleDeltaTime = Mathf.Max(0f, sampleDeltaTime);
    }
}

public static class TrajectoryMatchedPredictor
{
    public const double MaxEncounterHorizonSeconds = 172800;

    // Immutable main-thread snapshot; the worker never reads a ManeuverNode or NBody.
    public readonly struct EncounterBurn
    {
        public readonly bool Enabled, FiniteFuel;
        public readonly double Start, Duration, DryMass, FuelMass, Isp;
        public readonly float Thrust;
        public readonly BurnType Type;
        public readonly bool UsesVectorDirection;
        public readonly double3 VectorDirectionWorld;
        public double End => Start + Duration;
        public EncounterBurn(double start, double duration, BurnType type, float thrust,
            double dryMass, double fuelMass, double isp, bool finiteFuel)
        {
            Enabled = true; Start = start; Duration = duration; Type = type; Thrust = thrust;
            DryMass = dryMass; FuelMass = fuelMass; Isp = isp; FiniteFuel = finiteFuel;
            UsesVectorDirection = false; VectorDirectionWorld = double3.zero;
        }
        public EncounterBurn(double start, double duration, double3 vectorDirectionWorld, float thrust,
            double dryMass, double fuelMass, double isp, bool finiteFuel)
        {
            Enabled = true; Start = start; Duration = duration; Type = BurnType.Prograde; Thrust = thrust;
            DryMass = dryMass; FuelMass = fuelMass; Isp = isp; FiniteFuel = finiteFuel;
            UsesVectorDirection = true; VectorDirectionWorld = math.normalizesafe(vectorDirectionWorld, new double3(1, 0, 0));
        }
    }

    public readonly struct EncounterSample
    {
        public readonly double Time;
        public readonly double3 A, B, VA, VB;
        public EncounterSample(double time, double3 a, double3 b, double3 va, double3 vb)
        { Time = time; A = a; B = b; VA = va; VB = vb; }
        public Encounter AsEncounter => new Encounter(Time, A, B, VA, VB);
    }

    public readonly struct PropagatedPairState
    {
        public readonly double Time;
        public readonly double3 A, B, VA, VB;

        public PropagatedPairState(
            double time,
            double3 a,
            double3 b,
            double3 va,
            double3 vb)
        {
            Time = time;
            A = a;
            B = b;
            VA = va;
            VB = vb;
        }
    }

    public static bool TryPropagatePairToTime(
    TrajectoryMatchedPredictionWorkItem a,
    TrajectoryMatchedPredictionWorkItem b,
    double targetSeconds,
    CancellationToken cancellation,
    out PropagatedPairState state,
    EncounterBurn[] sequence = null,
    float maxCoastStepDt = MaxNativeStepDt)
    {
        state = default;

        if (!double.IsFinite(targetSeconds) ||
            targetSeconds <= 0 ||
            targetSeconds > MaxEncounterHorizonSeconds ||
            !ValidEncounterBody(a) ||
            !ValidEncounterBody(b) ||
            a.Mu != b.Mu)
            return false;

        // Never allow the search-specific coast step to be more accurate
        // than the normal native step, or absurdly large.
        float coastStepDt = math.clamp(
            maxCoastStepDt,
            MaxNativeStepDt,
            2.0f);

        EncounterBurn burn = default;
        int burnIndex = 0;

        if (sequence != null && sequence.Length > 0)
        {
            burn = sequence[0];

            for (int i = 0; i < sequence.Length; i++)
            {
                if (!sequence[i].Enabled ||
                    !double.IsFinite(sequence[i].Start) ||
                    !double.IsFinite(sequence[i].Duration) ||
                    sequence[i].Duration <= 0 ||
                    sequence[i].Thrust != burn.Thrust ||
                    sequence[i].Isp != burn.Isp ||
                    sequence[i].DryMass != burn.DryMass ||
                    sequence[i].FiniteFuel != burn.FiniteFuel ||
                    (i > 0 && sequence[i].Start < sequence[i - 1].End))
                    return false;
            }
        }

        double3[] p =
        {
        a.StartPosition,
        b.StartPosition
    };

        double3[] v =
        {
        a.StartVelocity,
        b.StartVelocity
    };

        double[] masses =
        {
        a.Mass,
        b.Mass
    };

        float[] cd =
        {
        a.DragCoefficient,
        b.DragCoefficient
    };

        float[] areas =
        {
        a.CrossSectionArea,
        b.CrossSectionArea
    };

        var thrust = new Vector3[2];
        var signs = new sbyte[2];
        var burning = new byte[2];
        var parity = new sbyte[2];

        double[] dry =
        {
        burn.Enabled ? burn.DryMass : a.Mass,
        b.Mass
    };

        double[] fuel =
        {
        burn.Enabled ? burn.FuelMass : 0,
        0
    };

        double[] isp =
        {
        burn.Enabled ? burn.Isp : 1,
        1
    };

        byte[] finite =
        {
        (byte)(burn.Enabled && burn.FiniteFuel ? 1 : 0),
        0
    };

        var deltaV = new double[2];

        Vector3 velocityCache = Vector3.right;
        Vector3 normalCache = Vector3.up;

        bool ValidState()
        {
            for (int i = 0; i < 2; i++)
            {
                if (!math.all(math.isfinite(p[i])) ||
                    !math.all(math.isfinite(v[i])) ||
                    math.length(p[i]) <= PhysicsConstants.EarthRadiusUnits)
                    return false;
            }

            return true;
        }

        if (!ValidState())
            return false;

        double time = 0;

        while (time < targetSeconds)
        {
            cancellation.ThrowIfCancellationRequested();

            // Jump directly to the next burn boundary or final time.
            double step = Math.Min(30, targetSeconds - time);

            if (burn.Enabled)
            {
                if (time < burn.Start)
                    step = Math.Min(step, burn.Start - time);
                else if (time < burn.End)
                    step = Math.Min(step, burn.End - time);
            }

            if (step <= 1e-10)
            {
                if (sequence != null &&
                    burnIndex + 1 < sequence.Length &&
                    time >= burn.End - 1e-9)
                {
                    burn = sequence[++burnIndex];
                    continue;
                }

                step = Math.Min(
                    1e-4,
                    targetSeconds - time);

                if (step <= 0)
                    break;
            }

            bool insideBurn =
                burn.Enabled &&
                time >= burn.Start - 1e-9 &&
                time < burn.End - 1e-9;

            if (insideBurn)
            {
                // A vector burn has a fixed world-space command. Batch its
                // native substeps instead of marshaling the same command 50
                // times per simulated second. Orbital directions still refresh
                // every 0.02 s, and all native integration steps stay <= 0.02 s.
                double remaining = step;

                while (remaining > 1e-10)
                {
                    cancellation.ThrowIfCancellationRequested();

                    float dt =
                        (float)Math.Min(
                            burn.UsesVectorDirection ? .5f : MaxNativeStepDt,
                            remaining);

                    ManeuverBurnMath.TryBuildBurnCommand(
                        burn.Type, burn.UsesVectorDirection, new Vector3((float)burn.VectorDirectionWorld.x,
                            (float)burn.VectorDirectionWorld.y, (float)burn.VectorDirectionWorld.z),
                        new Vector3(
                            (float)p[0].x,
                            (float)p[0].y,
                            (float)p[0].z),
                        new Vector3(
                            (float)v[0].x,
                            (float)v[0].y,
                            (float)v[0].z),
                        Vector3.zero,
                        burn.Thrust,
                        ref velocityCache,
                        ref normalCache,
                        out thrust[0],
                        out signs[0]);

                    burning[0] =
                        thrust[0].sqrMagnitude > 0
                            ? (byte)1
                            : (byte)0;

                    NativePhysics.BatchTwoBodyIntegrateMuExWithFuel(
                        p,
                        v,
                        masses,
                        thrust,
                        cd,
                        areas,
                        signs,
                        burning,
                        parity,
                        2,
                        a.Mu,
                        dt,
                        Math.Max(1, (int)Math.Ceiling(dt / MaxNativeStepDt)),
                        deltaV,
                        dry,
                        fuel,
                        isp,
                        finite);

                    masses[0] =
                        dry[0] + fuel[0];

                    remaining -= dt;
                }
            }
            else
            {
                // Coast:
                // this is where we're allowed to reduce search accuracy.
                thrust[0] = Vector3.zero;
                signs[0] = 0;
                burning[0] = 0;

                int substeps = Math.Max(
                    1,
                    (int)Math.Ceiling(
                        step / coastStepDt));

                NativePhysics.BatchTwoBodyIntegrateMuEx(
                    p,
                    v,
                    masses,
                    thrust,
                    cd,
                    areas,
                    signs,
                    burning,
                    parity,
                    2,
                    a.Mu,
                    (float)step,
                    substeps);
            }

            time += step;

            if (!ValidState())
                return false;

            if (sequence != null &&
                burnIndex + 1 < sequence.Length &&
                time >= burn.End - 1e-9)
            {
                burn =
                    sequence[++burnIndex];
            }
        }

        state = new PropagatedPairState(
            targetSeconds,
            p[0],
            p[1],
            v[0],
            v[1]);

        return true;
    }

    /// <summary>A fixed-epoch timeline. Querying remaining passes needs no propagation.</summary>
    public sealed class EncounterTimeline
    {
        private readonly EncounterSample[] samples;
        private readonly Encounter[] minima;
        public string Error { get; }
        public string Notice { get; }
        public double Horizon => samples == null || samples.Length == 0 ? 0 : samples[samples.Length - 1].Time;
        public bool IsValid => Error == null && samples != null && samples.Length > 1;

        public EncounterTimeline(string error) { Error = error; }
        internal EncounterTimeline(List<EncounterSample> samples, List<Encounter> minima, string notice)
        { this.samples = samples.ToArray(); this.minima = minima.ToArray(); Notice = notice; }

        public bool TrySample(double seconds, out EncounterSample sample)
        {
            sample = default;
            if (!IsValid || !double.IsFinite(seconds) || seconds < 0 || seconds > Horizon) return false;
            int lo = 0, hi = samples.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (samples[mid].Time <= seconds) lo = mid; else hi = mid;
            }
            var a = samples[lo]; var b = samples[hi];
            double dt = b.Time - a.Time, t = (seconds - a.Time) / dt;
            Interpolate(a.A, a.VA, b.A, b.VA, dt, t, out var pA, out var vA);
            Interpolate(a.B, a.VB, b.B, b.VB, dt, t, out var pB, out var vB);
            sample = new EncounterSample(seconds, pA, pB, vA, vB);
            return true;
        }

        private static void Interpolate(double3 p0, double3 v0, double3 p1, double3 v1,
            double dt, double t, out double3 p, out double3 v)
        {
            double t2 = t * t, t3 = t2 * t;
            p = (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * dt * v0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * dt * v1;
            v = ((6 * t2 - 6 * t) * p0 + (3 * t2 - 4 * t + 1) * dt * v0 + (-6 * t2 + 6 * t) * p1 + (3 * t2 - 2 * t) * dt * v1) / dt;
        }

        public bool TryGetClosest(double seconds, out Encounter best, double tieToleranceMeters = 0.1)
        {
            best = default;
            if (!TrySample(seconds, out var current)) return false;
            best = current.AsEncounter;
            // Prefer the earlier pass for effectively equal distances, avoiding numerical hopping.
            foreach (var candidate in minima)
                if (candidate.Seconds >= seconds && candidate.DistanceMeters < best.DistanceMeters - tieToleranceMeters)
                    best = candidate;
            var end = samples[samples.Length - 1].AsEncounter;
            if (end.DistanceMeters < best.DistanceMeters - tieToleranceMeters) best = end;
            return true;
        }

        // Check every interpolated segment, including segments whose endpoint
        // range rates do not bracket a minimum. Ambiguous grazing paths fail closed.
        public bool MaintainsClearance(double meters)
        {
            if (!IsValid || !(meters >= 0)) return false;
            double radius = meters / SimulationUnits.MetersPerUnit;
            for (int i = 1; i < samples.Length; i++)
            {
                var a = samples[i - 1]; var b = samples[i];
                double dt = b.Time - a.Time;
                double3 p0 = a.A - a.B, p3 = b.A - b.B;
                if (!ClearSegment(p0, p0 + (a.VA - a.VB) * (dt / 3),
                    p3 - (b.VA - b.VB) * (dt / 3), p3, radius, 0)) return false;
            }
            return true;
        }

        private static bool ClearSegment(double3 p0, double3 p1, double3 p2, double3 p3,
            double radius, int depth)
        {
            double3 min = math.min(math.min(p0, p1), math.min(p2, p3));
            double3 max = math.max(math.max(p0, p1), math.max(p2, p3));
            if (!math.all(math.isfinite(min)) || !math.all(math.isfinite(max))) return false;
            if (math.lengthsq(math.max(min, math.min(max, double3.zero))) > radius * radius) return true;
            if (math.lengthsq(p0) < radius * radius || math.lengthsq(p3) < radius * radius) return false;
            double error = Math.Max(math.distance(p1, math.lerp(p0, p3, 1.0 / 3)),
                math.distance(p2, math.lerp(p0, p3, 2.0 / 3)));
            double3 travel = p3 - p0;
            double travelSq = math.lengthsq(travel);
            double t = travelSq > 0 ? math.clamp(-math.dot(p0, travel) / travelSq, 0, 1) : 0;
            if (math.length(p0 + t * travel) > radius + error) return true;
            if (error <= 1e-8 || depth >= 32) return false;
            double3 a = (p0 + p1) / 2, b = (p1 + p2) / 2, c = (p2 + p3) / 2;
            double3 d = (a + b) / 2, e = (b + c) / 2, middle = (d + e) / 2;
            return ClearSegment(p0, a, d, middle, radius, depth + 1) &&
                ClearSegment(middle, e, c, p3, radius, depth + 1);
        }
    }

    public readonly struct Encounter
    {
        public readonly double Seconds, DistanceMeters, RelativeSpeedMetersPerSecond;
        public readonly double3 ControlledPosition, TargetPosition;
        public readonly string Error;
        public bool IsValid => Error == null;

        public Encounter(double seconds, double3 a, double3 b, double3 va, double3 vb)
        {
            Seconds = seconds;
            ControlledPosition = a;
            TargetPosition = b;
            DistanceMeters = math.distance(a, b) * SimulationUnits.MetersPerUnit;
            RelativeSpeedMetersPerSecond = math.distance(va, vb) * SimulationUnits.MetersPerUnit;
            Error = null;
        }

        public Encounter(string error)
        {
            this = default;
            Error = error;
        }
    }

    /// <summary>
    /// Paired coast prediction from one epoch. Operates only on copied values, never live bodies.
    /// Scans a bounded window and refines approaching-to-separating transitions using the
    /// same native integrator. Endpoint minima are retained rather than inventing an encounter.
    /// </summary>
    public static Encounter PredictClosestApproach(TrajectoryMatchedPredictionWorkItem a,
        TrajectoryMatchedPredictionWorkItem b, double horizonSeconds, CancellationToken cancellation)
    {
        var timeline = PredictEncounterTimeline(a, b, horizonSeconds, cancellation);
        return timeline.TryGetClosest(0, out var result) ? result : new Encounter(timeline.Error ?? "No prediction coverage.");
    }

    public static EncounterTimeline PredictEncounterTimeline(TrajectoryMatchedPredictionWorkItem a,
        TrajectoryMatchedPredictionWorkItem b, double horizonSeconds, CancellationToken cancellation,
        EncounterBurn burn = default, bool extendForBurnOrbit = false, EncounterBurn[] sequence = null)
    {
        int burnIndex = 0;
        if (sequence != null && sequence.Length > 0)
        {
            // Snapshot the schedule; all burns share the same engine and fuel reservoir.
            sequence = (EncounterBurn[])sequence.Clone();
            burn = sequence[0];
            for (int i = 0; i < sequence.Length; i++)
                if (!sequence[i].Enabled || !double.IsFinite(sequence[i].Start) ||
                    !double.IsFinite(sequence[i].Duration) || sequence[i].Duration <= 0 ||
                    sequence[i].Thrust != burn.Thrust || sequence[i].Isp != burn.Isp ||
                    sequence[i].DryMass != burn.DryMass || sequence[i].FiniteFuel != burn.FiniteFuel ||
                    (i > 0 && sequence[i].Start < sequence[i - 1].End))
                    return new EncounterTimeline("Invalid or overlapping maneuver sequence.");
        }
        if (!double.IsFinite(horizonSeconds) || horizonSeconds < 60 || horizonSeconds > MaxEncounterHorizonSeconds ||
            !ValidEncounterBody(a) || !ValidEncounterBody(b) || a.Mu != b.Mu)
            return new EncounterTimeline("Invalid prediction state or window.");
        if (burn.Enabled && (!double.IsFinite(burn.Start) || !double.IsFinite(burn.Duration) || burn.Duration <= 0 ||
            !float.IsFinite(burn.Thrust) || burn.Thrust < 0 || !double.IsFinite(burn.DryMass) || burn.DryMass <= 0 ||
            !double.IsFinite(burn.FuelMass) || burn.FuelMass < 0 || !double.IsFinite(burn.Isp) || burn.Isp <= 0))
            return new EncounterTimeline("Invalid maneuver or propulsion configuration.");

        double3[] p = { a.StartPosition, b.StartPosition };
        double3[] v = { a.StartVelocity, b.StartVelocity };
        double[] masses = { a.Mass, b.Mass };
        float[] cd = { a.DragCoefficient, b.DragCoefficient };
        float[] areas = { a.CrossSectionArea, b.CrossSectionArea };
        var thrust = new Vector3[2];
        var signs = new sbyte[2];
        var burning = new byte[2];
        var parity = new sbyte[2];
        var startP = new double3[2];
        var startV = new double3[2];
        var refineP = new double3[2];
        var refineV = new double3[2];
        var dry = new double[] { burn.Enabled ? burn.DryMass : a.Mass, b.Mass };
        var fuel = new double[] { burn.Enabled ? burn.FuelMass : 0, 0 };
        var isp = new double[] { burn.Enabled ? burn.Isp : 1, 1 };
        var finite = new byte[] { (byte)(burn.Enabled && burn.FiniteFuel ? 1 : 0), 0 };
        var deltaV = new double[2];
        Vector3 velocityCache = Vector3.right, normalCache = Vector3.up;
        var samples = new List<EncounterSample>();
        var minima = new List<Encounter>();

        void Advance(double3[] positions, double3[] velocities, double seconds, double startTime)
        {
            cancellation.ThrowIfCancellationRequested();
            if (seconds <= 0) return;
            if (burn.Enabled && startTime < burn.End && startTime + seconds > burn.Start)
            {
                // The outer scan splits at start/end; refresh direction every live-physics step.
                double remaining = seconds;
                while (remaining > 1e-10)
                {
                    cancellation.ThrowIfCancellationRequested();
                    float dt = (float)Math.Min(MaxNativeStepDt, remaining);
                    ManeuverBurnMath.TryBuildBurnCommand(burn.Type, burn.UsesVectorDirection,
                        new Vector3((float)burn.VectorDirectionWorld.x, (float)burn.VectorDirectionWorld.y,
                            (float)burn.VectorDirectionWorld.z),
                        new Vector3((float)positions[0].x, (float)positions[0].y, (float)positions[0].z),
                        new Vector3((float)velocities[0].x, (float)velocities[0].y, (float)velocities[0].z),
                        Vector3.zero, burn.Thrust, ref velocityCache, ref normalCache, out thrust[0], out signs[0]);
                    burning[0] = thrust[0].sqrMagnitude > 0 ? (byte)1 : (byte)0;
                    NativePhysics.BatchTwoBodyIntegrateMuExWithFuel(positions, velocities, masses, thrust, cd, areas,
                        signs, burning, parity, 2, a.Mu, dt, 1, deltaV, dry, fuel, isp, finite);
                    masses[0] = dry[0] + fuel[0];
                    remaining -= dt;
                }
                return;
            }
            thrust[0] = Vector3.zero; signs[0] = 0; burning[0] = 0;
            NativePhysics.BatchTwoBodyIntegrateMuEx(positions, velocities, masses, thrust, cd, areas,
                signs, burning, parity, 2, a.Mu, (float)seconds,
                Math.Max(1, (int)Math.Ceiling(seconds / MaxNativeStepDt)));
        }

        bool AboveSurface(double3[] positions, double3[] velocities)
        {
            for (int i = 0; i < 2; i++)
                if (!math.all(math.isfinite(positions[i])) || !math.all(math.isfinite(velocities[i])) ||
                    math.length(positions[i]) <= PhysicsConstants.EarthRadiusUnits)
                    return false;
            return true;
        }

        if (!AboveSurface(p, v)) return new EncounterTimeline("Prediction starts at/below the surface or is invalid.");
        samples.Add(new EncounterSample(0, p[0], p[1], v[0], v[1]));
        for (double time = 0; time < horizonSeconds;)
        {
            cancellation.ThrowIfCancellationRequested();
            Array.Copy(p, startP, 2);
            Array.Copy(v, startV, 2);
            double step = Math.Min(5, horizonSeconds - time);
            if (burn.Enabled)
            {
                if (time < burn.Start) step = Math.Min(step, burn.Start - time);
                else if (time < burn.End) step = Math.Min(step, Math.Min(0.25, burn.End - time));
            }
            double startMass = masses[0], startFuel = fuel[0];
            sbyte startParity = parity[0];
            Vector3 startVC = velocityCache, startHC = normalCache;
            double before = math.dot(p[1] - p[0], v[1] - v[0]);
            Advance(p, v, step, time);
            if (!AboveSurface(p, v))
                return samples.Count > 1
                    ? new EncounterTimeline(samples, minima, "Coverage ends before surface contact or invalid propagation.")
                    : new EncounterTimeline("Prediction reaches the surface or becomes invalid.");

            samples.Add(new EncounterSample(time + step, p[0], p[1], v[0], v[1]));
            double after = math.dot(p[1] - p[0], v[1] - v[0]);
            if (before < 0 && after >= 0)
            {
                double endMass = masses[0], endFuel = fuel[0];
                sbyte endParity = parity[0];
                Vector3 endVC = velocityCache, endHC = normalCache;
                void ResetRefinement()
                {
                    Array.Copy(startP, refineP, 2); Array.Copy(startV, refineV, 2);
                    masses[0] = startMass; fuel[0] = startFuel; parity[0] = startParity;
                    velocityCache = startVC; normalCache = startHC;
                }
                double left = 0, right = step;
                for (int iteration = 0; iteration < 18; iteration++)
                {
                    double middle = (left + right) * 0.5;
                    ResetRefinement();
                    Advance(refineP, refineV, middle, time);
                    if (math.dot(refineP[1] - refineP[0], refineV[1] - refineV[0]) < 0)
                        left = middle;
                    else right = middle;
                }
                double offset = (left + right) * 0.5;
                ResetRefinement();
                Advance(refineP, refineV, offset, time);
                var candidate = new Encounter(time + offset, refineP[0], refineP[1], refineV[0], refineV[1]);
                minima.Add(candidate);
                masses[0] = endMass; fuel[0] = endFuel; parity[0] = endParity;
                velocityCache = endVC; normalCache = endHC;
            }
            time += step;
            if (extendForBurnOrbit && burn.Enabled && Math.Abs(time - burn.End) < 1e-7)
            {
                double inverseAxis = 2 / math.length(p[0]) - math.lengthsq(v[0]) / a.Mu;
                if (inverseAxis > 0)
                {
                    double period = 2 * Math.PI / Math.Sqrt(a.Mu * inverseAxis * inverseAxis * inverseAxis);
                    if (double.IsFinite(period))
                        horizonSeconds = Math.Min(MaxEncounterHorizonSeconds, Math.Max(horizonSeconds, time + 2 * period));
                }
            }
            if (sequence != null && burnIndex + 1 < sequence.Length && time >= burn.End - 1e-9)
                burn = sequence[++burnIndex];
        }
        return new EncounterTimeline(samples, minima, null);
    }

    private static bool ValidEncounterBody(TrajectoryMatchedPredictionWorkItem body)
        => math.all(math.isfinite(body.StartPosition)) && math.all(math.isfinite(body.StartVelocity)) &&
            double.IsFinite(body.Mass) && body.Mass > 0 && double.IsFinite(body.Mu) && body.Mu > 0 &&
            float.IsFinite(body.DragCoefficient) && body.DragCoefficient >= 0 &&
            float.IsFinite(body.CrossSectionArea) && body.CrossSectionArea >= 0;

    private const float MaxNativeStepDt = 0.02f;
    private const double GUnity = PhysicsConstants.GDouble;

    public static bool TryBuildWorkItem(
        NBody body,
        BodyService bodyService,
        TrajectoryPredictionRequest request,
        out TrajectoryMatchedPredictionWorkItem workItem)
    {
        workItem = default;

        if (body == null || bodyService == null || bodyService.CentralBody == null)
            return false;

        double muUnity = GUnity * bodyService.CentralBody.TotalMassKilograms;
        workItem = new TrajectoryMatchedPredictionWorkItem(
            body.state.position,
            body.state.velocity,
            body.state.mass,
            body.dragCoefficient,
            (float)body.DragAreaSquareUnits,
            muUnity,
            request.Steps,
            request.DeltaTime,
            request.MaxOutputPoints
        );
        return true;
    }

    public static TrajectoryMatchedPredictionResult Predict(TrajectoryMatchedPredictionWorkItem workItem,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (workItem.Steps <= 0 || workItem.MaxOutputPoints <= 1)
            return new TrajectoryMatchedPredictionResult(Array.Empty<Vector3>(), 0f);

        int lodFactor = Mathf.Max(1, workItem.Steps / workItem.MaxOutputPoints);
        int outputCount = Mathf.CeilToInt((float)workItem.Steps / lodFactor);
        if (outputCount <= 0)
            return new TrajectoryMatchedPredictionResult(Array.Empty<Vector3>(), 0f);

        double3[] positions = { workItem.StartPosition };
        double3[] velocities = { workItem.StartVelocity };
        double[] masses = { workItem.Mass };
        Vector3[] thrusts = { Vector3.zero };
        float[] dragCoeffs = { workItem.DragCoefficient };
        float[] areas = { workItem.CrossSectionArea };
        sbyte[] normalSigns = { 0 };
        byte[] isThrusting = { 0 };
        sbyte[] latchedParity = { 0 };

        Vector3[] result = new Vector3[outputCount];
        int remainingSteps = workItem.Steps;

        for (int i = 0; i < outputCount && remainingSteps > 0; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            int stepChunk = Mathf.Min(lodFactor, remainingSteps);
            float chunkDt = stepChunk * workItem.DeltaTime;
            int substeps = Mathf.Max(1, Mathf.CeilToInt(chunkDt / MaxNativeStepDt));

            NativePhysics.BatchTwoBodyIntegrateMuEx(
                positions,
                velocities,
                masses,
                thrusts,
                dragCoeffs,
                areas,
                normalSigns,
                isThrusting,
                latchedParity,
                1,
                workItem.Mu,
                chunkDt,
                substeps
            );
            cancellation.ThrowIfCancellationRequested();

            result[i] = new Vector3(
                (float)positions[0].x,
                (float)positions[0].y,
                (float)positions[0].z
            );

            remainingSteps -= stepChunk;
        }

        return new TrajectoryMatchedPredictionResult(result, workItem.DeltaTime * lodFactor);
    }

}
