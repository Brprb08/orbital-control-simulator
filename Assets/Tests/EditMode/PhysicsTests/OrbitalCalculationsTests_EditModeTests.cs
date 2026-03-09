using NUnit.Framework;
using UnityEngine;
using Unity.Mathematics;
using System;

public class OrbitalCalculationsTests
{
    private SimTestRig rig;

    [TearDown]
    public void TearDown()
    {
        rig?.Dispose();
        rig = null;
    }

    private static double EarthMass => 5.972e24;
    private static double Mu => PhysicsConstants.G * EarthMass;

    private static double3 D3(double x, double y, double z) => new double3(x, y, z);

    private static double CircularSpeed(double radius)
    {
        return Math.Sqrt(Mu / radius);
    }

    private static double EscapeSpeed(double radius)
    {
        return Math.Sqrt(2.0 * Mu / radius);
    }

    private static OrbitalParameters Calc(double3 pos, double3 vel, double3? center = null, double? mass = null)
    {
        return OrbitalCalculations.CalculateOrbitalParameters(
            mass ?? EarthMass,
            center ?? double3.zero,
            pos,
            vel
        );
    }

    private static NBody CreateBody(string name, Transform parent, double3 pos, double3 vel, double trueMass = 1000.0)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3((float)pos.x, (float)pos.y, (float)pos.z);

        var body = go.AddComponent<NBody>();
        body.trueMass = trueMass;
        body.mass = (float)trueMass;
        body.state = new NBody.OrbitalState(
            pos,
            vel,
            0f,
            trueMass,
            body.radius,
            body.dragCoefficient,
            Vector3.zero
        );

        return body;
    }

    [Test]
    public void CalculateOrbitalParameters_Invalid_WhenCentralMassIsZero()
    {
        var result = OrbitalCalculations.CalculateOrbitalParameters(
            0.0,
            double3.zero,
            D3(700, 0, 0),
            D3(0, 0, 10)
        );

        Assert.That(result.isValid, Is.False);
    }

    [Test]
    public void CalculateOrbitalParameters_Invalid_WhenCentralMassIsNegative()
    {
        var result = OrbitalCalculations.CalculateOrbitalParameters(
            -1.0,
            double3.zero,
            D3(700, 0, 0),
            D3(0, 0, 10)
        );

        Assert.That(result.isValid, Is.False);
    }

    [Test]
    public void CalculateOrbitalParameters_Invalid_WhenRadiusTooSmall()
    {
        var result = Calc(
            D3(0.1, 0.0, 0.0),   // below R_MIN = 1
            D3(0.0, 0.0, 10.0)
        );

        Assert.That(result.isValid, Is.False);
    }

    [Test]
    public void CalculateOrbitalParameters_Invalid_WhenVelocityIsZero()
    {
        var result = Calc(
            D3(700.0, 0.0, 0.0),
            double3.zero
        );

        Assert.That(result.isValid, Is.False);
    }

    [Test]
    public void CalculateOrbitalParameters_Invalid_WhenVelocityTooSmall()
    {
        var result = Calc(
            D3(700.0, 0.0, 0.0),
            D3(0.0, 0.0, 1e-15)
        );

        Assert.That(result.isValid, Is.False);
    }

    [Test]
    public void CalculateOrbitalParameters_Invalid_WhenAngularMomentumIsZero()
    {
        // velocity parallel to radius -> h = 0
        var result = Calc(
            D3(700.0, 0.0, 0.0),
            D3(10.0, 0.0, 0.0)
        );

        Assert.That(result.isValid, Is.False);
    }

    [Test]
    public void CalculateOrbitalParameters_CircularOrbit_ReturnsExpectedValues()
    {
        double radius = 707.8;
        double3 pos = D3(radius, 0.0, 0.0);
        double3 vel = D3(0.0, 0.0, CircularSpeed(radius));

        var result = Calc(pos, vel);

        Assert.That(result.isValid, Is.True);
        Assert.That(result.eccentricity, Is.LessThan(1e-3f));
        Assert.That(result.isCircular, Is.True);

        Assert.That(result.semiMajorAxis, Is.EqualTo((float)radius).Within(0.1f));
        Assert.That(result.perigeeRadius, Is.EqualTo((float)radius).Within(0.5f));
        Assert.That(result.apogeeRadius, Is.EqualTo((float)radius).Within(0.5f));

        Assert.That(result.perigeePosition.magnitude, Is.EqualTo((float)radius).Within(1f));
        Assert.That(result.apogeePosition.magnitude, Is.EqualTo((float)radius).Within(1f));

        Assert.That(result.orbitalPeriod, Is.GreaterThan(0f));
        Assert.That(result.meanAnomaly, Is.InRange(0f, (float)(2.0 * Math.PI)));
        Assert.That(result.trueAnomaly, Is.InRange(0f, (float)(2.0 * Math.PI)));
        Assert.That(result.timeToPerigee, Is.GreaterThanOrEqualTo(0f));
        Assert.That(result.timeToApogee, Is.GreaterThanOrEqualTo(0f));
    }

    [Test]
    public void CalculateOrbitalParameters_EllipticOrbit_ReturnsPerigeeLessThanApogee()
    {
        double radius = 700.0;
        double circular = CircularSpeed(radius);

        // slightly sub-circular tangential speed => ellipse, starting near apogee/perigee depending on geometry
        double3 pos = D3(radius, 0.0, 0.0);
        double3 vel = D3(0.0, 0.0, circular * 0.9);

        var result = Calc(pos, vel);

        Assert.That(result.isValid, Is.True);
        Assert.That(result.eccentricity, Is.GreaterThan(0f));
        Assert.That(result.eccentricity, Is.LessThan(1f));
        Assert.That(result.isCircular, Is.False);

        Assert.That(result.semiMajorAxis, Is.GreaterThan(0f));
        Assert.That(result.perigeeRadius, Is.GreaterThan(0f));
        Assert.That(result.apogeeRadius, Is.GreaterThan(result.perigeeRadius));
        Assert.That(result.orbitalPeriod, Is.GreaterThan(0f));
    }

    [Test]
    public void CalculateOrbitalParameters_ParabolicBoundary_TreatedAsOpen()
    {
        double radius = 700.0;
        double3 pos = D3(radius, 0.0, 0.0);
        double3 vel = D3(0.0, 0.0, EscapeSpeed(radius));

        var result = Calc(pos, vel);

        Assert.That(result.isValid, Is.True);
        Assert.That(result.eccentricity, Is.GreaterThanOrEqualTo(1f));
        Assert.That(result.semiMajorAxis, Is.EqualTo(0f));
        Assert.That(result.apogeeRadius, Is.EqualTo(-1f));
        Assert.That(result.orbitalPeriod, Is.EqualTo(0f));
    }

    [Test]
    public void CalculateOrbitalParameters_HyperbolicOrbit_ReturnsOpenOrbitOutputs()
    {
        double radius = 700.0;
        double3 pos = D3(radius, 0.0, 0.0);
        double3 vel = D3(0.0, 0.0, EscapeSpeed(radius) * 1.1);

        var result = Calc(pos, vel);

        Assert.That(result.isValid, Is.True);
        Assert.That(result.eccentricity, Is.GreaterThanOrEqualTo(1f));
        Assert.That(result.isCircular, Is.False);
        Assert.That(result.semiMajorAxis, Is.EqualTo(0f));
        Assert.That(result.perigeeRadius, Is.GreaterThan(0f));
        Assert.That(result.apogeeRadius, Is.EqualTo(-1f));
        Assert.That(result.apogeePosition, Is.EqualTo(Vector3.zero));
        Assert.That(result.orbitalPeriod, Is.EqualTo(0f));
        Assert.That(result.timeToPerigee, Is.EqualTo(0f));
        Assert.That(result.timeToApogee, Is.EqualTo(0f));
    }

    [Test]
    public void CalculateOrbitalParameters_EquatorialOrbit_HasZeroInclinationAndZeroRaan()
    {
        double radius = 700.0;
        double3 pos = D3(radius, 0.0, 0.0);
        double3 vel = D3(0.0, 0.0, CircularSpeed(radius));

        var result = Calc(pos, vel);

        Assert.That(result.isValid, Is.True);
        Assert.That(result.inclination, Is.EqualTo(0f).Within(0.01f));
        Assert.That(result.RAAN, Is.EqualTo(0f).Within(0.01f));
    }

    [Test]
    public void CalculateOrbitalParameters_RetrogradeEquatorialOrbit_HasInclinationNear180()
    {
        double radius = 700.0;
        double3 pos = D3(radius, 0.0, 0.0);
        double3 vel = D3(0.0, 0.0, -CircularSpeed(radius));

        var result = Calc(pos, vel);

        Assert.That(result.isValid, Is.True);
        Assert.That(result.inclination, Is.EqualTo(180f).Within(0.01f));
        Assert.That(result.RAAN, Is.EqualTo(0f).Within(0.01f));
    }

    [Test]
    public void CalculateOrbitalParameters_InclinedOrbit_ReturnsInclinationInRange()
    {
        double radius = 700.0;
        double speed = CircularSpeed(radius);

        // split speed across y/z to make a tilted plane
        double vy = speed * 0.5;
        double vz = Math.Sqrt(speed * speed - vy * vy);

        double3 pos = D3(radius, 0.0, 0.0);
        double3 vel = D3(0.0, vy, vz);

        var result = Calc(pos, vel);

        Assert.That(result.isValid, Is.True);
        Assert.That(result.inclination, Is.GreaterThan(0f));
        Assert.That(result.inclination, Is.LessThan(180f));
        Assert.That(result.RAAN, Is.InRange(0f, 360f));
    }

    [Test]
    public void CalculateOrbitalParameters_OffsetCenterPosition_UsesRelativePosition()
    {
        double3 center = D3(100.0, 50.0, -25.0);
        double radius = 700.0;
        double3 pos = center + D3(radius, 0.0, 0.0);
        double3 vel = D3(0.0, 0.0, CircularSpeed(radius));

        var result = Calc(pos, vel, center);

        Assert.That(result.isValid, Is.True);
        Assert.That(result.eccentricity, Is.LessThan(1e-3f));
        Assert.That(result.semiMajorAxis, Is.EqualTo((float)radius).Within(0.1f));
    }

    [Test]
    public void CalculateOrbitalParameters_ClosedOrbit_AnomaliesAreFiniteAndInRange()
    {
        double radius = 700.0;
        double circular = CircularSpeed(radius);

        var result = Calc(
            D3(radius, 0.0, 0.0),
            D3(0.0, 0.0, circular * 0.95)
        );

        Assert.That(result.isValid, Is.True);
        Assert.That(float.IsNaN(result.trueAnomaly), Is.False);
        Assert.That(float.IsNaN(result.meanAnomaly), Is.False);
        Assert.That(result.trueAnomaly, Is.InRange(0f, (float)(2.0 * Math.PI)));
        Assert.That(result.meanAnomaly, Is.InRange(0f, (float)(2.0 * Math.PI)));
    }

    [Test]
    public void CalculateOrbitalParameters_ClosedOrbit_TimesAreNonNegative()
    {
        double radius = 700.0;
        double circular = CircularSpeed(radius);

        var result = Calc(
            D3(radius, 0.0, 0.0),
            D3(0.0, 0.0, circular * 0.95)
        );

        Assert.That(result.isValid, Is.True);
        Assert.That(result.timeToPerigee, Is.GreaterThanOrEqualTo(0f));
        Assert.That(result.timeToApogee, Is.GreaterThanOrEqualTo(0f));
        Assert.That(result.orbitalPeriod, Is.GreaterThan(0f));
    }

    [Test]
    public void TryParams_ReturnsDefault_WhenBodyIsNull()
    {
        var result = OrbitalCalculations.TryParams(null, null);

        Assert.That(result.isValid, Is.False);
    }

    [Test]
    public void TryParams_ReturnsDefault_WhenServiceIsNull()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        var sat = CreateBody("Sat", rig.Root.transform, D3(700, 0, 0), D3(0, 0, 10));

        var result = OrbitalCalculations.TryParams(sat, null);

        Assert.That(result.isValid, Is.False);
    }

    [Test]
    public void TryParams_ReturnsDefault_WhenCentralBodyIsMissing()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        var sat = CreateBody("Sat", rig.Root.transform, D3(700, 0, 0), D3(0, 0, 10));

        var svcGo = new GameObject("Svc");
        var svc = svcGo.AddComponent<BodyService>();

        try
        {
            var result = OrbitalCalculations.TryParams(sat, svc);
            Assert.That(result.isValid, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(svcGo);
        }
    }

    [Test]
    public void TryParams_ReturnsDefault_WhenBodyPositionContainsNaN()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        rig.Earth.trueMass = EarthMass;
        rig.Earth.mass = (float)EarthMass;
        rig.Earth.state = new NBody.OrbitalState(
            double3.zero,
            double3.zero,
            0f,
            EarthMass,
            rig.Earth.radius,
            rig.Earth.dragCoefficient,
            Vector3.zero
        );

        var go = new GameObject("Sat");
        go.transform.SetParent(rig.Root.transform, false);
        go.transform.position = Vector3.zero; // valid Unity transform position

        var sat = go.AddComponent<NBody>();
        sat.trueMass = 1000.0;
        sat.mass = 1000.0f;
        sat.state = new NBody.OrbitalState(
            new double3(double.NaN, 0.0, 0.0), // invalid sim-state only
            new double3(0.0, 0.0, 10.0),
            0f,
            sat.trueMass,
            sat.radius,
            sat.dragCoefficient,
            Vector3.zero
        );

        var result = OrbitalCalculations.TryParams(sat, rig.BodyService);

        Assert.That(result.isValid, Is.False);
    }

    [Test]
    public void TryParams_ReturnsDefault_WhenVelocityTooSmall()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        rig.Earth.trueMass = EarthMass;
        rig.Earth.mass = (float)EarthMass;
        rig.Earth.state = new NBody.OrbitalState(
            double3.zero,
            double3.zero,
            0f,
            EarthMass,
            rig.Earth.radius,
            rig.Earth.dragCoefficient,
            Vector3.zero
        );

        var sat = CreateBody(
            "Sat",
            rig.Root.transform,
            D3(700, 0, 0),
            D3(0, 0, 1e-8)
        );

        var result = OrbitalCalculations.TryParams(sat, rig.BodyService);

        Assert.That(result.isValid, Is.False);
    }

    [Test]
    public void TryParams_ReturnsValidParameters_ForValidBodyAndService()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        rig.Earth.trueMass = EarthMass;
        rig.Earth.mass = (float)EarthMass;
        rig.Earth.state = new NBody.OrbitalState(
            double3.zero,
            double3.zero,
            0f,
            EarthMass,
            rig.Earth.radius,
            rig.Earth.dragCoefficient,
            Vector3.zero
        );

        double radius = 700.0;
        var sat = CreateBody(
            "Sat",
            rig.Root.transform,
            D3(radius, 0, 0),
            D3(0, 0, CircularSpeed(radius))
        );

        rig.BodyService.Register(sat);

        var result = OrbitalCalculations.TryParams(sat, rig.BodyService);

        Assert.That(result.isValid, Is.True);
        Assert.That(result.eccentricity, Is.LessThan(1e-3f));
        Assert.That(result.semiMajorAxis, Is.EqualTo((float)radius).Within(0.1f));
    }

    [Test]
    public void GetInvariantDebug_Invalid_WhenBodyIsNull()
    {
        var dbg = OrbitalCalculations.GetInvariantDebug(null, null);

        Assert.That(dbg.valid, Is.False);
    }

    [Test]
    public void GetInvariantDebug_Invalid_WhenCentralBodyIsMissing()
    {
        var svcGo = new GameObject("Svc");
        var svc = svcGo.AddComponent<BodyService>();

        var satGo = new GameObject("Sat");
        var sat = satGo.AddComponent<NBody>();
        sat.state = new NBody.OrbitalState(
            D3(700, 0, 0),
            D3(0, 0, 10),
            0f,
            1000,
            sat.radius,
            sat.dragCoefficient,
            Vector3.zero
        );

        try
        {
            var dbg = OrbitalCalculations.GetInvariantDebug(sat, svc);
            Assert.That(dbg.valid, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(svcGo);
            UnityEngine.Object.DestroyImmediate(satGo);
        }
    }

    [Test]
    public void GetInvariantDebug_Invalid_WhenRadiusIsZero()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        rig.Earth.mass = (float)EarthMass;
        rig.Earth.trueMass = EarthMass;

        var sat = CreateBody("Sat", rig.Root.transform, double3.zero, D3(0, 0, 10));
        rig.BodyService.Register(sat);

        var dbg = OrbitalCalculations.GetInvariantDebug(sat, rig.BodyService);

        Assert.That(dbg.valid, Is.False);
    }

    [Test]
    public void GetInvariantDebug_ClosedOrbit_ReturnsFiniteClosedOrbitValues()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        rig.Earth.mass = (float)EarthMass;
        rig.Earth.trueMass = EarthMass;
        rig.Earth.transform.position = Vector3.zero;

        double radius = 700.0;
        var sat = CreateBody(
            "Sat",
            rig.Root.transform,
            D3(radius, 0, 0),
            D3(0, 0, CircularSpeed(radius))
        );
        rig.BodyService.Register(sat);

        var dbg = OrbitalCalculations.GetInvariantDebug(sat, rig.BodyService);

        Assert.That(dbg.valid, Is.True);
        Assert.That(dbg.radius, Is.EqualTo(radius).Within(1e-6));
        Assert.That(dbg.speed, Is.EqualTo(CircularSpeed(radius)).Within(1e-6));
        Assert.That(dbg.eccentricity, Is.LessThan(1e-3));
        Assert.That(double.IsNaN(dbg.semiMajorAxis), Is.False);
        Assert.That(dbg.semiMajorAxis, Is.EqualTo(radius).Within(0.1));
        Assert.That(double.IsNaN(dbg.perigeeRadius), Is.False);
        Assert.That(double.IsNaN(dbg.apogeeRadius), Is.False);
    }

    [Test]
    public void GetInvariantDebug_OpenOrbit_ReturnsNaNForClosedOnlyValues()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        rig.Earth.mass = (float)EarthMass;
        rig.Earth.trueMass = EarthMass;
        rig.Earth.transform.position = Vector3.zero;

        double radius = 700.0;
        var sat = CreateBody(
            "Sat",
            rig.Root.transform,
            D3(radius, 0, 0),
            D3(0, 0, EscapeSpeed(radius) * 1.1)
        );
        rig.BodyService.Register(sat);

        var dbg = OrbitalCalculations.GetInvariantDebug(sat, rig.BodyService);

        Assert.That(dbg.valid, Is.True);
        Assert.That(dbg.eccentricity, Is.GreaterThanOrEqualTo(1.0));
        Assert.That(double.IsNaN(dbg.semiMajorAxis), Is.True);
        Assert.That(double.IsNaN(dbg.perigeeRadius), Is.True);
        Assert.That(double.IsNaN(dbg.apogeeRadius), Is.True);
    }

    [Test]
    public void GetInvariantDebug_CircularOrbit_HasNegativeSpecificEnergy()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        rig.Earth.mass = (float)EarthMass;
        rig.Earth.trueMass = EarthMass;
        rig.Earth.transform.position = Vector3.zero;

        double radius = 700.0;
        var sat = CreateBody(
            "Sat",
            rig.Root.transform,
            D3(radius, 0, 0),
            D3(0, 0, CircularSpeed(radius))
        );
        rig.BodyService.Register(sat);

        var dbg = OrbitalCalculations.GetInvariantDebug(sat, rig.BodyService);

        Assert.That(dbg.valid, Is.True);
        Assert.That(dbg.specificEnergy, Is.LessThan(0.0));
        Assert.That(dbg.angularMomentumMag, Is.GreaterThan(0.0));
    }
}