using NUnit.Framework;
using UnityEngine;

public class BodyService_EditModeTests
{
    private SimTestRig rig;

    [TearDown]
    public void TearDown() => rig?.Dispose();

    [Test]
    public void Register_sets_central_when_flagged()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        Assert.AreEqual(rig.Earth, rig.BodyService.CentralBody);
        CollectionAssert.Contains(rig.BodyService.Bodies, rig.Earth);
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
    public void Deregister_clears_central_if_removed()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        Assert.NotNull(rig.BodyService.CentralBody);

        rig.BodyService.Deregister(rig.Earth);
        Assert.IsNull(rig.BodyService.CentralBody);
    }
}
