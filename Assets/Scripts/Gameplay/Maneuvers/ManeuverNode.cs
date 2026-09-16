using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Represents a planned orbital maneuver for an NBody object.
/// 
/// Authoritative execution schedule:
/// - burnTime
/// - duration
///
/// Legacy/debug mirrors:
/// - burnStartStep
/// - burnStepCount
/// 
/// Runtime burn execution uses seconds so larger Unity fixed ticks can be split
/// safely around maneuver boundaries.
/// </summary>
public class ManeuverNode
{
    public Vector3 position;

    public float burnTime;

    // Net velocity change over the finite burn, including gravity (world units/s).
    // This legacy vector is not propulsive delta-v expenditure.
    public Vector3 deltaV;
    public double predictedPropulsiveDeltaVMetersPerSecond;
    public double predictedFuelUsedKg;
    public bool insufficientPropellant;

    public GameObject marker;
    public NBody targetBody;

    public float duration;

    public bool isFinalized;
    public BurnType burnType;

    public List<Vector3> trajectorySnapshot;
    public float snapshotStartTime;
    public float snapshotDeltaTime;

    public bool isPinned;
    public Vector3 pinnedWorldPosition;

    // Mirrors using BodyRuntimeCoordinator.BaseSimulationStep for old UI/tests/debug.
    public int burnStartStep;
    public int burnStepCount;
}
