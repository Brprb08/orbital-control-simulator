using System;
using NUnit.Framework;

/// <summary>
/// Unit tests for the TLEParser class (two-body approximation).
/// Verifies parsing, propagation, and handling of malformed inputs.
/// Notes:
/// - Outputs are ECI-like position (meters) and velocity (m/s) using Vector3d.
/// - API under test: TLEParser.TryPropagate(line1, line2, whenUtc, out rEci_m, out vEci_mps, out tleEpochUtc)
/// </summary>
public class TLEParserTests_EditModeTests
{
    // Sample valid TLE (ISS) — same as before
    private const string Line1_Valid = "1 25544U 98067A   20029.54791435  .00001264  00000-0  29621-4 0  9993";
    private const string Line2_Valid = "2 25544  51.6448 172.4814 0007419  39.3392 104.3828 15.49163575210626";

    // Pick a propagation instant near the TLE epoch (20029.5479... ≈ 2020-01-29T13:08:58Z)
    private static readonly DateTime WhenNearEpochUtc = new DateTime(2020, 1, 29, 13, 8, 58, DateTimeKind.Utc);

    private static double Mag(Vector3d v) => Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);

    [Test]
    public void TryPropagate_ValidInput_ReturnsTrueAndOutputsVectors()
    {
        bool ok = TLEParser.TryPropagate(
            Line1_Valid, Line2_Valid, WhenNearEpochUtc,
            out Vector3d rEci_m, out Vector3d vEci_mps, out DateTime tleEpochUtc);

        Assert.IsTrue(ok);
        Assert.AreNotEqual(default(Vector3d), rEci_m, "Position should be non-zero.");
        Assert.AreNotEqual(default(Vector3d), vEci_mps, "Velocity should be non-zero.");

        // parsed epoch should be close to TLE's encoded epoch
        var expectedEpoch = new DateTime(2020, 1, 29, 13, 8, 58, DateTimeKind.Utc);
        Assert.That(Math.Abs((tleEpochUtc - expectedEpoch).TotalSeconds), Is.LessThan(5.0),
            "Parsed TLE epoch should be within a few seconds of expected.");
    }

    [Test]
    public void TryPropagate_EmptyLines_ReturnsFalse()
    {
        bool ok = TLEParser.TryPropagate(
            "", "", WhenNearEpochUtc, out _, out _, out _);

        Assert.IsFalse(ok);
    }

    [Test]
    public void TryPropagate_ShortLines_ReturnsFalse()
    {
        string shortLine = "1 25544"; // way too short
        bool ok = TLEParser.TryPropagate(
            shortLine, shortLine, WhenNearEpochUtc, out _, out _, out _);

        Assert.IsFalse(ok);
    }

    [Test]
    public void TryPropagate_InvalidNumericField_ReturnsFalse()
    {
        // Corrupt inclination number field
        string corruptedLine2 = Line2_Valid.Substring(0, 8) + "ABC.DEF " + Line2_Valid.Substring(17);
        bool ok = TLEParser.TryPropagate(
            Line1_Valid, corruptedLine2, WhenNearEpochUtc, out _, out _, out _);

        Assert.IsFalse(ok);
    }

    [Test]
    public void TryPropagate_PositionWithinExpectedOrbitalRange_InMeters()
    {
        // LEO orbital radius ~ 6.6e6–7.2e6 m
        TLEParser.TryPropagate(
            Line1_Valid, Line2_Valid, WhenNearEpochUtc,
            out Vector3d rEci_m, out _, out _);

        double r = Mag(rEci_m);
        Assert.That(r, Is.InRange(6.3e6, 8.5e6), "ECI radius should be within a reasonable LEO band (meters).");
    }

    [Test]
    public void TryPropagate_ZeroEccentricity_ParsesSuccessfully()
    {
        string line2ZeroEcc = Line2_Valid.Substring(0, 26) + "0000000" + Line2_Valid.Substring(33);

        bool ok = TLEParser.TryPropagate(
            Line1_Valid, line2ZeroEcc, WhenNearEpochUtc,
            out Vector3d r, out Vector3d v, out _);

        Assert.IsTrue(ok);
        Assert.AreNotEqual(default(Vector3d), r);
        Assert.AreNotEqual(default(Vector3d), v);
    }

    [Test]
    public void MalformedInclination_ReturnsFalse()
    {
        string broken = Line2_Valid.Substring(0, 8) + "********" + Line2_Valid.Substring(16);

        bool ok = TLEParser.TryPropagate(
            Line1_Valid, broken, WhenNearEpochUtc, out _, out _, out _);

        Assert.IsFalse(ok);
    }

    [Test]
    public void MalformedRAAN_ReturnsFalse()
    {
        string brokenRAAN = Line2_Valid.Substring(0, 17) + "********" + Line2_Valid.Substring(25);

        bool ok = TLEParser.TryPropagate(
            Line1_Valid, brokenRAAN, WhenNearEpochUtc, out _, out _, out _);

        Assert.IsFalse(ok);
    }

    [Test]
    public void MalformedEccentricity_TreatedAsZero_Succeeds()
    {
        // Corrupt the eccentricity field (cols 27–33). Parser maps non-numeric to 0.0.
        string broken = Line2_Valid.Substring(0, 26) + "#######" + Line2_Valid.Substring(33);

        bool okBroken = TLEParser.TryPropagate(
            Line1_Valid, broken, WhenNearEpochUtc, out Vector3d rBroken, out Vector3d vBroken, out _);

        // Build a version that explicitly sets ecc to 0.0; both should behave the same.
        string line2ZeroEcc = Line2_Valid.Substring(0, 26) + "0000000" + Line2_Valid.Substring(33);
        bool okZero = TLEParser.TryPropagate(
            Line1_Valid, line2ZeroEcc, WhenNearEpochUtc, out Vector3d rZero, out Vector3d vZero, out _);

        Assert.IsTrue(okBroken, "Parser should succeed and treat malformed eccentricity as 0.0.");
        Assert.IsTrue(okZero, "Zero-eccentricity case should also succeed.");

        // Since both resolve to e = 0, results should match closely.
        double posDiff = Math.Sqrt(Math.Pow(rBroken.x - rZero.x, 2) + Math.Pow(rBroken.y - rZero.y, 2) + Math.Pow(rBroken.z - rZero.z, 2));
        double velDiff = Math.Sqrt(Math.Pow(vBroken.x - vZero.x, 2) + Math.Pow(vBroken.y - vZero.y, 2) + Math.Pow(vBroken.z - vZero.z, 2));

        Assert.That(posDiff, Is.LessThan(1e-6), "Positions should be identical (within fp noise).");
        Assert.That(velDiff, Is.LessThan(1e-6), "Velocities should be identical (within fp noise).");
    }

    [Test]
    public void MalformedArgumentOfPerigee_ReturnsFalse()
    {
        string broken = Line2_Valid.Substring(0, 34) + "********" + Line2_Valid.Substring(42);

        bool ok = TLEParser.TryPropagate(
            Line1_Valid, broken, WhenNearEpochUtc, out _, out _, out _);

        Assert.IsFalse(ok);
    }

    [Test]
    public void MalformedMeanAnomaly_ReturnsFalse()
    {
        string broken = Line2_Valid.Substring(0, 43) + "********" + Line2_Valid.Substring(51);

        bool ok = TLEParser.TryPropagate(
            Line1_Valid, broken, WhenNearEpochUtc, out _, out _, out _);

        Assert.IsFalse(ok);
    }

    [Test]
    public void MalformedMeanMotion_ReturnsFalse()
    {
        string broken = Line2_Valid.Substring(0, 52) + "***********" + Line2_Valid.Substring(63);

        bool ok = TLEParser.TryPropagate(
            Line1_Valid, broken, WhenNearEpochUtc, out _, out _, out _);

        Assert.IsFalse(ok);
    }

    [Test]
    public void TryPropagate_DifferentMeanAnomalies_ProducesDifferentResults()
    {
        string line2A = Line2_Valid;
        string line2B = Line2_Valid.Substring(0, 43) + "204.3828" + Line2_Valid.Substring(51); // modify mean anomaly

        TLEParser.TryPropagate(Line1_Valid, line2A, WhenNearEpochUtc, out Vector3d rA, out Vector3d vA, out _);
        TLEParser.TryPropagate(Line1_Valid, line2B, WhenNearEpochUtc, out Vector3d rB, out Vector3d vB, out _);

        // Different true anomalies => different state vectors
        Assert.IsFalse(rA.x == rB.x && rA.y == rB.y && rA.z == rB.z, "Positions should differ with different mean anomalies.");
        Assert.IsFalse(vA.x == vB.x && vA.y == vB.y && vA.z == vB.z, "Velocities should differ with different mean anomalies.");
    }
}
