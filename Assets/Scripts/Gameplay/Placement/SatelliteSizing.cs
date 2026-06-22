using UnityEngine;

public static class SatelliteSizing
{
    // Simulation uses 1 world unit = 10 km = 10,000 meters.
    public const float SimMetersPerUnit = 10000f;
    public const float DefaultPhysicalRadiusMeters = 20f;
    public const float MinPhysicalRadiusMeters = 2f;
    public const float MaxPhysicalRadiusMeters = 300f;
    public const float MinVisualScale = 0.04f;
    public const float MaxVisualScale = 0.1f;
    public const float CameraDistanceRadius = 0.5f;
    private const float MaxVisualScaleRadiusMeters = MaxPhysicalRadiusMeters;

    public static float ResolvePhysicalRadiusSimUnits(Vector3 radiusMeters)
    {
        return ResolvePhysicalRadiusMeters(radiusMeters) / SimMetersPerUnit;
    }

    public static float ResolvePhysicalRadiusMeters(Vector3 radiusMeters)
    {
        float candidateMeters = Mathf.Max(
            Mathf.Abs(radiusMeters.x),
            Mathf.Abs(radiusMeters.y),
            Mathf.Abs(radiusMeters.z)
        );

        if (!(candidateMeters > 0f) || float.IsNaN(candidateMeters) || float.IsInfinity(candidateMeters))
            candidateMeters = DefaultPhysicalRadiusMeters;

        return Mathf.Clamp(candidateMeters, MinPhysicalRadiusMeters, MaxPhysicalRadiusMeters);
    }

    public static Vector3 ResolveVisualScale(Vector3 radiusMeters)
    {
        return Vector3.one * ResolveVisualScaleUnits(radiusMeters);
    }

    public static float ResolveVisualScaleUnits(Vector3 radiusMeters)
    {
        float physicalRadiusMeters = ResolvePhysicalRadiusMeters(radiusMeters);

        if (physicalRadiusMeters >= MaxVisualScaleRadiusMeters)
            return MaxVisualScale;

        float t = Mathf.InverseLerp(MinPhysicalRadiusMeters, MaxVisualScaleRadiusMeters, physicalRadiusMeters);
        return Mathf.Lerp(MinVisualScale, MaxVisualScale, Mathf.Clamp01(t));
    }

    public static Vector3 DefaultVisualScale()
    {
        return ResolveVisualScale(Vector3.one * DefaultPhysicalRadiusMeters);
    }

    public static Vector3 ResolveVisualScaleFromSimRadius(float radiusSimUnits)
    {
        return ResolveVisualScale(Vector3.one * radiusSimUnits * SimMetersPerUnit);
    }

    public static void ApplyVisualScale(NBody body)
    {
        if (body == null || body.isCentralBody)
            return;

        body.transform.localScale = ResolveVisualScaleFromSimRadius(body.radius);
        body.cameraDistanceRadius = CameraDistanceRadius;
    }
}
