using NUnit.Framework;
using UnityEngine;

public class ParsingUtilsTests
{
    [Test]
    public void TryParseVector3_ValidInput_ReturnsTrue()
    {
        string input = "1.0, 2.0, 3.0";
        bool success = ParsingUtils.TryParseVector3(input, out Vector3 result);

        Assert.IsTrue(success);
        Assert.AreEqual(new Vector3(1f, 2f, 3f), result);
    }

    [Test]
    public void TryParseVector3_InvalidFormat_ReturnsFalse()
    {
        string input = "1.0, 2.0"; // Missing z
        bool success = ParsingUtils.TryParseVector3(input, out Vector3 result);

        Assert.IsFalse(success);
        Assert.AreEqual(Vector3.zero, result);
    }

    [Test]
    public void TryParseMass_ValidValue_ReturnsTrue()
    {
        string input = "1000000";
        bool success = ParsingUtils.TryParseMass(input, out float mass);

        Assert.IsTrue(success);
        Assert.AreEqual(1000000f, mass);
    }

    [Test]
    public void TryParseMass_BelowMin_ReturnsFalse()
    {
        string input = "400"; // Below valid range
        bool success = ParsingUtils.TryParseMass(input, out float mass);

        Assert.IsFalse(success);
        Assert.AreEqual(0f, mass);
    }

    [Test]
    public void TryParseMass_NonNumeric_ReturnsFalse()
    {
        string input = "not-a-number";
        bool success = ParsingUtils.TryParseMass(input, out float mass);

        Assert.IsFalse(success);
        Assert.AreEqual(0f, mass);
    }

    [Test]
    public void TryParseMass_AboveMax_ReturnsFalse()
    {
        string input = "1e12"; // Above valid max
        bool success = ParsingUtils.TryParseMass(input, out float mass);

        Assert.IsFalse(success);
        Assert.AreEqual(0f, mass);
    }
}
