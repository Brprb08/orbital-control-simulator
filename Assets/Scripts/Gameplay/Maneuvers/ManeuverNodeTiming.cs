using UnityEngine;

public static class ManeuverNodeTiming
{
    public static bool TryGetBoundOrbitPeriod(BodyService bodyService, NBody body, out float orbitalPeriod)
    {
        orbitalPeriod = 0f;

        if (body == null || bodyService == null || bodyService.CentralBody == null)
            return false;

        var central = bodyService.CentralBody;
        var orbit = OrbitalCalculations.CalculateOrbitalParameters(
            central.trueMass,
            central.state.position,
            body.state.position,
            body.state.velocity
        );

        if (!orbit.isValid || orbit.eccentricity >= 1f || orbit.orbitalPeriod <= 0f)
            return false;

        orbitalPeriod = orbit.orbitalPeriod;
        return true;
    }

    public static float ResolveFutureBurnTime(float burnTime, float simulationTime, float orbitalPeriod)
    {
        if (!CanWrap(orbitalPeriod) || !float.IsFinite(burnTime) || !float.IsFinite(simulationTime))
            return burnTime;

        float timeToNode = burnTime - simulationTime;
        if (timeToNode > 0f)
            return burnTime;

        float wrappedTimeToNode = WrapTimeToNode(timeToNode, orbitalPeriod);
        if (Mathf.Approximately(wrappedTimeToNode, 0f))
            wrappedTimeToNode = orbitalPeriod;

        return simulationTime + wrappedTimeToNode;
    }

    public static float GetTimeToNode(float burnTime, float simulationTime, float orbitalPeriod)
    {
        if (!float.IsFinite(burnTime) || !float.IsFinite(simulationTime))
            return float.NaN;

        float timeToNode = burnTime - simulationTime;
        if (timeToNode >= 0f || !CanWrap(orbitalPeriod))
            return timeToNode;

        return WrapTimeToNode(timeToNode, orbitalPeriod);
    }

    private static float WrapTimeToNode(float timeToNode, float orbitalPeriod)
    {
        float wrapped = Mathf.Repeat(timeToNode, orbitalPeriod);
        return Mathf.Approximately(wrapped, orbitalPeriod) ? 0f : wrapped;
    }

    private static bool CanWrap(float orbitalPeriod)
    {
        return float.IsFinite(orbitalPeriod) && orbitalPeriod > 0f;
    }
}
