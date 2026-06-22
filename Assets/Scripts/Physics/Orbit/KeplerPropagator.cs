using System;
using Unity.Mathematics;

public static class KeplerPropagator
{
    private const double Small = 1e-10;
    private const int MaxIterations = 32;

    public static bool TryPropagateUniversal(
        double3 position,
        double3 velocity,
        double mu,
        double deltaTime,
        out double3 propagatedPosition,
        out double3 propagatedVelocity)
    {
        propagatedPosition = position;
        propagatedVelocity = velocity;

        if (!(mu > 0.0) || !IsFinite(deltaTime))
            return false;

        if (Math.Abs(deltaTime) < 1e-9)
            return true;

        double r0 = math.length(position);
        double v0Sq = math.lengthsq(velocity);
        if (!(r0 > Small) || !(v0Sq > Small))
            return false;

        double sqrtMu = Math.Sqrt(mu);
        double radialVelocity0 = math.dot(position, velocity) / r0;
        double alpha = 2.0 / r0 - v0Sq / mu;
        double chi = InitialUniversalAnomaly(alpha, sqrtMu, deltaTime, r0, radialVelocity0);

        if (!IsFinite(chi))
            return false;

        for (int i = 0; i < MaxIterations; i++)
        {
            double z = alpha * chi * chi;
            double c = StumpffC(z);
            double s = StumpffS(z);

            double f =
                r0 * radialVelocity0 / sqrtMu * chi * chi * c +
                (1.0 - alpha * r0) * chi * chi * chi * s +
                r0 * chi -
                sqrtMu * deltaTime;

            double df =
                r0 * radialVelocity0 / sqrtMu * chi * (1.0 - z * s) +
                (1.0 - alpha * r0) * chi * chi * c +
                r0;

            if (Math.Abs(df) < Small)
                return false;

            double correction = f / df;
            chi -= correction;

            if (Math.Abs(correction) < 1e-9)
                break;
        }

        double finalZ = alpha * chi * chi;
        double finalC = StumpffC(finalZ);
        double finalS = StumpffS(finalZ);

        double lagrangeF = 1.0 - chi * chi / r0 * finalC;
        double lagrangeG = deltaTime - chi * chi * chi * finalS / sqrtMu;
        double3 r = lagrangeF * position + lagrangeG * velocity;
        double rMag = math.length(r);

        if (!(rMag > Small))
            return false;

        double lagrangeFDot = sqrtMu / (rMag * r0) * (alpha * chi * chi * chi * finalS - chi);
        double lagrangeGDot = 1.0 - chi * chi / rMag * finalC;
        double3 v = lagrangeFDot * position + lagrangeGDot * velocity;

        if (!IsFinite(r) || !IsFinite(v))
            return false;

        propagatedPosition = r;
        propagatedVelocity = v;
        return true;
    }

    private static double InitialUniversalAnomaly(
        double alpha,
        double sqrtMu,
        double deltaTime,
        double r0,
        double radialVelocity0)
    {
        if (Math.Abs(alpha) > 1e-8)
            return sqrtMu * Math.Abs(alpha) * deltaTime;

        double denom = r0 * Math.Max(1e-8, Math.Abs(radialVelocity0));
        return sqrtMu * deltaTime / denom;
    }

    private static double StumpffC(double z)
    {
        if (z > 1e-8)
        {
            double sqrtZ = Math.Sqrt(z);
            return (1.0 - Math.Cos(sqrtZ)) / z;
        }

        if (z < -1e-8)
        {
            double sqrtNegZ = Math.Sqrt(-z);
            return (Math.Cosh(sqrtNegZ) - 1.0) / -z;
        }

        return 0.5 - z / 24.0 + z * z / 720.0 - z * z * z / 40320.0;
    }

    private static double StumpffS(double z)
    {
        if (z > 1e-8)
        {
            double sqrtZ = Math.Sqrt(z);
            return (sqrtZ - Math.Sin(sqrtZ)) / (sqrtZ * sqrtZ * sqrtZ);
        }

        if (z < -1e-8)
        {
            double sqrtNegZ = Math.Sqrt(-z);
            return (Math.Sinh(sqrtNegZ) - sqrtNegZ) / (sqrtNegZ * sqrtNegZ * sqrtNegZ);
        }

        return 1.0 / 6.0 - z / 120.0 + z * z / 5040.0 - z * z * z / 362880.0;
    }

    private static bool IsFinite(double value)
    {
        return !(double.IsNaN(value) || double.IsInfinity(value));
    }

    private static bool IsFinite(double3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }
}
