using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Controls camera modes and tracking behavior (NBody targets, placeholder Transforms, or Earth view).
/// Integrates with BodyService for dynamic satellite membership and coordinates between
/// CameraMovement (orbit/track) and FreeCamera (free-fly).
/// </summary>
public class CameraController : MonoBehaviour, ICameraTracker
{
    private const string SatelliteTag = "Satellite";
    private const string LogPrefix = "[CameraController]";
    private const float EarthReturnBodyDistance = 2500f;

    [Header("Core References (wired by SimContext.Initialize or via Inspector)")]
    [SerializeField] private CameraMovement _cameraMovement;
    [SerializeField] private FreeCamera _freeCamera;

    private BodyService _bodyService;
    private TutorialController _tutorialController;

    [Header("Tracking State (read-only)")]
    [SerializeField] private List<NBody> _bodies = new();
    [Tooltip("Index used when cycling through satellites; clamped as needed.")]
    [SerializeField] private int _currentIndex;

    private readonly CameraTrackingState _state = new();

    private bool _suppressUiSignals;
    private bool _pickedInitialTarget;

    /// <summary>List of currently tracked NBody objects.</summary>
    public IReadOnlyList<NBody> Bodies => _bodies;

    /// <summary>Current camera mode (Track, Earth, or Free).</summary>
    public CameraMode Mode => _state.Mode;

    /// <summary>True if the camera is in Free mode.</summary>
    public bool IsFree => _state.IsFree;

    /// <summary>True if the camera is in Earth view mode.</summary>
    public bool IsEarthView => _state.IsEarthView;

    /// <summary>True if the camera is tracking a placeholder Transform.</summary>
    public bool IsTrackingPlaceholder => _state.IsTrackingPlaceholder;

    /// <summary>The currently tracked NBody (null if tracking a placeholder or in Free mode).</summary>
    public NBody CurrentBody => _state.CurrentBody;

    /// <summary>The currently tracked placeholder Transform (null if tracking a body or in Free mode).</summary>
    public Transform CurrentPlaceholder => _state.CurrentPlaceholder;

    /// <summary>Raised when the overall camera mode changes.</summary>
    public event Action<CameraMode> OnModeChanged;

    /// <summary>Raised when the tracked NBody changes (null if switching away).</summary>
    public event Action<NBody> OnTrackedBodyChanged;

    /// <summary>Raised when the tracked placeholder Transform changes (null if switching away).</summary>
    public event Action<Transform> OnTrackedPlaceholderChanged;

    public bool switchedToPrevTrackedSat => _state.HasPreviousEarthTarget;

    /// <summary>
    /// Initializes the controller using the simulation context.
    /// Context values override any existing Inspector references when available.
    /// </summary>
    /// <param name="ctx">Simulation context providing services and camera components.</param>
    public void Initialize(SimContext ctx)
    {
        _cameraMovement = ctx.CameraMovement ?? _cameraMovement;
        _freeCamera = ctx.FreeCamera ?? _freeCamera;
        _bodyService = ctx.BodyService ?? _bodyService;
        _tutorialController = ctx.TutorialController ?? _tutorialController;

        if (_bodyService == null || _cameraMovement == null)
        {
            Debug.LogError($"{LogPrefix} Missing dependencies.");
            enabled = false;
            return;
        }

        Subscribe();
        RefreshBodiesList();
        TrySetInitialTarget();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    /// <summary>Refreshes the local satellite cache and clamps the current index.</summary>
    public void RefreshBodiesList()
    {
        _bodies = _bodyService?.GetSatellites()?.ToList() ?? new List<NBody>();

        if (_state.CurrentBody != null)
        {
            int idx = _bodies.IndexOf(_state.CurrentBody);
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
        StopPlaceholderEarthView(restorePlaceholderPreview: false);
        _state.BreakToFree();

        ApplyFreeCameraRig();
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

        if (_state.LastTrackedBeforePlaceholder != null)
        {
            var prev = _state.ConsumeLastTrackedBeforePlaceholder();
            TrackBody(prev);
            return;
        }

        if (_state.CurrentBody != null)
        {
            TrackBody(_state.CurrentBody);
            return;
        }

        if (_state.LastTrackedBeforeFree != null)
        {
            var sats = _bodyService?.GetSatellites();
            if (sats != null && sats.Contains(_state.LastTrackedBeforeFree))
            {
                var prev = _state.ConsumeLastTrackedBeforeFree();
                TrackBody(prev);
                return;
            }

            _state.ClearLastTrackedBeforeFree();
        }

        if (_state.LastTrackedBeforeEarth != null)
        {
            var prev = _state.ConsumeLastTrackedBeforeEarth();
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
        if (TryTogglePlaceholderEarthView())
            return;

        if (_state.Mode != CameraMode.Earth)
            TrackEarth(_bodyService?.CentralBody);
        else
            ExitEarthView();
    }

    /// <summary>Tracks the specified NBody; falls back to FreeCam if null.</summary>
    public void TrackBody(NBody body)
    {
        if (body == null)
        {
            BreakToFreeCam();
            return;
        }

        TrackTarget(CameraTarget.BodyTarget(body));
    }

    /// <summary>Tracks a placeholder Transform (e.g., temporary orbit); falls back to FreeCam if null.</summary>
    public void TrackPlaceholder(Transform placeholder)
    {
        if (placeholder == null)
        {
            BreakToFreeCam();
            return;
        }

        TrackTarget(CameraTarget.PlaceholderTarget(placeholder));
    }

    /// <summary>Enters Earth view by tracking the central body.</summary>
    public void TrackEarth(NBody earth)
    {
        if (earth == null || _cameraMovement == null)
        {
            BreakToFreeCam();
            return;
        }

        TrackTarget(CameraTarget.EarthTarget(earth));
    }

    /// <summary>Leaves Earth view and returns to the last valid tracked target.</summary>
    public void ExitEarthView()
    {
        _state.PreserveAngleNextTrack = true;

        if (_state.LastTrackedBeforeEarth != null)
        {
            var body = _state.LastTrackedBeforeEarth;
            TrackBody(body);
            _state.ClearLastTrackedBeforeEarth();
        }
        else if (_state.CurrentBody != null)
        {
            TrackBody(_state.CurrentBody);
        }
        else if (_state.CurrentPlaceholder != null)
        {
            TrackPlaceholder(_state.CurrentPlaceholder);
        }
        else
        {
            BreakToFreeCam();
        }
    }

    /// <summary>
    /// Previews a placeholder orbit while in Free mode (for UI feedback).
    /// Used in ObjectPlacementManager.
    /// </summary>
    public void PreviewPlaceholderInFree(Transform placeholder)
    {
        if (placeholder == null || _cameraMovement == null) return;
        if (_state.Mode != CameraMode.Free) return;

        StopPlaceholderEarthView(restorePlaceholderPreview: false);

        _state.PreviewPlaceholder(placeholder);

        ApplyPlaceholderPreviewRig(placeholder);

        EmitTrackedPlaceholder(placeholder);
    }

    /// <summary>Ends placeholder preview while in Free mode.</summary>
    public void EndPreviewPlaceholder()
    {
        if (_state.Mode == CameraMode.Free && _cameraMovement != null)
        {
            _cameraMovement.enabled = false;
            _cameraMovement.ClearFocus();
        }
    }

    /// <summary>Starts suppressing UI-related event signals.</summary>
    public void BeginUiSuppress() => _suppressUiSignals = true;

    /// <summary>Ends suppressing UI-related event signals.</summary>
    public void EndUiSuppress() => _suppressUiSignals = false;

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

    private void OnBodyAdded(NBody body)
    {
        if (body == null) return;
        if (!body.CompareTag(SatelliteTag)) return;
        if (_bodies.Contains(body)) return;

        _bodies.Add(body);

        if (!_pickedInitialTarget)
            TrySetInitialTarget();
    }

    private void OnBodyRemoved(NBody body)
    {
        if (body == null) return;

        _bodies.Remove(body);

        if (_state.CurrentBody == body)
        {
            _state.ClearCurrentTarget();
            ReturnToTracking();
        }
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

    private void SetMode(CameraMode next)
    {
        if (_state.Mode == next) return;

        var previous = _state.Mode;
        _state.SetMode(next);

        if (previous == CameraMode.Free && _state.Mode != CameraMode.Free)
            EndPreviewPlaceholder();

        ApplyModeToCamera(_state.Mode);
        EmitModeChanged(_state.Mode);
    }

    private void ApplyModeToCamera(CameraMode mode)
    {
        if (_cameraMovement == null) return;

        _cameraMovement.SetFreeCamMode(mode == CameraMode.Free);
        _cameraMovement.enabled = mode != CameraMode.Free;
    }

    private void TrackTarget(CameraTarget target)
    {
        StopPlaceholderEarthView(restorePlaceholderPreview: false);

        _state.Track(target);

        bool shouldRepoint = !_state.PreserveAngleNextTrack;
        _state.PreserveAngleNextTrack = false;

        if (!ApplyTargetRig(target, shouldRepoint))
        {
            BreakToFreeCam();
            return;
        }

        SetMode(target.Mode);
        EmitTargetChanged(target);
    }

    private bool ApplyTargetRig(CameraTarget target, bool shouldRepoint)
    {
        if (target.IsBody)
        {
            ApplyBodyTrackingRig(target.Body, shouldRepoint);
            return true;
        }

        if (target.IsPlaceholder)
        {
            ApplyPlaceholderTrackingRig(target.Placeholder, shouldRepoint);
            return true;
        }

        if (target.IsEarth)
        {
            ApplyEarthCameraRig(target.Body);
            return true;
        }

        return false;
    }

    private void EmitTargetChanged(CameraTarget target)
    {
        if (target.IsBody) EmitTrackedBody(target.Body);
        if (target.IsPlaceholder) EmitTrackedPlaceholder(target.Placeholder);
    }

    private void EmitModeChanged(CameraMode mode)
    {
        if (_suppressUiSignals) return;
        OnModeChanged?.Invoke(mode);
    }

    private void EmitTrackedBody(NBody body)
    {
        if (_suppressUiSignals) return;
        OnTrackedBodyChanged?.Invoke(body);
    }

    private void EmitTrackedPlaceholder(Transform placeholder)
    {
        if (_suppressUiSignals) return;
        OnTrackedPlaceholderChanged?.Invoke(placeholder);
    }

    private bool TryTogglePlaceholderEarthView()
    {
        if (_state.Mode != CameraMode.Free || _state.CurrentPlaceholder == null || _cameraMovement == null)
            return false;

        if (_state.PlaceholderEarthViewActive)
        {
            StopPlaceholderEarthView(restorePlaceholderPreview: true);
            return true;
        }

        NBody earth = _bodyService?.CentralBody;
        if (earth == null)
            return false;

        _state.BeginPlaceholderEarthView();
        ApplyEarthCameraRig(earth);
        return true;
    }

    private void StopPlaceholderEarthView(bool restorePlaceholderPreview)
    {
        if (!_state.PlaceholderEarthViewActive)
            return;

        _state.EndPlaceholderEarthView();

        if (_cameraMovement == null)
            return;

        _cameraMovement.ClearFocus();

        if (!restorePlaceholderPreview || _state.Mode != CameraMode.Free || _state.CurrentPlaceholder == null)
            return;

        ApplyPlaceholderPreviewRig(_state.CurrentPlaceholder);
    }

    private void ApplyFreeCameraRig()
    {
        if (_cameraMovement != null)
        {
            _cameraMovement.ClearFocus();
            _cameraMovement.PointCameraTowardCentralBody(
                Vector3.zero,
                _cameraMovement.MainCamera.transform.position
            );
        }

        _freeCamera?.TogglePlacementMode(true);
    }

    private void ApplyEarthCameraRig(NBody earth)
    {
        _freeCamera?.TogglePlacementMode(false);
        _cameraMovement.ApplyEarthFocus(earth);

        if (_tutorialController != null && _tutorialController.inTutorialMode)
            _tutorialController.hasSwitchedToEarthCam = true;
    }

    private void ApplyBodyTrackingRig(NBody body, bool shouldRepoint)
    {
        _freeCamera?.TogglePlacementMode(false);

        if (_cameraMovement == null)
            return;

        float? defaultDistanceOverride = _state.Mode == CameraMode.Earth
            ? EarthReturnBodyDistance
            : null;

        _cameraMovement.ApplyBodyFocus(body, defaultDistanceOverride);
        if (shouldRepoint)
            _cameraMovement.PointCameraTowardCentralBody(Vector3.zero, body.transform.position);
    }

    private void ApplyPlaceholderTrackingRig(Transform placeholder, bool shouldRepoint)
    {
        _freeCamera?.TogglePlacementMode(false);

        if (_cameraMovement == null)
            return;

        _cameraMovement.ApplyPlaceholderFocus(placeholder);
        if (shouldRepoint)
            _cameraMovement.PointCameraTowardCentralBody(Vector3.zero, placeholder.position);
    }

    private void ApplyPlaceholderPreviewRig(Transform placeholder)
    {
        if (_cameraMovement == null)
            return;

        _cameraMovement.enabled = true;
        _cameraMovement.SetFreeCamMode(false);
        _cameraMovement.ApplyPlaceholderFocus(placeholder);
        _cameraMovement.PointCameraTowardCentralBody(Vector3.zero, placeholder.position);
    }
}
