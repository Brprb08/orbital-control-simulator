using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Unit tests for the OrbitalCalculations static class.
/// Validates orbital element calculations using scaled units (1 unit = 10 km).
/// </summary>
public class OrbitalCalculationsTests
{
    private GameObject testObject;
    private Transform bodyTransform;

    /// <summary>
    /// Creates a test satellite GameObject before each test.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        testObject = new GameObject("TestSatellite");
        bodyTransform = testObject.transform;
    }

    /// <summary>
    /// Cleans up the test GameObject after each test.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(testObject);
    }

    /// <summary>
    /// Verifies that a near-circular orbit yields expected orbital parameters.
    /// Uses 1 unit = 10 km, placing satellite in LEO at ~707.8 km altitude.
    /// Validates eccentricity, semi-major axis, period, and apogee/perigee distance.
    /// </summary>
    [Test]
    public void CalculateOrbitalParameters_CircularOrbit_ScaledUnits()
    {
        float earthMass = 5.972e24f;
        Vector3 earthPosition = Vector3.zero;

        float earthRadius_km = 6378f;
        float altitude_km = 700f;
        float orbitRadius_km = earthRadius_km + altitude_km;

        float orbitRadius_units = orbitRadius_km / 10f;
        bodyTransform.position = new Vector3(orbitRadius_units, 0, 0);

        float mu = PhysicsConstants.G * earthMass;
        float velocity_kmps = Mathf.Sqrt(mu / orbitRadius_units);
        float velocity_units = velocity_kmps; // km/s → units/s

        Vector3 velocity = new Vector3(0, 0, velocity_units);

        OrbitalParameters result = OrbitalCalculations.CalculateOrbitalParameters(
            earthMass,
            earthPosition,
            bodyTransform,
            velocity
        );

        Assert.That(result.isValid, Is.True);
        Assert.That(result.eccentricity, Is.LessThan(1e-3f));
        Assert.That(result.semiMajorAxis, Is.EqualTo(orbitRadius_units).Within(0.05f));
        Assert.That(result.perigeePosition.magnitude, Is.EqualTo(orbitRadius_units).Within(1f));
        Assert.That(result.apogeePosition.magnitude, Is.EqualTo(orbitRadius_units).Within(1f));
        Assert.That(result.orbitalPeriod, Is.GreaterThan(5000f));
    }

    /// <summary>
    /// Verifies that an orbit with zero velocity is invalid.
    /// Also checks that an appropriate error log is emitted.
    /// </summary>
    [Test]
    public void CalculateOrbitalParameters_InvalidInput_ZeroVelocity()
    {
        float earthMass = 5.972e24f;
        Vector3 center = Vector3.zero;
        bodyTransform.position = new Vector3(700f / 10f, 0, 0); // 700 km in units
        Vector3 zeroVelocity = Vector3.zero;

        LogAssert.Expect(LogType.Error, "[ERROR] Position or velocity magnitude too small. Cannot compute orbital parameters.");

        var result = OrbitalCalculations.CalculateOrbitalParameters(earthMass, center, bodyTransform, zeroVelocity);
        Assert.That(result.isValid, Is.False);
    }

    /// <summary>
    /// Verifies that a velocity > escape velocity results in a hyperbolic orbit.
    /// Validates eccentricity >= 1 and ensures apogee is undefined (set to zero).
    /// </summary>
    [Test]
    public void CalculateOrbitalParameters_HyperbolicOrbit_ScaledUnits()
    {
        float earthMass = 5.972e24f;
        Vector3 center = Vector3.zero;
        float radius_km = 7000f;
        float radius_units = radius_km / 10f;

        bodyTransform.position = new Vector3(radius_units, 0, 0);

        float mu = PhysicsConstants.G * earthMass;
        float escapeVelocity_kmps = Mathf.Sqrt(2 * mu / radius_units);
        float escapeVelocity_units = escapeVelocity_kmps;

        Vector3 velocity = new Vector3(0, 0, escapeVelocity_units * 1.1f); // 10% over escape
        OrbitalParameters result = OrbitalCalculations.CalculateOrbitalParameters(earthMass, center, bodyTransform, velocity);
        Assert.That(result.isValid, Is.True);
        Assert.That(result.eccentricity, Is.GreaterThanOrEqualTo(1f));
        Assert.That(result.apogeePosition, Is.EqualTo(Vector3.zero)); // Hyperbolic orbit
    }
}
