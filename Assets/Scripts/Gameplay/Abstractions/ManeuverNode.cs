using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Represents a planned orbital maneuver for an NBody object,
/// used in ManeuverNodeManager.
/// Includes timing, delta-V, visualization data, and optional trajectory snapshots.
/// </summary>
public class ManeuverNode
{
    public Vector3 position;
    public float burnTime;
    public Vector3 deltaV;
    public GameObject marker;
    public NBody targetBody;
    public float duration;
    public bool isFinalized;
    public string burnType;

    public List<Vector3> trajectorySnapshot;
    public float snapshotStartTime;
    public float snapshotDeltaTime;

    public bool isPinned;
    public Vector3 pinnedWorldPosition;
}
