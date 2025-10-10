using UnityEngine;

/// <summary>
/// Listens to camera events and updates orbit visuals such as trajectories and line visibility.
/// Keeps visual responsibilities separate from CameraController.
/// </summary>
public class OrbitDecorators : MonoBehaviour
{
    [SerializeField] private TrajectoryRenderer trajectoryRenderer;
    [SerializeField] private LineVisibilityController lineVisibilityController;
    [SerializeField] private CameraController cameraController;

    /// <summary>Registers listeners for camera events.</summary>
    private void Awake()
    {
        if (!cameraController)
        {
            Debug.LogWarning("[OrbitDecorators] CameraController not found; orbit visuals disabled.");
            return;
        }

        cameraController.OnTrackedBodyChanged += HandleTrackedBodyChanged;
        cameraController.OnTrackedPlaceholderChanged += HandleTrackedPlaceholderChanged;
        cameraController.OnModeChanged += HandleModeChanged;
    }

    /// <summary>Performs an initial sync with the current camera state.</summary>
    private void Start()
    {
        if (!cameraController) return;

        if (cameraController.IsEarthView)
        {
            HandleModeChanged(CameraMode.Earth);
        }
        else if (cameraController.CurrentPlaceholder != null)
        {
            HandleTrackedPlaceholderChanged(cameraController.CurrentPlaceholder);
        }
        else if (cameraController.CurrentBody != null)
        {
            HandleTrackedBodyChanged(cameraController.CurrentBody);
        }
        else if (cameraController.IsFree)
        {
            Clear();
        }
    }

    /// <summary>Updates trajectory and line visibility when the tracked body changes.</summary>
    private void HandleTrackedBodyChanged(NBody body)
    {
        if (trajectoryRenderer != null)
        {
            // trajectoryRenderer.SetTrackedBody(body);
        }
        if (lineVisibilityController != null)
        {
            lineVisibilityController.SetTrackedBody(body);
        }
    }

    /// <summary>Clears all orbit visuals.</summary>
    private void Clear()
    {
        if (trajectoryRenderer != null)
            trajectoryRenderer.ClearAllLines();

        if (lineVisibilityController != null)
            lineVisibilityController.SetTrackedBody(null);
    }

    /// <summary>Unregisters all camera event listeners.</summary>
    private void OnDestroy()
    {
        if (cameraController == null) return;

        cameraController.OnTrackedBodyChanged -= HandleTrackedBodyChanged;
        cameraController.OnTrackedPlaceholderChanged -= HandleTrackedPlaceholderChanged;
        cameraController.OnModeChanged -= HandleModeChanged;
    }

    /// <summary>Placeholder tracking currently does not modify visuals (reserved for future use).</summary>
    private void HandleTrackedPlaceholderChanged(Transform _)
    {
        // Intentionally left blank.
        // Call Clear() here if placeholder previews should hide orbit visuals.
    }

    /// <summary>Handles camera mode changes to update or clear visuals appropriately.</summary>
    private void HandleModeChanged(CameraMode mode)
    {
        if (mode == CameraMode.Free)
        {
            Clear();
            return;
        }

        if (mode == CameraMode.Earth)
        {
            // Optionally call Clear() if Earth view should hide trajectories.
            return;
        }

        // In Track mode, sync visuals with the current target
        if (cameraController.CurrentBody != null)
        {
            HandleTrackedBodyChanged(cameraController.CurrentBody);
        }
        else if (cameraController.CurrentPlaceholder != null)
        {
            HandleTrackedPlaceholderChanged(cameraController.CurrentPlaceholder);
        }
        else
        {
            Clear();
        }
    }
}
