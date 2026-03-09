using System.Collections.Generic;
using UnityEngine;

public sealed class TrajectoryCentralBodyCache
{
    public const float DefaultEarthRadiusUnity = 637.8f;

    private NBody centralBody;
    private Transform centralBodyTransform;
    private float centralBodyRadiusWorld;

    public bool IsReady { get; private set; }

    public NBody CentralBody => centralBody;

    public Vector3 CenterPosition =>
        centralBodyTransform != null ? centralBodyTransform.position : Vector3.zero;

    public float RadiusWorld => centralBodyRadiusWorld;

    public TrajectoryCentralBodyCache(NBody centralBody)
    {
        Refresh(centralBody);
    }

    public void Refresh(NBody newCentralBody)
    {
        centralBody = newCentralBody;

        if (centralBody == null)
        {
            centralBodyTransform = null;
            centralBodyRadiusWorld = 0f;
            IsReady = false;
            return;
        }

        centralBodyTransform = centralBody.transform;
        centralBodyRadiusWorld = ResolveCentralBodyRadiusWorld(centralBody);
        IsReady = true;
    }

    public Vector3[] ClipTrajectorySphere(Vector3[] points)
    {
        if (points == null || points.Length < 2 || !IsReady)
            return points;

        Vector3 center = centralBodyTransform.position;
        float radius = centralBodyRadiusWorld;
        float radiusSquared = radius * radius;

        List<Vector3> clipped = new List<Vector3>(points.Length) { points[0] };

        for (int i = 1; i < points.Length; i++)
        {
            Vector3 a = points[i - 1];
            Vector3 b = points[i];
            Vector3 delta = b - a;

            Vector3 offset = a - center;
            float A = Vector3.Dot(delta, delta);
            float B = 2f * Vector3.Dot(offset, delta);
            float C = Vector3.Dot(offset, offset) - radiusSquared;

            float discriminant = B * B - 4f * A * C;
            if (discriminant < 0f)
            {
                clipped.Add(b);
                continue;
            }

            float sqrt = Mathf.Sqrt(discriminant);
            float inverse2A = 0.5f / A;

            float t0 = (-B - sqrt) * inverse2A;
            float t1 = (-B + sqrt) * inverse2A;

            bool hit = false;
            float tHit = float.PositiveInfinity;

            if (t0 >= 0f && t0 <= 1f)
            {
                hit = true;
                tHit = Mathf.Min(tHit, t0);
            }

            if (t1 >= 0f && t1 <= 1f)
            {
                hit = true;
                tHit = Mathf.Min(tHit, t1);
            }

            if (!hit)
            {
                clipped.Add(b);
                continue;
            }

            Vector3 hitPoint = a + tHit * delta;
            clipped.Add(hitPoint);
            return clipped.ToArray();
        }

        return clipped.ToArray();
    }

    public Vector3[] ClipToSingleOrbit(Vector3[] points, float fullTurnEpsilon, float minStepAngleRad)
    {
        if (points == null || points.Length < 3 || !IsReady)
            return points;

        Vector3 center = centralBodyTransform.position;
        Vector3 firstRadius = points[0] - center;

        if (firstRadius.sqrMagnitude < 1e-8f)
            return points;

        if (!TryComputeOrbitNormal(points, center, out Vector3 orbitNormal))
            return points;

        float threshold = Mathf.PI * 2f - fullTurnEpsilon;
        float accumulatedAngle = 0f;

        List<Vector3> output = new List<Vector3>(points.Length) { points[0] };
        Vector3 previous = firstRadius;

        for (int i = 1; i < points.Length; i++)
        {
            Vector3 current = points[i] - center;
            float deltaAngle = SignedAngleDelta(previous, current, orbitNormal);

            if (Mathf.Abs(deltaAngle) < minStepAngleRad)
            {
                output.Add(points[i]);
                previous = current;
                continue;
            }

            float nextAccumulatedAngle = accumulatedAngle + deltaAngle;

            if (Mathf.Abs(nextAccumulatedAngle) >= threshold)
            {
                float target = Mathf.Sign(nextAccumulatedAngle) * threshold;
                float needed = target - accumulatedAngle;
                float fraction = Mathf.Clamp01(needed / deltaAngle);

                Vector3 radiusA = points[i - 1] - center;
                Vector3 radiusB = points[i] - center;

                Vector3 dirA = radiusA.normalized;
                Vector3 dirB = radiusB.normalized;

                float angleBetween = SignedAngleDelta(dirA, dirB, orbitNormal);
                Quaternion rotation = Quaternion.AngleAxis(angleBetween * fraction * Mathf.Rad2Deg, orbitNormal);
                Vector3 cutDirection = rotation * dirA;
                float cutRadius = Mathf.Lerp(radiusA.magnitude, radiusB.magnitude, fraction);

                Vector3 cutPosition = center + cutDirection * cutRadius;
                output.Add(cutPosition);
                output.Add(output[0]);

                return output.ToArray();
            }

            accumulatedAngle = nextAccumulatedAngle;
            output.Add(points[i]);
            previous = current;
        }

        return output.ToArray();
    }

    private static bool TryComputeOrbitNormal(Vector3[] points, Vector3 center, out Vector3 normal)
    {
        normal = Vector3.zero;
        Vector3? previous = null;

        for (int i = 1; i < points.Length; i++)
        {
            Vector3 a = previous ?? (points[i - 1] - center);
            Vector3 b = points[i] - center;
            Vector3 cross = Vector3.Cross(a, b);

            float magnitude = cross.magnitude;
            if (magnitude > 1e-6f)
            {
                normal = cross / magnitude;
                return true;
            }

            previous = b;
        }

        return false;
    }

    private static float SignedAngleDelta(Vector3 a, Vector3 b, Vector3 normal)
    {
        a.Normalize();
        b.Normalize();

        float sin = Vector3.Dot(normal, Vector3.Cross(a, b));
        float cos = Mathf.Clamp(Vector3.Dot(a, b), -1f, 1f);

        return Mathf.Atan2(sin, cos);
    }

    private static float ResolveCentralBodyRadiusWorld(NBody central)
    {
        SphereCollider sphereCollider = central.GetComponent<SphereCollider>();
        if (sphereCollider != null)
        {
            float maxScale = Mathf.Max(
                central.transform.lossyScale.x,
                central.transform.lossyScale.y,
                central.transform.lossyScale.z
            );

            return sphereCollider.radius * maxScale;
        }

        try
        {
            System.Type type = central.GetType();

            var radiusField = type.GetField("radius");
            if (radiusField != null && radiusField.FieldType == typeof(float))
                return (float)radiusField.GetValue(central);

            var radiusProperty = type.GetProperty("radius");
            if (radiusProperty != null && radiusProperty.PropertyType == typeof(float))
                return (float)radiusProperty.GetValue(central, null);
        }
        catch
        {
            // Fall back below.
        }

        return DefaultEarthRadiusUnity;
    }
}