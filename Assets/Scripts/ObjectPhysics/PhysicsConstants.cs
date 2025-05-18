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
    public const float G = 6.67430e-23f;
}