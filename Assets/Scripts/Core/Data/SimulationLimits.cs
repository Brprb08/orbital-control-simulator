/// <summary>
/// Shared application limits. Distances are measured from the central body's center
/// unless explicitly named altitude. Manual placement and orbital clearance are
/// deliberately separate: manual launch may create a re-entry trajectory.
/// </summary>
public static class SimulationLimits
{
    public const float MaxBodyDistanceUnits = 40000f;
    public const float ManualPlacementMinDistanceUnits = 638f;
    public const float ManualPlacementMaxDistanceUnits = 5000f;
    public const double OrbitClearanceRadiusMultiplier = 1.001;
    public const float MaxPlacementSpeedKmPerSecond = 100f;
    public const float MaxPlacementSpeedUnitsPerSecond = MaxPlacementSpeedKmPerSecond / SimulationUnits.KilometersPerUnit;
    public const float MinPlacementSpeedSquared = 1e-6f;
    public const float MinSatelliteMassKg = 500f;
    public const double DefaultSatelliteFuelMassKg = 100.0;
    public const float MaxSatelliteMassKg = 1000000f;
    public const float MinSatelliteRadiusMeters = 2f;
    public const float MaxSatelliteRadiusMeters = 300f;
    public const int DefaultMaxConstellationSatellites = 50;
}
