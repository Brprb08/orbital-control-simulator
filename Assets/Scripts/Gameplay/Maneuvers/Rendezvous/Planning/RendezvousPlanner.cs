using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using Burn = TrajectoryMatchedPredictor.EncounterBurn;
using Timeline = TrajectoryMatchedPredictor.EncounterTimeline;

/// <summary>
/// Worker-only rendezvous solver. Seeds near-circular transfers analytically and
/// inclined or eccentric transfers with Lambert arcs, then corrects finite burns
/// with the shared flight predictor. No scene objects or live state.
/// </summary>
public static partial class RendezvousPlanner
{
    public enum TimingPreference { SaveFuel, Balanced, StartSooner }
    public enum ArrivalMode { Center, Safe, Waypoint, Intercept }
    public enum WaypointDirection { Prograde, Retrograde, RadialOut, RadialIn, Normal, AntiNormal }

    public readonly struct ArrivalGoal
    {
        public readonly ArrivalMode Mode;
        public readonly WaypointDirection Direction;
        public readonly double DistanceMeters, ContactDistanceMeters;
        public ArrivalGoal(ArrivalMode mode, WaypointDirection direction,
            double distanceMeters, double contactDistanceMeters)
        {
            Mode = mode;
            Direction = direction;
            DistanceMeters = Math.Max(0, distanceMeters);
            ContactDistanceMeters = Math.Max(0, contactDistanceMeters);
        }

        public double ClearanceMeters => Mode switch
        {
            ArrivalMode.Safe => ContactDistanceMeters + 10,
            ArrivalMode.Waypoint => ContactDistanceMeters +
                Math.Min(10, Math.Max(0, DistanceMeters - ContactDistanceMeters) * .5),
            _ => 0
        };
        public double PositionTolerance(double configured) => Mode switch
        {
            ArrivalMode.Intercept => Math.Min(configured, ContactDistanceMeters),
            ArrivalMode.Safe or ArrivalMode.Waypoint => Math.Min(configured,
                Math.Max(.1, Math.Min(50, Math.Min(DistanceMeters * .1,
                    (DistanceMeters - ClearanceMeters) * .5)))),
            _ => configured
        };
        public double3 Offset(double3 targetPosition, double3 targetVelocity)
        {
            if (Mode != ArrivalMode.Safe && Mode != ArrivalMode.Waypoint) return double3.zero;
            double3 radial = math.normalizesafe(targetPosition);
            double3 normal = math.normalizesafe(math.cross(targetPosition, targetVelocity));
            double3 prograde = math.normalizesafe(targetVelocity);
            double3 direction = Direction switch
            {
                WaypointDirection.Retrograde => -prograde,
                WaypointDirection.RadialOut => radial,
                WaypointDirection.RadialIn => -radial,
                WaypointDirection.Normal => -normal,
                WaypointDirection.AntiNormal => normal,
                _ => prograde
            };
            return direction * (DistanceMeters / 10000.0);
        }

        public double3 OffsetVelocity(double3 position, double3 velocity, double mu)
        {
            if (Mode != ArrivalMode.Safe && Mode != ArrivalMode.Waypoint) return double3.zero;
            double radius = math.length(position), speed = math.length(velocity);
            if (!(radius > 0) || !(speed > 0)) return double3.zero;
            double3 radial = position / radius, prograde = velocity / speed;
            double3 acceleration = -mu * position / (radius * radius * radius);
            double3 radialRate = (velocity - radial * math.dot(radial, velocity)) / radius;
            double3 progradeRate = (acceleration - prograde * math.dot(prograde, acceleration)) / speed;
            return (Direction switch {
                WaypointDirection.RadialOut => radialRate,
                WaypointDirection.RadialIn => -radialRate,
                WaypointDirection.Prograde => progradeRate,
                WaypointDirection.Retrograde => -progradeRate,
                _ => double3.zero
            }) * (DistanceMeters / 10000.0);
        }

        public bool IsSatisfied(double3 a, double3 va, double3 b, double3 vb,
            double mu, double distanceTolerance, double speedTolerance) =>
            Mode != ArrivalMode.Intercept &&
            math.distance(a, b) * 10000 > ContactDistanceMeters &&
            math.length(a - b - Offset(b, vb)) * 10000 <= PositionTolerance(distanceTolerance) &&
            math.length(va - vb - OffsetVelocity(b, vb, mu)) * 10000 <= speedTolerance;
    }

    /// <summary>Immutable worker snapshot; never reads a Unity object off-thread.</summary>
    public sealed class Settings
    {
        public readonly double MinHorizon, MaxHorizon;
        public readonly int ScalarCandidates, VectorCandidates, ExplicitVectorCandidates;
        public readonly int ScalarIterations, VectorIterations, FuelIterations;
        public readonly double BalancedWaitCost, StartSoonerWaitCost;
        public readonly double MaxBurnDeltaV, MaxVectorDeltaV, MaxBurnDuration;
        public readonly double OptimizerDistance, OptimizerSpeed, ValidationDistance, ValidationSpeed;
        public readonly ArrivalGoal Goal;
        public static Settings Default => new Settings(86400, 172800, 3, 4, 8, 14, 10, 1,
            25, 100, 5000, 6000, 1800, 100, .1, 200, .2);
        public Settings(double minHorizon, double maxHorizon, int scalarCandidates,
            int vectorCandidates, int explicitVectorCandidates, int scalarIterations,
            int vectorIterations, int fuelIterations, double balancedWaitCost,
            double startSoonerWaitCost, double maxBurnDeltaV, double maxVectorDeltaV,
            double maxBurnDuration, double optimizerDistance, double optimizerSpeed,
            double validationDistance, double validationSpeed, ArrivalGoal goal = default)
        {
            double horizonLimit = TrajectoryMatchedPredictor.MaxEncounterHorizonSeconds - 60;
            MinHorizon = Math.Clamp(double.IsFinite(minHorizon) ? minHorizon : 86400, 3600, horizonLimit);
            MaxHorizon = Math.Clamp(double.IsFinite(maxHorizon) ? maxHorizon : horizonLimit, MinHorizon, horizonLimit);
            ScalarCandidates = Math.Clamp(scalarCandidates, 1, 16);
            VectorCandidates = Math.Clamp(vectorCandidates, 1, 16);
            ExplicitVectorCandidates = Math.Clamp(explicitVectorCandidates, 1, 24);
            ScalarIterations = Math.Clamp(scalarIterations, 1, 32);
            VectorIterations = Math.Clamp(vectorIterations, 1, 32);
            FuelIterations = Math.Clamp(fuelIterations, 0, 8);
            BalancedWaitCost = Math.Max(0, balancedWaitCost);
            StartSoonerWaitCost = Math.Max(0, startSoonerWaitCost);
            MaxBurnDeltaV = Math.Max(1, maxBurnDeltaV);
            MaxVectorDeltaV = Math.Max(1, maxVectorDeltaV);
            MaxBurnDuration = Math.Max(.02, maxBurnDuration);
            OptimizerDistance = Math.Max(.01, optimizerDistance);
            OptimizerSpeed = Math.Max(.001, optimizerSpeed);
            ValidationDistance = Math.Max(OptimizerDistance, validationDistance);
            ValidationSpeed = Math.Max(OptimizerSpeed, validationSpeed);
            Goal = goal;
        }

        public Settings WithGoal(ArrivalGoal goal) => new Settings(MinHorizon, MaxHorizon,
            ScalarCandidates, VectorCandidates, ExplicitVectorCandidates,
            ScalarIterations, VectorIterations, FuelIterations, BalancedWaitCost,
            StartSoonerWaitCost, MaxBurnDeltaV, MaxVectorDeltaV, MaxBurnDuration,
            OptimizerDistance, OptimizerSpeed, ValidationDistance, ValidationSpeed, goal);
    }

    private const double MinExtraBurnDeltaVMetersPerSecond = 1;
    private const double MinExtraBurnDurationSeconds = 0.1;
    // Convert departure wait to a comparison cost, never to an extra burn or
    // fuel estimate. Arrival accuracy and physical limits remain hard checks.
    private static double PreferenceScore(double deltaV, double firstBurnSeconds,
        TimingPreference preference, Settings settings) => deltaV + firstBurnSeconds / 3600.0 *
        (preference == TimingPreference.StartSooner ? settings.StartSoonerWaitCost :
         preference == TimingPreference.Balanced ? settings.BalancedWaitCost : 0.0);

    /// <summary>Small thread-safe progress snapshot; never invokes UI from the worker.</summary>
    public sealed class Progress
    {
        private int evaluations, seed, iteration;
        private string stage = "Searching";
        public string Stage => Volatile.Read(ref stage);
        public int Evaluations => Volatile.Read(ref evaluations);
        public int Seed => Volatile.Read(ref seed);
        public int Iteration => Volatile.Read(ref iteration);
        internal void BeginEvaluation() => Interlocked.Increment(ref evaluations);
        internal void SetStage(string value) => Volatile.Write(ref stage, value);
        internal void SetIteration(int seedNumber, int iterationNumber)
        { Volatile.Write(ref seed, seedNumber); Volatile.Write(ref iteration, iterationNumber); }
    }
    public sealed class Result
    {
        public string Message;
        // The first choice is this result; alternatives are independently validated.
        public Result[] Choices;
        // Complete validated pool, retained when the UI changes preference.
        public Result[] Candidates;
        public Burn[] Burns;
        public double[] BurnDeltaVs;
        public Timeline Prediction, CoastReference;
        public double ArrivalSeconds, SeparationMeters, RelativeSpeed, DeltaV, FuelKg;
        public ArrivalGoal Goal;
        public double Mu, ArrivalDistanceTolerance, ArrivalSpeedTolerance;
        public double FlightPositionTolerance, FlightSpeedTolerance;
        public bool MatchesFlight(TrajectoryMatchedPredictor.EncounterSample expected,
            double3 a, double3 va, double3 b, double3 vb) =>
            math.distance(expected.A, a) * 10000 <= FlightPositionTolerance &&
            math.distance(expected.B, b) * 10000 <= FlightPositionTolerance &&
            math.distance(expected.VA, va) * 10000 <= FlightSpeedTolerance &&
            math.distance(expected.VB, vb) * 10000 <= FlightSpeedTolerance;
        // Ideal impulsive orbit-change reference, not a timed rendezvous solution.
        public double HohmannOrbitChangeDeltaV = double.NaN;
        public bool Success => Burns != null;
    }

    public static Result RankPlans(Result[] candidates, TimingPreference preference, Settings settings)
    {
        settings ??= Settings.Default;
        var ranked = new List<Result>(candidates);
        ranked.Sort((x, y) =>
        {
            int score = PreferenceScore(x.DeltaV, x.Burns[0].Start, preference, settings).CompareTo(
                PreferenceScore(y.DeltaV, y.Burns[0].Start, preference, settings));
            if (score != 0) return score;
            int fuel = x.DeltaV.CompareTo(y.DeltaV);
            if (fuel != 0) return fuel;
            int start = x.Burns[0].Start.CompareTo(y.Burns[0].Start);
            return start != 0 ? start : x.ArrivalSeconds.CompareTo(y.ArrivalSeconds);
        });
        var best = ranked[0];
        var choices = new List<Result> { best };
        double bestScore = PreferenceScore(best.DeltaV, best.Burns[0].Start, preference, settings);
        foreach (var candidate in ranked)
        {
            if (choices.Count == 3) break;
            double score = PreferenceScore(candidate.DeltaV, candidate.Burns[0].Start, preference, settings);
            if (score > bestScore + Math.Max(25, bestScore *
                (preference == TimingPreference.SaveFuel ? .10 : .15))) continue;
            if (choices.Exists(other => candidate.Burns.Length == other.Burns.Length &&
                Math.Abs(candidate.Burns[0].Start - other.Burns[0].Start) < 900 &&
                Math.Abs(candidate.ArrivalSeconds - other.ArrivalSeconds) < 1800)) continue;
            choices.Add(candidate);
        }
        foreach (var candidate in candidates) candidate.Candidates = candidates;
        best.Choices = choices.ToArray();
        best.Message = $"Validated {best.Burns.Length}-burn {best.Goal.Mode.ToString().ToLowerInvariant()} proposal; review before scheduling.";
        return best;
    }

}
