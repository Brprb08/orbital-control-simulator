using UnityEngine;

/// <summary>Immutable burn command for one physics step, including exact start/end boundaries.</summary>
internal readonly struct ScheduledBurn
{
    public NBody TargetBody { get; }
    public float StartTime { get; }
    public float EndTime { get; }
    private readonly BurnType burnType;
    private readonly float thrustNewtons;
    public bool IsValid => TargetBody != null;

    public ScheduledBurn(ManeuverNode node, float thrustNewtons)
    {
        TargetBody = node.targetBody;
        StartTime = node.burnTime;
        EndTime = ManeuverBurnMath.GetBurnEndTime(node);
        burnType = node.burnType;
        this.thrustNewtons = thrustNewtons;
    }

    public bool TryBuildCommand(Vector3 position, Vector3 velocity, Vector3 center,
        ref Vector3 velocityCache, ref Vector3 normalCache,
        out Vector3 force, out sbyte normalSign)
    {
        return ManeuverBurnMath.TryBuildBurnCommand(burnType, position, velocity, center,
            thrustNewtons, ref velocityCache, ref normalCache, out force, out normalSign);
    }
}
