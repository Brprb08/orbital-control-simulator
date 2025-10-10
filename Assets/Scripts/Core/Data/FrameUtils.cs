using UnityEngine;
using System;

/// <summary>
/// Provides utility methods for converting vectors between
/// Earth-Centered Inertial (ECI) coordinates and Unity's coordinate system.
/// Ensures consistent axis mapping and scaling between simulation and Unity units.
/// </summary>
static class FrameUtils
{
    /// <summary>
    /// Converts an ECI position vector (in meters) to Unity world space coordinates.
    /// </summary>
    /// <param name="rEci_m">Position in ECI frame, in meters.</param>
    /// <param name="metersPerUnit">Conversion factor between meters and Unity units.</param>
    /// <returns>Converted position vector in Unity space.</returns>
    public static Vector3 EciToUnity(Vector3d rEci_m, double metersPerUnit)
    {
        double uPerM = 1.0 / Math.Max(1e-9, metersPerUnit);

        // Axis remapping: Unity.Y = ECI.Z, Unity.Z = ECI.Y
        return new Vector3(
            (float)(rEci_m.x * uPerM),
            (float)(rEci_m.z * uPerM),
            (float)(rEci_m.y * uPerM)
        );
    }

    /// <summary>
    /// Converts an ECI velocity vector (in meters per second) to Unity world space coordinates.
    /// </summary>
    /// <param name="vEci_mps">Velocity in ECI frame, in meters per second.</param>
    /// <param name="metersPerUnit">Conversion factor between meters and Unity units.</param>
    /// <returns>Converted velocity vector in Unity space.</returns>
    public static Vector3 VelEciToUnity(Vector3d vEci_mps, double metersPerUnit)
    {
        double uPerM = 1.0 / Math.Max(1e-9, metersPerUnit);

        // Axis remapping: Unity.Y = ECI.Z, Unity.Z = ECI.Y
        return new Vector3(
            (float)(vEci_mps.x * uPerM),
            (float)(vEci_mps.z * uPerM),
            (float)(vEci_mps.y * uPerM)
        );
    }
}
