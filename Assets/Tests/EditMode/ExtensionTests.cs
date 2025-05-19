using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Unit tests for the Extensions utility class.
/// Validates conversions from Double3 -> Vector3 and Vector3 -> Double3.
/// </summary>
public class ExtensionTests
{
    /// <summary>
    /// Tests that converting a double3 to Vector3 returns correct float values.
    /// </summary>
    [Test]
    public void ToVector3_ConvertsCorrectly()
    {
        double3 d = new double3(1.1, 2.2, 3.3);
        Vector3 v = d.ToVector3();

        Assert.That(v.x, Is.EqualTo(1.1f).Within(0.0001f));
        Assert.That(v.y, Is.EqualTo(2.2f).Within(0.0001f));
        Assert.That(v.z, Is.EqualTo(3.3f).Within(0.0001f));
    }

    /// <summary>
    /// Tests that converting a Vector3 to double3 returns matching double values.
    /// </summary>
    [Test]
    public void ToDouble3_ConvertsCorrectly()
    {
        Vector3 v = new Vector3(4.4f, 5.5f, 6.6f);
        double3 d = v.ToDouble3();

        Assert.That(d.x, Is.EqualTo(4.4).Within(0.0001));
        Assert.That(d.y, Is.EqualTo(5.5).Within(0.0001));
        Assert.That(d.z, Is.EqualTo(6.6).Within(0.0001));
    }
}
