/// <summary>
/// Fixed simulation unit convention: distance in 10 km units, time in seconds, mass in kg.
/// These factors also convert velocity and acceleration because time remains in seconds.
/// This is not a runtime scale setting: native physics uses the same fixed convention.
/// </summary>
public static class SimulationUnits
{
    public const float MetersPerKilometer = 1000f;
    public const float KilometersPerUnit = 10f;
    public const float MetersPerUnit = KilometersPerUnit * MetersPerKilometer;

    // Internal force is kg * world-units / s². Mass remains in kilograms.
    public static float ForceNewtonsToWorld(float newtons) => newtons / MetersPerUnit;
}
