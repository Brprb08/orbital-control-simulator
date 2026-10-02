using UnityEngine;

/// <summary>Inspector-configurable policy for rendezvous search and validation.</summary>
[CreateAssetMenu(fileName = "RendezvousPlannerSettings", menuName = "Simulation/Rendezvous Planner Settings")]
public sealed class RendezvousPlannerSettings : ScriptableObject
{
    [Header("Planning window")]
    [Min(3600)] public double minimumPlanningHorizonSeconds = 86400;
    [Min(3600)] public double maximumPlanningHorizonSeconds = 172800;

    [Header("Search coverage")]
    [Range(1, 16)] public int scalarCandidatesPerBurnFamily = 3;
    [Range(1, 16)] public int vectorCandidatesPerBurnFamily = 4;
    [Range(1, 24)] public int explicitVectorCandidates = 8;
    [Range(1, 32)] public int scalarCorrectionIterations = 14;
    [Range(1, 32)] public int vectorCorrectionIterations = 10;
    [Range(0, 8)] public int fuelRefinementIterations = 1;

    [Header("Ranking")]
    [Min(0)] public double balancedWaitCostMetersPerSecondPerHour = 25;
    [Min(0)] public double startSoonerWaitCostMetersPerSecondPerHour = 100;

    [Header("Flight limits")]
    [Min(1)] public double maximumBurnDeltaVMetersPerSecond = 5000;
    [Tooltip("Total delta-v limit for both scalar and vector plans.")]
    [Min(1)] public double maximumVectorPlanDeltaVMetersPerSecond = 6000;
    [Min(0.02f)] public double maximumBurnDurationSeconds = 1800;

    [Header("Arrival validation")]
    [Min(0.01f)] public double optimizerDistanceMeters = 100;
    [Min(0.001f)] public double optimizerRelativeSpeedMetersPerSecond = 0.1;
    [Min(0.01f)] public double validationDistanceMeters = 200;
    [Min(0.001f)] public double validationRelativeSpeedMetersPerSecond = 0.2;

    public RendezvousPlanner.Settings Snapshot() => new RendezvousPlanner.Settings(
        minimumPlanningHorizonSeconds, maximumPlanningHorizonSeconds,
        scalarCandidatesPerBurnFamily, vectorCandidatesPerBurnFamily,
        explicitVectorCandidates, scalarCorrectionIterations,
        vectorCorrectionIterations, fuelRefinementIterations,
        balancedWaitCostMetersPerSecondPerHour,
        startSoonerWaitCostMetersPerSecondPerHour,
        maximumBurnDeltaVMetersPerSecond, maximumVectorPlanDeltaVMetersPerSecond,
        maximumBurnDurationSeconds, optimizerDistanceMeters,
        optimizerRelativeSpeedMetersPerSecond, validationDistanceMeters,
        validationRelativeSpeedMetersPerSecond);
}
