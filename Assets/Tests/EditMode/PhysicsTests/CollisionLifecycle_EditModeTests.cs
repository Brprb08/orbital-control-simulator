using System.Reflection;
using NUnit.Framework;
using Unity.Mathematics;

public class CollisionLifecycle_EditModeTests
{
    private SimTestRig rig;
    private BodyRuntimeCoordinator runtime;

    [SetUp]
    public void SetUp()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        runtime = rig.Root.AddComponent<BodyRuntimeCoordinator>();
        rig.Ctx.BodyRuntimeCoordinator = runtime;
        runtime.Initialize(rig.Ctx);
        rig.Earth.state.position = double3.zero;
    }

    [TearDown]
    public void TearDown() => rig?.Dispose();

    [Test]
    public void Satellite_collision_queues_both_bodies_and_emits_one_event()
    {
        int events = 0;
        double3 contact = default;
        runtime.SatellitesCollided += (a, b, position) => { events++; contact = position; };
        var collision = typeof(BodyRuntimeCoordinator).GetMethod("HandleSatelliteCollision",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var position = new double3(700, 1, 2);
        collision.Invoke(runtime, new object[] { rig.Satellites[0], rig.Satellites[1], position });
        collision.Invoke(runtime, new object[] { rig.Satellites[0], rig.Satellites[1], position });

        Assert.AreEqual(1, events);
        Assert.AreEqual(position, contact);
        Assert.IsTrue(IsPending(rig.Satellites[0]));
        Assert.IsTrue(IsPending(rig.Satellites[1]));
        Assert.IsFalse(IsPending(rig.Earth));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Earth_impact_queues_satellite_without_queuing_central_body(bool satelliteHeavier)
    {
        var satellite = rig.Satellites[0];
        if (satelliteHeavier)
            Assert.IsTrue(satellite.TrySetMassKilograms(rig.Earth.TotalMassKilograms * 2));
        satellite.state.position = new double3(rig.Earth.radius + satellite.radius - 0.01, 0, 0);
        typeof(BodyRuntimeCoordinator).GetMethod("CheckPostStepRemoval",
            BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(runtime, new object[] { satellite });

        Assert.IsTrue(IsPending(satellite));
        Assert.IsFalse(IsPending(rig.Earth));
        Assert.IsFalse(IsPending(rig.Satellites[1]));
    }

    [Test]
    public void Escape_queues_satellite_without_queuing_central_body()
    {
        var satellite = rig.Satellites[0];
        satellite.state.position = new double3(SimulationLimits.MaxBodyDistanceUnits + 1, 0, 0);
        typeof(BodyRuntimeCoordinator).GetMethod("CheckPostStepRemoval",
            BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(runtime, new object[] { satellite });

        Assert.IsTrue(IsPending(satellite));
        Assert.IsFalse(IsPending(rig.Earth));
    }

    private bool IsPending(NBody body) => (bool)typeof(BodyRuntimeCoordinator)
        .GetMethod("IsPendingRemoval", BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(runtime, new object[] { body });
}
