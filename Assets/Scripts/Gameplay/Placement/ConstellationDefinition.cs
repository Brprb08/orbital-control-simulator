/// <summary>
/// User-facing orbital layout for a Walker-style constellation.
/// Angles are degrees; semi-major axis is meters.
/// </summary>
public readonly struct ConstellationDefinition
{
    public readonly string NamePrefix;
    public readonly double Mass; // Dry mass per satellite, kilograms.
    public readonly double FuelMassKg;
    public readonly double SemiMajorAxisMeters;
    public readonly double Eccentricity;
    public readonly double InclinationDeg;
    public readonly double ArgumentOfPerigeeDeg;
    public readonly double RaanStartDeg;
    public readonly double RaanSpreadDeg;
    public readonly int Planes;
    public readonly int SatellitesPerPlane;
    public readonly int WalkerPhase;
    public readonly double TrueAnomalyOffsetDeg;

    public int TotalSatellites => checked(Planes * SatellitesPerPlane);

    public ConstellationDefinition(
        string namePrefix,
        double mass,
        double semiMajorAxisMeters,
        double eccentricity,
        double inclinationDeg,
        double argumentOfPerigeeDeg,
        double raanStartDeg,
        double raanSpreadDeg,
        int planes,
        int satellitesPerPlane,
        int walkerPhase,
        double trueAnomalyOffsetDeg,
        double fuelMassKg = SimulationLimits.DefaultSatelliteFuelMassKg)
    {
        NamePrefix = namePrefix;
        Mass = mass;
        FuelMassKg = fuelMassKg;
        SemiMajorAxisMeters = semiMajorAxisMeters;
        Eccentricity = eccentricity;
        InclinationDeg = inclinationDeg;
        ArgumentOfPerigeeDeg = argumentOfPerigeeDeg;
        RaanStartDeg = raanStartDeg;
        RaanSpreadDeg = raanSpreadDeg;
        Planes = planes;
        SatellitesPerPlane = satellitesPerPlane;
        WalkerPhase = walkerPhase;
        TrueAnomalyOffsetDeg = trueAnomalyOffsetDeg;
    }
}
