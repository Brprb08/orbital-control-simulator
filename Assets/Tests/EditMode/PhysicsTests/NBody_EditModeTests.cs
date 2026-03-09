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
    public void OrbitalState_constructor_computes_cross_section_area_from_radius()
    {
        double radius = 10.0;

        var state = new NBody.OrbitalState(
            double3.zero,
            double3.zero,
            0f,
            1000.0,
            radius,
            2.2f,
            Vector3.zero
        );

        Assert.AreEqual(Mathf.PI * 100f, (float)state.crossSectionArea, 1e-4f);
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
    public void CalculatePredictedTrajectoryGPU_Async_returns_early_when_no_relevant_bodies()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var sat = CreateLooseBody(rig.Root.transform, "Sat");

        InvokeStart(sat); // _relevantBodies becomes empty list

        bool callbackCalled = false;

        sat.CalculatePredictedTrajectoryGPU_Async(
            100,
            1f,
            _ => callbackCalled = true
        );

        Assert.IsFalse(callbackCalled);
    }
}