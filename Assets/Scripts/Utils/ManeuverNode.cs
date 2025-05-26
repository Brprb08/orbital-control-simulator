using UnityEngine;

/// <summary>
/// Represents a planned maneuver for an NBody object,
/// including burn time, duration, delta-V vector, and visual marker.
/// Used by the maneuver system to execute thrust at the correct time
/// and visualize orbital changes.
/// </summary>
public class ManeuverNode
{
    public Vector3 position;
    public Vector3 deltaV;
    public float burnTime;
    public GameObject marker;
    public NBody targetBody;
    public float duration;
    public float elapsedTime = 0f;
    public bool isFinalized = false;
    public string burnType;
}
