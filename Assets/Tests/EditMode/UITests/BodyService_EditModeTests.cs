using NUnit.Framework;
using UnityEngine;
using System.Reflection;

/// <summary>
/// Edit-mode tests for BodyService:
/// verifies registration, deregistration, central body behavior,
/// satellite filtering, events, and safe guard-path behavior.
/// </summary>
public class BodyService_EditModeTests
{
    private SimTestRig rig;

    [TearDown]
    public void TearDown()
    {
        rig?.Dispose();
        rig = null;
    }

    private static void InvokeFixedUpdate(BodyService svc)
    {
        var mi = typeof(BodyService).GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(mi, "Could not find private FixedUpdate() via reflection.");
        mi.Invoke(svc, null);
    }

    private static NBody CreateBody(
        Transform parent,
        string name,
        bool isCentral,
        string tag = "Untagged",
        float radius = 10f,
        float camRadius = 20f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.tag = tag;

        var body = go.AddComponent<NBody>();
        body.isCentralBody = isCentral;
        body.radius = radius;
        body.cameraDistanceRadius = camRadius;
        return body;
    }

    [Test]
    public void Register_sets_central_when_flagged()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        Assert.AreEqual(rig.Earth, rig.BodyService.CentralBody);
        CollectionAssert.Contains(rig.BodyService.Bodies, rig.Earth);
    }

    [Test]
    public void Register_adds_non_central_body_to_bodies()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        var sat = CreateBody(rig.Root.transform, "Sat", isCentral: false, tag: "Satellite");
        rig.BodyService.Register(sat);

        CollectionAssert.Contains(rig.BodyService.Bodies, sat);
    }

    [Test]
    public void Register_does_not_add_duplicate_body()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        var sat = CreateBody(rig.Root.transform, "Sat", isCentral: false, tag: "Satellite");
        rig.BodyService.Register(sat);
        rig.BodyService.Register(sat);

        int count = 0;
        foreach (var b in rig.BodyService.Bodies)
        {
            if (b == sat) count++;
        }

        Assert.AreEqual(1, count);
    }

    [Test]
    public void Register_second_central_body_replaces_CentralBody_reference()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        var otherCentral = CreateBody(rig.Root.transform, "Mars", isCentral: true, tag: "Untagged");
        rig.BodyService.Register(otherCentral);

        Assert.AreEqual(otherCentral, rig.BodyService.CentralBody);
        CollectionAssert.Contains(rig.BodyService.Bodies, rig.Earth);
        CollectionAssert.Contains(rig.BodyService.Bodies, otherCentral);
    }

    [Test]
    public void GetSatellites_filters_by_tag()
    {
        rig = SimTestBootstrap.CreateBasic(2);

        var sats = rig.BodyService.GetSatellites();
        Assert.AreEqual(2, sats.Count);
        Assert.AreEqual("Sat1", sats[0].name);
        Assert.AreEqual("Sat2", sats[1].name);
    }

    [Test]
    public void GetSatellites_excludes_untagged_non_central_body()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        var sat = CreateBody(rig.Root.transform, "NotTaggedSat", isCentral: false, tag: "Untagged");
        rig.BodyService.Register(sat);

        var sats = rig.BodyService.GetSatellites();

        CollectionAssert.DoesNotContain(sats, sat);
    }

    [Test]
    public void GetSatellites_excludes_central_body_even_if_not_untagged()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        var sats = rig.BodyService.GetSatellites();

        CollectionAssert.DoesNotContain(sats, rig.Earth);
    }

    [Test]
    public void Deregister_clears_central_if_removed()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        Assert.NotNull(rig.BodyService.CentralBody);

        rig.BodyService.Deregister(rig.Earth);

        Assert.IsNull(rig.BodyService.CentralBody);
    }

    [Test]
    public void Deregister_removes_non_central_body_from_bodies()
    {
        rig = SimTestBootstrap.CreateBasic(1);
        var sat = rig.Satellites[0];

        rig.BodyService.Deregister(sat);

        CollectionAssert.DoesNotContain(rig.BodyService.Bodies, sat);
    }

    [Test]
    public void Deregister_removes_satellite_from_GetSatellites()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        var sat = rig.Satellites[0];

        rig.BodyService.Deregister(sat);

        var sats = rig.BodyService.GetSatellites();
        CollectionAssert.DoesNotContain(sats, sat);
        Assert.AreEqual(1, sats.Count);
    }

    [Test]
    public void Deregister_unknown_body_is_noop()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        var stray = CreateBody(rig.Root.transform, "Stray", isCentral: false, tag: "Satellite");

        Assert.DoesNotThrow(() => rig.BodyService.Deregister(stray));
        Assert.AreEqual(rig.Earth, rig.BodyService.CentralBody);
    }

    [Test]
    public void Deregister_null_is_noop()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        Assert.DoesNotThrow(() => rig.BodyService.Deregister(null));
        Assert.AreEqual(rig.Earth, rig.BodyService.CentralBody);
    }

    [Test]
    public void Register_raises_BodyAdded()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        NBody added = null;
        rig.BodyService.BodyAdded += b => added = b;

        var sat = CreateBody(rig.Root.transform, "Sat", isCentral: false, tag: "Satellite");
        rig.BodyService.Register(sat);

        Assert.AreEqual(sat, added);
    }

    [Test]
    public void Deregister_raises_BodyRemoved()
    {
        rig = SimTestBootstrap.CreateBasic(1);

        NBody removed = null;
        rig.BodyService.BodyRemoved += b => removed = b;

        var sat = rig.Satellites[0];
        rig.BodyService.Deregister(sat);

        Assert.AreEqual(sat, removed);
    }

    [Test]
    public void Register_duplicate_does_not_raise_BodyAdded_twice()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        int addedCount = 0;
        rig.BodyService.BodyAdded += _ => addedCount++;

        var sat = CreateBody(rig.Root.transform, "Sat", isCentral: false, tag: "Satellite");
        rig.BodyService.Register(sat);
        rig.BodyService.Register(sat);

        Assert.AreEqual(1, addedCount);
    }

    [Test]
    public void Deregister_unknown_body_does_not_raise_BodyRemoved()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        int removedCount = 0;
        rig.BodyService.BodyRemoved += _ => removedCount++;

        var stray = CreateBody(rig.Root.transform, "Stray", isCentral: false, tag: "Satellite");
        rig.BodyService.Deregister(stray);

        Assert.AreEqual(0, removedCount);
    }

    [Test]
    public void Register_adds_AttitudeController_to_non_central_body()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        var sat = CreateBody(rig.Root.transform, "Sat", isCentral: false, tag: "Satellite");
        Assert.IsFalse(sat.TryGetComponent<AttitudeController>(out _));

        rig.BodyService.Register(sat);

        Assert.IsTrue(sat.TryGetComponent<AttitudeController>(out var att));
        Assert.NotNull(att);
        Assert.IsTrue(att.enabled);
    }

    [Test]
    public void Register_adds_and_disables_AttitudeController_for_central_body()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        Assert.IsTrue(rig.Earth.TryGetComponent<AttitudeController>(out var att));
        Assert.NotNull(att);
        Assert.IsFalse(att.enabled);
    }

    [Test]
    public void Register_does_not_duplicate_existing_AttitudeController()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        var sat = CreateBody(rig.Root.transform, "Sat", isCentral: false, tag: "Satellite");
        sat.gameObject.AddComponent<AttitudeController>();

        rig.BodyService.Register(sat);

        var all = sat.GetComponents<AttitudeController>();
        Assert.AreEqual(1, all.Length);
    }

    [Test]
    public void FixedUpdate_does_nothing_when_DrivePhysics_is_false()
    {
        rig = SimTestBootstrap.CreateBasic(1);
        rig.BodyService.DrivePhysics = false;

        Assert.DoesNotThrow(() => InvokeFixedUpdate(rig.BodyService));
    }

    [Test]
    public void FixedUpdate_does_nothing_when_no_satellites_exist()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        rig.BodyService.DrivePhysics = true;

        Assert.DoesNotThrow(() => InvokeFixedUpdate(rig.BodyService));
    }

    [Test]
    public void Initialize_bootstrap_registers_existing_bodies()
    {
        rig = SimTestBootstrap.CreateBasic(2);

        CollectionAssert.Contains(rig.BodyService.Bodies, rig.Earth);
        CollectionAssert.Contains(rig.BodyService.Bodies, rig.Satellites[0]);
        CollectionAssert.Contains(rig.BodyService.Bodies, rig.Satellites[1]);
        Assert.AreEqual(3, rig.BodyService.Bodies.Count);
    }

    [Test]
    public void Bodies_contains_registered_bodies_in_registration_order_for_bootstrap_case()
    {
        rig = SimTestBootstrap.CreateBasic(2);

        Assert.AreEqual("Earth", rig.BodyService.Bodies[0].name);
        Assert.AreEqual("Sat1", rig.BodyService.Bodies[1].name);
        Assert.AreEqual("Sat2", rig.BodyService.Bodies[2].name);
    }
}