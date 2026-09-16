using System;
using UnityEngine;

/// <summary>Numeric and physical limits shared by placement previews and spawn boundaries.</summary>
public static class PlacementSafety
{
    public const float MaxDistanceUnits = SimulationLimits.MaxBodyDistanceUnits;
    public const float MaxVelocityUnitsPerSecond = SimulationLimits.MaxPlacementSpeedUnitsPerSecond; // 100 km/s; manual escape trajectories are allowed.

    public static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    public static bool TryValidateVelocity(Vector3 velocity, out string error)
    {
        if (!IsFinite(velocity) || velocity.sqrMagnitude > MaxVelocityUnitsPerSecond * MaxVelocityUnitsPerSecond)
        {
            error = $"Velocity must be finite and no greater than {SimulationLimits.MaxPlacementSpeedKmPerSecond:0.###} km/s.";
            return false;
        }
        if (velocity.sqrMagnitude <= SimulationLimits.MinPlacementSpeedSquared)
        {
            error = "Set a non-zero velocity before launching this satellite.";
            return false;
        }
        error = null;
        return true;
    }

    public static bool TryValidatePosition(Vector3 position, double minimum, double maximum, out string error)
    {
        double distanceSquared = (double)position.x * position.x + (double)position.y * position.y + (double)position.z * position.z;
        if (!IsFinite(position) || !double.IsFinite(minimum) || !double.IsFinite(maximum) ||
            distanceSquared < minimum * minimum || distanceSquared > maximum * maximum)
        {
            error = $"Position must be finite and between {minimum:0.###} and {maximum:0.###} units from the center.";
            return false;
        }
        error = null;
        return true;
    }

    public static bool TryValidateOrbit(double a, double e, double earthRadiusMeters, double metersPerUnit, out string error)
    {
        if (!double.IsFinite(a) || !double.IsFinite(e) || a <= 0 || e < 0 || e >= 1 ||
            !double.IsFinite(earthRadiusMeters) || earthRadiusMeters <= 0 ||
            !double.IsFinite(metersPerUnit) || metersPerUnit <= 0)
        {
            error = "Orbit values must be finite, with a > 0 and 0 <= eccentricity < 1.";
            return false;
        }
        double perigee = a * (1 - e);
        double apogee = a * (1 + e);
        if (perigee <= earthRadiusMeters * SimulationLimits.OrbitClearanceRadiusMultiplier)
        {
            error = "Orbit intersects Earth or its placement clearance. Increase 'a' or reduce eccentricity.";
            return false;
        }
        if (!double.IsFinite(apogee) || apogee >= MaxDistanceUnits * metersPerUnit)
        {
            error = $"Orbit must remain below {MaxDistanceUnits * metersPerUnit / SimulationUnits.MetersPerKilometer:N0} km from Earth's center. Reduce 'a' or eccentricity.";
            return false;
        }
        error = null;
        return true;
    }
}
