using System.Text;
using UnityEngine;

/// <summary>
/// Reports missing bootstrap dependencies before partial initialization can begin.
/// </summary>
public static class BootstrapValidator
{
    public static bool TryValidate(BootstrapReferences references, out string error)
    {
        StringBuilder missing = new();

        if (references == null)
        {
            error = "[SimulationBootstrap] Missing required references object.";
            return false;
        }

        Require(references.lineVisibilityController, nameof(references.lineVisibilityController), missing);
        Require(references.bodyService, nameof(references.bodyService), missing);
        Require(references.bodyRuntimeCoordinator, nameof(references.bodyRuntimeCoordinator), missing);
        Require(references.cameraMovement, nameof(references.cameraMovement), missing);
        Require(references.cameraButtonProxy, nameof(references.cameraButtonProxy), missing);
        Require(references.freeCamera, nameof(references.freeCamera), missing);
        Require(references.uIRoot, nameof(references.uIRoot), missing);
        Require(references.cameraController, nameof(references.cameraController), missing);
        Require(references.bodyDropdownManager, nameof(references.bodyDropdownManager), missing);
        Require(references.trajectoryRenderer, nameof(references.trajectoryRenderer), missing);
        Require(references.maneuverNodeManager, nameof(references.maneuverNodeManager), missing);
        Require(references.thrustController, nameof(references.thrustController), missing);
        Require(references.timeController, nameof(references.timeController), missing);
        Require(references.trajectoryComputeController, nameof(references.trajectoryComputeController), missing);
        Require(references.objectPlacementManager, nameof(references.objectPlacementManager), missing);
        Require(references.pendingVelocityPlacementController, nameof(references.pendingVelocityPlacementController), missing);
        Require(references.randomSatelliteSpawner, nameof(references.randomSatelliteSpawner), missing);
        Require(references.satelliteSpawner, nameof(references.satelliteSpawner), missing);
        Require(references.rocketThrustAudio, nameof(references.rocketThrustAudio), missing);
        Require(references.tutorialController, nameof(references.tutorialController), missing);
        Require(references.attitudeUIController, nameof(references.attitudeUIController), missing);
        Require(references.nBodyVectorOverlayController, nameof(references.nBodyVectorOverlayController), missing);

        if (missing.Length == 0)
        {
            error = null;
            return true;
        }

        error = "[SimulationBootstrap] Missing required references:\n" + missing;
        return false;
    }

    private static void Require(Object value, string name, StringBuilder missing)
    {
        if (value == null)
            missing.Append("- ").AppendLine(name);
    }
}
