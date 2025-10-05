using UnityEngine;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using System.Collections;
using TMPro;

/// <summary>
/// Handles camera movement, setting the current tracked body, and switching between cameras.
/// Supports switching between tracking celestial bodies and free movement.
/// Also manages trajectory visualization and placeholder tracking for temporary objects.
/// </summary>
public class CameraController : MonoBehaviour, ICameraTracker
{
    [Header("Core References")]
    private GravityManager gravityManager;
    private LineVisibilityManager lineVisibilityManager;
    private BodyDropdownManager bodyDropdownManager;
    public TrajectoryRenderer trajectoryRenderer;
    public UIManager uIManager;
    private SimContext ctx;

    [Header("Camera Components")]
    private CameraMovement cameraMovement;
    private FreeCamera freeCamera;

    [Header("Tracking State")]
    public List<NBody> bodies;
    public List<NBody> Bodies => bodies;
    public int currentIndex = 0;
    public bool isTrackingPlaceholder = false;
    public bool inEarthViewCam = false;
    public bool inFreeCam = false;

    private NBody lastTrackedBeforePlaceholder;
    private NBody _currentBody;
    private Transform _currentPlaceholder;
    private NBody _lastTrackedBeforeEarth;

    // ICameraTracker events
    public event System.Action<NBody> OnTrackedBodyChanged;
    public event System.Action<Transform> OnTrackedPlaceholderChanged;
    public event System.Action<bool> OnFreeModeChanged;
    public event System.Action<bool> OnEarthViewChanged;

    // UI suppression (for ghost preview, etc.)
    private bool _suppressUiSignals = false;
    public void BeginUiSuppress() => _suppressUiSignals = true;
    public void EndUiSuppress() => _suppressUiSignals = false;

    private void EmitTrackedBody(NBody b) { if (!_suppressUiSignals) OnTrackedBodyChanged?.Invoke(b); }
    private void EmitTrackedPlaceholder(Transform t) { if (!_suppressUiSignals) OnTrackedPlaceholderChanged?.Invoke(t); }
    private void EmitFreeModeChanged(bool v) { if (!_suppressUiSignals) OnFreeModeChanged?.Invoke(v); }
    private void EmitEarthViewChanged(bool v) { if (!_suppressUiSignals) OnEarthViewChanged?.Invoke(v); }

    // ICameraTracker state
    public bool IsFree => inFreeCam;
    public bool IsEarthView => inEarthViewCam;
    public NBody CurrentBody => _currentBody;
    public Transform CurrentPlaceholder => _currentPlaceholder;

    public void Initialize(SimContext ctx)
    {
        gravityManager = ctx.GravityManager;
        lineVisibilityManager = ctx.LineVisibilityManager;
        bodyDropdownManager = ctx.BodyDropdownManager;
        cameraMovement = ctx.CameraMovement;
        uIManager = ctx.UIManager;
        freeCamera = ctx.FreeCamera;

        if (gravityManager == null) Debug.LogError("GravityManager instance is not set.");
        if (lineVisibilityManager == null) Debug.LogError("LineVisibilityManager instance is not set.");
        if (bodyDropdownManager == null) Debug.LogError("BodyDropdownManager instance is not set.");

        bodies = gravityManager.GetAllSatellites();
        if (bodies.Count > 0 && cameraMovement != null)
            StartCoroutine(InitializeCamera());
    }

    IEnumerator InitializeCamera()
    {
        yield return null; // wait for NBody.Start()
        if (lineVisibilityManager != null)
            lineVisibilityManager.SetTrackedBody(bodies[currentIndex]);

        ReturnToTracking();
        bodyDropdownManager.UpdateDropdownSelection();

        Debug.Log($"[CAMERA CONTROLLER]: Initial camera tracking: {bodies[currentIndex].name}");
    }

    public void BreakToFreeCam()
    {
        if (cameraMovement != null)
        {
            cameraMovement.SetTargetBody(null);
            cameraMovement.enabled = false;
            cameraMovement.isFreeCamMode = true;
            cameraMovement.inEarthCam = false;
        }

        uIManager.placementModeButton.interactable = true;

        inFreeCam = true;
        inEarthViewCam = false;
        if (freeCamera != null) freeCamera.TogglePlacementMode(true);

        EmitEarthViewChanged(false);
        EmitFreeModeChanged(true);
    }

    public void ReturnToTracking()
    {
        if (cameraMovement == null) return;

        if (lastTrackedBeforePlaceholder != null)
        {
            _currentBody = lastTrackedBeforePlaceholder;
            lastTrackedBeforePlaceholder = null;
        }

        if (_currentBody != null) { TrackBody(_currentBody); return; }

        if (_lastTrackedBeforeEarth != null)
        {
            var b = _lastTrackedBeforeEarth;
            _lastTrackedBeforeEarth = null;
            TrackBody(b);
            return;
        }

        if (bodies != null && bodies.Count > 0)
        {
            currentIndex = Mathf.Clamp(currentIndex, 0, bodies.Count - 1);
            TrackBody(bodies[currentIndex]);
            return;
        }

        inFreeCam = true;

        Debug.LogWarning("[CAMERA CONTROLLER]: No valid bodies to track.");
    }

    public void SwitchToEarthCam()
    {
        if (!inEarthViewCam)
        {
            var earth = gravityManager.CentralBody;
            TrackEarth(earth);
        }
        else
        {
            ExitEarthView();
        }
    }

    public void SetInEarthView(bool inEarthCam)
    {
        if (cameraMovement != null) cameraMovement.inEarthCam = inEarthCam;
    }

    public bool IsTracking(NBody body)
    {
        return cameraMovement != null && cameraMovement.targetBody == body;
    }

    public void RefreshBodiesList()
    {
        bodies = gravityManager.GetAllSatellites();

        if (_currentBody != null)
        {
            int idx = bodies.IndexOf(_currentBody);
            if (idx >= 0) currentIndex = idx;
            else if (bodies.Count > 0) currentIndex = Mathf.Clamp(currentIndex, 0, bodies.Count - 1);
        }
        else if (bodies.Count > 0 && currentIndex >= bodies.Count)
        {
            currentIndex = bodies.Count - 1;
        }
    }

    // ===== ICameraTracker commands

    public void TrackBody(NBody body)
    {
        if (body == null) { BreakToFreeCam(); return; }

        lastTrackedBeforePlaceholder = null;
        isTrackingPlaceholder = false;
        _currentPlaceholder = null;
        _currentBody = body;
        inEarthViewCam = false;
        inFreeCam = false;

        int idx = bodies != null ? bodies.IndexOf(body) : -1;
        if (idx >= 0) currentIndex = idx;

        if (cameraMovement != null)
        {
            cameraMovement.enabled = true;
            cameraMovement.SetTargetBody(body);
            cameraMovement.isFreeCamMode = false;
            cameraMovement.inEarthCam = false;
            cameraMovement.PointCameraTowardCentralBody(Vector3.zero, body.transform.position);
        }
        if (freeCamera != null) freeCamera.TogglePlacementMode(false);

        EmitTrackedBody(body);
        EmitFreeModeChanged(false);
        EmitEarthViewChanged(false);
    }

    public void TrackPlaceholder(Transform placeholder)
    {
        if (placeholder == null) { BreakToFreeCam(); return; }

        isTrackingPlaceholder = true;
        _currentPlaceholder = placeholder;
        Debug.Log("Current BoDy Placeholder: " + _currentBody);
        lastTrackedBeforePlaceholder = _currentBody;
        _currentBody = null;
        inEarthViewCam = false;

        if (cameraMovement != null)
        {
            cameraMovement.enabled = true;
            cameraMovement.SetTargetBodyPlaceholder(placeholder);
            cameraMovement.isFreeCamMode = false;
            cameraMovement.PointCameraTowardCentralBody(Vector3.zero, placeholder.position);
        }
        if (freeCamera != null) freeCamera.TogglePlacementMode(false);

        EmitTrackedPlaceholder(placeholder);
        EmitFreeModeChanged(false);
        EmitEarthViewChanged(false);
    }

    public void TrackEarth(NBody earth)
    {
        if (earth == null || cameraMovement == null) { BreakToFreeCam(); return; }

        if (cameraMovement.inEarthCam && cameraMovement.targetBody == earth) return;

        Debug.Log(_currentBody);
        if (_currentBody != null) _lastTrackedBeforeEarth = _currentBody;

        cameraMovement.SetTargetEarth(earth);
        cameraMovement.isFreeCamMode = false;
        cameraMovement.inEarthCam = true;

        if (freeCamera != null) freeCamera.TogglePlacementMode(false);

        inEarthViewCam = true;
        EmitEarthViewChanged(true);
        EmitFreeModeChanged(false);
    }

    public void ExitEarthView()
    {
        if (cameraMovement != null) cameraMovement.inEarthCam = false;
        inEarthViewCam = false;

        if (_lastTrackedBeforeEarth != null)
        {
            var b = _lastTrackedBeforeEarth;
            _lastTrackedBeforeEarth = null;
            TrackBody(b);
        }
        else if (_currentBody != null)
        {
            TrackBody(_currentBody);
        }
        else if (_currentPlaceholder)
        {
            TrackPlaceholder(_currentPlaceholder);
        }
        else
        {
            BreakToFreeCam();
        }

        EmitEarthViewChanged(false);
    }
}