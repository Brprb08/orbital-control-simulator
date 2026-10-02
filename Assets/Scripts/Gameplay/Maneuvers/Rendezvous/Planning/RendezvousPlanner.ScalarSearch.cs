using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using Burn = TrajectoryMatchedPredictor.EncounterBurn;
using Timeline = TrajectoryMatchedPredictor.EncounterTimeline;

public static partial class RendezvousPlanner
{
    public static Result Solve(TrajectoryMatchedPredictionWorkItem a,
        TrajectoryMatchedPredictionWorkItem b, Propulsion engine, CancellationToken cancel, double epoch = 0,
        Progress progress = null, int burnCount = 0,
        TimingPreference timingPreference = TimingPreference.SaveFuel, Settings settings = null)
    {
        settings ??= Settings.Default;
        Result Fail(string message) => new Result { Message = message };
        cancel.ThrowIfCancellationRequested();
        if (!float.IsFinite(engine.Thrust) || !double.IsFinite(engine.Isp) ||
            !double.IsFinite(engine.DryMass) || !double.IsFinite(engine.FuelMass) ||
            !double.IsFinite(epoch) || epoch < 0 || engine.FuelMass < 0 ||
            !(engine.Thrust > 0) || !(engine.Isp > 0) || !(engine.DryMass > 0) ||
            (engine.Finite && engine.FuelMass <= 0)) return Fail("A usable engine and propellant are required.");
        if (!Orbit(a, out double ra, out double ta, out double ea) ||
            !Orbit(b, out double rb, out double tb, out double eb)) return Fail("Both satellites need bound orbits.");
        double3 ha = math.normalize(math.cross(a.StartPosition, a.StartVelocity));
        double3 hb = math.normalize(math.cross(b.StartPosition, b.StartVelocity));
        double planeAngle = Math.Acos(math.clamp(math.dot(ha, hb), -1, 1)) * 180 / Math.PI;
        if (burnCount != 0 && (burnCount < (settings.Goal.Mode == ArrivalMode.Intercept ? 1 : 2) || burnCount > 4)) return Fail("Choose Auto or a supported burn count.");
        double planningHorizon = Math.Min(settings.MaxHorizon,
            Math.Max(settings.MinHorizon, 2 * Math.Max(ta, tb)));
        // Radius ratio alone does not make a transfer a 3D problem. Circular,
        // coplanar LEO/GEO-style rendezvous are exactly the cases for which the
        // analytic Hohmann/phasing seeds below are strongest. Sending them to
        // the generic Lambert corrector was both slower and much more likely to
        // select an unnecessarily expensive zero-revolution arc.
        if (settings.Goal.Mode == ArrivalMode.Intercept || ea > 0.02 || eb > 0.02 || planeAngle > 0.05)
            return SolveVectorTransfer(a, b, engine, cancel, epoch, planningHorizon, progress, burnCount,
                timingPreference, settings: settings);
        const double lead = 120;
        if (!KeplerPropagator.TryPropagateUniversal(a.StartPosition, a.StartVelocity, a.Mu, lead, out var pa, out _) ||
            !KeplerPropagator.TryPropagateUniversal(b.StartPosition, b.StartVelocity, b.Mu, lead, out var pb, out _))
            return Fail("Unable to seed the transfer from these states.");
        double phase = Math.Atan2(math.dot(ha, math.cross(pa, pb)), math.dot(pa, pb));
        double transferAxis = (ra + rb) * 0.5;
        double transferTime = Math.PI * Math.Sqrt(transferAxis * transferAxis * transferAxis / a.Mu);
        double va = Math.Sqrt(a.Mu / ra), vb = Math.Sqrt(a.Mu / rb);
        double departureSpeed = Math.Sqrt(a.Mu * (2 / ra - 1 / transferAxis));
        double arrivalSpeed = Math.Sqrt(a.Mu * (2 / rb - 1 / transferAxis));
        var seeds = new List<double[]>();
        int targetTurnLimit = Math.Min(64, Math.Max(6, (int)Math.Ceiling(planningHorizon / tb)));
        int phaseTurnLimit = Math.Min(64, Math.Max(4, (int)Math.Ceiling(planningHorizon / ta)));
        for (int targetTurns = 0; targetTurns <= targetTurnLimit; targetTurns++)
            for (int phaseTurns = 1; phaseTurns <= phaseTurnLimit; phaseTurns++)
            {
                double phaseTime = (Math.PI - phase + 2 * Math.PI * targetTurns) / (2 * Math.PI / tb) - transferTime;
                if (phaseTime <= 0 || lead + phaseTime + transferTime > planningHorizon) continue;
                double axis = Math.Pow(a.Mu * Math.Pow(phaseTime / (2 * Math.PI * phaseTurns), 2), 1.0 / 3);
                if (2 * axis - ra < PhysicsConstants.EarthRadiusUnits + 20 || ra < PhysicsConstants.EarthRadiusUnits + 20) continue;
                double phaseSpeedSq = a.Mu * (2 / ra - 1 / axis);
                if (phaseSpeedSq <= 0) continue;
                double phaseSpeed = Math.Sqrt(phaseSpeedSq);
                seeds.Add(new[] { (phaseSpeed - va) * 10000, (departureSpeed - phaseSpeed) * 10000,
                (vb - arrivalSpeed) * 10000, lead, lead + phaseTime, lead + phaseTime + transferTime });
            }
        // Two burns: wait for a transfer window, or phase once and recircularize
        // when both satellites share the same circular orbit.
        double na = 2 * Math.PI / ta, nb = 2 * Math.PI / tb;
        if (Math.Abs(na - nb) > 1e-10)
        {
            for (int k = -64; k <= 64; k++)
            {
                double wait = (Math.PI - phase - nb * transferTime + 2 * Math.PI * k) / (nb - na);
                if (wait < 0 || lead + wait + transferTime > planningHorizon) continue;
                seeds.Add(new[] { (departureSpeed - va) * 10000, (vb - arrivalSpeed) * 10000,
                    lead + wait, lead + wait + transferTime });
            }
        }
        else
        {
            foreach (var seed in seeds.ToArray())
                if (Math.Abs(seed[2]) < .01)
                    seeds.Add(new[] { seed[0], seed[1], seed[3], seed[4] });
        }

        // Four burns: transfer to a staging circular orbit, coast there to phase,
        // then transfer to the target. These are distinct maneuvers, not split burns.
        foreach (double rc in new[] { (ra + rb) * .5, Math.Min(ra, rb) * .97,
            Math.Max(ra, rb) * 1.03, Math.Max(ra, rb) * 1.08 })
        {
            if (rc < PhysicsConstants.EarthRadiusUnits + 20 || Math.Abs(rc - ra) < .1 || Math.Abs(rc - rb) < .1) continue;
            double ac = (ra + rc) * .5, cb = (rc + rb) * .5;
            double t1 = Math.PI * Math.Sqrt(ac * ac * ac / a.Mu);
            double t2 = Math.PI * Math.Sqrt(cb * cb * cb / a.Mu);
            double vc = Math.Sqrt(a.Mu / rc), nc = vc / rc;
            for (int k = -64; k <= 64; k++)
            {
                double wait = (phase + nb * (t1 + t2) + 2 * Math.PI * k) / (nc - nb);
                if (wait < 30 || lead + t1 + wait + t2 > planningHorizon) continue;
                seeds.Add(new[] {
                    (Math.Sqrt(a.Mu * (2 / ra - 1 / ac)) - va) * 10000,
                    (vc - Math.Sqrt(a.Mu * (2 / rc - 1 / ac))) * 10000,
                    (Math.Sqrt(a.Mu * (2 / rc - 1 / cb)) - vc) * 10000,
                    (vb - Math.Sqrt(a.Mu * (2 / rb - 1 / cb))) * 10000,
                    lead, lead + t1, lead + t1 + wait, lead + t1 + wait + t2 });
            }
        }
        seeds.RemoveAll(x => burnCount != 0 && x.Length / 2 != burnCount);
        seeds.Sort((x, y) =>
        {
            int budget = Budget(x).CompareTo(Budget(y));
            return budget != 0 ? budget : x[x.Length - 1].CompareTo(y[y.Length - 1]);
        });
        // Check engine/fuel constraints before spending any propagation work.
        seeds.RemoveAll(x => !BuildBurns(x, engine, epoch, settings, out _, out _, out _, out _, out _));
        if (seeds.Count == 0)
            return SolveVectorTransfer(a, b, engine, cancel, epoch, planningHorizon,
                progress, burnCount, timingPreference, settings);

        Trial Evaluate(double[] x, float coastStep)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.BeginEvaluation();

            if (!BuildBurns(
                    x,
                    engine,
                    epoch,
                    settings,
                    out Burn[] burns,
                    out double fuel,
                    out double dv,
                    out var deltaVs,
                    out _))
            {
                return null;
            }

            double end = burns[burns.Length - 1].End;

            if (end < 60)
            {
                return null;
            }

            if (end > planningHorizon)
            {
                return null;
            }

            if (!TrajectoryMatchedPredictor.TryPropagatePairToTime(
                    a,
                    b,
                    end,
                    cancel,
                    out var state,
                    burns,
                    coastStep))
            {
                return null;
            }

            double3 dr =
                (state.A - state.B - settings.Goal.Offset(state.B, state.VB)) * 10000;

            double3 velocity =
                settings.Goal.Mode == ArrivalMode.Intercept ? double3.zero :
                (state.VA - state.VB - settings.Goal.OffsetVelocity(state.B, state.VB, b.Mu)) * 10000;

            double[] residual =
            {
        dr.x,
        dr.y,
        dr.z,

        velocity.x * 300,
        velocity.y * 300,
        velocity.z * 300
    };

            double cost = 0;

            foreach (double value in residual)
                cost += value * value;

            return new Trial
            {
                Burns = burns,
                Residual = residual,
                Cost = cost,
                Fuel = fuel,
                Dv = dv,
                End = end,
                Parameters = (double[])x.Clone(),
                DeltaVs = deltaVs
            };
        }

        Trial Correct(double[] initialX, float coastStep, int maxIterations,
            bool robustJacobian = false, double[] residualBias = null)
        {
            Trial Sample(double[] parameters) => ApplyResidualBias(Evaluate(parameters, coastStep), residualBias);
            double[] x = (double[])initialX.Clone();
            int dimension = x.Length, count = dimension / 2;
            Trial current = Sample(x);

            if (current == null)
                return null;

            if (Accept(current, settings))
                return current;

            var jacobian = new double[6, dimension];
            bool BuildJacobian()
            {
                for (int j = 0; j < dimension; j++)
                {
                    double h = j < count ? 0.2 : 1.0;
                    double[] shifted = (double[])x.Clone();
                    shifted[j] += h;
                    Trial perturbed = Sample(shifted);
                    Trial opposite = null;
                    if (robustJacobian || perturbed == null)
                    {
                        shifted[j] = x[j] - h;
                        opposite = Sample(shifted);
                    }
                    if (perturbed == null && opposite == null) return false;
                    for (int i = 0; i < 6; i++)
                        jacobian[i, j] = perturbed != null && opposite != null
                            ? (perturbed.Residual[i] - opposite.Residual[i]) / (2 * h)
                            : perturbed != null
                                ? (perturbed.Residual[i] - current.Residual[i]) / h
                                : (current.Residual[i] - opposite.Residual[i]) / h;
                }
                return true;
            }
            if (!BuildJacobian()) return current;

            double damping = 0.001;
            int rejected = 0, rebuilds = 0;

            for (int iteration = 0;
                 iteration < maxIterations;
                 iteration++)
            {
                cancel.ThrowIfCancellationRequested();
                progress?.SetIteration(progress.Seed, iteration + 1);

                if (Accept(current, settings))
                    break;

                var matrix = new double[dimension, dimension + 1];

                // J^T J
                for (int i = 0; i < dimension; i++)
                {
                    for (int j = 0; j < dimension; j++)
                    {
                        for (int k = 0; k < 6; k++)
                        {
                            matrix[i, j] +=
                                jacobian[k, i] *
                                jacobian[k, j];
                        }
                    }

                    matrix[i, i] +=
                        damping *
                        Math.Max(1.0, matrix[i, i]);

                    // -J^T r
                    for (int k = 0; k < 6; k++)
                    {
                        matrix[i, dimension] -=
                            jacobian[k, i] *
                            current.Residual[k];
                    }
                }

                if (!SolveLinear(matrix, out var change))
                    break;

                double[] proposal =
                    (double[])x.Clone();

                for (int i = 0; i < dimension; i++)
                {
                    proposal[i] += Math.Clamp(
                        change[i],
                        i < count ? -50.0 : -30.0,
                        i < count ? 50.0 : 30.0);
                }

                Trial next =
                    Sample(proposal);

                if (next == null ||
                    next.Cost >= current.Cost)
                {
                    damping *= 10.0;
                    rejected++;
                    // Broyden is inexpensive, but can become stale after timing moves.
                    // Re-linearize only when it demonstrably stops improving.
                    if (rejected >= 2 && rebuilds < 2)
                    {
                        rebuilds++;
                        rejected = 0;
                        damping = .001;
                        if (!BuildJacobian()) break;
                    }
                    else if (rejected >= 3) break;
                    continue;
                }

                // -------------------------------------------------
                // Successful step:
                // update J using a Broyden rank-one update.
                //
                // Jnew = J + ((y - J*s) * s^T) / (s^T*s)
                // -------------------------------------------------

                double[] s = new double[dimension];
                double[] y = new double[6];

                double denominator = 0;

                for (int j = 0; j < dimension; j++)
                {
                    s[j] = proposal[j] - x[j];
                    denominator += s[j] * s[j];
                }

                for (int i = 0; i < 6; i++)
                {
                    y[i] =
                        next.Residual[i] -
                        current.Residual[i];
                }

                if (denominator > 1e-12)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        double js = 0;

                        for (int j = 0; j < dimension; j++)
                        {
                            js +=
                                jacobian[i, j] *
                                s[j];
                        }

                        double correction =
                            (y[i] - js) /
                            denominator;

                        for (int j = 0; j < dimension; j++)
                        {
                            jacobian[i, j] +=
                                correction * s[j];
                        }
                    }
                }

                x = proposal;
                current = next;

                rejected = 0;

                // Long transfers are much more nonlinear in burn timing. A
                // fresh central-difference Jacobian prevents the inexpensive
                // Broyden update from stalling a few hundred metres out.
                if (robustJacobian && !BuildJacobian()) break;

                damping =
                    Math.Max(
                        1e-7,
                        damping * 0.3);
            }

            return current;
        }

        // Search the same seed windows and burn families for every preference.
        // The preference only ranks plans after the candidate search.
        double radiusRatio = Math.Max(ra, rb) / Math.Min(ra, rb);
        float correctionStep = radiusRatio > 2 ? 2f : .5f;
        int candidatesPerFamily = settings.ScalarCandidates;
        int correctionIterations = settings.ScalarIterations;
        var acceptedTrials = new List<Trial>();
        int seedNumber = 0;
        int firstFamily = burnCount == 0 ? 2 : burnCount;
        int lastFamily = burnCount == 0 ? 4 : burnCount;
        for (int family = firstFamily; family <= lastFamily; family++)
        {
            Trial closest = null;
            bool familyConverged = false;
            foreach (var seed in DiverseSeeds(seeds, family, candidatesPerFamily, false))
            {
                progress?.SetIteration(++seedNumber, 0);
                Trial candidate = Correct(seed, correctionStep, correctionIterations);
                if (candidate == null) continue;
                if (!HasMeaningfulBurns(candidate)) continue;
                if (closest == null || candidate.Cost < closest.Cost) closest = candidate;
                familyConverged |= WithinValidationTolerance(candidate, settings);
                acceptedTrials.Add(candidate);
            }
            // One rescue per unsuccessful family, never a second solve for every seed.
            if (!familyConverged && closest != null)
            {
                var rescued = Correct(closest.Parameters, .5f, Math.Min(6, correctionIterations));
                if (rescued != null && rescued.Cost < closest.Cost && HasMeaningfulBurns(rescued))
                    acceptedTrials.Add(rescued);
            }
        }
        var result = FinishSearch(a, b, cancel, acceptedTrials, timingPreference, settings,
            x => Evaluate(x, correctionStep),
            (x, bias) => Correct(x, correctionStep, correctionIterations, residualBias: bias),
            false, progress, out _);
        if (result != null) return result;
        return SolveVectorTransfer(a, b, engine, cancel, epoch, planningHorizon,
            progress, burnCount, timingPreference, settings, acceptedTrials);
    }

    private static bool RouteSatisfied(Trial trial)
    {
        for (int i = 6; i < trial.Residual.Length; i += 3)
            if (trial.Residual[i] * trial.Residual[i] + trial.Residual[i + 1] * trial.Residual[i + 1] +
                trial.Residual[i + 2] * trial.Residual[i + 2] > 100) return false;
        return true;
    }

    private static bool Accept(Trial trial, Settings settings)
    {
        double distanceSq = 0, speedSq = 0;
        for (int i = 0; i < 3; i++) { distanceSq += trial.Residual[i] * trial.Residual[i]; speedSq += trial.Residual[i + 3] * trial.Residual[i + 3] / 90000; }
        double positionTolerance = settings.Goal.PositionTolerance(settings.OptimizerDistance);
        return RouteSatisfied(trial) && distanceSq <= positionTolerance * positionTolerance &&
            speedSq <= settings.OptimizerSpeed * settings.OptimizerSpeed;
    }

    private static bool WithinValidationTolerance(Trial trial, Settings settings)
    {
        double distanceSq = 0, speedSq = 0;
        for (int i = 0; i < 3; i++)
        {
            distanceSq += trial.Residual[i] * trial.Residual[i];
            speedSq += trial.Residual[i + 3] * trial.Residual[i + 3] / 90000;
        }
        double positionTolerance = settings.Goal.PositionTolerance(settings.ValidationDistance);
        return RouteSatisfied(trial) && distanceSq <= positionTolerance * positionTolerance &&
            speedSq <= settings.ValidationSpeed * settings.ValidationSpeed;
    }

    private static double Budget(double[] x)
    {
        double total = 0;
        for (int i = 0; i < x.Length / 2; i++) total += Math.Abs(x[i]);
        return total;
    }

    private static bool BuildBurns(
    double[] x,
    Propulsion engine,
    double epoch,
    Settings settings,
    out Burn[] burns,
    out double used,
    out double dv,
    out double[] deltaVs,
    out string failureReason)
    {
        int count = x.Length / 2;
        burns = new Burn[count];
        deltaVs = new double[count];
        used = 0;
        dv = 0;
        failureReason = null;

        double mass =
            engine.DryMass + engine.FuelMass;

        for (int i = 0; i < count; i++)
        {
            int burnNumber = i + 1;

            if (!double.IsFinite(x[i]))
            {
                failureReason =
                    $"burn {burnNumber} delta-v is not finite.";
                return false;
            }

            if (Math.Abs(x[i]) > settings.MaxBurnDeltaV)
            {
                failureReason =
                    $"burn {burnNumber} requires " +
                    $"{Math.Abs(x[i]):F1} m/s delta-v " +
                    $"but the limit is {settings.MaxBurnDeltaV:F0} m/s.";
                return false;
            }

            double deltaV = Math.Abs(x[i]);
            if (!(deltaV > 1e-4))
            {
                failureReason = $"burn {burnNumber} has negligible delta-v.";
                return false;
            }

            double consumed =
                engine.Finite
                    ? mass * (1 - Math.Exp(
                        -deltaV /
                        (engine.Isp * 9.80665)))
                    : 0;

            double fuelRemaining =
                engine.FuelMass - used;

            if (consumed > fuelRemaining)
            {
                failureReason =
                    $"burn {burnNumber} requires " +
                    $"{consumed:F1} kg fuel but only " +
                    $"{fuelRemaining:F1} kg remains.";
                return false;
            }

            double duration =
                engine.Finite
                    ? consumed *
                      engine.Isp *
                      9.80665 /
                      engine.Thrust
                    : deltaV *
                      mass /
                      engine.Thrust;

            if (!double.IsFinite(duration))
            {
                failureReason =
                    $"burn {burnNumber} produced an invalid duration.";
                return false;
            }

            if (duration > settings.MaxBurnDuration)
            {
                failureReason =
                    $"burn {burnNumber} requires " +
                    $"{duration:F1}s but the burn-duration " +
                    $"limit is {settings.MaxBurnDuration:F0}s.";
                return false;
            }

            if (duration <= 0)
            {
                failureReason =
                    $"burn {burnNumber} has a non-positive duration.";
                return false;
            }

            double center =
                x[count + i];

            double start =
                center - duration * 0.5;

            float absoluteStart =
                (float)(epoch + start);

            float absoluteEnd =
                absoluteStart + (float)duration;

            if (!float.IsFinite(absoluteStart) ||
                !float.IsFinite(absoluteEnd))
            {
                failureReason =
                    $"burn {burnNumber} has an invalid absolute time.";
                return false;
            }

            if (absoluteEnd <= absoluteStart)
            {
                absoluteEnd =
                    math.asfloat(
                        math.asuint(absoluteStart) + 1u);
            }

            start =
                (double)absoluteStart - epoch;

            duration =
                (double)absoluteEnd - absoluteStart;

            if (start < 10)
            {
                failureReason =
                    $"burn {burnNumber} starts too early " +
                    $"at {start:F1}s.";
                return false;
            }

            if (i > 0 &&
                start < burns[i - 1].End + 10)
            {
                failureReason =
                    $"burn {burnNumber} overlaps or is less than " +
                    $"10s after burn {burnNumber - 1}.";
                return false;
            }

            consumed =
                engine.Finite
                    ? duration *
                      engine.Thrust /
                      (engine.Isp * 9.80665)
                    : 0;

            fuelRemaining =
                engine.FuelMass - used;

            if (consumed > fuelRemaining)
            {
                failureReason =
                    $"burn {burnNumber} requires " +
                    $"{consumed:F1} kg after timing quantization, " +
                    $"but only {fuelRemaining:F1} kg remains.";
                return false;
            }

            deltaVs[i] =
                engine.Finite
                    ? engine.Isp *
                      9.80665 *
                      Math.Log(
                          mass /
                          (mass - consumed))
                    : duration *
                      engine.Thrust /
                      mass;

            if (!double.IsFinite(deltaVs[i]) || deltaVs[i] > settings.MaxBurnDeltaV ||
                duration > settings.MaxBurnDuration)
            {
                failureReason = $"burn {burnNumber} exceeds flight limits after timing quantization.";
                return false;
            }
            dv += deltaVs[i];

            burns[i] =
                new Burn(
                    start,
                    duration,
                    x[i] >= 0
                        ? BurnType.Prograde
                        : BurnType.Retrograde,
                    engine.Thrust,
                    engine.DryMass,
                    engine.FuelMass,
                    engine.Isp,
                    engine.Finite);

            used += consumed;
            mass -= consumed;
        }

        if (dv > settings.MaxVectorDeltaV)
        {
            failureReason = $"plan exceeds the {settings.MaxVectorDeltaV:F0} m/s total delta-v limit.";
            return false;
        }
        return true;
    }

}
