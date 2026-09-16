using NUnit.Framework;

public class ConstellationGenerator_EditModeTests
{
    private const double EarthMu = 3.986004418e14;
    private const double MetersPerUnit = 10000.0;

    [Test]
    public void Generate_creates_member_for_each_plane_slot_pair()
    {
        var definition = new ConstellationDefinition(
            "Test",
            1000.0,
            7000000.0,
            0.0,
            53.0,
            0.0,
            0.0,
            360.0,
            planes: 3,
            satellitesPerPlane: 4,
            walkerPhase: 1,
            trueAnomalyOffsetDeg: 0.0
        );

        var members = ConstellationGenerator.Generate(definition, EarthMu, MetersPerUnit);

        Assert.AreEqual(12, members.Count);
        Assert.AreEqual("Test P01-01", members[0].Spawn.Name);
        Assert.AreEqual(0, members[0].PlaneIndex);
        Assert.AreEqual(0, members[0].SlotIndex);
        Assert.AreEqual(1, members[0].MemberIndex);
        Assert.AreEqual("Test P03-04", members[11].Spawn.Name);
        Assert.AreEqual(2, members[11].PlaneIndex);
        Assert.AreEqual(3, members[11].SlotIndex);
        Assert.AreEqual(12, members[11].MemberIndex);
    }

    [Test]
    public void Generate_offsets_planes_by_walker_phase()
    {
        var definition = new ConstellationDefinition(
            "Phase",
            1000.0,
            7000000.0,
            0.0,
            53.0,
            0.0,
            0.0,
            360.0,
            planes: 2,
            satellitesPerPlane: 2,
            walkerPhase: 1,
            trueAnomalyOffsetDeg: 0.0
        );

        var members = ConstellationGenerator.Generate(definition, EarthMu, MetersPerUnit);

        Assert.AreEqual(4, members.Count);
        Assert.AreNotEqual(members[0].Spawn.Position, members[2].Spawn.Position);
        Assert.AreNotEqual(members[0].Spawn.Velocity, members[2].Spawn.Velocity);
    }
}
