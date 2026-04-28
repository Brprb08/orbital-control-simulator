using UnityEngine;

public static class ManeuverBurnMath
{
    private const float WorldForceScale = 10f;

    public static bool IsBurnActiveForStep(ManeuverNode node, NBody targetBody, int simulationStep)
    {
        return node != null &&
               node.isFinalized &&
               node.targetBody == targetBody &&
               simulationStep >= node.burnStartStep &&
               simulationStep < node.burnStartStep + node.burnStepCount;
    }

    public static bool TryBuildBurnCommand(
        BurnType burnType,
        Vector3 worldPos,
        Vector3 worldVel,
        Vector3 center,
        float effectiveForwardThrustMagnitude,
        ref Vector3 vCache,
        ref Vector3 hCache,
        out Vector3 thrustForce,
        out sbyte normalSign)
    {
        thrustForce = Vector3.zero;
        normalSign = GetNormalSign(burnType);

        float scaledMagnitude = effectiveForwardThrustMagnitude / WorldForceScale;
        if (!(scaledMagnitude > 0f))
            return false;

        Vector3 burnDir = AttitudeMath.ComputeBurnDirection(
            burnType,
            worldPos,
            worldVel,
            center,
            ref vCache,
            ref hCache
        );

        if (burnDir.sqrMagnitude < 1e-8f)
            burnDir = worldVel.sqrMagnitude > 1e-8f ? worldVel.normalized : Vector3.forward;
        else
            burnDir.Normalize();

        thrustForce = burnDir * scaledMagnitude;
        return true;
    }

    public static sbyte GetNormalSign(BurnType burnType)
    {
        return burnType switch
        {
            BurnType.Normal => (sbyte)1,
            BurnType.AntiNormal => (sbyte)-1,
            _ => (sbyte)0
        };
    }
}
