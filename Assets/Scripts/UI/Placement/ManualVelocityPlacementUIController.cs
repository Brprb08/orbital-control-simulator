using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns the manual velocity placement controls and exposes user intent as events.
/// </summary>
public sealed class ManualVelocityPlacementUIController : MonoBehaviour
{
    [Header("Velocity Controls")]
    [SerializeField] private TMP_InputField _velocityInputField;
    [SerializeField] private Slider _speedSlider;
    [SerializeField] private Button _setVelocityButton;
    [SerializeField] private TextMeshProUGUI _feedbackText;
    [SerializeField] private GameObject _orbitIntentControlsRoot;
    [SerializeField, Range(0.05f, 1f)] private float _intentSpeedTrimRange = 0.5f;

    [Header("Orbit Intent Preset Selection (Optional)")]
    [SerializeField] private ButtonSelectionGroup _speedIntentButtonGroup;
    [SerializeField] private int _circularizeButtonIndex = 0;
    [SerializeField] private int _raiseApogeeButtonIndex = 1;
    [SerializeField] private int _lowerPerigeeButtonIndex = 2;
    [SerializeField] private ButtonSelectionGroup _baseDirectionButtonGroup;
    [SerializeField] private int _progradeButtonIndex = 0;
    [SerializeField] private int _retrogradeButtonIndex = 1;

    [Header("Orbit Intent Hold Buttons (Optional)")]
    [SerializeField] private Button _radialOutHoldButton;
    [SerializeField] private Button _radialInHoldButton;
    [SerializeField] private Button _tiltPositiveHoldButton;
    [SerializeField] private Button _tiltNegativeHoldButton;
    [SerializeField] private float _holdInitialDelay = 0.25f;
    [SerializeField] private float _holdRepeatInterval = 0.08f;
    [SerializeField] private float _holdFastRepeatInterval = 0.03f;
    [SerializeField] private float _holdAccelerationDelay = 0.8f;

    [Header("Manual Orbit Readout (Optional)")]
    [SerializeField] private ManualOrbitReadout.References _manualOrbitReadoutRefs;

    public event Action CircularizeRequested;
    public event Action RetrogradeCircularizeRequested;
    public event Action RaiseApogeeRequested;
    public event Action LowerPerigeeRequested;
    public event Action RadialOutRequested;
    public event Action RadialInRequested;
    public event Action TiltPositiveRequested;
    public event Action TiltNegativeRequested;
    public event Action ProgradeRequested;
    public event Action RetrogradeRequested;
    public event Action ClearRadialRequested;
    public event Action ClearTiltRequested;
    public event Action ClearOrbitShapeRequested;
    public event Action ApplyVelocityRequested;
    public event Action ClearManualArtifactsRequested;
    public event Action ResetVelocityRequested;
    public event Action<float> SpeedSliderChanged;
    public event Action<string> VelocityTextChanged;

    private ManualOrbitReadout _manualOrbitReadout;
    private ManualVelocityControlsView _controlsView;
    private ManualVelocityButtonBinder _buttonBinder;
    private Func<string> _launchPreviewProvider;

    public void Initialize(SimContext ctx)
    {
        _manualOrbitReadout = new ManualOrbitReadout(_manualOrbitReadoutRefs);
        _manualOrbitReadout.Clear();

        _controlsView = new ManualVelocityControlsView(
            _velocityInputField,
            _speedSlider,
            _setVelocityButton,
            _feedbackText,
            _orbitIntentControlsRoot,
            _intentSpeedTrimRange,
            value => SpeedSliderChanged?.Invoke(value),
            text => VelocityTextChanged?.Invoke(text),
            () => _launchPreviewProvider?.Invoke()
        );
        _controlsView.Initialize();

        _buttonBinder = new ManualVelocityButtonBinder(
            _speedIntentButtonGroup,
            _circularizeButtonIndex,
            _raiseApogeeButtonIndex,
            _lowerPerigeeButtonIndex,
            _baseDirectionButtonGroup,
            _progradeButtonIndex,
            _retrogradeButtonIndex,
            _radialOutHoldButton,
            _radialInHoldButton,
            _tiltPositiveHoldButton,
            _tiltNegativeHoldButton,
            _holdInitialDelay,
            _holdRepeatInterval,
            _holdFastRepeatInterval,
            _holdAccelerationDelay
        );
        _buttonBinder.ConfigureHoldButtons(
            () => RadialOutRequested?.Invoke(),
            () => RadialInRequested?.Invoke(),
            () => TiltPositiveRequested?.Invoke(),
            () => TiltNegativeRequested?.Invoke()
        );

        SetPendingInteractable(false);
        SetVelocityControlsVisible(false);
    }

    private void OnDestroy()
    {
        _buttonBinder?.ClearHoldButtons();
        _controlsView?.Dispose();
    }

    public void SetLaunchPreviewProvider(Func<string> provider)
    {
        _launchPreviewProvider = provider;
    }

    public void SetPendingInteractable(bool pending)
    {
        _controlsView?.SetPendingInteractable(pending);
    }

    public void SyncVelocityInput(Vector3 velocity)
    {
        _controlsView?.SyncVelocityInput(velocity);
    }

    public void SyncSpeedSlider(bool useOrbitTrim, float trimScale, float sliderSpeed)
    {
        _controlsView?.SyncSpeedSlider(useOrbitTrim, trimScale, sliderSpeed);
    }

    public void SyncVelocityInputFromSlider(Vector3 velocity)
    {
        _controlsView?.SyncVelocityInputFromSlider(velocity);
    }

    public void RefreshSetVelocityButton(bool canApply)
    {
        _controlsView?.RefreshSetVelocityButton(canApply);
    }

    public void SetVelocityControlsVisible(bool visible)
    {
        _controlsView?.SetVelocityControlsVisible(visible);
        _manualOrbitReadout?.SetVisible(visible);
    }

    public void ResetVelocityControls()
    {
        _controlsView?.ResetVelocityControls();
    }

    public void SetFeedback(string message, bool appendLaunchPreview = true)
    {
        _controlsView?.SetFeedback(message, appendLaunchPreview);
    }

    public void RefreshSpeedIntent(ManualOrbitSpeedIntentSelection selection)
    {
        _buttonBinder?.RefreshSpeedIntent(selection);
    }

    public void RefreshBaseDirection(bool hasPendingPlacement, ManualOrbitBaseDirection direction)
    {
        _buttonBinder?.RefreshBaseDirection(hasPendingPlacement, direction);
    }

    public void RefreshManualOrbitReadout(
        GameObject pendingBody,
        Vector3 velocity,
        NBody centralBody,
        float kilometersPerUnit)
    {
        _manualOrbitReadout?.Refresh(pendingBody, velocity, centralBody, kilometersPerUnit);
    }

    public void ClearManualOrbitReadout()
    {
        _manualOrbitReadout?.Clear();
    }

    public static bool TryParseVelocityFromUI(string inputText, out Vector3 velocity)
    {
        return ManualVelocityControlsView.TryParseVelocityFromUI(inputText, out velocity);
    }

    public void StageCircularOrbitVelocity()
    {
        CircularizeRequested?.Invoke();
    }

    public void StageRetrogradeCircularVelocity()
    {
        RetrogradeCircularizeRequested?.Invoke();
    }

    public void StageRaiseApogeeVelocity()
    {
        RaiseApogeeRequested?.Invoke();
    }

    public void StageLowerPerigeeVelocity()
    {
        LowerPerigeeRequested?.Invoke();
    }

    public void StageRadialOutVelocity()
    {
        RadialOutRequested?.Invoke();
    }

    public void StageRadialInVelocity()
    {
        RadialInRequested?.Invoke();
    }

    public void StageNormalVelocity()
    {
        TiltPositiveRequested?.Invoke();
    }

    public void StageAntiNormalVelocity()
    {
        TiltNegativeRequested?.Invoke();
    }

    public void SelectProgradeBase()
    {
        ProgradeRequested?.Invoke();
    }

    public void SelectRetrogradeBase()
    {
        RetrogradeRequested?.Invoke();
    }

    public void SelectRadialOutModifier()
    {
        RadialOutRequested?.Invoke();
    }

    public void SelectRadialInModifier()
    {
        RadialInRequested?.Invoke();
    }

    public void ClearRadialModifier()
    {
        ClearRadialRequested?.Invoke();
    }

    public void SelectTiltPositiveModifier()
    {
        TiltPositiveRequested?.Invoke();
    }

    public void SelectTiltNegativeModifier()
    {
        TiltNegativeRequested?.Invoke();
    }

    public void ClearTiltModifier()
    {
        ClearTiltRequested?.Invoke();
    }

    public void ClearOrbitShapeModifiers()
    {
        ClearOrbitShapeRequested?.Invoke();
    }

    public void CallApplyVelocity()
    {
        ApplyVelocityRequested?.Invoke();
    }

    public void ClearManualArtifacts()
    {
        ClearManualArtifactsRequested?.Invoke();
    }

    public void ResetVelocityManager()
    {
        ResetVelocityRequested?.Invoke();
    }
}
