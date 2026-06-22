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
    public const float PlaceholderCameraRadius = SatelliteSizing.CameraDistanceRadius;
    public const float PreviewPlaceholderMinCameraDistance = 2.5f;
    public static readonly float PendingPlaceholderMinCameraDistance =
        CalculateRuntimeMinDistance(PlaceholderCameraRadius, isEarthFocus: false);

    public static CameraZoomSettings ForBody(NBody body, float? defaultDistanceOverride = null)
    {
        float cameraRadius = body.cameraDistanceRadius;
        float minDistance = CameraCalculations.CalculateMinDistance(cameraRadius);
        float maxDistance = CameraCalculations.CalculateMaxDistance(cameraRadius);
        float midpointDistance = (minDistance + maxDistance) / 2f;
        float closerFraction = cameraRadius <= 10f ? 0.15f : 0.25f;
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
        float radius = PlaceholderCameraRadius;
        float minDistance = GetPlaceholderMinDistance(placeholder);
        bool isPendingVelocityPlacement =
            placeholder != null && placeholder.GetComponent<PendingVelocityPlacementMarker>() != null;

        return new CameraZoomSettings(
            minDistance,
            maxDistance: PlaceholderMaxCameraDistance,
            defaultDistance: isPendingVelocityPlacement ? minDistance : 10f * radius,
            height: 0f
        );
    }

    public static float GetPlaceholderMinDistance(Transform placeholder)
    {
        return placeholder != null && placeholder.GetComponent<PendingVelocityPlacementMarker>() != null
            ? PendingPlaceholderMinCameraDistance
            : PreviewPlaceholderMinCameraDistance;
    }

    public static float CalculateRuntimeMinDistance(float radius, bool isEarthFocus)
    {
        if (isEarthFocus) return EarthCamMinDistance;
        if (radius <= 0.5f) return Mathf.Max(0.01f, radius * 0.7f);
        if (radius <= 100f) return radius * 5f;
        return radius + 400f;
    }
}
