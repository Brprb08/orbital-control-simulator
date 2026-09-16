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
        Vector3 placeholderRadiusMeters,
        double fuelMassKg = SimulationLimits.DefaultSatelliteFuelMassKg)
    {
        if (pendingBody == null)
            return ManualVelocityLaunchResult.Failed(null);

        if (!PlacementSafety.TryValidateVelocity(velocityToApply, out string error))
            return ManualVelocityLaunchResult.Failed(error);

        if (bodyService == null)
            return ManualVelocityLaunchResult.Failed("Body service is missing; cannot launch satellite.");
        if (!NBody.IsValidMassComposition(placeholderMass, fuelMassKg))
            return ManualVelocityLaunchResult.Failed("Invalid dry mass or fuel mass.");

        if (!PlacementSpawnBuilder.MassRange.Contains(placeholderMass))
            return ManualVelocityLaunchResult.Failed($"Mass must be between {SimulationLimits.MinSatelliteMassKg:N0} and {SimulationLimits.MaxSatelliteMassKg:N0} kg.");
        var radiusRange = PlacementSpawnBuilder.RadiusClamp;
        if (!radiusRange.Contains(placeholderRadiusMeters.x) || !radiusRange.Contains(placeholderRadiusMeters.y) ||
            !radiusRange.Contains(placeholderRadiusMeters.z))
            return ManualVelocityLaunchResult.Failed($"Each radius must be between {radiusRange.Min} and {radiusRange.Max} meters.");

        NBody central = bodyService.CentralBody;
        Vector3 center = central != null ? central.transform.position : Vector3.zero;
        double minimum = System.Math.Max(PlacementSpawnBuilder.PositionBounds.Min,
            (central != null ? central.radius : (float)PhysicsConstants.EarthRadiusUnits) + SatelliteSizing.ResolvePhysicalRadiusSimUnits(placeholderRadiusMeters));
        if (!PlacementSafety.TryValidatePosition(pendingBody.transform.position - center, minimum,
                PlacementSpawnBuilder.PositionBounds.Max, out error))
            return ManualVelocityLaunchResult.Failed(error);

        NBody nbody = SatelliteSpawner.InitializeSatellite(
            pendingBody, placeholderMass, placeholderRadiusMeters, velocityToApply,
            preserveExistingBodyProperties: true, fuelMassKg: fuelMassKg);
        bodyService.Register(nbody);

        ICameraTracker tracker = cameraTracker ?? ctx?.CameraTracker;
        tracker?.TrackBody(nbody);
        tracker?.ReturnToTracking();

        return ManualVelocityLaunchResult.Launched(nbody);
    }

}
