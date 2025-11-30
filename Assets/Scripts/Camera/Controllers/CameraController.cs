using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Controls camera modes and tracking behavior (NBody targets, placeholder Transforms, or Earth view).
/// Integrates with BodyService for dynamic satellite membership, updates UI via events,
/// and coordinates between CameraMovement (orbit/track) and FreeCamera (free-fly).
/// </summary>
public class CameraController : MonoBehaviour, ICameraTracker
{
    private const string SatelliteTag = "Satellite";
    private const string LogPrefix = "[CameraController]";

    [Header("Core References (wired by SimContext.Initialize or via Inspector)")]
    [SerializeField] private UIManager _uiManager;
    [SerializeField] private CameraMovement _cameraMovement;
    [SerializeField] private FreeCamera _freeCamera;

    private BodyService _bodyService;
    private SimContext _ctx;

    [Header("Tracking State (read-only)")]
    [SerializeField] private List<NBody> _bodies = new List<NBody>();
    /// <summary>List of currently tracked NBody objects.</summary>
    public IReadOnlyList<NBody> Bodies => _bodies;

    [Tooltip("Index used when cycling through satellites; clamped as needed.")]
    [SerializeField] private int _currentIndex;

    private CameraMode _mode = CameraMode.Track;
    /// <summary>Current camera mode (Track, Earth, or Free).</summary>
    public CameraMode Mode => _mode;
    /// <summary>True if the camera is in Free mode.</summary>
    public bool IsFree => _mode == CameraMode.Free;
    /// <summary>True if the camera is in Earth view mode.</summary>
    public bool IsEarthView => _mode == CameraMode.Earth;
    /// <summary>True if the camera is tracking a placeholder Transform.</summary>
    public bool IsTrackingPlaceholder => _currentPlaceholder != null && _currentBody == null;

    // Current tracking targets
    private NBody _currentBody;
    private Transform _currentPlaceholder;

    // History for returning to prior states
    private NBody _lastTrackedBeforeEarth;
    private NBody _lastTrackedBeforePlaceholder;
    private NBody _lastTrackedBeforeFree;

    // Preview target used in Free mode
    private Transform _previewTarget;

    private bool _preserveAngleNextTrack;
    private bool _suppressUiSignals;
    private bool _pickedInitialTarget;

    // Cached delegates for event safety
    private Action<NBody> _onBodyAddedHandler;
    private Action<NBody> _onBodyRemovedHandler;

    // -------- ICameraTracker: Current state --------

    /// <summary>The currently tracked NBody (null if tracking a placeholder or in Free mode).</summary>
    public NBody CurrentBody => _currentBody;

    /// <summary>The currently tracked placeholder Transform (null if tracking a body or in Free mode).</summary>
    public Transform CurrentPlaceholder => _currentPlaceholder;

    // -------- ICameraTracker: Events --------

    /// <summary>Raised when the overall camera mode changes.</summary>
    public event Action<CameraMode> OnModeChanged;

    /// <summary>Raised when the tracked NBody changes (null if switching away).</summary>
    public event Action<NBody> OnTrackedBodyChanged;

    /// <summary>Raised when the tracked placeholder Transform changes (null if switching away).</summary>
    public event Action<Transform> OnTrackedPlaceholderChanged;

    /// <summary>
    /// Initializes the controller using the simulation context.
    /// Context values override any existing Inspector references when available.
    /// </summary>
    /// <param name="ctx">Simulation context providing services and camera components.</param>
    public void Initialize(SimContext ctx)
    {
        _ctx = ctx;
        _cameraMovement = ctx.CameraMovement ?? _cameraMovement;
        _uiManager = ctx.UIManager ?? _uiManager;
        _freeCamera = ctx.FreeCamera ?? _freeCamera;
        _bodyService = ctx.BodyService ?? _bodyService;

        if (_bodyService == null || _cameraMovement == null)
        {
            Debug.LogError($"{LogPrefix} Missing dependencies.");
            return;
        }

        Subscribe();
        RefreshBodiesList();
        TrySetInitialTarget();
    }

    /// <summary>
    /// Attempts to set an initial target based on available satellites or the central body.
    /// </summary>
    private void TrySetInitialTarget()
    {
        if (_pickedInitialTarget || _cameraMovement == null) return;

        var sats = _bodyService.GetSatellites();
        var candidate = (sats.Count > 0)
            ? sats[Mathf.Clamp(_currentIndex, 0, sats.Count - 1)]
            : _bodyService.CentralBody;

        if (candidate != null)
        {
            TrackBody(candidate);
            _pickedInitialTarget = true;
        }
    }

    private void OnEnable() => Subscribe();
    private void OnDisable() => Unsubscribe();
    private void OnDestroy() => Unsubscribe();

    private void Subscribe()
    {
        if (_bodyService == null) return;
        _bodyService.BodyAdded -= OnBodyAdded;
        _bodyService.BodyRemoved -= OnBodyRemoved;
        _bodyService.BodyAdded += OnBodyAdded;
        _bodyService.BodyRemoved += OnBodyRemoved;
    }
    private void Unsubscribe()
    {
        if (_bodyService == null) return;
        _bodyService.BodyAdded -= OnBodyAdded;
        _bodyService.BodyRemoved -= OnBodyRemoved;
    }


    private void OnBodyAdded(NBody b)
    {
        if (b == null) return;
        if (b.CompareTag(SatelliteTag) && !_bodies.Contains(b))
            _bodies.Add(b);
    }

    private void OnBodyRemoved(NBody b)
    {
        if (b == null) return;

        _bodies.Remove(b);
        if (_currentBody == b)
        {
            _currentBody = null;
            ReturnToTracking();
        }
    }

    /// <summary>Refreshes the local satellite cache and clamps the current index.</summary>
    public void RefreshBodiesList()
    {
        _bodies = _bodyService?.GetSatellites()?.ToList() ?? new List<NBody>();

        if (_currentBody != null)
        {
            int idx = _bodies.IndexOf(_currentBody);
            _currentIndex = (idx >= 0) ? idx : Mathf.Clamp(_currentIndex, 0, _bodies.Count - 1);
        }
        else if (_bodies.Count > 0 && _currentIndex >= _bodies.Count)
        {
            _currentIndex = _bodies.Count - 1;
        }
    }

    /// <summary>Switches to FreeCam mode (no tracked body or placeholder).</summary>
    public void BreakToFreeCam()
    {
        if (_currentBody != null)
            _lastTrackedBeforeFree = _currentBody;

        _currentBody = null;
        _currentPlaceholder = null;

        _cameraMovement?.SetTargetBody(null);
        _cameraMovement?.SetTargetBodyPlaceholder(null);
        _cameraMovement?.PointCameraTowardCentralBody(Vector3.zero, _cameraMovement.MainCamera.transform.position);

        _freeCamera?.TogglePlacementMode(true);

        _preserveAngleNextTrack = true;

        EndPreviewPlaceholder();
        SetMode(CameraMode.Free);
    }

    /// <summary>Returns to the tracked target or falls back to FreeCam.</summary>
    public void ReturnToTracking()
    {
        if (_cameraMovement == null)
        {
            Debug.LogWarning($"{LogPrefix} Missing CameraMovement; switching to Free mode.");
            BreakToFreeCam();
            return;
        }

        if (_lastTrackedBeforePlaceholder != null)
        {
            var prev = _lastTrackedBeforePlaceholder;
            _lastTrackedBeforePlaceholder = null;
            TrackBody(prev);
            return;
        }

        if (_currentBody != null)
        {
            TrackBody(_currentBody);
            return;
        }

        if (_lastTrackedBeforeFree != null)
        {
            var sats = _bodyService?.GetSatellites();
            if (sats != null && sats.Contains(_lastTrackedBeforeFree))
            {
                var prev = _lastTrackedBeforeFree;
                _lastTrackedBeforeFree = null;
                TrackBody(prev);
                return;
            }
            _lastTrackedBeforeFree = null;
        }

        if (_lastTrackedBeforeEarth != null)
        {
            var prev = _lastTrackedBeforeEarth;
            _lastTrackedBeforeEarth = null;
            TrackBody(prev);
            return;
        }

        var available = _bodyService?.GetSatellites();
        if (available != null && available.Count > 0)
        {
            _currentIndex = Mathf.Clamp(_currentIndex, 0, available.Count - 1);
            TrackBody(available[_currentIndex]);
            return;
        }

        Debug.Log($"{LogPrefix} No satellites available; switching to Free mode.");
        BreakToFreeCam();
    }

    /// <summary>Toggles Earth view on/off.</summary>
    public void SwitchToEarthCam()
    {
        if (_mode != CameraMode.Earth)
            TrackEarth(_bodyService?.CentralBody);
        else
            ExitEarthView();
    }

    /// <summary>Tracks the specified NBody; falls back to FreeCam if null.</summary>
    public void TrackBody(NBody body)
    {
        if (body == null) { BreakToFreeCam(); return; }
        TrackTarget(body, null, CameraMode.Track);
    }

    /// <summary>Tracks a placeholder Transform (e.g., temporary orbit); falls back to FreeCam if null.</summary>
    public void TrackPlaceholder(Transform placeholder)
    {
        if (placeholder == null) { BreakToFreeCam(); return; }

        if (_currentBody != null) _lastTrackedBeforePlaceholder = _currentBody;
        TrackTarget(null, placeholder, CameraMode.Track);
    }

    /// <summary>Enters Earth view by tracking the central body.</summary>
    public void TrackEarth(NBody earth)
    {
        if (earth == null || _cameraMovement == null) { BreakToFreeCam(); return; }

        _currentPlaceholder = null;
        if (_currentBody != null) _lastTrackedBeforeEarth = _currentBody;

        _freeCamera?.TogglePlacementMode(false);
        _cameraMovement.SetTargetEarth(earth);
        SetMode(CameraMode.Earth);
    }

    /// <summary>Leaves Earth view and returns to the last valid tracked target.</summary>
    public void ExitEarthView()
    {
        _preserveAngleNextTrack = true;

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
        else if (_currentPlaceholder != null)
        {
            TrackPlaceholder(_currentPlaceholder);
        }
        else
        {
            BreakToFreeCam();
        }
    }

    /// <summary>Previews a placeholder orbit while in Free mode (for UI feedback). Used in ObjectPlacementManager</summary>
    public void PreviewPlaceholderInFree(Transform placeholder)
    {
        if (placeholder == null || _cameraMovement == null) return;
        if (_mode != CameraMode.Free) return;

        _previewTarget = placeholder;
        _currentPlaceholder = placeholder;
        _currentBody = null;

        _cameraMovement.enabled = true;
        _cameraMovement.isFreeCamMode = false;
        _cameraMovement.inEarthCam = false;
        _cameraMovement.SetTargetBodyPlaceholder(placeholder);
        _cameraMovement.PointCameraTowardCentralBody(Vector3.zero, placeholder.position);

        EmitTrackedPlaceholder(placeholder);
    }

    /// <summary>Ends placeholder preview while in Free mode.</summary>
    public void EndPreviewPlaceholder()
    {
        _previewTarget = null;

        if (_mode == CameraMode.Free && _cameraMovement != null)
        {
            _cameraMovement.enabled = false;
            _cameraMovement.SetTargetBodyPlaceholder(null);
        }
    }

    // Helpers

    private void SetMode(CameraMode next)
    {
        if (_mode == next) return;

        var prev = _mode;
        _mode = next;

        if (prev == CameraMode.Free && _mode != CameraMode.Free)
            EndPreviewPlaceholder();

        ApplyModeToCamera(_mode);
        EmitModeChanged(_mode);
    }

    private void ApplyModeToCamera(CameraMode mode)
    {
        if (_cameraMovement != null)
        {
            _cameraMovement.isFreeCamMode = mode == CameraMode.Free;
            _cameraMovement.inEarthCam = mode == CameraMode.Earth;
            _cameraMovement.enabled = mode != CameraMode.Free;
        }

        if (_uiManager != null)
            _uiManager.placementModeButton.interactable = mode == CameraMode.Free;
    }

    private void TrackTarget(NBody body, Transform placeholder, CameraMode mode)
    {
        _currentBody = body;
        _currentPlaceholder = placeholder;
        _freeCamera?.TogglePlacementMode(false);

        bool shouldRepoint = !_preserveAngleNextTrack;
        _preserveAngleNextTrack = false;

        if (_cameraMovement != null)
        {
            if (body != null)
            {
                _cameraMovement.SetTargetBody(body);
                if (shouldRepoint)
                    _cameraMovement.PointCameraTowardCentralBody(Vector3.zero, body.transform.position);
            }
            else if (placeholder != null)
            {
                _cameraMovement.SetTargetBodyPlaceholder(placeholder);
                if (shouldRepoint)
                    _cameraMovement.PointCameraTowardCentralBody(Vector3.zero, placeholder.position);
            }
            else
            {
                BreakToFreeCam();
                return;
            }
        }

        SetMode(mode);

        if (body != null) EmitTrackedBody(body);
        if (placeholder != null) EmitTrackedPlaceholder(placeholder);
    }

    // Used in ObjectPlacementManager
    public void BeginUiSuppress() => _suppressUiSignals = true;
    public void EndUiSuppress() => _suppressUiSignals = false;

    private void EmitModeChanged(CameraMode m) { if (!_suppressUiSignals) OnModeChanged?.Invoke(m); }
    private void EmitTrackedBody(NBody b) { if (!_suppressUiSignals) OnTrackedBodyChanged?.Invoke(b); }
    private void EmitTrackedPlaceholder(Transform t) { if (!_suppressUiSignals) OnTrackedPlaceholderChanged?.Invoke(t); }
}
