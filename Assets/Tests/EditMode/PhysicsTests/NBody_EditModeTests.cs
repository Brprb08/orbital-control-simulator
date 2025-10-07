using NUnit.Framework;
using UnityEngine;
using System.Reflection;

public class NBody_EditModeTests
{
    private SimTestRig rig;

    [TearDown]
    public void TearDown() => rig?.Dispose();

    [Test]
    public void Start_sets_velocity_zero_for_central_body()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var earth = rig.Earth;
        earth.velocity = new Vector3(1, 2, 3);

        // Call Start() without SendMessage (avoids Unity internal assert)
        var startMI = typeof(NBody).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance);
        startMI.Invoke(earth, null);

        Assert.AreEqual(Vector3.zero, earth.velocity);
    }

    [Test]
    public void Altitude_returns_distance_minus_earth_radius()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var sat = new GameObject("Sat").AddComponent<NBody>();
        sat.transform.SetParent(rig.Root.transform, false);

        // place at EarthRadius + 100km on x-axis (units are "km" in your property)
        const float earthR = 637.8137f;
        sat.transform.position = new Vector3(earthR + 100f, 0f, 0f);

        double alt = sat.altitude;
        Assert.AreEqual(100.0, alt, 0.05);
    }
}
