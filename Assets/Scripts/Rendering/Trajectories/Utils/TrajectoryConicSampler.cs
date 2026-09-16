using System;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Builds the geometric path of a bound two-body orbit directly from its state.
/// This is intended for rendering only: it avoids a time-horizon cutoff when a
/// maneuver creates a very long, otherwise ballistic ellipse.
/// </summary>
public static class TrajectoryConicSampler
{
    private const double MinimumEccentricityDirection = 1e-5;

    public static bool TrySampleBoundOrbit(
        Vector3 startPosition,
        Vector3 startVelocity,
        Vector3 centralPosition,
        double centralMass,
        int sampleCount,
        out Vector3[] points)
    {
        points = Array.Empty<Vector3>();

        double mu = PhysicsConstants.G * centralMass;
        if (!(mu > 0d))
            return false;

        double3 center = ToDouble3(centralPosition);
        double3 r = ToDouble3(startPosition) - center;
        double3 v = ToDouble3(startVelocity);
        double rMagnitude = math.length(r);
        double hMagnitude = math.length(math.cross(r, v));
        if (!(rMagnitude > 1e-6d) || !(hMagnitude > 1e-10d))
            return false;

        double speedSquared = math.lengthsq(v);
        double inverseSemiMajorAxis = 2d / rMagnitude - speedSquared / mu;
        if (!(inverseSemiMajorAxis > 0d))
            return false;

        double semiMajorAxis = 1d / inverseSemiMajorAxis;
        double3 normal = math.normalize(math.cross(r, v));
        double3 radialDirection = r / rMagnitude;
        double3 eccentricityVector = math.cross(v, math.cross(r, v)) / mu - radialDirection;
        double eccentricity = math.length(eccentricityVector);
        if (!(eccentricity >= 0d) || eccentricity >= 1d - 1e-7d)
            return false;

        double3 periapsisDirection = eccentricity > MinimumEccentricityDirection
            ? eccentricityVector / eccentricity
            : radialDirection;
        double3 transverseDirection = math.normalize(math.cross(normal, periapsisDirection));
        if (math.lengthsq(transverseDirection) < 1e-12d)
            return false;

        double cosTrueAnomaly = math.clamp(math.dot(periapsisDirection, radialDirection), -1d, 1d);
        double sinTrueAnomaly = math.clamp(math.dot(transverseDirection, radialDirection), -1d, 1d);
        double eccentricAnomaly;

        if (eccentricity <= MinimumEccentricityDirection)
        {
            eccentricAnomaly = Math.Atan2(sinTrueAnomaly, cosTrueAnomaly);
        }
        else
        {
            double denominator = 1d + eccentricity * cosTrueAnomaly;
            if (!(denominator > 1e-10d))
                return false;

            double cosE = (eccentricity + cosTrueAnomaly) / denominator;
            double sinE = Math.Sqrt(Math.Max(0d, 1d - eccentricity * eccentricity)) *
                          sinTrueAnomaly / denominator;
            eccentricAnomaly = Math.Atan2(sinE, cosE);
        }

        int count = Mathf.Clamp(sampleCount, 64, 2048);
        points = new Vector3[count + 1];
        double semiMinorAxis = semiMajorAxis * Math.Sqrt(Math.Max(0d, 1d - eccentricity * eccentricity));

        for (int i = 0; i <= count; i++)
        {
            double anomaly = eccentricAnomaly + (2d * Math.PI * i / count);
            double3 offset =
                periapsisDirection * (semiMajorAxis * (Math.Cos(anomaly) - eccentricity)) +
                transverseDirection * (semiMinorAxis * Math.Sin(anomaly));
            double3 position = center + offset;
            points[i] = new Vector3((float)position.x, (float)position.y, (float)position.z);
        }

        // Avoid a tiny numerical seam at the maneuver/current-state point.
        points[0] = startPosition;
        points[^1] = startPosition;
        return true;
    }

    private static double3 ToDouble3(Vector3 value)
    {
        return new double3(value.x, value.y, value.z);
    }
}
