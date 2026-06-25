using Unity.Mathematics;
using UnityEngine;

public readonly struct ManualVelocityLaunchResult
{
    public ManualVelocityLaunchResult(bool success, NBody body, string error)
    {
        Success = success;
        Body = body;
        Error = error;
    }

    public bool Success { get; }
    public NBody Body { get; }
    public string Error { get; }

    public static ManualVelocityLaunchResult Failed(string error)
    {
        return new ManualVelocityLaunchResult(false, null, error);
    }

    public static ManualVelocityLaunchResult Launched(NBody body)
    {
        return new ManualVelocityLaunchResult(true, body, null);
    }
}

/// <summary>
/// Converts a pending manual-placement satellite into a registered, tracked NBody.
/// </summary>
public sealed class ManualVelocityLaunchService
{
    private const float DefaultPlaceholderMass = 400000f;
    private const float MinVelocityToApplySqr = 1e-6f;

    private readonly SimContext ctx;
    private readonly BodyService bodyService;
    private readonly ICameraTracker cameraTracker;

    public ManualVelocityLaunchService(SimContext ctx, BodyService bodyService, ICameraTracker cameraTracker)
    {
        this.ctx = ctx;
        this.bodyService = bodyService;
        this.cameraTracker = cameraTracker;
    }

    public ManualVelocityLaunchResult TryLaunch(
        GameObject pendingBody,
        Vector3 velocityToApply,
        float placeholderMass,
        Vector3 placeholderRadiusMeters)
    {
        if (pendingBody == null)
            return ManualVelocityLaunchResult.Failed(null);

        if (velocityToApply.sqrMagnitude <= MinVelocityToApplySqr)
            return ManualVelocityLaunchResult.Failed("Set a non-zero velocity before launching this satellite.");

        if (bodyService == null)
            return ManualVelocityLaunchResult.Failed("Body service is missing; cannot launch satellite.");

        NBody nbody = EnsureNBody(pendingBody, placeholderMass);
        ApplySizeAndState(pendingBody, nbody, velocityToApply, placeholderRadiusMeters);
        EnsureAttitude(pendingBody);

        nbody.velocity = velocityToApply;
        bodyService.Register(nbody);

        ICameraTracker tracker = cameraTracker ?? ctx?.CameraTracker;
        tracker?.TrackBody(nbody);
        tracker?.ReturnToTracking();

        return ManualVelocityLaunchResult.Launched(nbody);
    }

    private NBody EnsureNBody(GameObject pendingBody, float placeholderMass)
    {
        NBody nbody = pendingBody.GetComponent<NBody>();
        if (nbody != null)
            return nbody;

        nbody = pendingBody.AddComponent<NBody>();

        float mass = placeholderMass > 0f ? placeholderMass : DefaultPlaceholderMass;
        nbody.mass = mass;
        nbody.trueMass = mass;
        nbody.cameraDistanceRadius = SatelliteSizing.CameraDistanceRadius;
        nbody.isCentralBody = false;
        nbody.Initialize(ctx);
        return nbody;
    }

    private static void ApplySizeAndState(
        GameObject pendingBody,
        NBody nbody,
        Vector3 velocityToApply,
        Vector3 placeholderRadiusMeters)
    {
        pendingBody.transform.localScale = SatelliteSizing.ResolveVisualScale(placeholderRadiusMeters);
        nbody.radius = SatelliteSizing.ResolvePhysicalRadiusSimUnits(placeholderRadiusMeters);
        nbody.state = new NBody.OrbitalState(
            new double3(
                pendingBody.transform.position.x,
                pendingBody.transform.position.y,
                pendingBody.transform.position.z
            ),
            new double3(velocityToApply.x, velocityToApply.y, velocityToApply.z),
            0f,
            nbody.trueMass,
            nbody.radius,
            nbody.dragCoefficient,
            Vector3.zero
        );
    }

    private static void EnsureAttitude(GameObject pendingBody)
    {
        AttitudeController attitude = pendingBody.GetComponent<AttitudeController>();
        if (attitude != null)
            return;

        attitude = pendingBody.AddComponent<AttitudeController>();
        attitude.mode = AttitudeController.PointingMode.Velocity;
        attitude.snapAttitude = false;
        attitude.maxSlewRateDegPerSec = 60f;
    }
}
