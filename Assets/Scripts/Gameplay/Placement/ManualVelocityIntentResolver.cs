using UnityEngine;

public readonly struct ManualVelocityIntentResult
{
    public ManualVelocityIntentResult(Vector3 direction, float circularSpeed, Vector3 velocity)
    {
        Direction = direction;
        CircularSpeed = circularSpeed;
        Velocity = velocity;
    }

    public Vector3 Direction { get; }
    public float CircularSpeed { get; }
    public Vector3 Velocity { get; }
}

/// <summary>
/// Resolves manual-placement orbit intent into a concrete launch velocity.
/// </summary>
public static class ManualVelocityIntentResolver
{
    public static bool TryResolve(
        ManualVelocityIntent intent,
        Transform pendingBody,
        NBody centralBody,
        float maxTiltDegrees,
        out ManualVelocityIntentResult result,
        out string error)
    {
        result = default;
        error = null;

        if (intent == null)
        {
            error = "Orbit intent is missing.";
            return false;
        }

        if (pendingBody == null)
        {
            error = "No pending satellite to stage velocity for.";
            return false;
        }

        if (centralBody == null)
        {
            error = "Central body is missing; cannot stage orbit velocity.";
            return false;
        }

        if (!(centralBody.trueMass > 0.0))
        {
            error = "Central body mass is invalid; cannot compute circular speed.";
            return false;
        }

        if (!TryBuildPlacementFrame(pendingBody.position, centralBody.transform.position, out _, out var radialOut, out var prograde, out var normal))
        {
            error = "Could not resolve an orbit direction from this placement.";
            return false;
        }

        float radius = Vector3.Distance(pendingBody.position, centralBody.transform.position);
        if (radius <= 1e-4f)
        {
            error = "Place the satellite away from the central body before staging velocity.";
            return false;
        }

        double mu = PhysicsConstants.G * centralBody.trueMass;
        float circularSpeed = Mathf.Sqrt((float)(mu / radius));
        if (!float.IsFinite(circularSpeed) || circularSpeed <= 0f)
        {
            error = "Central body mass is invalid; cannot compute circular speed.";
            return false;
        }

        Vector3 baseDirection = intent.BaseDirection == ManualOrbitBaseDirection.Retrograde
            ? -prograde
            : prograde;

        float tiltRadians = Mathf.Clamp(intent.TiltDegrees, -maxTiltDegrees, maxTiltDegrees) * Mathf.Deg2Rad;
        Vector3 tiltedBase = (baseDirection * Mathf.Cos(tiltRadians)) + (normal * Mathf.Sin(tiltRadians));
        Vector3 direction = tiltedBase + (radialOut * intent.RadialShapeAmount);

        if (direction.sqrMagnitude <= 1e-8f)
        {
            error = "Could not resolve an orbit direction from this placement.";
            return false;
        }

        direction.Normalize();
        Vector3 velocity = direction * circularSpeed * Mathf.Max(0.01f, intent.SpeedScale);
        result = new ManualVelocityIntentResult(direction, circularSpeed, velocity);
        return true;
    }

    private static bool TryBuildPlacementFrame(
        Vector3 bodyPosition,
        Vector3 centralPosition,
        out Vector3 center,
        out Vector3 radialOut,
        out Vector3 prograde,
        out Vector3 normal)
    {
        center = centralPosition;
        radialOut = bodyPosition - center;
        prograde = Vector3.zero;
        normal = Vector3.zero;

        if (radialOut.sqrMagnitude <= 1e-8f)
            return false;

        radialOut.Normalize();

        Vector3 referenceNormal = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(referenceNormal, radialOut)) > 0.95f)
            referenceNormal = Vector3.forward;

        prograde = Vector3.Cross(radialOut, referenceNormal);
        if (prograde.sqrMagnitude <= 1e-8f)
            return false;

        prograde.Normalize();
        normal = Vector3.Cross(radialOut, prograde).normalized;
        return normal.sqrMagnitude > 1e-8f;
    }
}
