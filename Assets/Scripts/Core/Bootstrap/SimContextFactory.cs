/// <summary>
/// Builds the shared simulation context from inspector-assigned bootstrap references.
/// </summary>
public static class SimContextFactory
{
    public static SimContext Create(BootstrapReferences references)
    {
        return new SimContext
        {
            LineVisibilityController = references.lineVisibilityController,
            BodyRuntimeCoordinator = references.bodyRuntimeCoordinator,
            CameraController = references.cameraController,
            CameraMovement = references.cameraMovement,
            FreeCamera = references.freeCamera,
            CameraInfoUIController = references.cameraInfoUIController,
            BodyDropdownManager = references.bodyDropdownManager,
            ManeuverNodeManager = references.maneuverNodeManager,
            ThrustController = references.thrustController,
            TimeController = references.timeController,
            UIRoot = references.uIRoot,
            TrajectoryComputeController = references.trajectoryComputeController,
            TrajectoryRenderer = references.trajectoryRenderer,
            ObjectPlacementManager = references.objectPlacementManager,
            ManualVelocityPlacementUIController = references.manualVelocityPlacementUIController,
            PendingVelocityPlacementController = references.pendingVelocityPlacementController,
            RocketThrustAudio = references.rocketThrustAudio,
            TutorialController = references.tutorialController,
            CameraButtonProxy = references.cameraButtonProxy,
            RandomSatelliteSpawner = references.randomSatelliteSpawner,
            BodyService = references.bodyService,
            AttitudeUIController = references.attitudeUIController,
            NBodyVectorOverlayController = references.nBodyVectorOverlayController,
        };
    }
}
