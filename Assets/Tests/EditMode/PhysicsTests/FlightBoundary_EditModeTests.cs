using System;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

public class FlightBoundary_EditModeTests
{
    [Test]
    public void Finite_fuel_burn_stops_at_empty_tank_and_does_not_add_delta_v_afterward()
    {
        var positions = new[] { new double3(700, 0, 0) };
        var velocities = new[] { new double3(0, 0.75, 0) };
        var masses = new[] { 101.0 };
        var thrusts = new[] { Vector3.up };
        var dry = new[] { 100.0 };
        var fuel = new[] { 1.0 };
        var deltaV = new double[1];
        var parity = new sbyte[1];
        void Step() => NativePhysics.BatchTwoBodyIntegrateMuExWithFuel(
            positions, velocities, masses, thrusts, new float[1], new float[1],
            new sbyte[1], new byte[] { 1 }, parity, 1, 398.6004418,
            0.2f, 20, deltaV, dry, fuel, new[] { 100.0 }, new byte[] { 1 });

        Step();
        Assert.AreEqual(0, fuel[0], 1e-9);
        Assert.Greater(deltaV[0], 0);
        Assert.Less(deltaV[0] * SimulationUnits.MetersPerUnit, 11,
            "A finite tank must cap delivered delta-v near the rocket-equation budget.");
        Step();
        Assert.AreEqual(0, deltaV[0], 1e-10,
            "The next integration batch must not thrust on an empty tank.");
    }

    [Test]
    public void Native_prediction_honors_cancellation_before_entering_integration()
    {
        var work = new TrajectoryMatchedPredictionWorkItem(
            new double3(700, 0, 0), new double3(0, 0.75, 0),
            100, 0, 0, 398.6004418, 100000, 0.02f, 5000);
        Assert.Throws<OperationCanceledException>(() =>
            TrajectoryMatchedPredictor.Predict(work, new CancellationToken(true)));
    }

    [Test, Timeout(5000)]
    public void Native_prediction_stops_when_canceled_during_a_long_request()
    {
        var work = new TrajectoryMatchedPredictionWorkItem(
            new double3(700, 0, 0), new double3(0, 0.75, 0),
            100, 0, 0, 398.6004418, 10000000, 0.02f, 10000);
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(20);
        Assert.Throws<OperationCanceledException>(() =>
            TrajectoryMatchedPredictor.Predict(work, cancellation.Token));
    }

    [Test]
    public void Native_prediction_still_returns_expected_sample_count_without_cancellation()
    {
        var work = new TrajectoryMatchedPredictionWorkItem(
            new double3(700, 0, 0), new double3(0, 0.75, 0),
            100, 0, 0, 398.6004418, 10, 0.02f, 10);
        var result = TrajectoryMatchedPredictor.Predict(work);
        Assert.AreEqual(10, result.Points.Length);
        Assert.AreEqual(0.02f, result.SampleDeltaTime, 1e-6f);
        Assert.Greater(result.Points[0].y, 0);
    }

    [Test]
    public void Two_scheduled_burns_change_controlled_flight_without_moving_the_target()
    {
        var controlled = Work(new double3(700, 0, 0));
        var target = Work(new double3(701, 0, 0));
        var burns = new[] {
            new TrajectoryMatchedPredictor.EncounterBurn(0.1, 0.2, BurnType.Prograde,
                10000, 100, 0, 300, false),
            new TrajectoryMatchedPredictor.EncounterBurn(0.5, 0.2, BurnType.Prograde,
                10000, 100, 0, 300, false)
        };
        Assert.IsTrue(TrajectoryMatchedPredictor.TryPropagatePairToTime(controlled, target,
            1, CancellationToken.None, out var coast));
        Assert.IsTrue(TrajectoryMatchedPredictor.TryPropagatePairToTime(controlled, target,
            1, CancellationToken.None, out var powered, burns));
        Assert.Greater(math.distance(coast.A, powered.A), 1e-4);
        Assert.Less(math.distance(coast.B, powered.B), 1e-6,
            "Different burn boundaries change integration chunking; target drift must stay below 1 cm.");
        Assert.Greater(math.length(powered.VA), math.length(coast.VA));
    }

    [Test]
    public void Overlapping_scheduled_burns_are_rejected_before_prediction()
    {
        var controlled = Work(new double3(700, 0, 0));
        var target = Work(new double3(701, 0, 0));
        var burns = new[] {
            new TrajectoryMatchedPredictor.EncounterBurn(0.1, 0.5, BurnType.Prograde,
                10000, 100, 0, 300, false),
            new TrajectoryMatchedPredictor.EncounterBurn(0.3, 0.2, BurnType.Prograde,
                10000, 100, 0, 300, false)
        };
        Assert.IsFalse(TrajectoryMatchedPredictor.TryPropagatePairToTime(controlled, target,
            1, CancellationToken.None, out _, burns));
    }

    [Test]
    public void Large_simulation_step_respects_burn_window_and_fuel_exhaustion()
    {
        var rig = SimTestBootstrap.CreateBasic(1);
        var exhaust = new GameObject("Test exhaust").AddComponent<ParticleSystem>();
        try
        {
            var body = rig.Satellites[0];
            Assert.IsTrue(body.TrySetMassesKilograms(100, 1));
            Assert.IsTrue(body.TryConfigurePropulsion(10000, 100));
            Assert.IsTrue(body.TrySetUnlimitedPropellant(false));
            body.dragCoefficient = 0;
            body.state.position = new double3(700, 0, 0);
            body.state.velocity = new double3(0, 0.75, 0);
            rig.Earth.state.position = double3.zero;
            var runtime = rig.Root.AddComponent<BodyRuntimeCoordinator>();
            rig.Ctx.BodyRuntimeCoordinator = runtime;
            runtime.Initialize(rig.Ctx);
            var thrust = rig.Root.AddComponent<ThrustController>();
            thrust.thrustParticles = exhaust;
            rig.Ctx.ThrustController = thrust;
            thrust.Initialize(rig.Ctx);
            var manager = rig.Root.AddComponent<ManeuverNodeManager>();
            rig.Ctx.ManeuverNodeManager = manager;
            var node = new ManeuverNode { targetBody = body, burnType = BurnType.Prograde,
                burnTime = 0.005f, duration = 1f, isFinalized = true };
            typeof(ManeuverNodeManager).GetProperty("CurrentNode").SetValue(manager, node);

            object stepper = typeof(BodyService)
                .GetField("physicsStepper", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(rig.BodyService);
            stepper.GetType().GetMethod("Step").Invoke(stepper, new object[] { 1.1f });

            Assert.AreEqual(1.1, runtime.SimulationTimeSeconds, 1e-5);
            Assert.AreEqual(0, body.FuelMassKilograms, 1e-6);
            Assert.AreEqual(100, body.state.mass, 1e-6);
            Assert.Greater(body.DeliveredDeltaVMetersPerSecond, 0);
            Assert.Less(body.DeliveredDeltaVMetersPerSecond, 11,
                "Delivery must stop at the finite tank budget even when warp crosses burn end.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(exhaust.gameObject);
            rig.Dispose();
        }
    }

    private static TrajectoryMatchedPredictionWorkItem Work(double3 position) =>
        new TrajectoryMatchedPredictionWorkItem(position, new double3(0, 0.75, 0),
            100, 0, 0, 398.6004418, 50, 0.02f, 50);
}
