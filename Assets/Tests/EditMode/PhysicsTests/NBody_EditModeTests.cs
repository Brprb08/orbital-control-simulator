using NUnit.Framework;
using UnityEngine;
using Unity.Mathematics;
using System.Reflection;
using System.Collections.Generic;

/// <summary>
/// Edit-mode tests for NBody:
/// - initialization
/// - altitude
/// - force accumulation
/// - sync from batch state
/// - basic prediction wrapper guards
/// </summary>
public class NBody_EditModeTests
{
    private SimTestRig rig;

    [Test]
    public void Fuel_adds_to_physics_mass_without_changing_dry_mass_or_motion()
    {
        rig = SimTestBootstrap.CreateBasic(1);
        var body = rig.Satellites[0];
        body.state.position = new double3(700, 2, 3);
        body.state.velocity = new double3(0, 0, 0.75);
        Assert.IsTrue(body.TrySetMassesKilograms(500, 100));
        Assert.AreEqual(500, body.trueMass);
        Assert.AreEqual(100, body.FuelMassKilograms);
        Assert.AreEqual(600, body.TotalMassKilograms);
        Assert.AreEqual(600, body.state.mass);
        Assert.AreEqual(10000.0 / 600.0, body.FullThrustAccelerationMetersPerSecondSquared, 1e-10);
        Assert.IsTrue(body.TrySetMassKilograms(900));
        Assert.AreEqual(100, body.FuelMassKilograms);
        Assert.AreEqual(1000, body.state.mass);
        Assert.AreEqual(new double3(700, 2, 3), body.state.position);
        Assert.AreEqual(new double3(0, 0, 0.75), body.state.velocity);
        Assert.IsFalse(body.TrySetFuelMassKilograms(-1));
        Assert.IsFalse(body.TrySetFuelMassKilograms(double.NaN));
        Assert.AreEqual(1000, body.state.mass);
        body.isThrusting = true;
        Assert.IsFalse(body.TrySetFuelMassKilograms(0));
        body.isThrusting = false;
        Assert.IsTrue(body.TrySetFuelMassKilograms(0));
        Assert.AreEqual(900, body.state.mass);
    }

    [Test]
    public void Start_preserves_dry_mass_and_loads_fuel_into_physics_mass()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var body = CreateLooseBody(rig.Root.transform);
        Assert.AreEqual(0, body.FuelMassKilograms, "Existing scene bodies must not gain default placement fuel.");
        body.TrySetMassesKilograms(420000, 100);
        InvokeStart(body);
        Assert.AreEqual(420000, body.trueMass);
        Assert.AreEqual(420100, body.state.mass);
    }

    [Test]
    public void Propulsion_is_per_body_and_mass_edits_update_physics_without_resetting_motion()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        var a = rig.Satellites[0];
        var b = rig.Satellites[1];
        a.state.position = new double3(700, 2, 3);
        a.state.velocity = new double3(0, 0, 0.75);
        Assert.IsTrue(a.TrySetMassKilograms(500));
        Assert.IsTrue(a.TryConfigurePropulsion(10000f, 320f, 0.5f));
        Assert.AreEqual(10.0, a.FullThrustAccelerationMetersPerSecondSquared);
        Assert.AreEqual(10000f, b.EffectiveThrustNewtons);
        Assert.IsTrue(a.UnlimitedPropellant);
        a.mass = 1000f;
        Assert.AreEqual(1000.0, a.state.mass);
        Assert.AreEqual(5.0, a.FullThrustAccelerationMetersPerSecondSquared);
        Assert.AreEqual(new double3(700, 2, 3), a.state.position);
        Assert.AreEqual(new double3(0, 0, 0.75), a.state.velocity);
    }

    [Test]
    public void Propulsion_rejects_invalid_values_and_edits_during_burns()
    {
        rig = SimTestBootstrap.CreateBasic(1);
        var body = rig.Satellites[0];
        Assert.IsFalse(body.TryConfigurePropulsion(float.NaN, 300));
        Assert.IsFalse(body.TryConfigurePropulsion(10000, 0));
        Assert.IsFalse(body.TryConfigurePropulsion(float.MaxValue, 300, 2));
        Assert.IsFalse(body.TrySetMassKilograms(double.PositiveInfinity));
        Assert.IsFalse(body.TrySetMassKilograms(0));
        body.isThrusting = true;
        Assert.IsFalse(body.TryConfigurePropulsion(5000, 300));
        Assert.IsFalse(body.TrySetMassKilograms(500));
        body.isThrusting = false;
        Assert.IsTrue(body.TryConfigurePropulsion(0, 300));
        Assert.AreEqual(0, body.EffectiveThrustNewtons);
    }

    [Test]
    public void Legacy_thrust_defaults_migrate_once_and_custom_configuration_survives_initialization()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var controller = rig.Root.AddComponent<ThrustController>();
        typeof(ThrustController).GetField("legacyThrustKilonewtons", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, 12f);
        typeof(ThrustController).GetField("legacyThrustScale", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, 2f);
        rig.Ctx.ThrustController = controller;
        var body = CreateLooseBody(rig.Root.transform);
        body.Initialize(rig.Ctx);
        Assert.AreEqual(12000f, body.EngineThrustNewtons);
        Assert.AreEqual(24000f, body.EffectiveThrustNewtons);
        Assert.IsTrue(body.TryConfigurePropulsion(250, 900));
        body.Initialize(rig.Ctx);
        Assert.AreEqual(250f, body.EffectiveThrustNewtons);
        Assert.AreEqual(900f, body.SpecificImpulseSeconds);
    }

    [TestCase(BurnType.Prograde, 500)]
    [TestCase(BurnType.Retrograde, 500)]
    [TestCase(BurnType.RadialIn, 500)]
    [TestCase(BurnType.RadialOut, 500)]
    [TestCase(BurnType.Normal, 500)]
    [TestCase(BurnType.AntiNormal, 500)]
    [TestCase(BurnType.Prograde, 1000)]
    [TestCase(BurnType.Normal, 1000)]
    public void Manual_and_node_commands_use_newtons_and_native_delta_v_counts_every_direction(BurnType type, double kilograms)
    {
        rig = SimTestBootstrap.CreateBasic(1);
        var body = rig.Satellites[0];
        body.TrySetMassKilograms(kilograms);
        body.TryConfigurePropulsion(10000f, 300f);
        var pos = new Vector3(700, 0, 0);
        var vel = new Vector3(0, 0, 0.75f);
        var vCache = Vector3.right;
        var hCache = Vector3.up;
        Assert.IsTrue(ManeuverBurnMath.TryBuildBurnCommand(type, pos, vel, Vector3.zero,
            body.EffectiveThrustNewtons, ref vCache, ref hCache, out var force, out var normal));
        Assert.AreEqual(1f, force.magnitude, 1e-6f);
        var controller = rig.Root.AddComponent<ThrustController>();
        body.state.force = Vector3.zero;
        controller.ApplyThrust(body, force.normalized);
        Assert.Less(Vector3.Distance(force, body.state.force), 1e-6f);
        var dv = new double[1];
        NativePhysics.BatchTwoBodyIntegrateMuExWithDeltaV(
            new[] { new double3(700, 0, 0) }, new[] { new double3(0, 0, 0.75) },
            new[] { body.state.mass }, new[] { body.state.force }, new float[1], new float[1],
            new[] { normal }, new byte[] { 1 }, new sbyte[1], 1, 398.6004418, 1f, 50, dv);
        Assert.AreEqual(10000.0 / kilograms, dv[0] * SimulationUnits.MetersPerUnit, 1e-5);
        Assert.AreEqual(kilograms, body.state.mass, "Unlimited propellant must not consume mass.");
    }

    [TestCase(BurnType.Prograde)]
    [TestCase(BurnType.Retrograde)]
    [TestCase(BurnType.RadialIn)]
    [TestCase(BurnType.RadialOut)]
    [TestCase(BurnType.Normal)]
    [TestCase(BurnType.AntiNormal)]
    public void Maneuver_preview_delta_v_matches_execution_across_partial_step_boundaries(BurnType type)
    {
        rig = SimTestBootstrap.CreateBasic(1);
        var body = rig.Satellites[0];
        body.TrySetMassesKilograms(400, 100);
        body.TryConfigurePropulsion(10000, 300);
        body.dragCoefficient = 0;
        body.state.position = new double3(700, 0, 0);
        body.state.velocity = new double3(0, 0, 0.75);
        rig.Earth.state.position = double3.zero;
        var runtime = rig.Root.AddComponent<BodyRuntimeCoordinator>();
        rig.Ctx.BodyRuntimeCoordinator = runtime;
        runtime.Initialize(rig.Ctx);
        var thrust = rig.Root.AddComponent<ThrustController>();
        thrust.thrustParticles = new GameObject("Test exhaust").AddComponent<ParticleSystem>();
        rig.Ctx.ThrustController = thrust;
        thrust.Initialize(rig.Ctx);
        var manager = rig.Root.AddComponent<ManeuverNodeManager>();
        rig.Ctx.ManeuverNodeManager = manager;
        var node = new ManeuverNode { targetBody = body, burnType = type, burnTime = 0.005f, duration = 0.047f, isFinalized = true };
        typeof(ManeuverNodeManager).GetProperty("CurrentNode").SetValue(manager, node);
        Assert.IsFalse(body.TryConfigurePropulsion(20000, 300));
        Assert.IsFalse(body.TrySetMassKilograms(1000));
        Assert.IsFalse(body.TrySetFuelMassKilograms(0));
        var renderer = rig.Root.AddComponent<TrajectoryRenderer>();
        var preview = rig.Root.AddComponent<ManeuverPreviewController>();
        preview.Initialize(rig.BodyService, runtime, renderer);
        try
        {
            preview.RequestReadoutRefresh(node, immediate: true);
            Assert.AreEqual(0.94, node.predictedPropulsiveDeltaVMetersPerSecond, 1e-5);
            Assert.AreEqual(0.0, body.DeliveredDeltaVMetersPerSecond, "Preview must not mutate live telemetry.");
            object stepper = typeof(BodyService).GetField("physicsStepper", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rig.BodyService);
            stepper.GetType().GetMethod("Step").Invoke(stepper, new object[] { 0.1f });
            Assert.AreEqual(node.predictedPropulsiveDeltaVMetersPerSecond, body.DeliveredDeltaVMetersPerSecond, 1e-5);
            Assert.AreEqual(500.0, body.state.mass);
            Assert.AreEqual(100.0, body.FuelMassKilograms, "Fuel is not consumed in this phase.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(thrust.thrustParticles.gameObject);
        }
    }

    [TearDown]
    public void TearDown()
    {
        rig?.Dispose();
        rig = null;
    }

    private static void InvokeStart(NBody body)
    {
        var mi = typeof(NBody).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(mi, "Could not find private Start() via reflection.");
        mi.Invoke(body, null);
    }

    private static NBody CreateLooseBody(Transform parent, string name = "Body")
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<NBody>();
    }

    [Test]
    public void Start_sets_velocity_zero_for_central_body()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var earth = rig.Earth;
        earth.velocity = new Vector3(1f, 2f, 3f);

        InvokeStart(earth);

        Assert.AreEqual(Vector3.zero, earth.velocity);
    }

    [Test]
    public void Start_keeps_velocity_for_non_central_body()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var sat = CreateLooseBody(rig.Root.transform, "Sat");
        sat.isCentralBody = false;
        sat.velocity = new Vector3(4f, 5f, 6f);

        InvokeStart(sat);

        Assert.AreEqual(new Vector3(4f, 5f, 6f), sat.velocity);
    }

    [Test]
    public void Start_initializes_state_from_transform_and_velocity()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var sat = CreateLooseBody(rig.Root.transform, "Sat");

        sat.transform.position = new Vector3(10f, 20f, 30f);
        sat.velocity = new Vector3(1f, 2f, 3f);
        sat.trueMass = 1234.0;
        sat.radius = 9f;
        sat.dragCoefficient = 2.5f;

        InvokeStart(sat);

        Assert.AreEqual(10.0, sat.state.position.x, 1e-6);
        Assert.AreEqual(20.0, sat.state.position.y, 1e-6);
        Assert.AreEqual(30.0, sat.state.position.z, 1e-6);

        Assert.AreEqual(1.0, sat.state.velocity.x, 1e-6);
        Assert.AreEqual(2.0, sat.state.velocity.y, 1e-6);
        Assert.AreEqual(3.0, sat.state.velocity.z, 1e-6);

        Assert.AreEqual(1234.0, sat.state.mass, 1e-6);
        Assert.AreEqual(9.0, sat.state.radius, 1e-6);
        Assert.AreEqual(2.5f, sat.state.dragCoefficient);
    }

    [Test]
    public void Altitude_returns_distance_minus_earth_radius()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var sat = new GameObject("Sat").AddComponent<NBody>();
        sat.transform.SetParent(rig.Root.transform, false);

        const float earthR = 637.8137f;
        var pos = new Vector3(earthR + 100f, 0f, 0f);

        sat.transform.position = pos;
        sat.state = new NBody.OrbitalState(
            new double3(pos.x, pos.y, pos.z),
            double3.zero,
            0f,
            sat.trueMass,
            sat.radius,
            sat.dragCoefficient,
            Vector3.zero
        );

        double alt = sat.altitude;
        Assert.AreEqual(100.0, alt, 0.05);
    }

    [Test]
    public void Altitude_is_negative_inside_earth_radius()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var sat = CreateLooseBody(rig.Root.transform, "Sat");

        sat.state = new NBody.OrbitalState(
            new double3(600.0, 0.0, 0.0),
            double3.zero,
            0f,
            sat.trueMass,
            sat.radius,
            sat.dragCoefficient,
            Vector3.zero
        );

        Assert.That(sat.altitude, Is.LessThan(0.0));
    }

    [Test]
    public void Altitude_at_earth_radius_is_zeroish()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var sat = CreateLooseBody(rig.Root.transform, "Sat");

        sat.state = new NBody.OrbitalState(
            new double3(637.8137, 0.0, 0.0),
            double3.zero,
            0f,
            sat.trueMass,
            sat.radius,
            sat.dragCoefficient,
            Vector3.zero
        );

        Assert.AreEqual(0.0, sat.altitude, 1e-4);
    }

    [Test]
    public void AddForce_accumulates_force_once()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var sat = CreateLooseBody(rig.Root.transform, "Sat");

        sat.state = new NBody.OrbitalState(
            double3.zero,
            double3.zero,
            0f,
            sat.trueMass,
            sat.radius,
            sat.dragCoefficient,
            Vector3.zero
        );

        sat.AddForce(new Vector3(1f, 2f, 3f));

        Assert.AreEqual(new Vector3(1f, 2f, 3f), sat.state.force);
    }

    [Test]
    public void AddForce_accumulates_force_multiple_times()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var sat = CreateLooseBody(rig.Root.transform, "Sat");

        sat.state = new NBody.OrbitalState(
            double3.zero,
            double3.zero,
            0f,
            sat.trueMass,
            sat.radius,
            sat.dragCoefficient,
            Vector3.zero
        );

        sat.AddForce(new Vector3(1f, 0f, 0f));
        sat.AddForce(new Vector3(0f, 2f, 0f));
        sat.AddForce(new Vector3(0f, 0f, 3f));

        Assert.AreEqual(new Vector3(1f, 2f, 3f), sat.state.force);
    }

    [Test]
    public void SyncAfterBatch_copies_state_position_to_transform()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var sat = CreateLooseBody(rig.Root.transform, "Sat");

        sat.state = new NBody.OrbitalState(
            new double3(100.0, 200.0, 300.0),
            double3.zero,
            0f,
            sat.trueMass,
            sat.radius,
            sat.dragCoefficient,
            new Vector3(9f, 9f, 9f)
        );

        sat.SyncAfterBatch();

        Assert.AreEqual(new Vector3(100f, 200f, 300f), sat.transform.position);
    }

    [Test]
    public void SyncAfterBatch_copies_state_velocity_to_velocity_field()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var sat = CreateLooseBody(rig.Root.transform, "Sat");

        sat.state = new NBody.OrbitalState(
            double3.zero,
            new double3(7.0, 8.0, 9.0),
            0f,
            sat.trueMass,
            sat.radius,
            sat.dragCoefficient,
            Vector3.zero
        );

        sat.SyncAfterBatch();

        Assert.AreEqual(new Vector3(7f, 8f, 9f), sat.velocity);
    }

    [Test]
    public void SyncAfterBatch_clears_force_after_sync()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var sat = CreateLooseBody(rig.Root.transform, "Sat");

        sat.state = new NBody.OrbitalState(
            double3.zero,
            double3.zero,
            0f,
            sat.trueMass,
            sat.radius,
            sat.dragCoefficient,
            new Vector3(5f, 6f, 7f)
        );

        sat.SyncAfterBatch();

        Assert.AreEqual(Vector3.zero, sat.state.force);
    }

    [Test]
    public void Drag_area_is_independent_of_radius_and_fuel_and_supports_an_override()
    {
        rig = SimTestBootstrap.CreateBasic(1);
        var body = rig.Satellites[0];
        Assert.IsTrue(body.TrySetMassesKilograms(500, 100));
        Assert.IsTrue(body.TrySetDragAreaSquareMeters(0));
        Assert.AreEqual(2.0, body.DragAreaSquareMeters, 1e-12);
        Assert.AreEqual(2e-8, body.DragAreaSquareUnits, 1e-16);
        body.radius = 100f;
        Assert.IsTrue(body.TrySetFuelMassKilograms(5000));
        Assert.AreEqual(2.0, body.DragAreaSquareMeters, 1e-12);
        Assert.IsTrue(body.TrySetMassKilograms(4000));
        Assert.AreEqual(8.0, body.DragAreaSquareMeters, 1e-12);
        Assert.IsTrue(body.TrySetDragAreaSquareMeters(7));
        Assert.IsTrue(body.TrySetMassKilograms(500));
        Assert.AreEqual(7.0, body.DragAreaSquareMeters);
        Assert.IsFalse(body.TrySetDragAreaSquareMeters(-1));
        Assert.IsFalse(body.TrySetDragAreaSquareMeters(float.NaN));
        Assert.IsFalse(body.TrySetDragAreaSquareMeters(float.PositiveInfinity));
        body.isThrusting = true;
        Assert.IsFalse(body.TrySetDragAreaSquareMeters(0));
        body.isThrusting = false;
        Assert.IsTrue(body.TrySetDragAreaSquareMeters(0));
        Assert.AreEqual(2.0, body.DragAreaSquareMeters, 1e-12);
    }

    [Test]
    public void OrbitalState_constructor_uses_default_central_mass_when_zero_or_negative()
    {
        var stateZero = new NBody.OrbitalState(
            double3.zero,
            double3.zero,
            0f,
            1000.0,
            5.0,
            2.2f,
            Vector3.zero
        );

        var stateNegative = new NBody.OrbitalState(
            double3.zero,
            double3.zero,
            -1f,
            1000.0,
            5.0,
            2.2f,
            Vector3.zero
        );

        Assert.AreEqual(5.972e24f, stateZero.centralBodyMass);
        Assert.AreEqual(5.972e24f, stateNegative.centralBodyMass);
    }

    [Test]
    public void OrbitalState_constructor_keeps_positive_central_mass()
    {
        var state = new NBody.OrbitalState(
            double3.zero,
            double3.zero,
            42f,
            1000.0,
            5.0,
            2.2f,
            Vector3.zero
        );

        Assert.AreEqual(42f, state.centralBodyMass);
    }

    [Test]
    public void CalculatePredictedTrajectoryGPU_Async_completes_once_with_empty_result_when_no_relevant_bodies()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var sat = CreateLooseBody(rig.Root.transform, "Sat");

        InvokeStart(sat); // _relevantBodies becomes empty list

        int callbackCount = 0;
        Vector3[] result = null;

        sat.CalculatePredictedTrajectoryGPU_Async(
            100,
            1f,
            points =>
            {
                callbackCount++;
                result = points;
            }
        );

        Assert.AreEqual(1, callbackCount, "Even an empty prediction must complete its request.");
        Assert.IsNotNull(result);
        Assert.IsEmpty(result);
    }
}
