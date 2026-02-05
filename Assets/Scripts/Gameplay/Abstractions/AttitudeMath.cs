using UnityEngine;

public static class AttitudeMath
{
    private const float V_MIN = 0.01f;
    private const float H_MIN = 1e-5f;
    private const float EPS = 1e-8f;

    public static Vector3 ComputeBurnDirection(
        BurnType burnType,
        Vector3 worldPos,
        Vector3 worldVel,
        Vector3 center,
        ref Vector3 vCache,
        ref Vector3 hCache)
    {
        Vector3 r = worldPos - center;
        Vector3 rHat = SafeNorm(r, Vector3.up);
        Vector3 vHat = SafeNorm(worldVel, vCache);

        // Right-hand-rule orbit normal: h = r × v
        Vector3 h = Vector3.Cross(r, worldVel);
        Vector3 hHat = SafeNorm(h, hCache);

        bool okV = worldVel.magnitude > V_MIN;
        bool okH = h.magnitude > H_MIN;

        switch (burnType)
        {
            case BurnType.Prograde:
                return okV ? vHat : vCache;

            case BurnType.Retrograde:
                return -(okV ? vHat : vCache);

            case BurnType.RadialIn:
                return -rHat;

            case BurnType.RadialOut:
                return rHat;

            case BurnType.Normal:
                // ALWAYS +h = r × v
                return okH ? hHat : Vector3.up;

            case BurnType.AntiNormal:
                // ALWAYS -h
                return okH ? -hHat : -Vector3.up;

            default:
                return okV ? vHat : vCache;
        }
    }

    private static Vector3 SafeNorm(Vector3 v, Vector3 fallback)
    {
        float m = v.magnitude;
        return (m > EPS)
            ? (v / m)
            : (fallback.sqrMagnitude > 0f ? fallback.normalized : Vector3.forward);
    }
}
