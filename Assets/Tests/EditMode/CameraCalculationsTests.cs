using UnityEngine;
using NUnit.Framework;

/// <summary>
/// Unit tests for the CameraCalculations utility class.
/// Validates behavior of angle normalization, clamping, and camera distance calculations.
/// </summary>
public class CameraCalculationsTest
{

    /// <summary>
    /// Tests that NormalizeAngle correctly wraps angles to the range [-180, 180].
    /// </summary>
    [Test]
    public void NormalizeAngle_WrapsCorrectly()
    {
        Assert.That(CameraCalculations.NormalizeAngle(190f), Is.EqualTo(-170f));
        Assert.That(CameraCalculations.NormalizeAngle(-190f), Is.EqualTo(170f));
        Assert.That(CameraCalculations.NormalizeAngle(360f), Is.EqualTo(0f));
    }

    /// <summary>
    /// Tests that ClampAngle normalizes and clamps input angles within specified bounds.
    /// </summary>
    [Test]
    public void ClampAngle_RespectsBounds()
    {
        float result = CameraCalculations.ClampAngle(200f, -45f, 45f);
        Assert.That(result, Is.EqualTo(-45f));

        result = CameraCalculations.ClampAngle(-200f, -90f, 90f);
        Assert.That(result, Is.EqualTo(90f).Within(1f));  // normalized to 160 then clamped
    }

    /// <summary>
    /// Tests that CalculateMinDistance returns expected camera distances
    /// for small, medium, and large object radii.
    /// </summary>
    /// <param name="radius">Radius of the tracked object.</param>
    /// <returns>Expected minimum camera distance.</returns>
    [TestCase(0.1f, ExpectedResult = 1f)]
    [TestCase(0.5f, ExpectedResult = 5f)]
    [TestCase(10f, ExpectedResult = 40f)]
    [TestCase(150f, ExpectedResult = 550f)]
    public float CalculateMinDistance_Works(float radius)
    {
        return CameraCalculations.CalculateMinDistance(radius);
    }

    /// <summary>
    /// Tests that CalculateMaxDistance enforces minimum thresholds
    /// and scales appropriately with object size.
    /// </summary>
    /// <param name="radius">Radius of the tracked object.</param>
    /// <returns>Expected maximum camera distance.</returns>
    [TestCase(0.1f, ExpectedResult = 2000f)]
    [TestCase(1f, ExpectedResult = 2000f)]
    [TestCase(50f, ExpectedResult = 5000f)]
    [TestCase(150f, ExpectedResult = 2150f)]
    public float CalculateMaxDistance_Works(float radius)
    {
        return CameraCalculations.CalculateMaxDistance(radius);
    }
}
