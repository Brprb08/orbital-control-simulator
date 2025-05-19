using UnityEngine;

/// <summary>
/// Provides utility functions for clamping and normalizing angles,
/// and for calculating camera distance based on object radius.
/// </summary>
public static class CameraCalculations
{

    /// <summary>
    /// Clamps an angle between a minimum and maximum value.
    /// </summary>
    /// <param name="angle">The angle to clamp.</param>
    /// <param name="min">Minimum allowable angle.</param>
    /// <param name="max">Maximum allowable angle.</param>
    /// <returns>The clamped angle.</returns>
    public static float ClampAngle(float angle, float min, float max)
    {
        angle = NormalizeAngle(angle);
        return Mathf.Clamp(angle, min, max);
    }

    /// <summary>
    /// Normalizes an angle to be within the range -180 to 180 degrees.
    /// </summary>
    /// <param name="angle">The angle to normalize.</param>
    /// <returns>The normalized angle.</returns>
    public static float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    /// <summary>
    /// Calculates the minimum camera distance based on the object's radius.
    /// </summary>
    /// <param name="radius">Radius of the object being tracked.</param>
    /// <returns>The minimum camera distance.</returns>
    public static float CalculateMinDistance(float radius)
    {
        if (radius <= 0.5f)
        {
            return Mathf.Max(0.4f, radius * 10f);
        }
        else if (radius > 0.5f && radius <= 100f)
        {
            return radius * 4f;
        }
        else
        {
            return radius + 400f;
        }
    }

    /// <summary>
    /// Calculates the maximum camera distance based on the object's radius.
    /// </summary>
    /// <param name="radius">Radius of the object being tracked.</param>
    /// <returns>The maximum camera distance.</returns>
    public static float CalculateMaxDistance(float radius)
    {
        float minimumMaxDistance = 2000f;

        if (radius <= 0.5f)
        {
            return Mathf.Max(minimumMaxDistance, radius * 500f);  // Small objects
        }
        else if (radius > 0.5f && radius <= 100f)
        {
            return Mathf.Max(minimumMaxDistance, radius * 100f);  // Medium objects
        }
        else
        {
            return Mathf.Max(minimumMaxDistance, radius + 2000f);  // Large onjects
        }
    }
}