using NUnit.Framework;

public class ManeuverNodeTiming_EditModeTests
{
    [Test]
    public void ResolveFutureBurnTime_wraps_past_node_to_next_orbit()
    {
        float resolved = ManeuverNodeTiming.ResolveFutureBurnTime(
            burnTime: 20f,
            simulationTime: 21f,
            orbitalPeriod: 5000f
        );

        Assert.AreEqual(5020f, resolved, 0.001f);
    }

    [Test]
    public void GetTimeToNode_wraps_countdown_after_zero()
    {
        float timeToNode = ManeuverNodeTiming.GetTimeToNode(
            burnTime: 20f,
            simulationTime: 21f,
            orbitalPeriod: 5000f
        );

        Assert.AreEqual(4999f, timeToNode, 0.001f);
    }

    [Test]
    public void ResolveFutureBurnTime_moves_exact_zero_to_next_orbit()
    {
        float resolved = ManeuverNodeTiming.ResolveFutureBurnTime(
            burnTime: 20f,
            simulationTime: 20f,
            orbitalPeriod: 5000f
        );

        Assert.AreEqual(5020f, resolved, 0.001f);
    }

    [Test]
    public void GetTimeToNode_keeps_positive_countdown_unwrapped()
    {
        float timeToNode = ManeuverNodeTiming.GetTimeToNode(
            burnTime: 20f,
            simulationTime: 5f,
            orbitalPeriod: 5000f
        );

        Assert.AreEqual(15f, timeToNode, 0.001f);
    }
}
