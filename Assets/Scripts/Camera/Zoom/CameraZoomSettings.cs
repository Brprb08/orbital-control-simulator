using UnityEngine;

internal readonly struct CameraZoomSettings
{
    public CameraZoomSettings(float minDistance, float maxDistance, float defaultDistance, float? height)
    {
        MinDistance = minDistance;
        MaxDistance = maxDistance;
        DefaultDistance = defaultDistance;
        Height = height;
    }

    public float MinDistance { get; }
    public float MaxDistance { get; }
    public float DefaultDistance { get; }
    public float? Height { get; }
}

internal static class CameraZoomSettingsFactory
{
    public const float EarthCamMinDistance = 750f;
    public const float EarthCamDefaultDistance = 2000f;
    public const float PlaceholderMaxCameraDistance = 800f;

    public static CameraZoomSettings ForBody(NBody body, float? defaultDistanceOverride = null)
    {
        float minDistance = CameraCalculations.CalculateMinDistance(body.radius);
        float maxDistance = CameraCalculations.CalculateMaxDistance(body.radius);
        float midpointDistance = (minDistance + maxDistance) / 2f;
        float closerFraction = body.radius <= 10f ? 0.15f : 0.25f;
        float defaultDistance = defaultDistanceOverride
            ?? minDistance + (midpointDistance - minDistance) * closerFraction;

        return new CameraZoomSettings(
            minDistance,
            maxDistance: 10000f,
            defaultDistance,
            height: null
        );
    }

    public static CameraZoomSettings ForEarth(NBody earth)
    {
        return new CameraZoomSettings(
            minDistance: CameraCalculations.CalculateMinDistance(earth.radius) * 5f,
            maxDistance: 30000f,
            defaultDistance: EarthCamDefaultDistance,
            height: null
        );
    }

    public static CameraZoomSettings ForPlaceholder(Transform placeholder)
    {
        float radius = placeholder.localScale.x;

        return new CameraZoomSettings(
            minDistance: CalculateRuntimeMinDistance(radius, isEarthFocus: false),
            maxDistance: PlaceholderMaxCameraDistance,
            defaultDistance: 10f * radius,
            height: 0.2f * radius
        );
    }

    public static float CalculateRuntimeMinDistance(float radius, bool isEarthFocus)
    {
        if (isEarthFocus) return EarthCamMinDistance;
        if (radius <= 0.5f) return Mathf.Max(0.01f, radius * 0.7f);
        if (radius <= 100f) return radius * 5f;
        return radius + 400f;
    }
}
