using UnityEngine;

public static class AttitudeMath
{
    public static Vector3 ComputeBurnDirection(
        BurnType burnType,
        Vector3 worldPos,
        Vector3 worldVel,
        Vector3 center,
        ref Vector3 vCache,
        ref Vector3 hCache)
    {
        OrbitalFrame frame = OrbitalFrameUtility.Build(
            worldPos,
            worldVel,
            center,
            ref vCache,
            ref hCache
        );

        return burnType switch
        {
            BurnType.Prograde => frame.prograde,
            BurnType.Retrograde => frame.retrograde,
            BurnType.RadialIn => frame.radialIn,
            BurnType.RadialOut => frame.radialOut,
            BurnType.Normal => frame.normal,
            BurnType.AntiNormal => frame.antiNormal,
            _ => frame.prograde
        };
    }
}