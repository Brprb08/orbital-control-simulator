using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;

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
    // Planner-only vector command. Manual nodes continue to use burnType.
    public bool usesVectorDirection;
    public Vector3 vectorDirectionWorld;

    // A later manual node starts from the preceding burn's predicted end state.
    public bool hasPredictionSeed;
    public float predictionSeedTime;
    public double3 predictionSeedPosition;
    public double3 predictionSeedVelocity;
    public double predictionSeedMassKg;
    public double predictionSeedFuelKg;
    public bool hasPredictedPostBurnState;
    public float predictedBurnEndTime;
    public double3 predictedPostBurnPosition;
    public double3 predictedPostBurnVelocity;
    public double predictedPostBurnMassKg;
    public double predictedPostBurnFuelKg;

    public List<Vector3> trajectorySnapshot;
    public float snapshotStartTime;
    public float snapshotDeltaTime;

    public bool isPinned;
    public Vector3 pinnedWorldPosition;

    // Mirrors using BodyRuntimeCoordinator.BaseSimulationStep for old UI/tests/debug.
    public int burnStartStep;
    public int burnStepCount;
}
