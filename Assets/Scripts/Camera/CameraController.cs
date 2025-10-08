using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Controls camera modes and targets (NBody, placeholder Transform, or central Earth view).
/// Integrates with BodyService for dynamic body membership, dispatches events for UI,
/// and coordinates between CameraMovement (orbit/track) and FreeCamera (free-fly).
/// </summary>
public class CameraController : MonoBehaviour, ICameraTracker
{
    private const string SatelliteTag = "Satellite";

    [Header("Core References (can be wired by SimContext.Initialize or via Inspector)")]
    [SerializeField] private UIManager _uiManager;
    [SerializeField] private CameraMovement _cameraMovement;
    [SerializeField] private FreeCamera _freeCamera;

    private BodyService _bodyService;
    private SimContext _ctx;

    [Header("Tracking State (read-only via property)")]
    [SerializeField] private List<NBody> _bodies = new List<NBody>();
    public IReadOnlyList<NBody> Bodies => _bodies;

    [Tooltip("Index hint for cycling through satellites; clamped when used.")]
    [SerializeField] private int _currentIndex;

    private CameraMode _mode = CameraMode.Track;
    public CameraMode Mode => _mode;
    public bool IsFree => _mode == CameraMode.Free;
    public bool IsEarthView => _mode == CameraMode.Earth;
    public bool IsTrackingPlaceholder => _currentPlaceholder != null && _currentBody == null;

    // Current targets
    private NBody _currentBody;
    private Transform _currentPlaceholder;

    // History for returns
    private NBody _lastTrackedBeforeEarth;
    private NBody _lastTrackedBeforePlaceholder;
    private NBody _lastTrackedBeforeFree;

    // Preview (Free mode) target
    private Transform _previewTarget;

    private bool _preserveAngleNextTrack = false;

    // Event suppression flag (e.g., while doing temporary UI-driven changes)
    private bool _suppressUiSignals;

    // Delegates cached so we can unsubscribe safely
    private Action<NBody> _onBodyAddedHandler;
    private Action<NBody> _onBodyRemovedHandler;

    private const string LogPrefix = "[CameraController]";

    // ------------- ICameraTracker: State accessors -------------

    /// The currently tracked NBody (null if tracking placeholder or free)
    public NBody CurrentBody => _currentBody;

    /// The currently tracked placeholder Transform (null if tracking body or free)
    public Transform CurrentPlaceholder => _currentPlaceholder;

    // ------------- ICameraTracker: Events -------------

    /// Raised when the overall camera mode changes
    public event Action<CameraMode> OnModeChanged;

    /// Raised when the tracked NBody changes (may be null if switching away)
    public event Action<NBody> OnTrackedBodyChanged;

    /// Raised when the tracked placeholder Transform changes (may be null if switching away)
    public event Action<Transform> OnTrackedPlaceholderChanged;

    /// <summary>
    /// Initializes this controller from a simulation context. If references are already set in the Inspector,
    /// they will be overwritten by the context values if non-null.
    /// </summary>
    /// <param name="ctx">Simulation context providing services and camera components.</param>
    public void Initialize(SimContext ctx)
    {
        _ctx = ctx;

        // ===== REFACTOR: allow Inspector wiring but prefer context when present
        _cameraMovement = ctx.CameraMovement ?? _cameraMovement;
        _uiManager = ctx.UIManager ?? _uiManager;
        _freeCamera = ctx.FreeCamera ?? _freeCamera;
        _bodyService = ctx.BodyService ?? _bodyService;

        if (_bodyService == null) Debug.LogError($"{LogPrefix} BodyService missing from context!");
        if (_cameraMovement == null) Debug.LogError($"{LogPrefix} CameraMovement missing!");

        // Seed local list from service (or empty)
        RefreshBodiesList();

        // Subscribe to service events
        EnsureServiceSubscriptions(true);

        // Kick initial tracking if we have bodies
        if (_bodies.Count > 0 && _cameraMovement != null)
        {
            StartCoroutine(Co_InitializeCamera());
        }
    }

    /// <summary>
    /// Unity lifecycle: when enabled, ensure service subscriptions are hooked (covers domain reloads/scene toggles).
    /// </summary>
    private void OnEnable()
    {
        EnsureServiceSubscriptions(true); // ===== REFACTOR: move subscribe to OnEnable for safety
    }

    /// <summary>
    /// Unity lifecycle: when disabled, unhook subscriptions to avoid leaks.
    /// </summary>
    private void OnDisable()
    {
        EnsureServiceSubscriptions(false); // ===== REFACTOR: unsubscribe in OnDisable for safety
    }

    /// <summary>
    /// Waits a frame for dependent Start() calls (e.g., NBody) then returns to tracking a valid target.
    /// </summary>
    private IEnumerator Co_InitializeCamera()
    {
        yield return null; // wait for NBody.Start()
        ReturnToTracking();

        if (_bodies.Count > 0)
            Debug.Log($"{LogPrefix} Initial camera tracking: {_bodies[Mathf.Clamp(_currentIndex, 0, _bodies.Count - 1)].name}");
    }

    // ------------- Service wiring -------------

    /// <summary>
    /// Subscribes or unsubscribes from BodyService events.
    /// </summary>
    /// <param name="subscribe">True to subscribe; false to unsubscribe.</param>
    private void EnsureServiceSubscriptions(bool subscribe)
    {
        if (_bodyService == null) return;

        if (subscribe)
        {
            // Cache delegates so -= works reliably
            if (_onBodyAddedHandler == null) _onBodyAddedHandler = OnBodyAdded;
            if (_onBodyRemovedHandler == null) _onBodyRemovedHandler = OnBodyRemoved;

            _bodyService.BodyAdded -= _onBodyAddedHandler;   // idempotent
            _bodyService.BodyRemoved -= _onBodyRemovedHandler; // idempotent
            _bodyService.BodyAdded += _onBodyAddedHandler;
            _bodyService.BodyRemoved += _onBodyRemovedHandler;
        }
        else
        {
            if (_onBodyAddedHandler != null) _bodyService.BodyAdded -= _onBodyAddedHandler;
            if (_onBodyRemovedHandler != null) _bodyService.BodyRemoved -= _onBodyRemovedHandler;
        }
    }

    /// <summary>
    /// Handles a newly added NBody. Adds to local cache if tagged as a satellite.
    /// </summary>
    /// <param name="b">The body that was added.</param>
    private void OnBodyAdded(NBody b)
    {
        if (b == null) return;

        // Prefer semantic/component checks in production; tag retained for parity
        if (b.CompareTag(SatelliteTag))
        {
            if (!_bodies.Contains(b))
                _bodies.Add(b);
        }
        // Index clamping handled at use-sites
    }

    /// <summary>
    /// Handles removal of an NBody. If it was being tracked, falls back to a safe target.
    /// </summary>
    /// <param name="b">The body that was removed.</param>
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

    // ------------- Public API -------------

    /// <summary>
    /// Refreshes the local satellite cache from BodyService and clamps the index and current selection.
    /// </summary>
    public void RefreshBodiesList()
    {
        // ===== REFACTOR: prefer service data each refresh
        _bodies = _bodyService?.GetSatellites()?.ToList() ?? new List<NBody>();

        if (_currentBody != null)
        {
            int idx = _bodies.IndexOf(_currentBody);
            if (idx >= 0) _currentIndex = idx;
            else if (_bodies.Count > 0) _currentIndex = Mathf.Clamp(_currentIndex, 0, _bodies.Count - 1);
        }
        else if (_bodies.Count > 0 && _currentIndex >= _bodies.Count)
        {
            _currentIndex = _bodies.Count - 1;
        }
    }

    /// <summary>
    /// Breaks out to free camera mode (no tracked body/placeholder).
    /// </summary>
    public void BreakToFreeCam()
    {
        // remember what we were tracking (if any)
        if (_currentBody != null)
            _lastTrackedBeforeFree = _currentBody;

        _currentBody = null;
        _currentPlaceholder = null;

        if (_cameraMovement != null)
        {
            _cameraMovement.SetTargetBody(null);
            _cameraMovement.SetTargetBodyPlaceholder(null);
        }

        if (_freeCamera) _freeCamera.TogglePlacementMode(true);

        _preserveAngleNextTrack = true;

        EndPreviewPlaceholder();
        SetMode(CameraMode.Free);
    }

    /// <summary>
    /// Attempts to return to a sensible tracked target (last body, Earth fallback, next available satellite),
    /// otherwise falls back to FreeCam.
    /// </summary>
    public void ReturnToTracking()
    {
        if (_cameraMovement == null)
        {
            Debug.LogWarning($"{LogPrefix} ReturnToTracking: CameraMovement missing, switching to Free.");
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

        // NEW: try the body we had before entering Free
        if (_lastTrackedBeforeFree != null)
        {
            // Only track it if it still exists in BodyService (avoid dead refs)
            var sats = _bodyService?.GetSatellites();
            if (sats != null && sats.Contains(_lastTrackedBeforeFree))
            {
                var prev = _lastTrackedBeforeFree;
                _lastTrackedBeforeFree = null;
                TrackBody(prev);
                return;
            }
            _lastTrackedBeforeFree = null; // clear stale ref
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

        Debug.Log($"{LogPrefix} No satellites available; switching to Free.");
        BreakToFreeCam();
    }


    /// <summary>
    /// Toggles Earth view: enters if not in Earth mode; otherwise exits to prior body/placeholder or Free.
    /// </summary>
    public void SwitchToEarthCam()
    {
        if (_mode != CameraMode.Earth)
        {
            var earth = _bodyService?.CentralBody;
            TrackEarth(earth);
        }
        else
        {
            ExitEarthView();
        }
    }

    // ------------- ICameraTracker commands -------------

    /// <summary>
    /// Tracks a specific NBody. If null, falls back to FreeCam.
    /// </summary>
    /// <param name="body">Target body to track.</param>
    public void TrackBody(NBody body)
    {
        if (body == null) { BreakToFreeCam(); return; }
        TrackTarget(body, null, CameraMode.Track);
    }

    /// <summary>
    /// Tracks a placeholder Transform (e.g., temporary orbit). If null, falls back to FreeCam.
    /// </summary>
    /// <param name="placeholder">Placeholder transform to track.</param>
    public void TrackPlaceholder(Transform placeholder)
    {
        if (placeholder == null) { BreakToFreeCam(); return; }

        // Remember last real body to return to after placeholder usage
        if (_currentBody != null) _lastTrackedBeforePlaceholder = _currentBody;

        TrackTarget(null, placeholder, CameraMode.Track);
    }

    /// <summary>
    /// Enters Earth view by tracking the central body via CameraMovement.
    /// </summary>
    /// <param name="earth">Central body (Earth). If null, falls back to FreeCam.</param>
    public void TrackEarth(NBody earth)
    {
        if (earth == null || _cameraMovement == null) { BreakToFreeCam(); return; }

        _currentPlaceholder = null;
        if (_currentBody != null) _lastTrackedBeforeEarth = _currentBody;

        if (_freeCamera) _freeCamera.TogglePlacementMode(false);

        _cameraMovement.SetTargetEarth(earth);
        SetMode(CameraMode.Earth);
        // Earth mode does not emit body/placeholder events by design
    }

    /// <summary>
    /// Leaves Earth view, returning to the last tracked body, current body, placeholder, or Free.
    /// </summary>
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

    /// <summary>
    /// In Free mode, previews orbiting a placeholder without switching out of Free. Emits placeholder event for UI.
    /// </summary>
    /// <param name="placeholder">Placeholder to preview.</param>
    public void PreviewPlaceholderInFree(Transform placeholder)
    {
        if (placeholder == null || _cameraMovement == null) return;
        if (_mode != CameraMode.Free) return;

        _previewTarget = placeholder;

        _currentPlaceholder = placeholder;
        _currentBody = null;

        // ===== REFACTOR: temporarily drive CameraMovement while staying in Free mode
        _cameraMovement.enabled = true;
        _cameraMovement.isFreeCamMode = false;
        _cameraMovement.inEarthCam = false;
        _cameraMovement.SetTargetBodyPlaceholder(placeholder);
        _cameraMovement.PointCameraTowardCentralBody(Vector3.zero, placeholder.position);

        EmitTrackedPlaceholder(placeholder); // UI remains "Free", listeners still get the target
    }

    /// <summary>
    /// Ends Free mode preview if active; disables temporary CameraMovement control.
    /// </summary>
    public void EndPreviewPlaceholder()
    {
        _previewTarget = null;

        if (_mode == CameraMode.Free && _cameraMovement != null)
        {
            _cameraMovement.enabled = false;
            _cameraMovement.SetTargetBodyPlaceholder(null);
        }
    }

    // ------------- Internals -------------

    /// <summary>
    /// Sets the mode (idempotent), applies flags, ends preview if leaving Free, and emits events.
    /// </summary>
    /// <param name="next">Mode to set.</param>
    private void SetMode(CameraMode next)
    {
        if (_mode == next) return;

        var prev = _mode;
        _mode = next;

        if (prev == CameraMode.Free && next != CameraMode.Free)
            EndPreviewPlaceholder();

        ApplyModeToCamera(next);

        // EmitFree(next == CameraMode.Free);
        // EmitEarth(next == CameraMode.Earth);
        EmitModeChanged(next);
    }

    /// <summary>
    /// Centralized place to drive CameraMovement/FreeCamera flags from a single mode enum.
    /// </summary>
    /// <param name="mode">Mode to apply.</param>
    private void ApplyModeToCamera(CameraMode mode)
    {
        if (_cameraMovement != null)
        {
            _cameraMovement.isFreeCamMode = (mode == CameraMode.Free);
            _cameraMovement.inEarthCam = (mode == CameraMode.Earth);
            _cameraMovement.enabled = (mode != CameraMode.Free); // Free uses FreeCamera by default
        }

        if (_uiManager != null)
        {
            // Only interactable in Free, per original intent
            _uiManager.placementModeButton.interactable = (mode == CameraMode.Free);
        }
    }

    /// <summary>
    /// Core helper that unifies body/placeholder tracking logic and event emission.
    /// </summary>
    /// <param name="body">Body to track (optional).</param>
    /// <param name="placeholder">Placeholder to track (optional).</param>
    /// <param name="mode">Target mode (Track/Earth/Free). Earth ignored here; use TrackEarth for that.</param>
    private void TrackTarget(NBody body, Transform placeholder, CameraMode mode)
    {
        _currentBody = body;
        _currentPlaceholder = placeholder;

        if (_freeCamera) _freeCamera.TogglePlacementMode(false);

        bool shouldRepoint = !_preserveAngleNextTrack;
        _preserveAngleNextTrack = false;

        if (_cameraMovement != null)
        {
            if (body != null)
            {
                _cameraMovement.SetTargetBody(body);
                if (shouldRepoint)
                {
                    _cameraMovement.PointCameraTowardCentralBody(Vector3.zero, body.transform.position);
                }
            }
            else if (placeholder != null)
            {
                _cameraMovement.SetTargetBodyPlaceholder(placeholder);
                if (shouldRepoint)
                {
                    _cameraMovement.PointCameraTowardCentralBody(Vector3.zero, placeholder.position);
                }
            }
            else
            {
                // Safety: nothing provided, switch to free
                BreakToFreeCam();
                return;
            }
        }

        SetMode(mode);

        // Emit after state is consistent
        if (body != null) EmitTrackedBody(body);
        if (placeholder != null) EmitTrackedPlaceholder(placeholder);
    }

    // ------------- Event emission with suppression -------------

    /// <summary>Suppresses UI/event emissions until disposed.</summary>
    private struct UiSuppressScope : IDisposable
    {
        private readonly CameraController _owner;
        public UiSuppressScope(CameraController owner) { _owner = owner; _owner._suppressUiSignals = true; }
        public void Dispose() { _owner._suppressUiSignals = false; }
    }

    /// <summary>Begin suppressing UI/event emissions. Make sure to call EndUiSuppress() or use UiSuppressScope.</summary>
    public void BeginUiSuppress() => _suppressUiSignals = true;

    /// <summary>End suppression of UI/event emissions.</summary>
    public void EndUiSuppress() => _suppressUiSignals = false;

    private void EmitModeChanged(CameraMode m) { if (!_suppressUiSignals) OnModeChanged?.Invoke(m); }
    private void EmitTrackedBody(NBody b) { if (!_suppressUiSignals) OnTrackedBodyChanged?.Invoke(b); }
    private void EmitTrackedPlaceholder(Transform t) { if (!_suppressUiSignals) OnTrackedPlaceholderChanged?.Invoke(t); }
    // private void EmitFree(bool v) { if (!_suppressUiSignals) OnFreeModeChanged?.Invoke(v); }
    // private void EmitEarth(bool v) { if (!_suppressUiSignals) OnEarthViewChanged?.Invoke(v); }

    // ------------- Navigation helpers (optional) -------------

    /// <summary>
    /// Tracks the next satellite in the current ordered list, if any.
    /// </summary>
    public void TrackNextBody()
    {
        var sats = _bodyService?.GetSatellites();
        if (sats == null || sats.Count == 0) return;

        _currentIndex = Mathf.Clamp(_currentIndex + 1, 0, sats.Count - 1);
        TrackBody(sats[_currentIndex]);
    }

    /// <summary>
    /// Tracks the previous satellite in the current ordered list, if any.
    /// </summary>
    public void TrackPrevBody()
    {
        var sats = _bodyService?.GetSatellites();
        if (sats == null || sats.Count == 0) return;

        _currentIndex = Mathf.Clamp(_currentIndex - 1, 0, sats.Count - 1);
        TrackBody(sats[_currentIndex]);
    }
}