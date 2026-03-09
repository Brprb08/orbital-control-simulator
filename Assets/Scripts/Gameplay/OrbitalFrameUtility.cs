using UnityEngine;

public static class OrbitalFrameUtility
{
    private const float EPS = 1e-8f;
    private const float V_MIN = 0.01f;
    private const float H_MIN = 1e-5f;

    public static OrbitalFrame Build(
        Vector3 worldPos,
        Vector3 worldVel,
        Vector3 center,
        ref Vector3 vCache,
        ref Vector3 hCache)
    {
        Vector3 r = worldPos - center;

        Vector3 radialOut = SafeNorm(r, Vector3.up);
        Vector3 radialIn = -radialOut;

        bool hasVelocity = worldVel.magnitude > V_MIN;
        Vector3 prograde = hasVelocity ? worldVel.normalized : SafeNorm(vCache, Vector3.right);
        Vector3 retrograde = -prograde;

        Vector3 h = Vector3.Cross(r, worldVel);
        bool hasNormal = h.magnitude > H_MIN;

        Vector3 normal;
        Vector3 antiNormal;

        if (hasNormal)
        {
            Vector3 hHat = h.normalized;

            // Project convention: normal = -(r × v).normalized
            normal = -hHat;
            antiNormal = hHat;

            hCache = hHat;
        }
        else
        {
            Vector3 fallbackH = SafeNorm(hCache, Vector3.up);
            normal = -fallbackH;
            antiNormal = fallbackH;
        }

        if (hasVelocity)
            vCache = prograde;

        return new OrbitalFrame
        {
            radialOut = radialOut,
            radialIn = radialIn,
            prograde = prograde,
            retrograde = retrograde,
            normal = normal,
            antiNormal = antiNormal,
            hasVelocity = hasVelocity,
            hasNormal = hasNormal
        };
    }

    private static Vector3 SafeNorm(Vector3 v, Vector3 fallback)
    {
        float m = v.magnitude;
        if (m > EPS)
            return v / m;

        float fb = fallback.magnitude;
        if (fb > EPS)
            return fallback / fb;

        return Vector3.forward;
    }
}