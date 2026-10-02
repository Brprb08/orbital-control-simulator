using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using Burn = TrajectoryMatchedPredictor.EncounterBurn;
using Timeline = TrajectoryMatchedPredictor.EncounterTimeline;

public static partial class RendezvousPlanner
{
    private static Result SolveVectorTransfer(TrajectoryMatchedPredictionWorkItem a,
        TrajectoryMatchedPredictionWorkItem b, Propulsion engine, CancellationToken cancel,
        double epoch, double horizon, Progress progress, int requestedBurnCount,
        TimingPreference timingPreference, Settings settings = null, List<Trial> scalarTrials = null)
    {
        settings ??= Settings.Default;
        progress?.SetStage("Searching vector transfers");
        Result Fail(string message) => new Result { Message = message };
        Orbit(a, out double aa, out double pa, out _);
        Orbit(b, out double ab, out double pb, out _);
        double[] departures = { 120, 600, 1800, Math.Min(pa, pb) * .25,
            Math.Min(pa, pb) * .5, Math.Min(pa, pb) };
        double transferAxis = (aa + ab) * .5;
        double hohmann = Math.PI * Math.Sqrt(transferAxis * transferAxis * transferAxis / a.Mu);
        // For circular pairs with a modest plane offset, the coplanar Hohmann
        // phase remains a useful departure seed. Lambert then accounts for the
        // actual 3D geometry. Sparse generic windows can miss this low-energy
        // family and favor an unnecessarily radial transfer.
        var departureWindows = new List<double>(departures);
        // Cover the entire horizon, including eccentric and highly inclined
        // cases where the circular phase/node estimates below do not apply.
        for (int window = 0; window <= 96; window++)
            departureWindows.Add(120 + (horizon - 780) * window / 96.0);
        double3 normalA = math.normalizesafe(math.cross(a.StartPosition, a.StartVelocity));
        double3 normalB = math.normalizesafe(math.cross(b.StartPosition, b.StartVelocity));
        double planeCos = math.dot(normalA, normalB);
        double nA = 2 * Math.PI / pa, nB = 2 * Math.PI / pb;
        if (planeCos > Math.Cos(10.0 * Math.PI / 180) &&
            Math.Abs(nB - nA) > 1e-10)
        {
            double phase = Math.Atan2(math.dot(normalA,
                math.cross(a.StartPosition, b.StartPosition)),
                math.dot(a.StartPosition, b.StartPosition));
            for (int revolution = -32; revolution <= 32; revolution++)
            {
                double departure = (Math.PI - nB * hohmann - phase +
                    2 * Math.PI * revolution) / (nB - nA);
                if (departure >= 120 && departure + hohmann < horizon - 60 &&
                    !departureWindows.Exists(t => Math.Abs(t - departure) < 1))
                    departureWindows.Add(departure);
            }
        }
        if (planeCos > Math.Cos(10.0 * Math.PI / 180) && planeCos < Math.Cos(.05 * Math.PI / 180))
        {
            void AddNodeWindows(double3 position, double3 velocity, double3 otherNormal,
                double meanMotion, double arrivalOffset)
            {
                double sine = math.dot(otherNormal, velocity) / meanMotion;
                double cosine = math.dot(otherNormal, position);
                double firstAngle = Math.Atan2(-cosine, sine);
                for (int node = -1; node < 2 * horizon * meanMotion / Math.PI + 2; node++)
                {
                    double center = (firstAngle + node * Math.PI) / meanMotion - arrivalOffset;
                    foreach (double offset in new[] { -300.0, 0.0, 300.0 })
                    {
                        double departure = center + offset;
                        if (departure >= 120 && departure + hohmann < horizon - 60 &&
                            !departureWindows.Exists(t => Math.Abs(t - departure) < 1))
                            departureWindows.Add(departure);
                    }
                }
            }
            AddNodeWindows(a.StartPosition, a.StartVelocity, normalB, nA, 0);
            AddNodeWindows(b.StartPosition, b.StartVelocity, normalA, nB, hohmann);
        }
        var times = new List<double>();
        void AddTime(double seconds)
        {
            seconds = Math.Clamp(seconds, 600, horizon - 180);
            for (int i = 0; i < times.Count; i++) if (Math.Abs(times[i] - seconds) < 1) return;
            times.Add(seconds);
        }
        foreach (double scale in new[] { .35, .5, .7, .85, 1.0, 1.2, 1.5, 2.0 }) AddTime(hohmann * scale);
        foreach (double scale in new[] { .25, .5, 1.0, 1.5, 2.0 }) AddTime(Math.Max(pa, pb) * scale);

        // This stage uses analytic propagation only. Refine several different
        // low-cost windows before spending full-physics evaluations on them.
        double[] TransferSeed(double departureTime, double flightTime, bool longWay)
        {
            if (departureTime < 120 || departureTime >= horizon - 600 ||
                flightTime < 600 || departureTime + flightTime >= horizon - 60 ||
                !KeplerPropagator.TryPropagateUniversal(a.StartPosition, a.StartVelocity, a.Mu,
                    departureTime, out var departurePosition, out var departureVelocity) ||
                !KeplerPropagator.TryPropagateUniversal(b.StartPosition, b.StartVelocity, b.Mu,
                    departureTime + flightTime, out var targetPosition, out var targetVelocity)) return null;
            cancel.ThrowIfCancellationRequested();
            if (!TrySolveLambert(departurePosition, targetPosition + settings.Goal.Offset(targetPosition, targetVelocity), a.Mu, flightTime,
                    longWay, out var transferDeparture, out var transferArrival)) return null;
            double3 dv1 = (transferDeparture - departureVelocity) * 10000;
            double3 dv2 = settings.Goal.Mode == ArrivalMode.Intercept ? double3.zero :
                (targetVelocity + settings.Goal.OffsetVelocity(targetPosition, targetVelocity, b.Mu) - transferArrival) * 10000;
            if (math.length(dv1) > settings.MaxBurnDeltaV ||
                math.length(dv2) > settings.MaxBurnDeltaV) return null;
            double penalty = TransferArcPenalty(departurePosition, transferDeparture,
                a.Mu, flightTime, math.length(targetPosition));
            return new[] { dv1.x, dv1.y, dv1.z, dv2.x, dv2.y, dv2.z,
                departureTime, departureTime + flightTime, penalty };
        }
        double SeedScore(double[] seed) => seed == null ? double.PositiveInfinity :
            seed[seed.Length - 1] * 1e6 + VectorBudget(seed);
        var transferSeeds = new List<double[]>();
        foreach (double departureTime in departureWindows)
            foreach (double flightTime in times)
                for (int branch = 0; branch < 2; branch++)
                {
                    var seed = TransferSeed(departureTime, flightTime, branch == 1);
                    if (seed != null) transferSeeds.Add(seed);
                }
        transferSeeds.Sort((x, y) => SeedScore(x).CompareTo(SeedScore(y)));
        foreach (var initial in DiverseSeeds(transferSeeds, 2, 12, true))
        {
            var current = initial;
            double departureStep = Math.Min(pa, pb) * .125;
            double flightStep = hohmann * .15;
            for (int iteration = 0; iteration < 16; iteration++)
            {
                var next = current;
                for (int departureDirection = -1; departureDirection <= 1; departureDirection++)
                    for (int flightDirection = -1; flightDirection <= 1; flightDirection++)
                    {
                        if (departureDirection == 0 && flightDirection == 0) continue;
                        for (int branch = 0; branch < 2; branch++)
                        {
                            var candidate = TransferSeed(current[6] + departureDirection * departureStep,
                                current[7] - current[6] + flightDirection * flightStep, branch == 1);
                            if (SeedScore(candidate) < SeedScore(next)) next = candidate;
                        }
                    }
                if (ReferenceEquals(next, current))
                {
                    departureStep *= .5;
                    flightStep *= .5;
                    if (Math.Max(departureStep, flightStep) < 2) break;
                }
                else current = next;
            }
            transferSeeds.Add(current);
        }
        var seeds = new List<double[]>();
        foreach (var transfer in transferSeeds)
        {
            double departureTime = transfer[6], flightTime = transfer[7] - transfer[6];
            var dv1 = new double3(transfer[0], transfer[1], transfer[2]);
            var dv2 = new double3(transfer[3], transfer[4], transfer[5]);
            if (!KeplerPropagator.TryPropagateUniversal(a.StartPosition, a.StartVelocity, a.Mu,
                departureTime, out var departurePosition, out var departureVelocity)) continue;
            double3 transferDeparture = departureVelocity + dv1 / 10000;
            int firstFamily = requestedBurnCount == 0 ? (settings.Goal.Mode == ArrivalMode.Intercept ? 1 : 2) : requestedBurnCount;
            int lastFamily = requestedBurnCount == 0 ? 4 : requestedBurnCount;
            for (int family = firstFamily; family <= lastFamily; family++)
            {
                if (settings.Goal.Mode == ArrivalMode.Intercept)
                {
                    // Arrival is a coast endpoint, independent of the final correction burn.
                    var intercept = new double[family * 4 + 2];
                    intercept[0] = dv1.x; intercept[1] = dv1.y; intercept[2] = dv1.z;
                    intercept[family * 3] = departureTime;
                    for (int i = 1; i < family; i++)
                    {
                        double3 correction = math.normalizesafe(transferDeparture) * Math.Max(2, math.length(dv1) * .01);
                        intercept[i * 3] = correction.x; intercept[i * 3 + 1] = correction.y;
                        intercept[i * 3 + 2] = correction.z;
                        intercept[family * 3 + i] = departureTime + flightTime * i / family;
                    }
                    intercept[intercept.Length - 2] = departureTime + flightTime;
                    intercept[intercept.Length - 1] = transfer[8];
                    seeds.Add(intercept);
                    continue;
                }
                var seed = new double[family * 4 + 1];
                seed[0]=dv1.x; seed[1]=dv1.y; seed[2]=dv1.z;
                seed[(family-1)*3]=dv2.x;
                seed[(family-1)*3+1]=dv2.y;
                seed[(family-1)*3+2]=dv2.z;
                seed[family*3]=departureTime;
                seed[family*4-1]=departureTime+flightTime;
                // Seed a real intermediate correction, not a near-zero
                // placeholder that only inflates the reported burn count.
                for (int middle=1; middle<family-1; middle++)
                {
                    double fraction=(double)middle/(family-1);
                    if (!KeplerPropagator.TryPropagateUniversal(departurePosition,
                            transferDeparture,a.Mu,flightTime*fraction,out _,out var midVelocity))
                        midVelocity=transferDeparture;
                    double3 direction=math.normalizesafe(midVelocity,new double3(1,0,0));
                    double middleDeltaV=Math.Max(2.0,
                        (math.length(dv1)+math.length(dv2))*.01);
                    middleDeltaV=Math.Max(middleDeltaV,
                        engine.Thrust/(engine.DryMass+engine.FuelMass)*
                        MinExtraBurnDurationSeconds*2);
                    seed[middle*3]=direction.x*middleDeltaV;
                    seed[middle*3+1]=direction.y*middleDeltaV;
                    seed[middle*3+2]=direction.z*middleDeltaV;
                    seed[family*3+middle]=departureTime+flightTime*fraction;
                }
                seed[seed.Length-1]=transfer[8];
                seeds.Add(seed);
            }
        }
        // Scalar burns can stall because they only adjust tangential thrust.
        // Preserve their best transfer windows when moving to vector correction,
        // instead of throwing away that work and relying only on Lambert seeds.
        var warmSeeds = new List<double[]>();
        if (scalarTrials != null)
            for (int family = 2; family <= 4; family++)
            {
                Trial closest = null;
                foreach (var trial in scalarTrials)
                    if (trial.Burns.Length == family && (closest == null || trial.Cost < closest.Cost))
                        closest = trial;
                if (closest == null) continue;
                var seed = new double[family * 4 + 1];
                bool valid = true;
                for (int i = 0; i < family; i++)
                {
                    var burn = closest.Burns[i];
                    double center = burn.Start + burn.Duration * .5;
                    progress?.BeginEvaluation();
                    if (!TrajectoryMatchedPredictor.TryPropagatePairToTime(a, b, center,
                        cancel, out var state, closest.Burns, 2f))
                    { valid = false; break; }
                    double3 direction = math.normalizesafe(state.VA);
                    double magnitude = closest.DeltaVs[i] * (burn.Type == BurnType.Retrograde ? -1 : 1);
                    seed[i * 3] = direction.x * magnitude;
                    seed[i * 3 + 1] = direction.y * magnitude;
                    seed[i * 3 + 2] = direction.z * magnitude;
                    seed[family * 3 + i] = center;
                }
                if (valid && BuildVectorBurns(seed, engine, epoch, settings, out _, out _, out _, out _))
                    warmSeeds.Add(seed);
            }
        // Reject candidates that cannot be represented by this engine before numerical work.
        seeds.RemoveAll(x => !BuildVectorBurns(x, engine, epoch, settings, out _, out _, out _, out _));
        seeds.Sort((x, y) => (x[x.Length-1] * 1e6 + VectorBudget(x)).CompareTo(
            y[y.Length-1] * 1e6 + VectorBudget(y)));
        if (seeds.Count == 0 && warmSeeds.Count == 0)
            return Fail($"No feasible Lambert transfer fit the planning window and " +
                $"{settings.MaxBurnDeltaV:F0} m/s per-burn limit.");

        double3 RouteOffset(int route, int stage, int count, double3 position, double3 velocity)
        {
            double radius = Math.Max(settings.Goal.DistanceMeters * 2,
                settings.Goal.ContactDistanceMeters + 300);
            var staging = new ArrivalGoal(ArrivalMode.Waypoint, (WaypointDirection)(route - 1),
                radius, settings.Goal.ContactDistanceMeters);
            double3 offset = staging.Offset(position, velocity);
            if (count == 4 && stage == 2)
            {
                double3 final = settings.Goal.Offset(position, velocity);
                offset = math.normalizesafe(offset + math.normalizesafe(final) * (radius / 10000),
                    math.normalizesafe(math.cross(position, velocity))) * (radius / 10000);
            }
            return offset;
        }

        Trial Evaluate(double[] x, float coastStep)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.BeginEvaluation();
            if (!BuildVectorBurns(x, engine, epoch, settings, out var burns, out double fuel,
                    out double dv, out var deltaVs)) return null;
            double end = settings.Goal.Mode == ArrivalMode.Intercept ? x[x.Length - 2] : burns[burns.Length-1].End;
            if (!double.IsFinite(end) || end < burns[burns.Length - 1].End) return null;
            if (end > horizon || !TrajectoryMatchedPredictor.TryPropagatePairToTime(a, b, end,
                    cancel, out var state, burns, coastStep)) return null;
            double3 dr = (state.A - state.B - settings.Goal.Offset(state.B, state.VB)) * 10000;
            double3 velocity = settings.Goal.Mode == ArrivalMode.Intercept ? double3.zero :
                (state.VA - state.VB - settings.Goal.OffsetVelocity(state.B, state.VB, b.Mu)) * 10000;
            double[] residual = { dr.x, dr.y, dr.z, velocity.x * 300,
                velocity.y * 300, velocity.z * 300 };
            // Negative seed metadata identifies a staged approach. Keep the
            // intermediate positions constrained while correcting finite burns.
            int route = x[x.Length - 1] < 0 ? (int)-x[x.Length - 1] : 0;
            if (route > 0)
            {
                int count = burns.Length;
                Array.Resize(ref residual, 6 + 3 * (count - 2));
                for (int stage = 1; stage < count - 1; stage++)
                {
                    double time = x[count * 3 + stage];
                    if (!TrajectoryMatchedPredictor.TryPropagatePairToTime(a, b, time,
                        cancel, out var waypoint, burns, coastStep)) return null;
                    double3 error = (waypoint.A - waypoint.B -
                        RouteOffset(route, stage, count, waypoint.B, waypoint.VB)) * 10000;
                    int index = 6 + 3 * (stage - 1);
                    residual[index] = error.x; residual[index + 1] = error.y; residual[index + 2] = error.z;
                }
            }
            double cost = 0; foreach (double value in residual) cost += value * value;
            return new Trial { Burns=burns, Residual=residual, Cost=cost, Fuel=fuel,
                Dv=dv, End=end, DeltaVs=deltaVs, Parameters=(double[])x.Clone() };
        }

        Trial Correct(double[] seed, float coastStep = 2f, double[] residualBias = null, int iterationLimit = -1)
        {
            Trial Sample(double[] parameters) => ApplyResidualBias(Evaluate(parameters, coastStep), residualBias);
            double[] x = (double[])seed.Clone();
            int count=(x.Length-1)/4;
            int dimension=x.Length-1;
            Trial current = Sample(x);
            if (current == null || Accept(current, settings)) return current;
            int rows = current.Residual.Length;
            double[,] jacobian = new double[rows, dimension];
            bool BuildJacobian()
            {
                for (int j = 0; j < dimension; j++)
                {
                    double[] shifted = (double[])x.Clone();
                    double h = j < count*3 ? .5 : 2;
                    shifted[j] += h;
                    Trial perturbed = Sample(shifted);
                    if (perturbed == null)
                    {
                        h = -h;
                        shifted[j] = x[j] + h;
                        perturbed = Sample(shifted);
                    }
                    if (perturbed == null) return false;
                    for (int i = 0; i < rows; i++)
                        jacobian[i,j] = (perturbed.Residual[i] - current.Residual[i]) / h;
                }
                return true;
            }
            if (!BuildJacobian()) return current;
            double damping=.001;
            int rejected = 0, rebuilds = 0;
            for (int iteration=0; iteration<(iterationLimit < 0 ? settings.VectorIterations : iterationLimit) && !Accept(current, settings); iteration++)
            {
                progress?.SetIteration(progress.Seed, iteration + 1);
                var matrix = new double[dimension, dimension + 1];
                for (int i=0;i<dimension;i++)
                {
                    for (int j=0;j<dimension;j++) for (int k=0;k<rows;k++)
                        matrix[i,j] += jacobian[k,i]*jacobian[k,j];
                    matrix[i,i] += damping*Math.Max(1,matrix[i,i]);
                    for (int k=0;k<rows;k++) matrix[i,dimension] -= jacobian[k,i]*current.Residual[k];
                }
                if (!SolveLinear(matrix,out var change)) break;
                double[] proposal=(double[])x.Clone();
                for(int i=0;i<dimension;i++) proposal[i]+=Math.Clamp(change[i],i<count*3?-100:-60,i<count*3?100:60);
                Trial next=Sample(proposal);
                if(next==null||next.Cost>=current.Cost)
                {
                    damping *= 10;
                    if (++rejected >= 2 && rebuilds < 2)
                    {
                        rebuilds++; rejected = 0; damping = .001;
                        if (!BuildJacobian()) break;
                    }
                    else if (rejected >= 3) break;
                    continue;
                }
                double[] step=new double[dimension]; double denom=0;
                for(int j=0;j<dimension;j++){step[j]=proposal[j]-x[j];denom+=step[j]*step[j];}
                if(denom>1e-12) for(int i=0;i<rows;i++)
                {
                    double js=0; for(int j=0;j<dimension;j++) js+=jacobian[i,j]*step[j];
                    double correction=(next.Residual[i]-current.Residual[i]-js)/denom;
                    for(int j=0;j<dimension;j++) jacobian[i,j]+=correction*step[j];
                }
                x=proposal; current=next; rejected=0; damping=Math.Max(1e-7,damping*.3);
            }
            return current;
        }

        var acceptedTrials = new List<Trial>();
        int firstSearchFamily = requestedBurnCount == 0 ? (settings.Goal.Mode == ArrivalMode.Intercept ? 1 : 2) : requestedBurnCount;
        int lastSearchFamily = requestedBurnCount == 0 ? 4 : requestedBurnCount;
        int evaluated = 0;
        for (int family = firstSearchFamily; family <= lastSearchFamily; family++)
        {
            Trial closest = null;
            bool familyConverged = false;
            int candidateLimit = requestedBurnCount == 0 ? settings.VectorCandidates : settings.ExplicitVectorCandidates;
            var familySeeds = DiverseSeeds(seeds, family, candidateLimit, true);
            foreach (var warm in warmSeeds)
                if ((warm.Length - 1) / 4 == family)
                {
                    if (familySeeds.Count == candidateLimit) familySeeds.RemoveAt(familySeeds.Count - 1);
                    familySeeds.Insert(0, warm);
                }
            foreach (var seed in familySeeds)
            {
                progress?.SetIteration(++evaluated, 0);
                Trial candidate = Correct(seed);
                if (candidate == null) continue;
                if (!HasMeaningfulBurns(candidate)) continue;
                if (closest == null || candidate.Cost < closest.Cost) closest = candidate;
                familyConverged |= WithinValidationTolerance(candidate, settings);
                acceptedTrials.Add(candidate);
            }
            if (!familyConverged && closest != null)
            {
                var rescued = Correct(closest.Parameters, .5f, iterationLimit: Math.Min(6, settings.VectorIterations));
                if (rescued != null && rescued.Cost < closest.Cost && HasMeaningfulBurns(rescued))
                    acceptedTrials.Add(rescued);
            }
        }
        var result = FinishSearch(a, b, cancel, acceptedTrials, timingPreference, settings,
            x => Evaluate(x, 2f),
            (x, bias) => Correct(x, 2f, bias, settings.VectorIterations), true, progress, out string failure);
        if (result != null || settings.Goal.ClearanceMeters <= 0) return result ?? Fail(failure);
        if (requestedBurnCount == 2)
            return Fail(failure + " A routed approach may need 3 or 4 burns; choose Auto to search those routes.");

        // Bounded dogleg search. Every leg has its own Lambert seed, and every
        // staging point remains constrained during correction. Full-path clearance
        // validation below still decides whether a route is safe to offer.
        progress?.SetStage("Searching approach routes");
        transferSeeds.Sort((x, y) => SeedScore(x).CompareTo(SeedScore(y)));
        var routed = new List<Trial>();
        var routeWindows = DiverseSeeds(transferSeeds, 2, 2, true);
        foreach (var window in routeWindows)
        {
            int first = requestedBurnCount == 0 ? 3 : requestedBurnCount;
            int last = requestedBurnCount == 0 ? 4 : requestedBurnCount;
            for (int count = first; count <= last; count++)
                for (int route = 1; route <= 6; route++)
                {
                    cancel.ThrowIfCancellationRequested();
                    double departure = window[6], arrivalTime = window[7], flight = arrivalTime - departure;
                    double approach = Math.Min(flight * .4, Math.Clamp(settings.Goal.DistanceMeters / 2, 300, 1800));
                    var timesAt = new double[count];
                    var positionsAt = new double3[count];
                    var desiredVelocity = new double3[count];
                    timesAt[0] = departure; timesAt[count - 1] = arrivalTime;
                    bool valid = KeplerPropagator.TryPropagateUniversal(a.StartPosition, a.StartVelocity,
                        a.Mu, departure, out positionsAt[0], out desiredVelocity[0]);
                    for (int stage = 1; stage < count && valid; stage++)
                    {
                        if (stage < count - 1) timesAt[stage] = arrivalTime - approach * (count - 1 - stage) / (count - 2);
                        valid = KeplerPropagator.TryPropagateUniversal(b.StartPosition, b.StartVelocity,
                            b.Mu, timesAt[stage], out var targetPosition, out var targetVelocity);
                        positionsAt[stage] = targetPosition + (stage == count - 1
                            ? settings.Goal.Offset(targetPosition, targetVelocity)
                            : RouteOffset(route, stage, count, targetPosition, targetVelocity));
                        desiredVelocity[stage] = targetVelocity + settings.Goal.OffsetVelocity(targetPosition, targetVelocity, b.Mu);
                    }
                    if (!valid) continue;
                    var impulses = new double3[count];
                    double3 incoming = desiredVelocity[0];
                    for (int leg = 0; leg < count - 1 && valid; leg++)
                    {
                        valid = TrySolveLambert(positionsAt[leg], positionsAt[leg + 1], a.Mu,
                            timesAt[leg + 1] - timesAt[leg], false, out var outgoing, out var arriving);
                        impulses[leg] = (outgoing - incoming) * 10000;
                        incoming = arriving;
                    }
                    if (!valid) continue;
                    impulses[count - 1] = (desiredVelocity[count - 1] - incoming) * 10000;
                    var seed = new double[count * 4 + 1];
                    for (int i = 0; i < count; i++)
                    {
                        seed[i * 3] = impulses[i].x; seed[i * 3 + 1] = impulses[i].y;
                        seed[i * 3 + 2] = impulses[i].z; seed[count * 3 + i] = timesAt[i];
                    }
                    seed[seed.Length - 1] = -route;
                    if (!BuildVectorBurns(seed, engine, epoch, settings, out _, out _, out _, out _)) continue;
                    progress?.SetIteration(++evaluated, 0);
                    var trial = Correct(seed);
                    if (trial != null && HasMeaningfulBurns(trial)) routed.Add(trial);
                }
            // Validate after each window so a successful route ends the fallback.
            result = FinishSearch(a, b, cancel, routed, timingPreference, settings,
                x => Evaluate(x, 2f), (x, bias) => Correct(x, 2f, bias), true, progress, out var routeFailure);
            if (result != null) return result;
            if (routed.Count > 0) failure = routeFailure;
            routed.Clear();
        }
        return Fail(failure + " No clear staged approach was found within the route search budget.");
    }

}
