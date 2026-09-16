using NUnit.Framework;

public class ManeuverNodeTiming_EditModeTests
{
    [Test]
    public void DoesBurnOverlap_detects_burn_start_inside_large_step()
    {
        var target = new UnityEngine.GameObject("Target").AddComponent<NBody>();
        var node = new ManeuverNode
        {
            isFinalized = true,
            targetBody = target,
            burnTime = 10f,
            duration = 2f
        };

        Assert.IsTrue(ManeuverBurnMath.DoesBurnOverlap(node, target, 9f, 11f));

        UnityEngine.Object.DestroyImmediate(target.gameObject);
    }

    [Test]
    public void DoesBurnOverlap_ignores_step_touching_burn_end()
    {
        var target = new UnityEngine.GameObject("Target").AddComponent<NBody>();
        var node = new ManeuverNode
        {
            isFinalized = true,
            targetBody = target,
            burnTime = 10f,
            duration = 2f
        };

        Assert.IsFalse(ManeuverBurnMath.DoesBurnOverlap(node, target, 12f, 14f));

        UnityEngine.Object.DestroyImmediate(target.gameObject);
    }

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
