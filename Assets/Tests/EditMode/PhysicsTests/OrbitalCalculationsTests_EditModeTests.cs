using NUnit.Framework;
using UnityEngine;
using Unity.Mathematics;
using System;
using UnityEngine.TestTools;

/// <summary>
/// Unit tests for orbital parameter computation using the project’s scaled units (km → units/10).
/// Covers circular orbits, invalid inputs (zero velocity), and hyperbolic trajectories,
/// asserting validity flags, eccentricity ranges, characteristic distances, and period behavior.
/// </summary>
public class OrbitalCalculationsTests
{
    [Test]
    public void CalculateOrbitalParameters_CircularOrbit_ScaledUnits()
    {
        float earthMass = 5.972e24f;
        Vector3 earthPosition = Vector3.zero;

        float earthRadius_km = 6378f;
        float altitude_km = 700f;
        float orbitRadius_km = earthRadius_km + altitude_km;

        float orbitRadius_units = orbitRadius_km / 10f;

        // Position and velocity in double3
        double3 position_d = new double3(orbitRadius_units, 0, 0);

        double mu = PhysicsConstants.G * earthMass;
        double velocity_units = Math.Sqrt(mu / orbitRadius_units);
        double3 velocity_d = new double3(0, 0, velocity_units);

        OrbitalParameters result = OrbitalCalculations.CalculateOrbitalParameters(
            earthMass,
            earthPosition,
            position_d,
            velocity_d
        );

        Assert.That(result.isValid, Is.True);
        Assert.That(result.eccentricity, Is.LessThan(1e-3f));
        Assert.That(result.semiMajorAxis, Is.EqualTo(orbitRadius_units).Within(0.05f));
        Assert.That(result.perigeePosition.magnitude, Is.EqualTo(orbitRadius_units).Within(1f));
        Assert.That(result.apogeePosition.magnitude, Is.EqualTo(orbitRadius_units).Within(1f));
        Assert.That(result.orbitalPeriod, Is.GreaterThan(5000f));
    }

    [Test]
    public void CalculateOrbitalParameters_InvalidInput_ZeroVelocity()
    {
        float earthMass = 5.972e24f;
        Vector3 center = Vector3.zero;

        double3 position_d = new double3(700f / 10f, 0, 0); // 700 km in units
        double3 zeroVelocity_d = double3.zero;

        LogAssert.Expect(LogType.Warning, "[ERROR] Position or velocity magnitude too small. Cannot compute orbital parameters.");

        var result = OrbitalCalculations.CalculateOrbitalParameters(
            earthMass,
            center,
            position_d,
            zeroVelocity_d
        );

        Assert.That(result.isValid, Is.False);
    }

    [Test]
    public void CalculateOrbitalParameters_HyperbolicOrbit_ScaledUnits()
    {
        float earthMass = 5.972e24f;
        Vector3 center = Vector3.zero;

        float radius_km = 7000f;
        float radius_units = radius_km / 10f;
        double3 position_d = new double3(radius_units, 0, 0);

        double mu = PhysicsConstants.G * earthMass;
        double escapeVelocity_units = Math.Sqrt(2 * mu / radius_units);

        // 10% faster than escape velocity
        double3 velocity_d = new double3(0, 0, escapeVelocity_units * 1.1);

        OrbitalParameters result = OrbitalCalculations.CalculateOrbitalParameters(
            earthMass,
            center,
            position_d,
            velocity_d
        );

        Assert.That(result.isValid, Is.True);
        Assert.That(result.eccentricity, Is.GreaterThanOrEqualTo(1f));
        Assert.That(result.apogeePosition, Is.EqualTo(Vector3.zero)); // hyperbolic orbits have no apogee
    }
}
