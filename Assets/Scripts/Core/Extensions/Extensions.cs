using UnityEngine;
using Unity.Mathematics;

/// <summary>
/// Used in NBody for switching double position and velocity to doubles, or vice versa.
/// </summary>
public static class Double3Extensions
{
    public static Vector3 ToVector3(this double3 d)
    {
        return new Vector3((float)d.x, (float)d.y, (float)d.z);
    }

    public static double3 ToDouble3(this Vector3 v)
    {
        return new double3(v.x, v.y, v.z);
    }
}