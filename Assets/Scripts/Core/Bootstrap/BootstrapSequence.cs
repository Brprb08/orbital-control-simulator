using UnityEngine;

/// <summary>
/// Owns dependency-order initialization after the simulation context has been built.
/// </summary>
public static class BootstrapSequence
{
    public static void Initialize(SimContext ctx, BootstrapReferences references, GameObject fallbackHost)
    {
        references.lineVisibilityController.Initialize(ctx);
        references.bodyService.Initialize(ctx);
        references.bodyRuntimeCoordinator.Initialize(ctx);
        references.cameraMovement.Initialize(ctx);

        references.cameraInfoUIController = EnsureCameraInfoUIController(references, fallbackHost);
        ctx.CameraInfoUIController = references.cameraInfoUIController;
        references.cameraInfoUIController.Initialize(ctx);

        references.cameraButtonProxy.Initialize(ctx);
        references.freeCamera.Initialize(ctx);
        references.uIRoot.Initialize(ctx);
        references.cameraController.Initialize(ctx);
        references.bodyDropdownManager.Initialize(ctx);
        references.trajectoryRenderer.Initialize(ctx);
        references.maneuverNodeManager.Initialize(ctx);
        references.thrustController.Initialize(ctx);
        references.timeController.Initialize(ctx);
        references.trajectoryComputeController.Initialize(ctx);
        references.objectPlacementManager.Initialize(ctx);
        references.manualVelocityPlacementUIController?.Initialize(ctx);
        references.pendingVelocityPlacementController.Initialize(ctx);
        references.randomSatelliteSpawner.Initialize(ctx);
        references.satelliteSpawner.Initialize(ctx);
        references.rocketThrustAudio.Initialize(ctx);
        references.tutorialController.Initialize(ctx);
        references.attitudeUIController.Initialize(ctx);
        references.nBodyVectorOverlayController.Initialize(ctx);
    }

    private static CameraInfoUIController EnsureCameraInfoUIController(
        BootstrapReferences references,
        GameObject fallbackHost)
    {
        if (references.cameraInfoUIController != null)
            return references.cameraInfoUIController;

        if (references.cameraMovement != null &&
            references.cameraMovement.TryGetComponent(out CameraInfoUIController existing))
        {
            return existing;
        }

        GameObject host = references.cameraMovement != null
            ? references.cameraMovement.gameObject
            : fallbackHost;

        return host.AddComponent<CameraInfoUIController>();
    }
}
