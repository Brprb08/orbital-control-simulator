using System;
using UnityEngine;

/// <summary>
/// Inspector-assigned scene references required to compose the simulation.
/// </summary>
[Serializable]
public sealed class BootstrapReferences
{
    [Header("Core")]
    public BodyService bodyService;
    public BodyRuntimeCoordinator bodyRuntimeCoordinator;
    public LineVisibilityController lineVisibilityController;
    public TimeController timeController;

    [Header("Camera")]
    public CameraController cameraController;
    public CameraMovement cameraMovement;
    public FreeCamera freeCamera;
    public CameraInfoUIController cameraInfoUIController;
    public CameraButtonProxy cameraButtonProxy;

    [Header("UI")]
    public UIRoot uIRoot;
    public BodyDropdownManager bodyDropdownManager;
    public AttitudeUIController attitudeUIController;
    public NBodyVectorOverlayController nBodyVectorOverlayController;

    [Header("Trajectory And Maneuvers")]
    public ManeuverNodeManager maneuverNodeManager;
    public ThrustController thrustController;
    public TrajectoryComputeController trajectoryComputeController;
    public TrajectoryRenderer trajectoryRenderer;

    [Header("Placement")]
    public ObjectPlacementManager objectPlacementManager;
    public ManualVelocityPlacementUIController manualVelocityPlacementUIController;
    public PendingVelocityPlacementController pendingVelocityPlacementController;
    public RandomSatelliteSpawner randomSatelliteSpawner;
    public SatelliteSpawner satelliteSpawner;

    [Header("Other")]
    public RocketThrustAudio rocketThrustAudio;
    public TutorialController tutorialController;
}
