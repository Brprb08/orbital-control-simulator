using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Handles click-and-drag velocity input for a body, keeps the UI in sync,
/// and drives short/long trajectory previews through the TrajectoryRenderer.
/// </summary>
public class VelocityDragManager : MonoBehaviour
{
    [Header("References - Components")]
    [SerializeField] private Camera _mainCamera;
    [SerializeField] public TrajectoryRenderer trajectoryRenderer;
    [SerializeField] private TutorialController _tutorialController;

    private ICameraTracker _cameraTracker;
    private BodyService _bodyService;
    // private UIManager _uiManager;
    private ObjectPlacementManager _objectPlacementManager;
    private SimContext _ctx;

    [Header("References - UI")]
    [SerializeField] private TMP_InputField _velocityInputField;
    [SerializeField] private Slider _speedSlider;
    [SerializeField] private Button _setVelocityButton;
    [SerializeField] private TextMeshProUGUI _feedbackText;

    [Header("Manual Orbit Readout (Optional)")]
    [SerializeField] private ManualOrbitReadout.References _manualOrbitReadoutRefs;

    [Header("Planet to Apply Velocity To")]
    [SerializeField] public GameObject planet;
    [SerializeField] private float _sphereRadiusMultiplier = 10f;

    [Header("Mass Handling")]
    [SerializeField] public float placeholderMass;
    private Vector3 placeholderRadiusMeters;

    private bool _isDragging;
    private bool _isVelocitySet;
    private bool _manualVelocityPlacementUiActive;
    private Vector3 _currentVelocity;
    private Vector3 _dragDirection = Vector3.zero;
    private float _sliderSpeed;

    private const float MaxVelocityMagnitude = 5.0f; // currently unused, kept for possible future clamp

    private GameObject _dragSphereObject;
    private SphereCollider _dragSphereCollider;

    [Header("Preview Settings")]
    [Tooltip("Delay (seconds) after input stops before running the long preview.")]
    [SerializeField] private float _longPreviewDelay = 0.2f;
    [SerializeField] private int _longPreviewSteps = 5000;
    [SerializeField] private float _longPreviewDt = 10f;
    private Coroutine _longPreviewCoroutine;

    [Header("Runtime Arrow")]
    [SerializeField] private RuntimeArrow _dragArrow;
    [SerializeField] private float _arrowLengthVisualMultiplier = 12f;
    [SerializeField] private float _arrowHeadLengthVisualMultiplier = 3f;
    [SerializeField] private float _arrowThicknessVisualMultiplier = 0.25f;
    [SerializeField] private float _arrowHeadRadiusVisualMultiplier = 0.6f;
    [SerializeField] private Color _arrowColor = new Color(0.3f, 1f, 1f, 1f);

    public bool HasAppliedVelocity => _isVelocitySet;
    public bool IsManualVelocityPlacementActive => _manualVelocityPlacementUiActive;

    [Header("Performance Tuning")]
    [Tooltip("Minimum time (seconds) between quick preview recomputes while dragging.")]
    [SerializeField] private float _minPreviewInterval = 0.05f; // 20 Hz
    [Tooltip("Angular change (degrees) required to trigger a new quick preview.")]
    [SerializeField] private float _directionAngleThresholdDeg = 0.5f;
    [Tooltip("Speed magnitude delta required to trigger a new quick preview.")]
    [SerializeField] private float _speedThreshold = 0.01f;

    private float _lastPreviewTime;
    private Vector3 _lastPreviewVel;
    private Vector3 _lastPreviewDir;

    private Vector3 _lastArrowStart, _lastArrowEnd;
    private ManualOrbitReadout _manualOrbitReadout;

    private const float DefaultPlaceholderMass = 400000f;

    private bool HasPendingPlacement() => _manualVelocityPlacementUiActive && planet != null;

    /// <summary>
    /// Binds references from the sim context, wires up UI and input handlers,
    /// and sets up the temporary drag sphere and runtime arrow.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        _ctx = ctx;
        // _uiManager = ctx.UIManager;
        trajectoryRenderer = ctx.TrajectoryRenderer ?? trajectoryRenderer;
        _objectPlacementManager = ctx.ObjectPlacementManager;
        _cameraTracker = ctx.CameraTracker;
        _tutorialController = ctx.TutorialController ?? _tutorialController;
        _bodyService = ctx.BodyService;
        _manualOrbitReadout = new ManualOrbitReadout(_manualOrbitReadoutRefs);

        if (_speedSlider != null)
        {
            _speedSlider.onValueChanged.AddListener(OnSpeedSliderChanged);
            _speedSlider.interactable = false;
        }

        if (_velocityInputField != null)
        {
            _velocityInputField.onValueChanged.AddListener(OnVelocityInputChanged);
            _velocityInputField.interactable = false;
        }

        if (_setVelocityButton != null)
            _setVelocityButton.interactable = false;

        _dragSphereObject = new GameObject("DragSphereTemp");
        _dragSphereCollider = _dragSphereObject.AddComponent<SphereCollider>();
        _dragSphereCollider.isTrigger = true;
        _dragSphereObject.layer = LayerMask.NameToLayer("DragSphere");
        _dragSphereObject.SetActive(false);

        EnsureDragArrow();
        _dragArrow.Hide();

        _lastArrowStart = _lastArrowEnd = new Vector3(float.NaN, float.NaN, float.NaN);

        _lastPreviewDir = new Vector3(float.NaN, float.NaN, float.NaN);
        _lastPreviewVel = new Vector3(float.NaN, float.NaN, float.NaN);
        _lastPreviewTime = -999f;
        _manualVelocityPlacementUiActive = false;

        _manualOrbitReadout.Clear();
        UpdateManualPlacementUi();
    }

    public void ConfigurePendingPlacement(GameObject pendingPlanet, float mass, Vector3 radiusMeters)
    {
        ResetDragManager();
        planet = pendingPlanet;
        placeholderMass = mass;
        placeholderRadiusMeters = radiusMeters;
        _manualVelocityPlacementUiActive = pendingPlanet != null;

        if (pendingPlanet != null && !pendingPlanet.TryGetComponent(out PendingVelocityPlacementMarker _))
            pendingPlanet.AddComponent<PendingVelocityPlacementMarker>();

        if (_velocityInputField != null)
            _velocityInputField.interactable = pendingPlanet != null;

        if (_speedSlider != null)
            _speedSlider.interactable = pendingPlanet != null;

        if (_setVelocityButton != null)
            _setVelocityButton.interactable = false;

        UpdateManualPlacementUi();
        _ctx?.UIRoot?.RefreshAllUi();
    }

    /// <summary>
    /// Makes sure there is a RuntimeArrow instance and applies basic styling.
    /// </summary>
    private void EnsureDragArrow()
    {
        if (_dragArrow == null)
        {
            var go = new GameObject("DragArrow");
            _dragArrow = go.AddComponent<RuntimeArrow>();
        }

        _dragArrow.SetColor(_arrowColor);
        _dragArrow.Hide();
    }

    /// <summary>
    /// Handles mouse input for starting/continuing/ending the drag gesture and
    /// keeps the initial "hint" arrow visible when no velocity is set.
    /// </summary>
    private void Update()
    {
        if (!HasPendingPlacement())
            return;

        if (!_isVelocitySet && !_dragArrow.gameObject.activeSelf)
        {
            Vector3 start = planet.transform.position;

            var center = _ctx.BodyService.CentralBody.transform.position;
            Vector3 dir = (center - start).normalized;

            Vector3 end = start + dir * GetArrowLength();
            _dragDirection = dir;
            ShowArrowCached(start, end);
        }

        if (_isVelocitySet) return;

        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            StartDrag();
        }

        if (_isDragging && Input.GetMouseButton(0))
        {
            UpdateDrag();
        }

        if (Input.GetMouseButtonUp(0) && _isDragging)
        {
            EndDrag();
        }
    }

    /// <summary>
    /// Begins a drag session: enables the drag sphere, shows a default arrow,
    /// and kicks off an initial preview.
    /// </summary>
    private void StartDrag()
    {
        if (!HasPendingPlacement() || _mainCamera == null) return;

        CancelLongPreviewDebounce();
        _isDragging = true;

        if (_tutorialController != null && _tutorialController.inTutorialMode)
            _tutorialController.hasClickAndDrag = true;

        _dragSphereObject.transform.SetPositionAndRotation(planet.transform.position, Quaternion.identity);
        _dragSphereObject.transform.localScale = Vector3.one;
        _dragSphereCollider.radius = GetDragSphereRadius();
        _dragSphereObject.SetActive(true);

        ShowArrowCached(
            planet.transform.position,
            planet.transform.position + Vector3.forward * GetArrowLength()
        );

        SetUIInteractable(true);
        _dragDirection = Vector3.forward;

        _lastPreviewTime = -999f;
        TryQuickPreview();
    }

    /// <summary>
    /// Updates drag direction based on mouse position on a virtual sphere
    /// and pushes that into the arrow and quick preview.
    /// </summary>
    private void UpdateDrag()
    {
        Vector3 sphereCenter = planet.transform.position;
        float radius = GetDragSphereRadius();
        Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);
        Vector3 intersection = GetFarSideIntersection(ray, sphereCenter, radius);

        _dragDirection = (intersection - sphereCenter).normalized;
        _currentVelocity = _dragDirection * _sliderSpeed;

        Vector3 arrowEnd = sphereCenter + _dragDirection * GetArrowLength();
        ShowArrowCached(sphereCenter, arrowEnd);

        RefreshManualOrbitReadout();
        TryQuickPreview();
    }

    /// <summary>
    /// Ends the drag gesture but keeps the ghost arrow around
    /// and schedules a long preview after a short idle.
    /// </summary>
    private void EndDrag()
    {
        _isDragging = false;
        _dragSphereObject.SetActive(false);
        ScheduleLongPreviewForGhost();
    }

    /// <summary>
    /// Called when the speed slider changes. Updates current velocity,
    /// UI display, arrow, and triggers a new preview.
    /// </summary>
    public void OnSpeedSliderChanged(float value)
    {
        if (!HasPendingPlacement())
            return;

        _sliderSpeed = value;
        _currentVelocity = _dragDirection * _sliderSpeed;

        if (_tutorialController != null && _tutorialController.inTutorialMode)
            _tutorialController.hasAddVelocity = true;

        if (_velocityInputField != null && _currentVelocity != Vector3.zero)
        {
            _velocityInputField.onValueChanged.RemoveListener(OnVelocityInputChanged);
            _velocityInputField.text = FormatVelocityForUI(_currentVelocity);
            _velocityInputField.onValueChanged.AddListener(OnVelocityInputChanged);
        }

        RefreshManualOrbitReadout();
        TryQuickPreview();
        UpdateArrowFromCurrent();

        if (!_isDragging)
        {
            CancelLongPreviewDebounce();
            ScheduleLongPreviewForGhost();
        }
    }

    /// <summary>
    /// Formats velocity for the text field. Note: values are scaled into km/s-ish.
    /// </summary>
    private string FormatVelocityForUI(Vector3 v)
    {
        return $"{(v.x * 10f):F2}, {(v.z * 10f):F2}, {(v.y * 10f):F2}";
    }

    private bool TryParseVelocityFromUI(string inputText, out Vector3 velocity)
    {
        velocity = Vector3.zero;

        if (!ParsingUtils.TryParseVector3(inputText, out var uiVelocity))
            return false;

        velocity = new Vector3(
            uiVelocity.x / 10f,
            uiVelocity.z / 10f,
            uiVelocity.y / 10f
        );
        return true;
    }

    /// <summary>
    /// Parses manual velocity input and refreshes the ghost arrow and preview.
    /// </summary>
    private void OnVelocityInputChanged(string inputText)
    {
        if (!HasPendingPlacement())
            return;

        if (string.IsNullOrWhiteSpace(inputText)) return;

        if (TryParseVelocityFromUI(inputText, out var newVelocity))
        {
            _currentVelocity = newVelocity;
            if (_currentVelocity.sqrMagnitude > 1e-6f)
                _dragDirection = _currentVelocity.normalized;
            if (_setVelocityButton != null) _setVelocityButton.interactable = true;
            UpdateArrowFromCurrent();
            RefreshManualOrbitReadout();
            TryQuickPreview();
        }

        if (!_isDragging)
        {
            CancelLongPreviewDebounce();
            ScheduleLongPreviewForGhost();
        }
    }

    /// <summary>
    /// Button callback: applies the currently staged velocity to the planet.
    /// </summary>
    public void callApplyVelocity()
    {
        trajectoryRenderer?.ClearPreview();
        ApplyVelocityToPlanet(_currentVelocity);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>
    /// Makes sure the planet has an NBody (and attitude) and then
    /// actually applies the velocity, registers it, and hands it off to tracking.
    /// </summary>
    public void ApplyVelocityToPlanet(Vector3 velocityToApply)
    {
        if (!HasPendingPlacement()) return;

        var nbody = planet.GetComponent<NBody>();
        if (nbody == null)
        {
            nbody = planet.AddComponent<NBody>();

            float mass = (placeholderMass > 0f) ? placeholderMass : DefaultPlaceholderMass;

            nbody.mass = mass;
            nbody.trueMass = mass;
            nbody.cameraDistanceRadius = SatelliteSizing.CameraDistanceRadius;
            nbody.isCentralBody = false;
            nbody.Initialize(_ctx);
        }

        planet.transform.localScale = SatelliteSizing.ResolveVisualScale(placeholderRadiusMeters);
        nbody.radius = SatelliteSizing.ResolvePhysicalRadiusSimUnits(placeholderRadiusMeters);
        nbody.state = new NBody.OrbitalState(
            new Unity.Mathematics.double3(planet.transform.position.x, planet.transform.position.y, planet.transform.position.z),
            new Unity.Mathematics.double3(velocityToApply.x, velocityToApply.y, velocityToApply.z),
            0f,
            nbody.trueMass,
            nbody.radius,
            nbody.dragCoefficient,
            Vector3.zero
        );

        var attitude = planet.GetComponent<AttitudeController>();
        if (attitude == null)
        {
            attitude = planet.AddComponent<AttitudeController>();
            attitude.mode = AttitudeController.PointingMode.Velocity;
            attitude.snapAttitude = false;
            attitude.maxSlewRateDegPerSec = 60f;
        }

        if (_tutorialController != null && _tutorialController.inTutorialMode)
            _tutorialController.hasSetVelocity = true;

        nbody.velocity = velocityToApply;
        _bodyService.Register(nbody);

        CancelLongPreviewDebounce();

        var tracker = _cameraTracker ?? _ctx.CameraTracker;
        tracker?.TrackBody(nbody);
        tracker?.ReturnToTracking();

        trajectoryRenderer?.RequestFullOrbitPass();
        planet = null;
        placeholderRadiusMeters = default;
        _isVelocitySet = true;
        _manualVelocityPlacementUiActive = false;

        _dragArrow.Hide();
        _manualOrbitReadout?.Clear();
        UpdateManualPlacementUi();
        _objectPlacementManager?.ClearPendingPlacement();
        EventSystem.current?.SetSelectedGameObject(null);

        if (_velocityInputField != null)
        {
            _velocityInputField.text = "";
            _velocityInputField.interactable = false;
        }

        if (_speedSlider != null)
        {
            _speedSlider.interactable = false;
            _speedSlider.value = 0f;
        }

        if (_setVelocityButton != null)
            _setVelocityButton.interactable = false;

        _ctx?.UIRoot?.RefreshAllUi();
    }

    /// <summary>
    /// Returns the intersection on the far side of a sphere along the given ray.
    /// Used to project mouse drags onto a virtual sphere around the planet.
    /// </summary>
    private Vector3 GetFarSideIntersection(Ray ray, Vector3 sphereCenter, float radius)
    {
        Vector3 d = ray.direction.normalized;
        Vector3 oc = ray.origin - sphereCenter;

        float b = 2f * Vector3.Dot(oc, d);
        float c = oc.sqrMagnitude - (radius * radius);
        float disc = b * b - 4f * c;

        if (disc < 0f) return sphereCenter + (d * radius);

        float sqrtDisc = Mathf.Sqrt(disc);
        float t1 = (-b - sqrtDisc) / 2f;
        float t2 = (-b + sqrtDisc) / 2f;

        float chosenT = (t2 >= 0f) ? t2 : t1;
        if (chosenT < 0f) return sphereCenter + (d * radius);

        return ray.origin + d * chosenT;
    }

    /// <summary>
    /// Rebuilds the arrow from the current direction and planet position.
    /// </summary>
    private void UpdateArrowFromCurrent()
    {
        if (planet == null || _dragArrow == null) return;

        Vector3 startPos = planet.transform.position;
        Vector3 dir = (_dragDirection.sqrMagnitude > 1e-6f) ? _dragDirection : Vector3.forward;
        Vector3 end = startPos + dir * GetArrowLength();
        ShowArrowCached(startPos, end);
    }

    private float GetPendingVisualScale()
    {
        if (placeholderRadiusMeters != Vector3.zero)
            return SatelliteSizing.ResolveVisualScaleUnits(placeholderRadiusMeters);

        return planet != null
            ? Mathf.Max(planet.transform.localScale.x, SatelliteSizing.MinVisualScale)
            : SatelliteSizing.MinVisualScale;
    }

    private float GetDragSphereRadius()
    {
        return Mathf.Max(0.25f, GetPendingVisualScale() * _sphereRadiusMultiplier);
    }

    private float GetArrowLength()
    {
        return Mathf.Max(0.35f, GetPendingVisualScale() * _arrowLengthVisualMultiplier);
    }

    private float GetArrowHeadLength()
    {
        return Mathf.Max(0.08f, GetPendingVisualScale() * _arrowHeadLengthVisualMultiplier);
    }

    private float GetArrowThickness()
    {
        return Mathf.Max(0.006f, GetPendingVisualScale() * _arrowThicknessVisualMultiplier);
    }

    private float GetArrowHeadRadius()
    {
        return Mathf.Max(0.018f, GetPendingVisualScale() * _arrowHeadRadiusVisualMultiplier);
    }

    /// <summary>
    /// Enables or disables the velocity UI controls as a group.
    /// </summary>
    private void SetUIInteractable(bool enable)
    {
        if (_velocityInputField != null) _velocityInputField.interactable = enable;
        if (_speedSlider != null) _speedSlider.interactable = enable;
        if (_setVelocityButton != null) _setVelocityButton.interactable = enable;
    }

    /// <summary>
    /// Returns true if the drag direction or speed has changed enough
    /// to justify another quick preview.
    /// </summary>
    private bool ChangedEnough()
    {
        if (_dragDirection == Vector3.zero) return false;

        bool firstDir = float.IsNaN(_lastPreviewDir.x);
        bool firstVel = float.IsNaN(_lastPreviewVel.x);
        if (firstDir || firstVel) return true;

        bool dirChanged = Vector3.Angle(_lastPreviewDir, _dragDirection) > _directionAngleThresholdDeg;
        bool spdChanged = Mathf.Abs(_currentVelocity.magnitude - _lastPreviewVel.magnitude) > _speedThreshold;

        return dirChanged || spdChanged;
    }

    /// <summary>
    /// Schedules a short, cheap preview trajectory, throttled by time and input deltas.
    /// </summary>
    private void TryQuickPreview()
    {
        if (!HasPendingPlacement() || trajectoryRenderer == null) return;

        var svc = _ctx?.BodyService;
        if (svc == null || svc.CentralBody == null) return;

        if ((Time.unscaledTime - _lastPreviewTime) < _minPreviewInterval) return;
        if (!ChangedEnough()) return;

        float massForPreview = (placeholderMass > 0f) ? placeholderMass : DefaultPlaceholderMass;
        trajectoryRenderer.QuickPreviewFromState(planet.transform.position, _currentVelocity, massForPreview);

        _lastPreviewTime = Time.unscaledTime;
        _lastPreviewDir = _dragDirection;
        _lastPreviewVel = _currentVelocity;
    }

    /// <summary>
    /// Shows the arrow only when its endpoints have actually changed.
    /// </summary>
    private void ShowArrowCached(Vector3 start, Vector3 end)
    {
        if ((start - _lastArrowStart).sqrMagnitude < 1e-6f &&
            (end - _lastArrowEnd).sqrMagnitude < 1e-6f) return;

        _dragArrow.Show(start, end, GetArrowThickness(), GetArrowHeadLength(), GetArrowHeadRadius());
        _lastArrowStart = start;
        _lastArrowEnd = end;
    }

    /// <summary>
    /// Starts a timer that will trigger a longer, cheaper trajectory preview
    /// after the user stops interacting.
    /// </summary>
    private void ScheduleLongPreviewForGhost()
    {
        if (!HasPendingPlacement() || trajectoryRenderer == null) return;
        if (_longPreviewCoroutine != null) StopCoroutine(_longPreviewCoroutine);
        _longPreviewCoroutine = StartCoroutine(LongPreviewAfterIdle());
    }

    /// <summary>
    /// Waits for a small idle window, then kicks off a longer preview
    /// using the current velocity as a ghost orbit.
    /// </summary>
    private IEnumerator LongPreviewAfterIdle()
    {
        float t = 0f;
        while (t < _longPreviewDelay)
        {
            if (_isDragging) yield break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!HasPendingPlacement()) yield break;

        float massForPreview = (placeholderMass > 0f) ? placeholderMass : DefaultPlaceholderMass;
        trajectoryRenderer.QuickPreviewOnceLong(
            planet.transform.position,
            _currentVelocity,
            massForPreview,
            _longPreviewSteps,
            _longPreviewDt,
            singleOrbit: true,
            smoothClosedLoop: false
        );
        _longPreviewCoroutine = null;
    }

    /// <summary>
    /// Cancels any pending long-preview debounce coroutine.
    /// </summary>
    private void CancelLongPreviewDebounce()
    {
        if (_longPreviewCoroutine != null) StopCoroutine(_longPreviewCoroutine);
        _longPreviewCoroutine = null;
    }

    /// <summary>
    /// Clears drag-related state, hides visuals, and throws away any ghost previews.
    /// </summary>
    public void ClearManualArtifacts()
    {
        CancelLongPreviewDebounce();
        _isDragging = false;
        _isVelocitySet = false;
        _manualVelocityPlacementUiActive = false;
        _dragArrow.Hide();
        _manualOrbitReadout?.Clear();
        UpdateManualPlacementUi();

        ResetVelocityControls();
        UIHelpers.SetActive(_dragSphereObject, false);

        planet = null;
        placeholderRadiusMeters = default;

        trajectoryRenderer?.ClearPreview();
        trajectoryRenderer?.ClearPreManeuverOrbit();
        _ctx?.UIRoot?.RefreshAllUi();
    }

    /// <summary>
    /// Resets high-level state so the drag flow can run again for a new body.
    /// </summary>
    public void ResetDragManager()
    {
        CancelLongPreviewDebounce();
        _isDragging = false;
        _isVelocitySet = false;
        _manualVelocityPlacementUiActive = false;
        _currentVelocity = Vector3.zero;
        _dragDirection = Vector3.zero;
        _sliderSpeed = 0f;
        placeholderMass = 0f;
        placeholderRadiusMeters = default;
        planet = null;

        ResetVelocityControls();
        UIHelpers.SetActive(_dragSphereObject, false);

        if (_dragArrow != null)
            _dragArrow.Hide();

        _manualOrbitReadout?.Clear();
        UpdateManualPlacementUi();
        trajectoryRenderer?.ClearPreview();
        trajectoryRenderer?.ClearPreManeuverOrbit();
    }

    private void UpdateManualPlacementUi()
    {
        SetVelocityControlsVisible(IsManualVelocityPlacementActive);
        _manualOrbitReadout?.SetVisible(IsManualVelocityPlacementActive);
    }

    private void SetVelocityControlsVisible(bool visible)
    {
        UIHelpers.SetActive(_velocityInputField != null ? _velocityInputField.gameObject : null, visible);
        UIHelpers.SetActive(_setVelocityButton != null ? _setVelocityButton.gameObject : null, visible);

        GameObject sliderRoot = null;
        if (_speedSlider != null)
        {
            Transform sliderTransform = _speedSlider.transform;
            sliderRoot = sliderTransform.parent != null && sliderTransform.parent.name == "Slider_Velocity"
                ? sliderTransform.parent.gameObject
                : sliderTransform.gameObject;
        }

        UIHelpers.SetActive(sliderRoot, visible);

        Transform panelRoot = _velocityInputField != null ? _velocityInputField.transform.parent : null;
        UIHelpers.SetChildActive(panelRoot, "Txt_Velocity", visible);
        UIHelpers.SetChildActive(panelRoot, "VelocityLabel", visible);
    }

    private float GetKilometersPerUnit()
    {
        if (_objectPlacementManager != null)
            return (float)(_objectPlacementManager.MetersPerUnit / 1000.0);

        return 10f;
    }

    private void RefreshManualOrbitReadout()
    {
        _manualOrbitReadout?.Refresh(
            planet,
            _currentVelocity,
            _bodyService != null ? _bodyService.CentralBody : null,
            GetKilometersPerUnit()
        );
    }

    private void ResetVelocityControls()
    {
        UIHelpers.ClearInput(_velocityInputField, clearSelection: false);
        UIHelpers.SetInteractable(_velocityInputField, false);

        if (_speedSlider != null)
            _speedSlider.value = 0f;

        UIHelpers.SetInteractable(_speedSlider, false);
        UIHelpers.SetInteractable(_setVelocityButton, false);
    }
}
