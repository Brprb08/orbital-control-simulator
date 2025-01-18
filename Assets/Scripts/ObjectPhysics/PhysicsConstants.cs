public static class PhysicsConstants
{
    /**
    * Adjusted G for 1 unit = 10 km scale:
    * G in SI: ~6.67430e-11 m^3/(kg·s^2)
    * 1 unit = 10,000 m, so 1 unit^3 = (10,000 m)^3 = 1e12 m^3
    * G in unit scale: G * (1 / 1e12) = 6.67430e-11 / 1e12 = 6.67430e-23
    **/
    public const float G = 6.67430e-23f;
}