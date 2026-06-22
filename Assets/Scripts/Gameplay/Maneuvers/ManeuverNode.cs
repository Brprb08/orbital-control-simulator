using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Represents a planned orbital maneuver for an NBody object.
/// 
/// Authoritative execution schedule:
/// - burnStartStep
/// - burnStepCount
///
/// UI/debug mirrors:
/// - burnTime
/// - duration
/// 
/// burnTime and duration are kept in sync with the step schedule for display,
/// but runtime burn execution should use the step fields.
/// </summary>
public class ManeuverNode
{
    public Vector3 position;

    // UI/debug mirror of burnStartStep * fixedDt
    public float burnTime;

    // Preview/display result
    public Vector3 deltaV;

    public GameObject marker;
    public NBody targetBody;

    // UI/debug mirror of burnStepCount * fixedDt
    public float duration;

    public bool isFinalized;
    public BurnType burnType;

    public List<Vector3> trajectorySnapshot;
    public float snapshotStartTime;
    public float snapshotDeltaTime;

    public bool isPinned;
    public Vector3 pinnedWorldPosition;

    public int burnStartStep;
    public int burnStepCount;
}
