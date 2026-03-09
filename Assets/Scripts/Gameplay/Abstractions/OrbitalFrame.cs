using UnityEngine;

/// <summary>
/// Derived orbital reference frame from position and velocity relative to a central body.
/// 
/// Conventions in this project:
/// - radialOut = normalized(position - center)
/// - prograde = normalized(velocity) when available
/// - normal = -(r × v).normalized when available
///   (this matches the existing project convention used by AttitudeMath / AttitudeController)
/// - antiNormal = -normal
/// </summary>
public struct OrbitalFrame
{
    public Vector3 radialOut;
    public Vector3 radialIn;

    public Vector3 prograde;
    public Vector3 retrograde;

    public Vector3 normal;
    public Vector3 antiNormal;

    public bool hasVelocity;
    public bool hasNormal;
}