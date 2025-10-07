using UnityEngine;

/// <summary>
/// Listens to camera events and updates trajectory + line visibility.
/// Keeps visuals out of CameraController.
/// </summary>
public class OrbitDecorators : MonoBehaviour
{
    [SerializeField] private TrajectoryRenderer trajectoryRenderer;
    [SerializeField] private LineVisibilityManager lineVisibilityManager;
    [SerializeField] private CameraController cameraController;

    /// <summary>
    /// Unity Awake: wires event listeners.
    /// </summary>
    private void Awake()
    {
        if (!cameraController)
        {
            Debug.LogWarning("[OrbitDecorators] CameraController not found; orbit visuals disabled.");
            return;
        }

        cameraController.OnTrackedBodyChanged += HandleTrackedBodyChanged;
        cameraController.OnTrackedPlaceholderChanged += HandleTrackedPlaceholderChanged;

        // ===== REFACTOR: replace legacy OnFreeModeChanged/OnEarthViewChanged with OnModeChanged
        cameraController.OnModeChanged += HandleModeChanged;
    }

    /// <summary>
    /// Unity Start: perform an initial sync to the current controller state.
    /// </summary>
    private void Start()
    {
        if (!cameraController) return;

        // ===== REFACTOR: initial sync via consolidated properties and mode
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

    /// <summary>
    /// Sets the tracked body in renderers/visibility managers.
    /// </summary>
    /// <param name="body">The body that is now tracked.</param>
    private void HandleTrackedBodyChanged(NBody body)
    {
        if (trajectoryRenderer != null)
        {
            trajectoryRenderer.SetTrackedBody(body);
            trajectoryRenderer.orbitIsDirty = true;
        }
        if (lineVisibilityManager != null)
        {
            lineVisibilityManager.SetTrackedBody(body);
        }
    }

    /// <summary>
    /// Clears all trajectory and line visuals.
    /// </summary>
    private void Clear()
    {
        if (trajectoryRenderer != null)
        {
            trajectoryRenderer.ClearAllLinesAndUI();
        }
        if (lineVisibilityManager != null)
        {
            lineVisibilityManager.SetTrackedBody(null);
        }
    }

    /// <summary>
    /// Unity OnDestroy: unhooks listeners.
    /// </summary>
    private void OnDestroy()
    {
        if (cameraController == null) return;

        cameraController.OnTrackedBodyChanged -= HandleTrackedBodyChanged;
        cameraController.OnTrackedPlaceholderChanged -= HandleTrackedPlaceholderChanged;

        // ===== REFACTOR: unsubscribe from OnModeChanged instead of legacy events
        cameraController.OnModeChanged -= HandleModeChanged;
    }

    /// <summary>
    /// Placeholder selection currently doesn't change visuals; reserved for future behavior.
    /// </summary>
    /// <param name="_">The placeholder transform.</param>
    private void HandleTrackedPlaceholderChanged(Transform _)
    {
        // Intentionally left blank (keeps whatever body visuals are shown).
        // If you want to hide lines when previewing a placeholder, call Clear() here.
    }

    /// <summary>
    /// Responds to consolidated camera mode changes.
    /// </summary>
    /// <param name="mode">New camera mode.</param>
    private void HandleModeChanged(CameraMode mode)
    {
        // ===== REFACTOR: consolidate legacy free/earth handling here
        if (mode == CameraMode.Free)
        {
            Clear();
            return;
        }

        if (mode == CameraMode.Earth)
        {
            // NO-OP: keep showing the currently tracked satellite’s lines.
            // You can choose to Clear() here if Earth view should hide trajectories.
            return;
        }

        // In Track mode, ensure visuals match whichever target is active
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