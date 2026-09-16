public static class PhysicsConstants
{
    /// <summary>
    /// Adjusted gravitational constant for a scale where 1 Unity unit = 10 km.
    /// Based on SI value of G ≈ 6.67430 × 10⁻¹¹ m³ / (kg·s²).
    /// 
    /// Unit conversion:
    /// • 1 unit = 10,000 meters → 1 unit³ = (10,000 m)³ = 1 × 10¹² m³  
    /// • Therefore, G in Unity units = G_SI / 1e12 ≈ 6.67430 × 10⁻²³
    /// </summary>
    public const double GDouble = 6.67430e-23;
    public const float G = (float)GDouble;
    public const double EarthMuMeters = 3.986004418e14;
    public const double EarthRadiusMeters = 6378137.0;
    public const double EarthRadiusUnits = EarthRadiusMeters / SimulationUnits.MetersPerUnit;

    // Preserve the existing rounded trajectory fallback; actual bodies supply their own radius.
    public const float LegacyTrajectoryEarthRadiusUnits = 637.8f;
    public const float AtmosphereReferenceTopAltitudeKm = 1000f;
    // Native model preserves reference values through 1000 km, fading its extension to zero at 1100 km.
    public const float AtmosphericDragCutoffAltitudeKm = 1100f;
}
