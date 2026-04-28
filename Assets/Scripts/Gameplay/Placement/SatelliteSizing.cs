using UnityEngine;

public static class SatelliteSizing
{
    // Simulation uses 1 world unit = 10 km = 10,000 meters.
    public const float SimMetersPerUnit = 10000f;
    public const float DefaultPhysicalRadiusMeters = 2f;
    public const float MinPhysicalRadiusMeters = 0.25f;
    public const float MaxPhysicalRadiusMeters = 8f;

    public static float ResolvePhysicalRadiusSimUnits(Vector3 visualScale)
    {
        float radiusMeters = ResolvePhysicalRadiusMeters(visualScale);
        return radiusMeters / SimMetersPerUnit;
    }

    public static float ResolvePhysicalRadiusMeters(Vector3 visualScale)
    {
        float candidateMeters = Mathf.Max(
            Mathf.Abs(visualScale.x),
            Mathf.Abs(visualScale.y),
            Mathf.Abs(visualScale.z)
        );

        if (!(candidateMeters > 0f) || float.IsNaN(candidateMeters) || float.IsInfinity(candidateMeters))
            candidateMeters = DefaultPhysicalRadiusMeters;

        return Mathf.Clamp(candidateMeters, MinPhysicalRadiusMeters, MaxPhysicalRadiusMeters);
    }
}
