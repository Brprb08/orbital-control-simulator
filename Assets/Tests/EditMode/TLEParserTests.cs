using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Unit tests for the TLEParser class.
/// Verifies correct parsing of valid TLE strings and appropriate handling of invalid inputs.
/// </summary>
public class TLEParserTests
{
    // Sample valid TLE (ISS)
    private const string Line1_Valid = "1 25544U 98067A   20029.54791435  .00001264  00000-0  29621-4 0  9993";
    private const string Line2_Valid = "2 25544  51.6448 172.4814 0007419  39.3392 104.3828 15.49163575210626";

    [Test]
    public void TryParseTLE_ValidInput_ReturnsTrueAndOutputsVectors()
    {
        bool success = TLEParser.TryParseTLE(Line1_Valid, Line2_Valid, out Vector3 position, out Vector3 velocity);

        Assert.IsTrue(success);
        Assert.That(position.magnitude, Is.GreaterThan(0));
        Assert.That(velocity.magnitude, Is.GreaterThan(0));
    }

    [Test]
    public void TryParseTLE_EmptyLines_ReturnsFalse()
    {
        LogAssert.Expect(LogType.Error, "Invalid TLE input. Each line must be at least 69 characters.");
        bool result = TLEParser.TryParseTLE("", "", out _, out _);
        Assert.IsFalse(result);
    }

    [Test]
    public void TryParseTLE_ShortLines_ReturnsFalse()
    {
        LogAssert.Expect(LogType.Error, "Invalid TLE input. Each line must be at least 69 characters.");
        string shortLine = "1 25544"; // way too short
        bool result = TLEParser.TryParseTLE(shortLine, shortLine, out _, out _);
        Assert.IsFalse(result);
    }

    [Test]
    public void TryParseTLE_InvalidNumericField_ReturnsFalse()
    {
        LogAssert.Expect(LogType.Error, "Invalid TLE input. Each line must be at least 69 characters.");
        string corruptedLine2 = Line2_Valid.Substring(0, 8) + "ABC.DEF " + Line2_Valid.Substring(17); // corrupt inclination
        bool result = TLEParser.TryParseTLE(Line1_Valid, corruptedLine2, out _, out _);
        Assert.IsFalse(result);
    }

    [Test]
    public void TryParseTLE_PositionWithinExpectedOrbitalRange()
    {
        TLEParser.TryParseTLE(Line1_Valid, Line2_Valid, out Vector3 position, out _);
        float distanceFromEarth = position.magnitude;
        Assert.That(distanceFromEarth, Is.InRange(400, 10000)); // in km, approximate for LEO
    }

    [Test]
    public void TryParseTLE_ZeroEccentricity_ParsesSuccessfully()
    {
        string line2ZeroEcc = Line2_Valid.Substring(0, 26) + "0000000" + Line2_Valid.Substring(33);
        bool result = TLEParser.TryParseTLE(Line1_Valid, line2ZeroEcc, out Vector3 pos, out Vector3 vel);

        Assert.IsTrue(result);
        Assert.That(pos.magnitude, Is.GreaterThan(0));
        Assert.That(vel.magnitude, Is.GreaterThan(0));
    }

    [Test]
    public void MalformedInclination_ReturnsFalse()
    {
        LogAssert.Expect(LogType.Error, "Invalid TLE input. One or more fields are non-numeric or malformed.");
        string broken = Line2_Valid.Substring(0, 8) + "********" + Line2_Valid.Substring(16);
        bool result = TLEParser.TryParseTLE(Line1_Valid, broken, out _, out _);
        Assert.IsFalse(result);
    }

    [Test]
    public void TryParseTLE_MalformedRAAN_ThrowsAndReturnsFalse()
    {
        LogAssert.Expect(LogType.Error, "Invalid TLE input. One or more fields are non-numeric or malformed.");
        string brokenRAAN = Line2_Valid.Substring(0, 17) + "********" + Line2_Valid.Substring(25);
        bool result = TLEParser.TryParseTLE(Line1_Valid, brokenRAAN, out _, out _);

        Assert.IsFalse(result);
    }

    [Test]
    public void MalformedEccentricity_ReturnsFalse()
    {
        LogAssert.Expect(LogType.Error, "Invalid TLE input. One or more fields are non-numeric or malformed.");
        string broken = Line2_Valid.Substring(0, 26) + "#######" + Line2_Valid.Substring(33);
        bool result = TLEParser.TryParseTLE(Line1_Valid, broken, out _, out _);
        Assert.IsFalse(result);
    }

    [Test]
    public void MalformedArgumentOfPerigee_ReturnsFalse()
    {
        LogAssert.Expect(LogType.Error, "Invalid TLE input. One or more fields are non-numeric or malformed.");
        string broken = Line2_Valid.Substring(0, 34) + "********" + Line2_Valid.Substring(42);
        bool result = TLEParser.TryParseTLE(Line1_Valid, broken, out _, out _);
        Assert.IsFalse(result);
    }

    [Test]
    public void MalformedMeanAnomaly_ReturnsFalse()
    {
        LogAssert.Expect(LogType.Error, "Invalid TLE input. One or more fields are non-numeric or malformed.");
        string broken = Line2_Valid.Substring(0, 43) + "********" + Line2_Valid.Substring(51);
        bool result = TLEParser.TryParseTLE(Line1_Valid, broken, out _, out _);
        Assert.IsFalse(result);
    }

    [Test]
    public void MalformedMeanMotion_ReturnsFalse()
    {
        LogAssert.Expect(LogType.Error, "Invalid TLE input. One or more fields are non-numeric or malformed.");
        string broken = Line2_Valid.Substring(0, 52) + "***********" + Line2_Valid.Substring(63);
        bool result = TLEParser.TryParseTLE(Line1_Valid, broken, out _, out _);
        Assert.IsFalse(result);
    }

    [Test]
    public void TryParseTLE_DifferentMeanAnomalies_ProducesDifferentResults()
    {
        string line2A = Line2_Valid;
        string line2B = Line2_Valid.Substring(0, 43) + "204.3828" + Line2_Valid.Substring(51); // modify mean anomaly

        TLEParser.TryParseTLE(Line1_Valid, line2A, out Vector3 posA, out Vector3 velA);
        TLEParser.TryParseTLE(Line1_Valid, line2B, out Vector3 posB, out Vector3 velB);

        Assert.AreNotEqual(posA, posB);
        Assert.AreNotEqual(velA, velB);
    }
}
