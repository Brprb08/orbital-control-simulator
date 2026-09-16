using UnityEngine;

/// <summary>
/// Shared camera rules for orbital paths and markers. Visuals retain their own
/// appearance, user toggles, and distance thresholds.
/// </summary>
public static class CameraVisibilityPolicy
{
    // Earth view retains the selected satellite. Free view clears orbit selection,
    // even when it contains a placement placeholder.
    public static NBody SelectedBody(ICameraTracker tracker)
        => tracker != null && tracker.Mode != CameraMode.Free ? tracker.CurrentBody : null;

    public static bool IsSelectedBody(ICameraTracker tracker, NBody body)
        => body != null && SelectedBody(tracker) == body;

    public static bool TryGetSelectedConstellation(ICameraTracker tracker, ConstellationRegistry registry,
        out ConstellationRecord constellation, out ConstellationPlaneRecord plane)
    {
        constellation = null;
        plane = null;
        NBody body = SelectedBody(tracker);
        return body != null && registry != null && registry.TryGetPlaneForBody(body, out constellation, out plane);
    }

    public static bool IsOutsideCloseUp(Camera camera, Vector3 position, float distance)
        => camera != null && Vector3.Distance(camera.transform.position, position) > distance;

    public static bool IsOutsideCloseUp(Camera camera, NBody body, float distance)
        => body != null && IsOutsideCloseUp(camera, body.RenderPosition, distance);

    public static bool IsTrackedOrbitVisible(ICameraTracker tracker, Camera camera, NBody body, float distance)
        => IsSelectedBody(tracker, body) && IsOutsideCloseUp(camera, body, distance);

    public static bool IsInsideViewport(Vector3 viewportPosition, float inset)
        => viewportPosition.z > 0f &&
           viewportPosition.x > inset && viewportPosition.x < 1f - inset &&
           viewportPosition.y > inset && viewportPosition.y < 1f - inset;

    public static bool IsTargetIndicatorFarEnough(ICameraTracker tracker, Vector3 cameraPosition,
        Vector3 targetPosition, float minimumDistanceSquared)
        => (tracker != null && tracker.IsEarthView) ||
           (cameraPosition - targetPosition).sqrMagnitude >= minimumDistanceSquared;

    public static bool IsOccludedByCentralBody(Vector3 camPos, Vector3 targetPos, NBody central)
    {
        if (central == null)
            return false;

        Vector3 center = central.transform.position;

        float radius = (float)central.radius;
        if (radius <= 0f)
            return false;

        Vector3 camToTarget = targetPos - camPos;
        float segLength = camToTarget.magnitude;
        if (segLength <= Mathf.Epsilon)
            return false;

        Vector3 dir = camToTarget / segLength;

        Vector3 camToCenter = center - camPos;

        float t = Vector3.Dot(camToCenter, dir);

        if (t <= 0f || t >= segLength)
            return false;

        Vector3 closestPoint = camPos + dir * t;
        float distanceToCenter = (closestPoint - center).magnitude;

        return distanceToCenter < radius;
    }
}
