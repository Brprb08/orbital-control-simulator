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
    [SerializeField] private GravityManager gravityManager;

    void Awake()
    {
        if (!cameraController)
        {
            Debug.LogWarning("[OrbitDecorators] CameraController not found; orbit visuals disabled.");
            return;
        }

        cameraController.OnTrackedBodyChanged += SetTracked;
        cameraController.OnTrackedPlaceholderChanged += OnPlaceholder;
        cameraController.OnFreeModeChanged += OnFreeMode;
        cameraController.OnEarthViewChanged += OnEarthView;
    }

    void Start()
    {
        if (!cameraController) return;

        // Initial sync to current state
        if (cameraController.IsEarthView)
        {
            OnEarthView(true);
        }
        else if (cameraController.CurrentPlaceholder != null)
        {
            OnPlaceholder(cameraController.CurrentPlaceholder);
        }
        else if (cameraController.CurrentBody != null)
        {
            SetTracked(cameraController.CurrentBody);
        }
        else if (cameraController.IsFree)
        {
            Clear();
        }
    }


    private void SetTracked(NBody body)
    {
        if (trajectoryRenderer != null)
        {
            trajectoryRenderer.SetTrackedBody(body);
            trajectoryRenderer.orbitIsDirty = true;
        }
        if (lineVisibilityManager != null)
            lineVisibilityManager.SetTrackedBody(body);
    }

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

    void OnDestroy()
    {
        if (cameraController == null) return;
        cameraController.OnTrackedBodyChanged -= SetTracked;
        cameraController.OnTrackedPlaceholderChanged -= OnPlaceholder;
        cameraController.OnFreeModeChanged -= OnFreeMode;
        cameraController.OnEarthViewChanged -= OnEarthView;
    }

    private void OnPlaceholder(Transform _)
    {
        // Clear();
    }

    private void OnFreeMode(bool isFree)
    {
        if (isFree) Clear();
    }

    private void OnEarthView(bool inEarth)
    {
        // NO-OP: keep showing the currently tracked satellite’s lines.
        // Do nothing on enter or exit EarthCam.
    }

}
