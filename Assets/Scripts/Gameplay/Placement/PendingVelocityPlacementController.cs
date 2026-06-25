using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

/// <summary>
/// Handles manual-placement velocity intent controls, keeps the UI in sync,
/// and drives short/long trajectory previews through the TrajectoryRenderer.
/// </summary>
public class PendingVelocityPlacementController : MonoBehaviour
{
    [Header("References - Components")]
    [SerializeField] public TrajectoryRenderer trajectoryRenderer;
    [SerializeField] private TutorialController _tutorialController;

    private ICameraTracker _cameraTracker;
    private BodyService _bodyService;
    // private UIManager _uiManager;
    private ObjectPlacementManager _objectPlacementManager;
    private SimContext _ctx;

    [Header("Planet to Apply Velocity To")]
    [SerializeField] public GameObject planet;

    [Header("Mass Handling")]
    [SerializeField] public float placeholderMass;
    private Vector3 placeholderRadiusMeters;

    [Header("Orbit Intent Presets")]
    [SerializeField] private float raiseApogeeSpeedScale = 1.08f;
    [SerializeField] private float lowerPerigeeSpeedScale = 0.92f;
    [SerializeField, Range(0.01f, 0.5f)] private float radialClickStep = 0.01f;
    [SerializeField, Range(0f, 1f)] private float maxRadialAmount = 0.75f;
    [SerializeField, Range(0.1f, 15f)] private float tiltClickDegrees = 1f;
    [SerializeField, Range(0f, 90f)] private float maxTiltDegrees = 90f;
    [SerializeField, Range(0f, 2f)] private float defaultIntentSpeedScale = 1f;

    private bool _isVelocitySet;
    private bool _manualVelocityPlacementUiActive;
    private bool _usingOrbitIntentControls;
    private bool _suppressTutorialVelocityCredit;
    private Vector3 _currentVelocity;
    private Vector3 _stagedDirection = Vector3.zero;
    private float _sliderSpeed;
    private readonly ManualVelocityIntent _orbitIntent = new();

    [Header("Preview Settings")]
    [Tooltip("Delay (seconds) after input stops before running the long preview.")]
    [SerializeField] private float _longPreviewDelay = 0.2f;

    [Header("Runtime Arrow")]
    [FormerlySerializedAs("_dragArrow")]
    [SerializeField] private RuntimeArrow _directionArrow;
    [SerializeField] private float _arrowLengthVisualMultiplier = 8f;
    [SerializeField] private float _arrowHeadLengthVisualMultiplier = 2f;
    [SerializeField] private float _arrowThicknessVisualMultiplier = 0.25f;
    [SerializeField] private float _arrowHeadRadiusVisualMultiplier = 0.6f;
    [SerializeField] private Color _arrowColor = new Color(0.3f, 1f, 1f, 1f);

    public bool HasAppliedVelocity => _isVelocitySet;
    public bool IsManualVelocityPlacementActive => _manualVelocityPlacementUiActive;

    [Header("Performance Tuning")]
    [Tooltip("Minimum time (seconds) between quick preview recomputes while editing velocity.")]
    [SerializeField] private float _minPreviewInterval = 0.05f; // 20 Hz
    [Tooltip("Angular change (degrees) required to trigger a new quick preview.")]
    [SerializeField] private float _directionAngleThresholdDeg = 0.5f;
    [Tooltip("Speed magnitude delta required to trigger a new quick preview.")]
    [SerializeField] private float _speedThreshold = 0.01f;

    private Vector3 _lastArrowStart, _lastArrowEnd;
    private ManualVelocityPlacementUIController _manualVelocityUi;
    private ManualVelocityPreviewController _previewController;
    private ManualVelocityLaunchService _launchService;

    private const float DefaultPlaceholderMass = 400000f;
    private const float MinVelocityToApplySqr = 1e-6f;
    private const float DefaultVelocityScale = 1f;

    private bool HasPendingPlacement() => _manualVelocityPlacementUiActive && planet != null;

    /// <summary>
    /// Binds references from the sim context, wires up UI and input handlers,
    /// and sets up the runtime arrow.
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
        _manualVelocityUi = ctx.ManualVelocityPlacementUIController;
        SubscribeManualVelocityUi();
        _launchService = new ManualVelocityLaunchService(_ctx, _bodyService, _cameraTracker);

        EnsureDirectionArrow();
        _directionArrow.Hide();

        _lastArrowStart = _lastArrowEnd = new Vector3(float.NaN, float.NaN, float.NaN);

        _previewController = new ManualVelocityPreviewController(
            this,
            () => trajectoryRenderer,
            () => _ctx?.BodyService,
            _minPreviewInterval,
            _directionAngleThresholdDeg,
            _speedThreshold,
            _longPreviewDelay
        );
        _manualVelocityPlacementUiActive = false;
        _orbitIntent.Reset(defaultIntentSpeedScale);

        _manualVelocityUi?.ClearManualOrbitReadout();
        UpdateManualPlacementUi();
    }

    private void OnDestroy()
    {
        _previewController?.CancelLongPreview();
        UnsubscribeManualVelocityUi();
    }

    public void ConfigurePendingPlacement(GameObject pendingPlanet, float mass, Vector3 radiusMeters)
    {
        ResetVelocityManager();
        planet = pendingPlanet;
        placeholderMass = mass;
        placeholderRadiusMeters = radiusMeters;
        _manualVelocityPlacementUiActive = pendingPlanet != null;
        ResetOrbitIntentState();

        if (pendingPlanet != null && !pendingPlanet.TryGetComponent(out PendingVelocityPlacementMarker _))
            pendingPlanet.AddComponent<PendingVelocityPlacementMarker>();

        _manualVelocityUi?.SetPendingInteractable(pendingPlanet != null);

        UpdateManualPlacementUi();
        _suppressTutorialVelocityCredit = true;
        try
        {
            StageCircularOrbitVelocity();
        }
        finally
        {
            _suppressTutorialVelocityCredit = false;
        }
        SetVelocityFeedback(
            pendingPlanet != null
                ? "Circular orbit staged. Adjust it or click Set Velocity."
                : string.Empty
        );
        _ctx?.UIRoot?.RefreshAllUi();
    }

    public void StageCircularOrbitVelocity()
    {
        _orbitIntent.StageCircular(DefaultVelocityScale);
        RefreshOrbitIntentButtonStates();
        RestageOrbitIntent("Circular orbit staged. Click Set Velocity or choose another intent.");
    }

    public void StageRetrogradeCircularVelocity()
    {
        _orbitIntent.StageRetrogradeCircular(DefaultVelocityScale);
        RefreshOrbitIntentButtonStates();
        RestageOrbitIntent("Retrograde circular-speed velocity staged.");
    }

    public void StageRaiseApogeeVelocity()
    {
        _orbitIntent.StageRaiseApogee(raiseApogeeSpeedScale);
        RefreshSpeedIntentButtonStates();
        RestageOrbitIntent("Raise-apogee velocity staged.");
    }

    public void StageLowerPerigeeVelocity()
    {
        _orbitIntent.StageLowerPerigee(lowerPerigeeSpeedScale);
        RefreshSpeedIntentButtonStates();
        RestageOrbitIntent("Lower-perigee velocity staged.");
    }

    public void StageRadialOutVelocity()
    {
        SelectRadialOutModifier();
    }

    public void StageRadialInVelocity()
    {
        SelectRadialInModifier();
    }

    public void StageNormalVelocity()
    {
        SelectTiltPositiveModifier();
    }

    public void StageAntiNormalVelocity()
    {
        SelectTiltNegativeModifier();
    }

    public void SelectProgradeBase()
    {
        _orbitIntent.SelectPrograde();
        RefreshBaseDirectionButtonState();
        RestageOrbitIntent("Prograde base selected.");
    }

    public void SelectRetrogradeBase()
    {
        _orbitIntent.SelectRetrograde();
        RefreshBaseDirectionButtonState();
        RestageOrbitIntent("Retrograde base selected.");
    }

    public void SelectRadialOutModifier()
    {
        _orbitIntent.StepRadial(1f, radialClickStep, maxRadialAmount);
        RestageOrbitIntent("Radial-out shaping applied.");
    }

    public void SelectRadialInModifier()
    {
        _orbitIntent.StepRadial(-1f, radialClickStep, maxRadialAmount);
        RestageOrbitIntent("Radial-in shaping applied.");
    }

    public void ClearRadialModifier()
    {
        _orbitIntent.ClearRadial();
        RestageOrbitIntent("Radial shaping cleared.");
    }

    public void SelectTiltPositiveModifier()
    {
        _orbitIntent.StepTilt(-1f, tiltClickDegrees, maxTiltDegrees);
        RestageOrbitIntent("Tilt-plus shaping applied.");
    }

    public void SelectTiltNegativeModifier()
    {
        _orbitIntent.StepTilt(1f, tiltClickDegrees, maxTiltDegrees);
        RestageOrbitIntent("Tilt-minus shaping applied.");
    }

    public void ClearTiltModifier()
    {
        _orbitIntent.ClearTilt();
        RestageOrbitIntent("Tilt shaping cleared.");
    }

    public void ClearOrbitShapeModifiers()
    {
        _orbitIntent.ClearShapeModifiers(DefaultVelocityScale);
        RefreshOrbitIntentButtonStates();
        RestageOrbitIntent("Orbit shaping cleared.");
    }

    public void SetVelocitySpeedScale(float scale)
    {
        if (!HasPendingPlacement())
            return;

        if (!float.IsFinite(scale) || scale <= 0f)
        {
            SetVelocityFeedback("Velocity scale must be greater than zero.");
            return;
        }

        _orbitIntent.SetVelocityScale(scale);
        RefreshSpeedIntentButtonStates();
        RestageOrbitIntent($"{scale:0.##}x circular-speed velocity staged.");
    }

    private void SetIntentFeedback(string message)
    {
        string summary = GetOrbitIntentSummary();
        SetVelocityFeedback($"{message}\n{summary}");
    }

    private void RestageOrbitIntent(string feedback, bool syncSpeedSlider = true)
    {
        if (!HasPendingPlacement())
            return;

        NBody central = GetCentralBody();
        if (!ManualVelocityIntentResolver.TryResolve(
                _orbitIntent,
                planet != null ? planet.transform : null,
                central,
                maxTiltDegrees,
                out ManualVelocityIntentResult resolved,
                out string error))
        {
            SetVelocityFeedback(error);
            return;
        }

        StageVelocity(
            resolved.Velocity,
            feedback,
            orbitIntent: true,
            syncSpeedSlider: syncSpeedSlider
        );
        SetIntentFeedback(feedback);
    }

    private void RefreshSpeedIntentButtonStates()
    {
        _manualVelocityUi?.RefreshSpeedIntent(_orbitIntent.SpeedSelection);
    }

    private void RefreshBaseDirectionButtonState()
    {
        _manualVelocityUi?.RefreshBaseDirection(HasPendingPlacement(), _orbitIntent.BaseDirection);
    }

    private void RefreshOrbitIntentButtonStates()
    {
        RefreshSpeedIntentButtonStates();
        RefreshBaseDirectionButtonState();
    }

    private void StageVelocity(Vector3 velocity, string feedback, bool orbitIntent = false, bool syncSpeedSlider = true)
    {
        if (!HasPendingPlacement())
            return;

        _previewController?.CancelLongPreview();
        _usingOrbitIntentControls = orbitIntent;
        _currentVelocity = velocity;
        _sliderSpeed = velocity.magnitude;

        if (_currentVelocity.sqrMagnitude > MinVelocityToApplySqr)
            _stagedDirection = _currentVelocity.normalized;

        if (!_suppressTutorialVelocityCredit &&
            _tutorialController != null &&
            _tutorialController.inTutorialMode)
        {
            _tutorialController.hasAddVelocity = true;
        }

        _manualVelocityUi?.SyncVelocityInput(_currentVelocity);
        if (syncSpeedSlider)
            _manualVelocityUi?.SyncSpeedSlider(_usingOrbitIntentControls, _orbitIntent.SpeedTrimScale, _sliderSpeed);
        RefreshManualOrbitReadout();

        _previewController?.ResetChangeTracking();
        RefreshVelocityPreview();
        UpdateArrowFromCurrent();
        RefreshSetVelocityButtonState();
        if (!orbitIntent)
            SetVelocityFeedback(feedback);
    }

    private void ResetOrbitIntentState()
    {
        _orbitIntent.Reset(defaultIntentSpeedScale);
        _usingOrbitIntentControls = false;
        RefreshOrbitIntentButtonStates();
    }

    private string GetOrbitIntentSummary()
    {
        return _orbitIntent.BuildSummary();
    }

    private NBody GetCentralBody()
    {
        if (_bodyService != null && _bodyService.CentralBody != null)
            return _bodyService.CentralBody;

        return _ctx?.BodyService != null ? _ctx.BodyService.CentralBody : null;
    }

    /// <summary>
    /// Makes sure there is a RuntimeArrow instance and applies basic styling.
    /// </summary>
    private void EnsureDirectionArrow()
    {
        if (_directionArrow == null)
        {
            var go = new GameObject("VelocityDirectionArrow");
            _directionArrow = go.AddComponent<RuntimeArrow>();
        }

        _directionArrow.SetColor(_arrowColor);
        _directionArrow.Hide();
    }

    /// <summary>
    /// Called when the speed slider changes. Updates current velocity,
    /// UI display, arrow, and triggers a new preview.
    /// </summary>
    public void OnSpeedSliderChanged(float value)
    {
        if (!HasPendingPlacement())
            return;

        if (_usingOrbitIntentControls)
        {
            _orbitIntent.SelectSpeedIntent(ManualOrbitSpeedIntentSelection.None);
            _orbitIntent.SetTrimScale(value);
            RefreshSpeedIntentButtonStates();
            RestageOrbitIntent("Velocity trim adjusted.", syncSpeedSlider: false);
        }
        else
        {
            _sliderSpeed = value;
            _currentVelocity = _stagedDirection * _sliderSpeed;

            if (_tutorialController != null && _tutorialController.inTutorialMode)
                _tutorialController.hasAddVelocity = true;

            _manualVelocityUi?.SyncVelocityInputFromSlider(_currentVelocity);

            RefreshManualOrbitReadout();
            RefreshVelocityPreview();
            RefreshSetVelocityButtonState();
            UpdateArrowFromCurrent();
        }
    }

    /// <summary>
    /// Parses manual velocity input and refreshes the ghost arrow and preview.
    /// </summary>
    private void OnVelocityInputChanged(string inputText)
    {
        if (!HasPendingPlacement())
            return;

        if (string.IsNullOrWhiteSpace(inputText)) return;

        if (ManualVelocityPlacementUIController.TryParseVelocityFromUI(inputText, out var newVelocity))
        {
            _usingOrbitIntentControls = false;
            _orbitIntent.SelectSpeedIntent(ManualOrbitSpeedIntentSelection.None);
            RefreshSpeedIntentButtonStates();
            _currentVelocity = newVelocity;
            if (_currentVelocity.sqrMagnitude > 1e-6f)
                _stagedDirection = _currentVelocity.normalized;
            if (_tutorialController != null && _tutorialController.inTutorialMode)
                _tutorialController.hasAddVelocity = true;
            _sliderSpeed = _currentVelocity.magnitude;
            _manualVelocityUi?.SyncSpeedSlider(_usingOrbitIntentControls, _orbitIntent.SpeedTrimScale, _sliderSpeed);
            RefreshSetVelocityButtonState();
            UpdateArrowFromCurrent();
            RefreshManualOrbitReadout();
            RefreshVelocityPreview();
            SetVelocityFeedback(
                CanApplyCurrentVelocity()
                    ? "Velocity staged. Click Set Velocity to launch and track this satellite."
                    : "Set a non-zero velocity before launching this satellite."
            );
        }
        else
        {
            RefreshSetVelocityButtonState();
            SetVelocityFeedback("Invalid velocity. Use x,y,z, for example 0,7.6,0.", appendLaunchPreview: false);
        }
    }

    /// <summary>
    /// Button callback: applies the currently staged velocity to the planet.
    /// </summary>
    private void ApplyStagedVelocity()
    {
        if (!CanApplyCurrentVelocity())
        {
            RefreshSetVelocityButtonState();
            SetVelocityFeedback("Set a non-zero velocity before launching this satellite.");
            return;
        }

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
        if (velocityToApply.sqrMagnitude <= MinVelocityToApplySqr)
        {
            RefreshSetVelocityButtonState();
            SetVelocityFeedback("Set a non-zero velocity before launching this satellite.");
            return;
        }

        ManualVelocityLaunchResult result = _launchService.TryLaunch(
            planet,
            velocityToApply,
            placeholderMass,
            placeholderRadiusMeters
        );

        if (!result.Success)
        {
            RefreshSetVelocityButtonState();
            if (!string.IsNullOrWhiteSpace(result.Error))
                SetVelocityFeedback(result.Error);
            return;
        }

        _previewController?.CancelLongPreview();

        if (_tutorialController != null && _tutorialController.inTutorialMode)
            _tutorialController.hasSetVelocity = true;

        trajectoryRenderer?.RequestFullOrbitPass();
        planet = null;
        placeholderRadiusMeters = default;
        _isVelocitySet = true;
        _manualVelocityPlacementUiActive = false;
        _orbitIntent.SelectSpeedIntent(ManualOrbitSpeedIntentSelection.None);
        RefreshSpeedIntentButtonStates();
        RefreshBaseDirectionButtonState();

        _directionArrow.Hide();
        _manualVelocityUi?.ClearManualOrbitReadout();
        UpdateManualPlacementUi();
        _objectPlacementManager?.ClearPendingPlacement();
        SetVelocityFeedback("Satellite launched and tracked. Open maneuver controls to plan a burn.");
        EventSystem.current?.SetSelectedGameObject(null);

        _manualVelocityUi?.ResetVelocityControls();

        _ctx?.UIRoot?.RefreshAllUi();
    }

    /// <summary>
    /// Rebuilds the arrow from the current staged direction and planet position.
    /// </summary>
    private void UpdateArrowFromCurrent()
    {
        if (planet == null || _directionArrow == null) return;

        Vector3 startPos = planet.transform.position;
        Vector3 dir = (_stagedDirection.sqrMagnitude > 1e-6f) ? _stagedDirection : Vector3.forward;
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

    private bool CanApplyCurrentVelocity()
    {
        return HasPendingPlacement() && _currentVelocity.sqrMagnitude > MinVelocityToApplySqr;
    }

    private void RefreshSetVelocityButtonState()
    {
        _manualVelocityUi?.RefreshSetVelocityButton(CanApplyCurrentVelocity());
    }

    private void SetVelocityFeedback(string message, bool appendLaunchPreview = true)
    {
        _manualVelocityUi?.SetFeedback(message, appendLaunchPreview);
    }

    private string BuildLaunchPreviewText()
    {
        if (!HasPendingPlacement() || _currentVelocity.sqrMagnitude <= MinVelocityToApplySqr)
            return null;

        NBody central = _bodyService != null ? _bodyService.CentralBody : null;
        if (central == null)
            central = _ctx?.BodyService != null ? _ctx.BodyService.CentralBody : null;

        if (central == null || !(central.trueMass > 0.0))
            return null;

        OrbitalParameters orbit = OrbitalCalculations.CalculateOrbitalParameters(
            central.trueMass,
            ToDouble3(central.transform.position),
            ToDouble3(planet.transform.position),
            ToDouble3(_currentVelocity)
        );

        if (!orbit.isValid)
            return null;

        float kilometersPerUnit = GetKilometersPerUnit();
        float perigeeKm = (orbit.perigeeRadius - (float)central.radius) * kilometersPerUnit;

        if (orbit.apogeeRadius < 0f)
        {
            return perigeeKm <= 0f
                ? "Launch preview: escape path intersects the planet."
                : $"Launch preview: escape trajectory, perigee {perigeeKm:F1} km.";
        }

        float apogeeKm = (orbit.apogeeRadius - (float)central.radius) * kilometersPerUnit;

        if (perigeeKm <= 0f)
            return $"Launch preview: impact likely, perigee {perigeeKm:F1} km.";

        if (orbit.eccentricity < 0.05f)
            return $"Launch preview: stable near-circular orbit, perigee {perigeeKm:F1} km.";

        return $"Launch preview: stable elliptical orbit, perigee {perigeeKm:F1} km, apogee {apogeeKm:F1} km.";
    }

    private static double3 ToDouble3(Vector3 value)
    {
        return new double3(value.x, value.y, value.z);
    }

    /// <summary>
    /// Shows the arrow only when its endpoints have actually changed.
    /// </summary>
    private void ShowArrowCached(Vector3 start, Vector3 end)
    {
        if ((start - _lastArrowStart).sqrMagnitude < 1e-6f &&
            (end - _lastArrowEnd).sqrMagnitude < 1e-6f) return;

        _directionArrow.Show(start, end, GetArrowThickness(), GetArrowHeadLength(), GetArrowHeadRadius());
        _lastArrowStart = start;
        _lastArrowEnd = end;
    }

    private void RefreshVelocityPreview()
    {
        float massForPreview = (placeholderMass > 0f) ? placeholderMass : DefaultPlaceholderMass;
        _previewController?.RequestPreview(
            HasPendingPlacement(),
            planet,
            _currentVelocity,
            _stagedDirection,
            massForPreview
        );
    }

    /// <summary>
    /// Clears manual velocity state, hides visuals, and throws away any ghost previews.
    /// </summary>
    public void ClearManualArtifacts()
    {
        _previewController?.CancelLongPreview();
        _isVelocitySet = false;
        _manualVelocityPlacementUiActive = false;
        _orbitIntent.SelectSpeedIntent(ManualOrbitSpeedIntentSelection.None);
        RefreshOrbitIntentButtonStates();
        _directionArrow.Hide();
        _manualVelocityUi?.ClearManualOrbitReadout();
        UpdateManualPlacementUi();
        SetVelocityFeedback(string.Empty);

        ResetVelocityControls();
        planet = null;
        placeholderRadiusMeters = default;

        trajectoryRenderer?.ClearPreview();
        trajectoryRenderer?.ClearPreManeuverOrbit();
        _ctx?.UIRoot?.RefreshAllUi();
    }

    /// <summary>
    /// Resets high-level state so the velocity flow can run again for a new body.
    /// </summary>
    public void ResetVelocityManager()
    {
        _previewController?.CancelLongPreview();
        _isVelocitySet = false;
        _manualVelocityPlacementUiActive = false;
        _currentVelocity = Vector3.zero;
        _stagedDirection = Vector3.zero;
        _sliderSpeed = 0f;
        ResetOrbitIntentState();
        placeholderMass = 0f;
        placeholderRadiusMeters = default;
        planet = null;

        ResetVelocityControls();
        if (_directionArrow != null)
            _directionArrow.Hide();

        _manualVelocityUi?.ClearManualOrbitReadout();
        UpdateManualPlacementUi();
        SetVelocityFeedback(string.Empty);
        trajectoryRenderer?.ClearPreview();
        trajectoryRenderer?.ClearPreManeuverOrbit();
    }

    private void UpdateManualPlacementUi()
    {
        _manualVelocityUi?.SetVelocityControlsVisible(IsManualVelocityPlacementActive);
    }

    private float GetKilometersPerUnit()
    {
        if (_objectPlacementManager != null)
            return (float)(_objectPlacementManager.MetersPerUnit / 1000.0);

        return 10f;
    }

    private void RefreshManualOrbitReadout()
    {
        _manualVelocityUi?.RefreshManualOrbitReadout(
            planet,
            _currentVelocity,
            _bodyService != null ? _bodyService.CentralBody : null,
            GetKilometersPerUnit()
        );
    }

    private void ResetVelocityControls()
    {
        _manualVelocityUi?.ResetVelocityControls();
    }

    private void SubscribeManualVelocityUi()
    {
        if (_manualVelocityUi == null)
            return;

        _manualVelocityUi.SetLaunchPreviewProvider(BuildLaunchPreviewText);
        _manualVelocityUi.CircularizeRequested += StageCircularOrbitVelocity;
        _manualVelocityUi.RetrogradeCircularizeRequested += StageRetrogradeCircularVelocity;
        _manualVelocityUi.RaiseApogeeRequested += StageRaiseApogeeVelocity;
        _manualVelocityUi.LowerPerigeeRequested += StageLowerPerigeeVelocity;
        _manualVelocityUi.RadialOutRequested += SelectRadialOutModifier;
        _manualVelocityUi.RadialInRequested += SelectRadialInModifier;
        _manualVelocityUi.TiltPositiveRequested += SelectTiltPositiveModifier;
        _manualVelocityUi.TiltNegativeRequested += SelectTiltNegativeModifier;
        _manualVelocityUi.ProgradeRequested += SelectProgradeBase;
        _manualVelocityUi.RetrogradeRequested += SelectRetrogradeBase;
        _manualVelocityUi.ClearRadialRequested += ClearRadialModifier;
        _manualVelocityUi.ClearTiltRequested += ClearTiltModifier;
        _manualVelocityUi.ClearOrbitShapeRequested += ClearOrbitShapeModifiers;
        _manualVelocityUi.ApplyVelocityRequested += ApplyStagedVelocity;
        _manualVelocityUi.ClearManualArtifactsRequested += ClearManualArtifacts;
        _manualVelocityUi.ResetVelocityRequested += ResetVelocityManager;
        _manualVelocityUi.SpeedSliderChanged += OnSpeedSliderChanged;
        _manualVelocityUi.VelocityTextChanged += OnVelocityInputChanged;
    }

    private void UnsubscribeManualVelocityUi()
    {
        if (_manualVelocityUi == null)
            return;

        _manualVelocityUi.CircularizeRequested -= StageCircularOrbitVelocity;
        _manualVelocityUi.RetrogradeCircularizeRequested -= StageRetrogradeCircularVelocity;
        _manualVelocityUi.RaiseApogeeRequested -= StageRaiseApogeeVelocity;
        _manualVelocityUi.LowerPerigeeRequested -= StageLowerPerigeeVelocity;
        _manualVelocityUi.RadialOutRequested -= SelectRadialOutModifier;
        _manualVelocityUi.RadialInRequested -= SelectRadialInModifier;
        _manualVelocityUi.TiltPositiveRequested -= SelectTiltPositiveModifier;
        _manualVelocityUi.TiltNegativeRequested -= SelectTiltNegativeModifier;
        _manualVelocityUi.ProgradeRequested -= SelectProgradeBase;
        _manualVelocityUi.RetrogradeRequested -= SelectRetrogradeBase;
        _manualVelocityUi.ClearRadialRequested -= ClearRadialModifier;
        _manualVelocityUi.ClearTiltRequested -= ClearTiltModifier;
        _manualVelocityUi.ClearOrbitShapeRequested -= ClearOrbitShapeModifiers;
        _manualVelocityUi.ApplyVelocityRequested -= ApplyStagedVelocity;
        _manualVelocityUi.ClearManualArtifactsRequested -= ClearManualArtifacts;
        _manualVelocityUi.ResetVelocityRequested -= ResetVelocityManager;
        _manualVelocityUi.SpeedSliderChanged -= OnSpeedSliderChanged;
        _manualVelocityUi.VelocityTextChanged -= OnVelocityInputChanged;
        _manualVelocityUi.SetLaunchPreviewProvider(null);
    }
}
