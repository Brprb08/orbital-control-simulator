using UnityEngine;

public static class AttitudeMath
{
    private const float V_MIN = 0.01f;
    private const float H_MIN = 1e-5f;
    private const float ANG_MIN_DEG = 5f;
    private const float EPS = 1e-8f;

    private const float POLAR_INCL_TOL_DEG = 0.1f;
    private static readonly Vector3 WORLD_NORTH = Vector3.up;
    private const float NORTH_VEL_EPS = 1e-4f;

    public static Vector3 ComputeBurnDirection(
        BurnType burnType,
        Vector3 worldPos,
        Vector3 worldVel,
        Vector3 center,
        ref Vector3 vCache,
        ref Vector3 hCache,
        ref int lastPolarSign,
        bool useParitySwap = true)
    {
        Vector3 r = worldPos - center;
        Vector3 rHat = SafeNorm(r, Vector3.up);
        Vector3 vHat = SafeNorm(worldVel, vCache);

        Vector3 h = Vector3.Cross(r, worldVel);
        Vector3 hHat = SafeNorm(h, hCache);

        float alpha = Vector3.Angle(rHat, vHat);
        bool okV = worldVel.magnitude > V_MIN;
        bool okH = h.magnitude > H_MIN && alpha > ANG_MIN_DEG;

        BuildTangentFrame(rHat, out var tHat, out var nFallback);

        int liveSign = ComputeLiveSign(vHat, h, hHat, ref lastPolarSign);
        bool progradeForMapping = (liveSign > 0);

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
                if (okH)
                {
                    // For attitude swap 90°-swap behavior,
                    // but for burns we want pure +h.
                    if (useParitySwap)
                        return progradeForMapping ? -hHat : hHat;
                    else
                        return hHat;          // pure +h
                }
                return -nFallback;

            case BurnType.AntiNormal:
                if (okH)
                {
                    if (useParitySwap)
                        return progradeForMapping ? hHat : -hHat;
                    else
                        return -hHat;         // pure -h
                }
                return nFallback;

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

    private static void BuildTangentFrame(Vector3 rHat, out Vector3 tHat, out Vector3 nFallback)
    {
        Vector3 refUp = (Mathf.Abs(Vector3.Dot(rHat, Vector3.up)) < 0.9f)
            ? Vector3.up
            : Vector3.forward;

        tHat = Vector3.Cross(refUp, rHat);
        if (tHat.sqrMagnitude < EPS)
        {
            refUp = Vector3.right;
            tHat = Vector3.Cross(refUp, rHat);
        }
        tHat.Normalize();

        nFallback = Vector3.Cross(rHat, tHat);
        nFallback.Normalize();
    }

    private static int ComputeLiveSign(
        Vector3 vHat,
        Vector3 h,
        Vector3 hHat,
        ref int lastPolarSign)
    {
        int defaultSign = (h.y < 0f) ? +1 : -1;

        // If h is degenerate, just fall back to old behavior
        if (h.sqrMagnitude <= H_MIN * H_MIN)
            return defaultSign;

        bool nearPolar = IsNearPolar(hHat);

        // If not near polar, keep original behavior
        if (!nearPolar)
            return defaultSign;

        // Polar case: ascending vs descending based on velocity along "north"
        float vNorth = Vector3.Dot(vHat, WORLD_NORTH);

        // Deadband to avoid jitter right at the turning point
        if (Mathf.Abs(vNorth) < NORTH_VEL_EPS)
            return lastPolarSign;

        // ascending (vNorth > 0) vs descending (vNorth < 0)
        int sign = (vNorth > 0f) ? -1 : +1;
        lastPolarSign = sign;
        return sign;
    }

    private static bool IsNearPolar(Vector3 hHat)
    {
        float cosI = Mathf.Clamp(Vector3.Dot(hHat, Vector3.up), -1f, 1f);
        float inclDeg = Mathf.Acos(Mathf.Abs(cosI)) * Mathf.Rad2Deg;
        return Mathf.Abs(inclDeg - 90f) <= POLAR_INCL_TOL_DEG;
    }
}
