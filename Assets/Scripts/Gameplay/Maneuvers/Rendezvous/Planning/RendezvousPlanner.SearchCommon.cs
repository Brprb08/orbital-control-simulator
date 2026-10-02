using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using Burn = TrajectoryMatchedPredictor.EncounterBurn;
using Timeline = TrajectoryMatchedPredictor.EncounterTimeline;

public static partial class RendezvousPlanner
{
    private static double HohmannReference(TrajectoryMatchedPredictionWorkItem a,
        TrajectoryMatchedPredictionWorkItem b)
    {
        if (!Orbit(a, out double ra, out _, out double ea) ||
            !Orbit(b, out double rb, out _, out double eb) || ea > 0.02 || eb > 0.02 ||
            Math.Abs(ra - rb) / Math.Min(ra, rb) < 0.01) return double.NaN;
        double3 na = math.normalizesafe(math.cross(a.StartPosition, a.StartVelocity));
        double3 nb = math.normalizesafe(math.cross(b.StartPosition, b.StartVelocity));
        if (math.dot(na, nb) < Math.Cos(0.05 * Math.PI / 180.0)) return double.NaN;
        double axis = (ra + rb) * 0.5;
        double first = Math.Abs(Math.Sqrt(a.Mu * (2 / ra - 1 / axis)) - Math.Sqrt(a.Mu / ra));
        double second = Math.Abs(Math.Sqrt(a.Mu / rb) - Math.Sqrt(a.Mu * (2 / rb - 1 / axis)));
        return (first + second) * 10000.0;
    }

    private static List<double[]> DiverseSeeds(List<double[]> sortedSeeds, int family,
        int limit, bool vector)
    {
        var familySeeds = new List<double[]>();
        foreach (var seed in sortedSeeds)
            if ((vector ? (seed.Length - 1) / 4 : seed.Length / 2) == family)
                familySeeds.Add(seed);
        int departureIndex = vector ? family * 3 : family;
        int arrivalIndex = vector ? (familySeeds.Count > 0 && familySeeds[0].Length % 4 == 2 ? family * 4 : family * 4 - 1) : family * 2 - 1;
        var byDeparture = new List<double[]>(familySeeds);
        var byArrival = new List<double[]>(familySeeds);
        int CompareTime(double[] x, double[] y, int index)
        {
            int time = x[index].CompareTo(y[index]);
            return time != 0 ? time : (vector ? VectorBudget(x).CompareTo(VectorBudget(y)) : Budget(x).CompareTo(Budget(y)));
        }
        byDeparture.Sort((x, y) => CompareTime(x, y, departureIndex));
        byArrival.Sort((x, y) => CompareTime(x, y, arrivalIndex));
        var selected = new List<double[]>(limit);
        bool SameWindow(double[] x, double[] y) =>
            Math.Abs(x[departureIndex] - y[departureIndex]) < 900 &&
            Math.Abs(x[arrivalIndex] - y[arrivalIndex]) < 1800;
        void Add(double[] seed, bool requireNewWindow)
        {
            if (selected.Count >= limit || selected.Contains(seed)) return;
            if (requireNewWindow && selected.Exists(other => SameWindow(seed, other))) return;
            // Do not spend correction work on identical copies of refined seeds.
            if (selected.Exists(other =>
            {
                for (int i = 0; i <= arrivalIndex; i++)
                    if (Math.Abs(seed[i] - other[i]) > 1e-6) return false;
                return true;
            })) return;
            selected.Add(seed);
        }
        // Reserve most work for low-energy windows, with guaranteed early
        // departure/arrival coverage. Fill unused diversity slots with alternate
        // geometries in the same window instead of silently doing less work.
        foreach (var seed in familySeeds)
        {
            if (selected.Count >= Math.Max(1, limit - 2)) break;
            Add(seed, true);
        }
        foreach (var ordering in new[] { byDeparture, byArrival })
        {
            int before = selected.Count;
            foreach (var seed in ordering)
            {
                Add(seed, true);
                if (selected.Count > before || selected.Count == limit) break;
            }
        }
        foreach (var seed in familySeeds) Add(seed, false);
        return selected;
    }

    private static bool HasMeaningfulBurns(Trial trial)
    {
        if (trial.Burns.Length <= 2) return true;
        // Constrained staging burns can be small compared with the orbital transfer.
        bool routed = trial.Residual.Length > 6;
        double minimumDeltaV = routed ? .01 : Math.Max(MinExtraBurnDeltaVMetersPerSecond, trial.Dv * 0.005);
        for (int i = 0; i < trial.Burns.Length; i++)
            if (trial.DeltaVs[i] < minimumDeltaV ||
                trial.Burns[i].Duration < (routed ? .02 : MinExtraBurnDurationSeconds))
                return false;
        return true;
    }

    private static Result ValidatedResult(TrajectoryMatchedPredictionWorkItem a,
        TrajectoryMatchedPredictionWorkItem b, CancellationToken cancel,
        Trial trial, Timeline path, TrajectoryMatchedPredictor.EncounterSample arrival, Settings settings)
    {
        double positionBudget = Math.Min(25, settings.Goal.PositionTolerance(settings.ValidationDistance) / 4);
        if (settings.Goal.ClearanceMeters > 0)
            positionBudget = Math.Min(positionBudget,
                (settings.Goal.ClearanceMeters - settings.Goal.ContactDistanceMeters) / 8);
        // The validated timeline already contains the unpowered coast before
        // departure. ProposalCurrent never samples this reference after that.
        return new Result {
            Burns = trial.Burns, BurnDeltaVs = trial.DeltaVs, Prediction = path,
            CoastReference = path, ArrivalSeconds = trial.End,
            SeparationMeters = math.distance(arrival.A, arrival.B) * 10000,
            RelativeSpeed = math.distance(arrival.VA, arrival.VB) * 10000,
            FuelKg = trial.Fuel, DeltaV = trial.Dv,
            Goal = settings.Goal,
            Mu = a.Mu,
            ArrivalDistanceTolerance = settings.ValidationDistance,
            ArrivalSpeedTolerance = settings.ValidationSpeed,
            FlightPositionTolerance = positionBudget,
            FlightSpeedTolerance = Math.Min(settings.ValidationSpeed / 4, positionBudget / 30),
            HohmannOrbitChangeDeltaV = HohmannReference(a, b)
        };
    }

    public readonly struct Propulsion
    {
        public readonly float Thrust;
        public readonly double DryMass, FuelMass, Isp;
        public readonly bool Finite;
        public Propulsion(float thrust, double dry, double fuel, double isp, bool finite)
        { Thrust = thrust; DryMass = dry; FuelMass = fuel; Isp = isp; Finite = finite; }
    }

    private sealed class Trial
    {
        public Burn[] Burns;
        public double[] Residual;
        public double Cost, Fuel, Dv, End;
        public double[] DeltaVs;
        public double[] Parameters;
    }

    private static Trial ApplyResidualBias(Trial trial, double[] bias)
    {
        if (trial == null || bias == null) return trial;
        trial.Cost = 0;
        for (int i = 0; i < trial.Residual.Length; i++)
        {
            if (i < bias.Length) trial.Residual[i] += bias[i];
            trial.Cost += trial.Residual[i] * trial.Residual[i];
        }
        return trial;
    }

    private static Result FinishSearch(TrajectoryMatchedPredictionWorkItem a,
        TrajectoryMatchedPredictionWorkItem b, CancellationToken cancel, List<Trial> candidates,
        TimingPreference preference, Settings settings, Func<double[], Trial> evaluate,
        Func<double[], double[], Trial> correct, bool vector, Progress progress, out string failure)
    {
        failure = "No physically valid burn sequence survived correction.";
        if (candidates.Count == 0) return null;
        candidates.Sort((x, y) => x.Dv.CompareTo(y.Dv));
        Trial cheapest = candidates[0];
        double reference = HohmannReference(a, b);
        // A transfer already near the circular orbit-change reference gains
        // little from another numerical optimization of every burn family.
        if (cheapest.Residual.Length == 6 && settings.Goal.Mode != ArrivalMode.Intercept && Accept(cheapest, settings) &&
            (!double.IsFinite(reference) || cheapest.Dv > reference + Math.Max(5, reference * .01)))
        {
            progress?.SetStage("Refining fuel");
            Trial refined = RefineFuel(cheapest, cheapest.Burns.Length * (vector ? 3 : 1),
                evaluate, x => correct(x, null), settings.FuelIterations, settings);
            if (!ReferenceEquals(refined, cheapest)) candidates.Add(refined);
        }

        // Prefer converged endpoints, but retain near misses for flight-model
        // recovery. All preferences use exactly the same selection procedure.
        var orderings = new List<Trial>[3];
        for (int mode = 0; mode < orderings.Length; mode++)
        {
            var timing = (TimingPreference)mode;
            orderings[mode] = new List<Trial>(candidates);
            orderings[mode].Sort((x, y) =>
            {
                bool xConverged = WithinValidationTolerance(x, settings);
                bool yConverged = WithinValidationTolerance(y, settings);
                if (xConverged != yConverged) return xConverged ? -1 : 1;
                if (!xConverged)
                {
                    int accuracy = x.Cost.CompareTo(y.Cost);
                    if (accuracy != 0) return accuracy;
                }
                int score = PreferenceScore(x.Dv, x.Burns[0].Start, timing, settings).CompareTo(
                    PreferenceScore(y.Dv, y.Burns[0].Start, timing, settings));
                if (score != 0) return score;
                int fuel = x.Dv.CompareTo(y.Dv);
                return fuel != 0 ? fuel : x.End.CompareTo(y.End);
            });
        }
        var shortlist = new List<Trial>();
        void Add(Trial candidate)
        {
            if (candidate != null && shortlist.Count < 6 && !shortlist.Contains(candidate))
                shortlist.Add(candidate);
        }
        // Start with each preference winner, then retain the closest endpoint
        // from each burn family. Agreement between preferences must not shrink
        // the recovery pool down to two nearly identical failed candidates.
        foreach (var ordering in orderings) Add(ordering[0]);
        for (int family = settings.Goal.Mode == ArrivalMode.Intercept ? 1 : 2; family <= 4; family++)
        {
            Trial closest = null;
            foreach (var candidate in candidates)
                if (candidate.Burns.Length == family && (closest == null || candidate.Cost < closest.Cost))
                    closest = candidate;
            Add(closest);
        }
        for (int rank = 1; rank < candidates.Count && shortlist.Count < 6; rank++)
            foreach (var ordering in orderings) Add(ordering[rank]);

        var validated = new List<Result>();
        int index = 0;
        int recoveryReplays = 6; // At most twelve full replays including the six initial validations.
        double bestError = double.PositiveInfinity;
        foreach (var initial in shortlist)
        {
            Trial candidate = initial;
            progress?.SetStage($"Validating {++index}/{shortlist.Count}");
            // Near misses enter here too. Re-measure the error after correction,
            // sharing a fixed recovery budget across the whole shortlist.
            // Never build a Jacobian by replaying an entire orbit at 0.02 s.
            double previousCost = double.PositiveInfinity;
            for (int pass = 0; pass < 3; pass++)
            {
                cancel.ThrowIfCancellationRequested();
                var path = TrajectoryMatchedPredictor.PredictEncounterTimeline(a, b,
                    candidate.End + 60, cancel, sequence: candidate.Burns);
                if (!path.IsValid || !path.TrySample(candidate.End, out var arrival))
                {
                    if (!double.IsFinite(bestError)) failure = path.Error ?? path.Notice ?? "Flight prediction did not reach arrival.";
                    break;
                }
                if (!TimelineWithinTransferBounds(path, candidate.End))
                {
                    if (!double.IsFinite(bestError)) failure = "Candidate trajectories exceeded the transfer altitude limits.";
                    break;
                }
                double3 dr = (arrival.A - arrival.B - settings.Goal.Offset(arrival.B, arrival.VB)) * 10000;
                double3 dv = settings.Goal.Mode == ArrivalMode.Intercept ? double3.zero :
                    (arrival.VA - arrival.VB - settings.Goal.OffsetVelocity(arrival.B, arrival.VB, b.Mu)) * 10000;
                double fullCost = math.lengthsq(dr) + math.lengthsq(dv) * 90000;
                if (fullCost < bestError)
                {
                    bestError = fullCost;
                    failure = $"Closest checked arrival: {math.length(dr):F1} m and {math.length(dv):F3} m/s; " +
                        $"requires <= {settings.Goal.PositionTolerance(settings.ValidationDistance):F0} m" +
                        (settings.Goal.Mode == ArrivalMode.Intercept ? "." :
                            $" and <= {settings.ValidationSpeed:F3} m/s.");
                }
                bool clearApproach = settings.Goal.ClearanceMeters <= 0 ||
                    path.TryGetClosest(0, out var closest, 0) &&
                    closest.DistanceMeters >= settings.Goal.ClearanceMeters &&
                    path.MaintainsClearance(settings.Goal.ClearanceMeters);
                if (!clearApproach && math.length(dr) <=
                    settings.Goal.PositionTolerance(settings.ValidationDistance) &&
                    math.length(dv) <= settings.ValidationSpeed)
                    failure = "Candidate approach passes inside the target clearance zone.";
                if (math.length(dr) <= settings.Goal.PositionTolerance(settings.ValidationDistance) &&
                    math.length(dv) <= settings.ValidationSpeed && clearApproach)
                {
                    validated.Add(ValidatedResult(a, b, cancel, candidate, path, arrival, settings));
                    break;
                }
                if (pass == 2 || recoveryReplays == 0 || !(fullCost < previousCost)) break;
                previousCost = fullCost;
                Trial coarse = evaluate(candidate.Parameters);
                if (coarse == null) break;
                double[] bias = { dr.x, dr.y, dr.z, dv.x * 300, dv.y * 300, dv.z * 300 };
                for (int i = 0; i < bias.Length; i++) bias[i] -= coarse.Residual[i];
                candidate = correct(candidate.Parameters, bias);
                if (candidate == null || !HasMeaningfulBurns(candidate)) break;
                recoveryReplays--;
            }
        }
        return validated.Count == 0 ? null : RankPlans(validated.ToArray(), preference, settings);
    }

    // Reduce dV along the directions left free by the six encounter constraints.
    // The corrector restores the nonlinear encounter after each proposed step;
    // a cheaper candidate is retained only if it still satisfies the constraints.
    private static Trial RefineFuel(Trial initial, int burnParameters,
        Func<double[], Trial> evaluate, Func<double[], Trial> correct, int iterationLimit,
        Settings settings)
    {
        if (iterationLimit == 0) return initial;
        // Derivatives and the base residual must use the same propagation accuracy.
        Trial current = evaluate(initial.Parameters);
        if (current == null) return initial;
        if (!Accept(current, settings)) current = correct(initial.Parameters);
        if (current == null || !Accept(current, settings) || !HasMeaningfulBurns(current)) return initial;
        int dimension = burnParameters + initial.Burns.Length;
        for (int iteration = 0; iteration < iterationLimit; iteration++)
        {
            var jacobian = new double[6, dimension];
            var gradient = new double[dimension];
            var scales = new double[dimension];
            for (int j = 0; j < dimension; j++)
            {
                scales[j] = j < burnParameters ? 20 : 120;
                double h = j < burnParameters ? .5 : 2;
                var shifted = (double[])current.Parameters.Clone();
                shifted[j] += h;
                Trial perturbed = evaluate(shifted);
                if (perturbed == null)
                {
                    shifted[j] -= 2 * h;
                    h = -h;
                    perturbed = evaluate(shifted);
                }
                if (perturbed == null) return current;
                for (int row = 0; row < 6; row++)
                    jacobian[row, j] = (perturbed.Residual[row] - current.Residual[row]) / h * scales[j];
                gradient[j] = (perturbed.Dv - current.Dv) / h * scales[j];
            }
            // Normalize constraint rows so meters and velocity weighting do not
            // set the regularization strength or hide nearly coplanar directions.
            for (int row = 0; row < 6; row++)
            {
                double norm = 0;
                for (int j = 0; j < dimension; j++) norm += jacobian[row, j] * jacobian[row, j];
                norm = Math.Sqrt(Math.Max(norm, 1e-20));
                for (int j = 0; j < dimension; j++) jacobian[row, j] /= norm;
            }
            var system = new double[6, 7];
            for (int row = 0; row < 6; row++)
            {
                for (int col = 0; col < 6; col++)
                    for (int j = 0; j < dimension; j++)
                        system[row, col] += jacobian[row, j] * jacobian[col, j];
                system[row, row] += 1e-8;
                for (int j = 0; j < dimension; j++) system[row, 6] += jacobian[row, j] * gradient[j];
            }
            if (!SolveLinear(system, out var multiplier)) break;
            double largest = 0;
            for (int j = 0; j < dimension; j++)
            {
                for (int row = 0; row < 6; row++) gradient[j] -= jacobian[row, j] * multiplier[row];
                largest = Math.Max(largest, Math.Abs(gradient[j]));
            }
            if (largest < 1e-5) break;
            Trial improvement = null;
            for (double step = 1; step >= .25; step *= .5)
            {
                var proposal = (double[])current.Parameters.Clone();
                for (int j = 0; j < dimension; j++)
                    proposal[j] -= step * scales[j] * gradient[j] / largest;
                Trial candidate = correct(proposal);
                if (candidate != null && Accept(candidate, settings) && HasMeaningfulBurns(candidate) &&
                    candidate.Dv < current.Dv - .1)
                { improvement = candidate; break; }
            }
            if (improvement == null) break;
            current = improvement;
        }
        return current.Dv < initial.Dv ? current : initial;
    }

}
