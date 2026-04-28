using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Globalization;
using System.Text;

public class ManeuverNodeUIController : MonoBehaviour
{
    [Header("Node UI")]
    public Slider nodeTimeSlider;
    public TMP_Dropdown burnDropdown;
    public Button placeNodeButton;
    public Button removeNodeButton;
    public Button setupNodeButton;

    [Header("Burn Tuning UI")]
    public Slider burnDurationSlider;
    public Slider thrustScaleSlider;
    public TMP_Text burnDurationLabel;
    public TMP_Text thrustScaleLabel;
    [SerializeField] private TMP_Text maneuverFeedbackText;

    [Header("Exact Input UI (Optional)")]
    [SerializeField] private TMP_InputField nodeTimeInputField;
    [SerializeField] private TMP_InputField burnDurationInputField;
    [SerializeField] private TMP_InputField thrustScaleInputField;

    [Header("Exact Input Step Buttons (Optional)")]
    [SerializeField] private Button nodeTimeDecreaseButton;
    [SerializeField] private Button nodeTimeIncreaseButton;
    [SerializeField] private Button burnDurationDecreaseButton;
    [SerializeField] private Button burnDurationIncreaseButton;
    [SerializeField] private Button thrustScaleDecreaseButton;
    [SerializeField] private Button thrustScaleIncreaseButton;

    [Header("Burn Duration Settings")]
    [SerializeField] private float minBurnDuration = 1f;
    [SerializeField] private float maxBurnDuration = 120f;
    [SerializeField] private float burnDurationStep = 0.01f;

    [Header("Thrust Scale Settings")]
    [SerializeField] private float minThrustScale = 0.1f;
    [SerializeField] private float maxThrustScale = 3f;
    [SerializeField] private float thrustScaleStep = 0.01f;

    [Header("UX Settings")]
    [SerializeField] private float sliderUpdateMinInterval = 0.02f;
    [SerializeField] private float stepButtonInitialDelay = 0.3f;
    [SerializeField] private float stepButtonRepeatInterval = 0.1f;
    [SerializeField] private float stepButtonFastRepeatInterval = 0.04f;
    [SerializeField] private float stepButtonAccelerationDelay = 0.8f;
    [SerializeField] private string setupNodePreviewMessage = "Set maneuver timing and thrust, then place node.";
    [SerializeField] private string setupNodeFinalizedMessage = "Remove node to place another, or wait for node to fire.";

    private bool allowNodeSlider = true;
    private float nextNodeSliderAllowed;
    private bool setupButtonResolved;
    private bool suppressUiSync;
    private float nodeTimeSampleDelta;

    private const int TimeDecimalPlaces = 2;
    private const int BurnDurationDecimalPlaces = 2;
    private const int ThrustScaleDecimalPlaces = 3;

    public float BurnDuration { get; private set; }
    public float ThrustScale { get; private set; }

    public event Action<float> NodeTimeSliderChanged;
    public event Action<float> BurnDurationChanged;
    public event Action<float> ThrustScaleChanged;

    public void Initialize(float defaultBurnDuration, float defaultThrustScale, bool allowNodeSlider)
    {
        this.allowNodeSlider = allowNodeSlider;
        nodeTimeSampleDelta = 0f;

        PopulateBurnDropdown();
        SetupNodeTimeSlider();
        SetupBurnDurationSlider(defaultBurnDuration);
        SetupThrustScaleSlider(defaultThrustScale);
        SetupStepButtons();

        UpdateBurnDurationLabel();
        UpdateThrustScaleLabel();

        if (placeNodeButton != null)
            placeNodeButton.interactable = false;

        if (removeNodeButton != null)
            removeNodeButton.interactable = false;

        SetBurnTuningInteractable(false);
        SetSetupNodeButtonInteractable(true);
        ClearManeuverFeedback();
    }

    public void Dispose()
    {
        if (nodeTimeSlider != null)
            nodeTimeSlider.onValueChanged.RemoveAllListeners();

        if (burnDurationSlider != null)
            burnDurationSlider.onValueChanged.RemoveListener(OnBurnDurationSliderChanged);

        if (thrustScaleSlider != null)
            thrustScaleSlider.onValueChanged.RemoveListener(OnThrustScaleSliderChanged);

        if (nodeTimeInputField != null)
            nodeTimeInputField.onValueChanged.RemoveListener(OnNodeTimeInputChanged);
        if (nodeTimeInputField != null)
            nodeTimeInputField.onEndEdit.RemoveListener(OnNodeTimeInputEndEdit);

        if (burnDurationInputField != null)
            burnDurationInputField.onValueChanged.RemoveListener(OnBurnDurationInputChanged);
        if (burnDurationInputField != null)
            burnDurationInputField.onEndEdit.RemoveListener(OnBurnDurationInputEndEdit);

        if (thrustScaleInputField != null)
            thrustScaleInputField.onValueChanged.RemoveListener(OnThrustScaleInputChanged);
        if (thrustScaleInputField != null)
            thrustScaleInputField.onEndEdit.RemoveListener(OnThrustScaleInputEndEdit);

        if (nodeTimeDecreaseButton != null)
            nodeTimeDecreaseButton.onClick.RemoveListener(OnNodeTimeDecreaseClicked);
        if (nodeTimeIncreaseButton != null)
            nodeTimeIncreaseButton.onClick.RemoveListener(OnNodeTimeIncreaseClicked);
        if (burnDurationDecreaseButton != null)
            burnDurationDecreaseButton.onClick.RemoveListener(OnBurnDurationDecreaseClicked);
        if (burnDurationIncreaseButton != null)
            burnDurationIncreaseButton.onClick.RemoveListener(OnBurnDurationIncreaseClicked);
        if (thrustScaleDecreaseButton != null)
            thrustScaleDecreaseButton.onClick.RemoveListener(OnThrustScaleDecreaseClicked);
        if (thrustScaleIncreaseButton != null)
            thrustScaleIncreaseButton.onClick.RemoveListener(OnThrustScaleIncreaseClicked);

        ClearStepButton(nodeTimeDecreaseButton);
        ClearStepButton(nodeTimeIncreaseButton);
        ClearStepButton(burnDurationDecreaseButton);
        ClearStepButton(burnDurationIncreaseButton);
        ClearStepButton(thrustScaleDecreaseButton);
        ClearStepButton(thrustScaleIncreaseButton);
    }

    private void PopulateBurnDropdown()
    {
        if (burnDropdown == null)
            return;

        burnDropdown.ClearOptions();

        var burnOptions = Enum.GetValues(typeof(BurnType));
        foreach (BurnType t in burnOptions)
            burnDropdown.options.Add(new TMP_Dropdown.OptionData(t.ToDisplayName()));
    }

    public BurnType GetBurnChoice()
    {
        if (burnDropdown == null)
            return BurnType.Prograde;

        return BurnTypeExtensions.FromDropdownIndex(burnDropdown.value);
    }

    private void SetupNodeTimeSlider()
    {
        if (nodeTimeSlider == null)
            return;

        nodeTimeSlider.interactable = false;
        nodeTimeSlider.onValueChanged.RemoveAllListeners();
        nodeTimeSampleDelta = 0f;

        if (nodeTimeInputField != null)
        {
            nodeTimeInputField.onValueChanged.RemoveListener(OnNodeTimeInputChanged);
            nodeTimeInputField.onEndEdit.RemoveListener(OnNodeTimeInputEndEdit);
            nodeTimeInputField.SetTextWithoutNotify(string.Empty);
            nodeTimeInputField.interactable = false;
            nodeTimeInputField.contentType = TMP_InputField.ContentType.DecimalNumber;
            nodeTimeInputField.onValidateInput = ValidateNodeTimeCharacter;
            nodeTimeInputField.onValueChanged.AddListener(OnNodeTimeInputChanged);
            nodeTimeInputField.onEndEdit.AddListener(OnNodeTimeInputEndEdit);
        }
    }

    private void SetupBurnDurationSlider(float defaultBurnDuration)
    {
        if (burnDurationSlider == null)
            return;

        burnDurationSlider.minValue = minBurnDuration;
        burnDurationSlider.maxValue = maxBurnDuration;
        burnDurationSlider.wholeNumbers = false;

        BurnDuration = Mathf.Clamp(defaultBurnDuration, minBurnDuration, maxBurnDuration);

        burnDurationSlider.onValueChanged.RemoveAllListeners();
        burnDurationSlider.SetValueWithoutNotify(BurnDuration);
        burnDurationSlider.onValueChanged.AddListener(OnBurnDurationSliderChanged);

        if (burnDurationInputField != null)
        {
            burnDurationInputField.onValueChanged.RemoveListener(OnBurnDurationInputChanged);
            burnDurationInputField.onEndEdit.RemoveListener(OnBurnDurationInputEndEdit);
            burnDurationInputField.SetTextWithoutNotify(FormatBurnDuration(BurnDuration));
            burnDurationInputField.contentType = TMP_InputField.ContentType.DecimalNumber;
            burnDurationInputField.onValidateInput = ValidateBurnDurationCharacter;
            burnDurationInputField.onValueChanged.AddListener(OnBurnDurationInputChanged);
            burnDurationInputField.onEndEdit.AddListener(OnBurnDurationInputEndEdit);
        }
    }

    private void SetupThrustScaleSlider(float defaultThrustScale)
    {
        if (thrustScaleSlider == null)
            return;

        thrustScaleSlider.minValue = minThrustScale;
        thrustScaleSlider.maxValue = maxThrustScale;
        thrustScaleSlider.wholeNumbers = false;

        ThrustScale = Mathf.Clamp(defaultThrustScale, minThrustScale, maxThrustScale);

        thrustScaleSlider.onValueChanged.RemoveAllListeners();
        thrustScaleSlider.SetValueWithoutNotify(ThrustScale);
        thrustScaleSlider.onValueChanged.AddListener(OnThrustScaleSliderChanged);

        if (thrustScaleInputField != null)
        {
            thrustScaleInputField.onValueChanged.RemoveListener(OnThrustScaleInputChanged);
            thrustScaleInputField.onEndEdit.RemoveListener(OnThrustScaleInputEndEdit);
            thrustScaleInputField.SetTextWithoutNotify(FormatThrustScale(ThrustScale));
            thrustScaleInputField.contentType = TMP_InputField.ContentType.DecimalNumber;
            thrustScaleInputField.onValidateInput = ValidateThrustScaleCharacter;
            thrustScaleInputField.onValueChanged.AddListener(OnThrustScaleInputChanged);
            thrustScaleInputField.onEndEdit.AddListener(OnThrustScaleInputEndEdit);
        }
    }

    private void SetupStepButtons()
    {
        if (nodeTimeDecreaseButton != null)
            ConfigureStepButton(nodeTimeDecreaseButton, OnNodeTimeDecreaseClicked);

        if (nodeTimeIncreaseButton != null)
            ConfigureStepButton(nodeTimeIncreaseButton, OnNodeTimeIncreaseClicked);

        if (burnDurationDecreaseButton != null)
            ConfigureStepButton(burnDurationDecreaseButton, OnBurnDurationDecreaseClicked);

        if (burnDurationIncreaseButton != null)
            ConfigureStepButton(burnDurationIncreaseButton, OnBurnDurationIncreaseClicked);

        if (thrustScaleDecreaseButton != null)
            ConfigureStepButton(thrustScaleDecreaseButton, OnThrustScaleDecreaseClicked);

        if (thrustScaleIncreaseButton != null)
            ConfigureStepButton(thrustScaleIncreaseButton, OnThrustScaleIncreaseClicked);
    }

    public void SetupNodeSlider(ManeuverNode node)
    {
        if (!allowNodeSlider || node == null || nodeTimeSlider == null)
            return;

        var trajectory = node.trajectorySnapshot;
        if (trajectory == null || trajectory.Count < 2)
            return;

        nodeTimeSlider.wholeNumbers = false;
        nodeTimeSlider.minValue = 0f;
        nodeTimeSlider.maxValue = trajectory.Count - 1;

        float dt = Mathf.Max(1e-5f, node.snapshotDeltaTime);
        float floatIndex = (node.burnTime - node.snapshotStartTime) / dt;
        nodeTimeSampleDelta = dt;

        nodeTimeSlider.onValueChanged.RemoveAllListeners();
        nodeTimeSlider.SetValueWithoutNotify(Mathf.Clamp(floatIndex, 0f, nodeTimeSlider.maxValue));
        nodeTimeSlider.onValueChanged.AddListener(HandleNodeTimeSliderChanged);
        nodeTimeSlider.interactable = true;

        SyncNodeTimeInputFromSlider();
    }

    public void SetNodeSliderValueWithoutNotify(float value)
    {
        if (nodeTimeSlider != null)
            nodeTimeSlider.SetValueWithoutNotify(value);

        SyncNodeTimeInputFromSlider();
    }

    public void SetEditingEnabled(bool enabled)
    {
        SetNodeTimeSliderInteractable(enabled);

        SetBurnTuningInteractable(enabled);

        if (placeNodeButton != null)
            placeNodeButton.interactable = enabled;

        if (removeNodeButton != null)
            removeNodeButton.interactable = true;
    }

    public void ResetEditingUI()
    {
        if (nodeTimeSlider != null)
        {
            nodeTimeSlider.interactable = false;
            nodeTimeSlider.onValueChanged.RemoveAllListeners();
        }

        nodeTimeSampleDelta = 0f;

        if (nodeTimeInputField != null)
        {
            nodeTimeInputField.interactable = false;
            nodeTimeInputField.SetTextWithoutNotify(string.Empty);
        }

        if (nodeTimeDecreaseButton != null)
            nodeTimeDecreaseButton.interactable = false;

        if (nodeTimeIncreaseButton != null)
            nodeTimeIncreaseButton.interactable = false;

        SetBurnTuningInteractable(false);

        if (placeNodeButton != null)
            placeNodeButton.interactable = false;

        if (removeNodeButton != null)
            removeNodeButton.interactable = false;
    }

    public void SetSetupNodeButtonInteractable(bool active)
    {
        SetSetupNodeButtonState(active, blockedByExistingNode: false);
    }

    public void SetSetupNodeButtonState(bool active, bool blockedByExistingNode)
    {
        Button button = ResolveSetupNodeButton();

        if (button != null)
        {
            button.interactable = active;

            // Clear keyboard/controller focus so the button cannot look selected
            // after transitioning into a blocked state.
            if (!active && UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject == button.gameObject)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }

    public void SetNodeTimeSliderInteractable(bool active)
    {
        if (nodeTimeSlider != null)
            nodeTimeSlider.interactable = active && allowNodeSlider;

        if (nodeTimeInputField != null)
            nodeTimeInputField.interactable = active && allowNodeSlider;

        if (nodeTimeDecreaseButton != null)
            nodeTimeDecreaseButton.interactable = active && allowNodeSlider;

        if (nodeTimeIncreaseButton != null)
            nodeTimeIncreaseButton.interactable = active && allowNodeSlider;
    }

    public void SetPlaceButtonInteractable(bool active)
    {
        if (placeNodeButton != null)
            placeNodeButton.interactable = active;
    }

    public void SetBurnTuningInteractable(bool interactable)
    {
        if (burnDurationSlider != null)
            burnDurationSlider.interactable = interactable;

        if (thrustScaleSlider != null)
            thrustScaleSlider.interactable = interactable;

        if (burnDurationInputField != null)
            burnDurationInputField.interactable = interactable;

        if (thrustScaleInputField != null)
            thrustScaleInputField.interactable = interactable;

        if (burnDurationDecreaseButton != null)
            burnDurationDecreaseButton.interactable = interactable;

        if (burnDurationIncreaseButton != null)
            burnDurationIncreaseButton.interactable = interactable;

        if (thrustScaleDecreaseButton != null)
            thrustScaleDecreaseButton.interactable = interactable;

        if (thrustScaleIncreaseButton != null)
            thrustScaleIncreaseButton.interactable = interactable;
    }

    private void HandleNodeTimeSliderChanged(float value)
    {
        if (suppressUiSync)
            return;

        SyncNodeTimeInputFromSlider();

        if (Time.unscaledTime < nextNodeSliderAllowed)
            return;

        nextNodeSliderAllowed = Time.unscaledTime + sliderUpdateMinInterval;
        NodeTimeSliderChanged?.Invoke(value);
    }

    private void OnBurnDurationSliderChanged(float value)
    {
        if (suppressUiSync)
            return;

        BurnDuration = value;
        UpdateBurnDurationLabel();
        SyncBurnDurationInput();
        BurnDurationChanged?.Invoke(BurnDuration);
    }

    private void OnThrustScaleSliderChanged(float value)
    {
        if (suppressUiSync)
            return;

        ThrustScale = value;
        UpdateThrustScaleLabel();
        SyncThrustScaleInput();
        ThrustScaleChanged?.Invoke(ThrustScale);
    }

    private void OnNodeTimeInputChanged(string value)
    {
        if (suppressUiSync || nodeTimeSlider == null || !allowNodeSlider)
            return;

        if (!TryParseFloat(value, out float seconds))
            return;

        float sampleDt = GetNodeTimeSampleDelta();
        if (sampleDt <= 0f)
            return;

        float quantizedSeconds = QuantizeTimeSeconds(seconds, sampleDt);
        float floatIndex = quantizedSeconds / sampleDt;
        float clampedIndex = Mathf.Clamp(floatIndex, nodeTimeSlider.minValue, nodeTimeSlider.maxValue);
        SetNodeSliderFromInput(clampedIndex, preserveTypedText: true);
    }

    private void OnBurnDurationInputChanged(string value)
    {
        if (suppressUiSync || burnDurationSlider == null)
            return;

        if (!TryParseFloat(value, out float parsed))
            return;

        float clamped = Mathf.Clamp(parsed, minBurnDuration, maxBurnDuration);
        clamped = QuantizeBurnDuration(clamped);
        SetBurnDurationFromInput(clamped, preserveTypedText: true);
    }

    private void OnThrustScaleInputChanged(string value)
    {
        if (suppressUiSync || thrustScaleSlider == null)
            return;

        if (!TryParseFloat(value, out float parsed))
            return;

        float clamped = Mathf.Clamp(parsed, minThrustScale, maxThrustScale);
        clamped = QuantizeThrustScale(clamped);
        SetThrustScaleFromInput(clamped, preserveTypedText: true);
    }

    private void OnNodeTimeInputEndEdit(string value)
    {
        if (nodeTimeInputField == null)
            return;

        if (!TryParseFloat(value, out float seconds))
        {
            SyncNodeTimeInputFromSlider(force: true);
            return;
        }

        float sampleDt = GetNodeTimeSampleDelta();
        if (sampleDt <= 0f)
        {
            SyncNodeTimeInputFromSlider(force: true);
            return;
        }

        float quantizedSeconds = QuantizeTimeSeconds(seconds, sampleDt);
        float floatIndex = Mathf.Clamp(quantizedSeconds / sampleDt, nodeTimeSlider.minValue, nodeTimeSlider.maxValue);
        SetNodeSliderFromInput(floatIndex, preserveTypedText: false);
    }

    private void OnBurnDurationInputEndEdit(string value)
    {
        if (burnDurationInputField == null)
            return;

        if (!TryParseFloat(value, out float parsed))
        {
            SyncBurnDurationInput(force: true);
            return;
        }

        float clamped = Mathf.Clamp(parsed, minBurnDuration, maxBurnDuration);
        clamped = QuantizeBurnDuration(clamped);
        SetBurnDurationFromInput(clamped, preserveTypedText: false);
    }

    private void OnThrustScaleInputEndEdit(string value)
    {
        if (thrustScaleInputField == null)
            return;

        if (!TryParseFloat(value, out float parsed))
        {
            SyncThrustScaleInput(force: true);
            return;
        }

        float clamped = Mathf.Clamp(parsed, minThrustScale, maxThrustScale);
        clamped = QuantizeThrustScale(clamped);
        SetThrustScaleFromInput(clamped, preserveTypedText: false);
    }

    private void OnNodeTimeDecreaseClicked()
    {
        StepNodeTime(-1f);
    }

    private void OnNodeTimeIncreaseClicked()
    {
        StepNodeTime(1f);
    }

    private void OnBurnDurationDecreaseClicked()
    {
        StepBurnDuration(-GetBurnDurationStep());
    }

    private void OnBurnDurationIncreaseClicked()
    {
        StepBurnDuration(GetBurnDurationStep());
    }

    private void OnThrustScaleDecreaseClicked()
    {
        StepThrustScale(-GetThrustScaleStep());
    }

    private void OnThrustScaleIncreaseClicked()
    {
        StepThrustScale(GetThrustScaleStep());
    }

    private void UpdateBurnDurationLabel()
    {
        if (burnDurationLabel != null)
            burnDurationLabel.text = $"{FormatBurnDuration(BurnDuration)} s";
    }

    private void UpdateThrustScaleLabel()
    {
        if (thrustScaleLabel != null)
            thrustScaleLabel.text = $"{FormatThrustScale(ThrustScale)}x";
    }

    private void SetNodeSliderFromInput(float value, bool preserveTypedText)
    {
        if (nodeTimeSlider == null)
            return;

        suppressUiSync = true;
        nodeTimeSlider.SetValueWithoutNotify(value);
        suppressUiSync = false;

        if (!preserveTypedText)
            SyncNodeTimeInputFromSlider(force: true);
        HandleNodeTimeSliderChanged(value);
    }

    private void StepNodeTime(float direction)
    {
        if (nodeTimeSlider == null || !allowNodeSlider)
            return;

        float nextValue = Mathf.Clamp(
            nodeTimeSlider.value + Mathf.Sign(direction),
            nodeTimeSlider.minValue,
            nodeTimeSlider.maxValue
        );

        SetNodeSliderFromInput(nextValue, preserveTypedText: false);
    }

    private void SetBurnDurationFromInput(float value, bool preserveTypedText)
    {
        if (burnDurationSlider == null)
            return;

        suppressUiSync = true;
        burnDurationSlider.SetValueWithoutNotify(value);
        suppressUiSync = false;

        BurnDuration = value;
        UpdateBurnDurationLabel();
        if (!preserveTypedText)
            SyncBurnDurationInput(force: true);
        BurnDurationChanged?.Invoke(BurnDuration);
    }

    private void StepBurnDuration(float delta)
    {
        if (burnDurationSlider == null)
            return;

        float nextValue = Mathf.Clamp(BurnDuration + delta, minBurnDuration, maxBurnDuration);
        nextValue = QuantizeBurnDuration(nextValue);
        SetBurnDurationFromInput(nextValue, preserveTypedText: false);
    }

    private void SetThrustScaleFromInput(float value, bool preserveTypedText)
    {
        if (thrustScaleSlider == null)
            return;

        suppressUiSync = true;
        thrustScaleSlider.SetValueWithoutNotify(value);
        suppressUiSync = false;

        ThrustScale = value;
        UpdateThrustScaleLabel();
        if (!preserveTypedText)
            SyncThrustScaleInput(force: true);
        ThrustScaleChanged?.Invoke(ThrustScale);
    }

    private void StepThrustScale(float delta)
    {
        if (thrustScaleSlider == null)
            return;

        float nextValue = Mathf.Clamp(ThrustScale + delta, minThrustScale, maxThrustScale);
        nextValue = QuantizeThrustScale(nextValue);
        SetThrustScaleFromInput(nextValue, preserveTypedText: false);
    }

    private void SyncNodeTimeInputFromSlider(bool force = false)
    {
        if (nodeTimeInputField == null || nodeTimeSlider == null)
            return;

        if (!force && nodeTimeInputField.isFocused)
            return;

        float seconds = nodeTimeSlider.value * GetNodeTimeSampleDelta();
        nodeTimeInputField.SetTextWithoutNotify(FormatNodeTime(seconds));
    }

    private void SyncBurnDurationInput(bool force = false)
    {
        if (burnDurationInputField == null)
            return;

        if (!force && burnDurationInputField.isFocused)
            return;

        burnDurationInputField.SetTextWithoutNotify(FormatBurnDuration(BurnDuration));
    }

    private void SyncThrustScaleInput(bool force = false)
    {
        if (thrustScaleInputField == null)
            return;

        if (!force && thrustScaleInputField.isFocused)
            return;

        thrustScaleInputField.SetTextWithoutNotify(FormatThrustScale(ThrustScale));
    }

    private float GetNodeTimeSampleDelta()
    {
        return Mathf.Max(0f, nodeTimeSampleDelta);
    }

    private float GetBurnDurationStep()
    {
        return Mathf.Max(Time.fixedDeltaTime, burnDurationStep);
    }

    private float GetThrustScaleStep()
    {
        float minStep = 1f / Mathf.Pow(10f, ThrustScaleDecimalPlaces);
        return Mathf.Max(minStep, thrustScaleStep);
    }

    private void ConfigureStepButton(Button button, Action action)
    {
        if (button == null || action == null)
            return;

        // Replace the click event entirely so old inspector-wired listeners
        // cannot double-fire alongside the hold-repeat handler.
        button.onClick = new Button.ButtonClickedEvent();

        HoldRepeatButton repeatButton = button.GetComponent<HoldRepeatButton>();
        if (repeatButton == null)
            repeatButton = button.gameObject.AddComponent<HoldRepeatButton>();

        ConfigureRepeatTiming(repeatButton);
        repeatButton.Configure(action);
    }

    private void ConfigureRepeatTiming(HoldRepeatButton repeatButton)
    {
        if (repeatButton == null)
            return;

        repeatButton.SetTiming(
            stepButtonInitialDelay,
            stepButtonRepeatInterval,
            stepButtonFastRepeatInterval,
            stepButtonAccelerationDelay
        );
    }

    private static void ClearStepButton(Button button)
    {
        if (button == null)
            return;

        HoldRepeatButton repeatButton = button.GetComponent<HoldRepeatButton>();
        repeatButton?.Clear();
    }

    private static bool TryParseFloat(string value, out float parsed)
    {
        parsed = 0f;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        return float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
               float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out parsed);
    }

    private static float QuantizeTimeSeconds(float seconds, float step)
    {
        if (step <= 0f)
            return seconds;

        return Mathf.Ceil(seconds / step) * step;
    }

    private static float QuantizeBurnDuration(float seconds)
    {
        float step = Mathf.Max(1e-5f, Time.fixedDeltaTime);
        return Mathf.Ceil(seconds / step) * step;
    }

    private static float QuantizeThrustScale(float scale)
    {
        float multiplier = Mathf.Pow(10f, ThrustScaleDecimalPlaces);
        return Mathf.Round(scale * multiplier) / multiplier;
    }

    private char ValidateNodeTimeCharacter(string text, int charIndex, char addedChar)
    {
        return ValidateDecimalCharacter(text, charIndex, addedChar, TimeDecimalPlaces);
    }

    private char ValidateBurnDurationCharacter(string text, int charIndex, char addedChar)
    {
        return ValidateDecimalCharacter(text, charIndex, addedChar, BurnDurationDecimalPlaces);
    }

    private char ValidateThrustScaleCharacter(string text, int charIndex, char addedChar)
    {
        return ValidateDecimalCharacter(text, charIndex, addedChar, ThrustScaleDecimalPlaces);
    }

    private static char ValidateDecimalCharacter(string text, int charIndex, char addedChar, int maxDecimals)
    {
        if (char.IsControl(addedChar))
            return addedChar;

        if (char.IsDigit(addedChar))
        {
            string decimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            char separator = decimalSeparator.Length > 0 ? decimalSeparator[0] : '.';
            int separatorIndex = text.IndexOf(separator);
            if (separatorIndex < 0)
                separatorIndex = text.IndexOf('.');

            if (separatorIndex >= 0 && charIndex > separatorIndex)
            {
                int decimalsExisting = text.Length - separatorIndex - 1;
                if (decimalsExisting >= maxDecimals)
                    return '\0';
            }

            return addedChar;
        }

        if (addedChar == '.' || addedChar == ',')
        {
            if (text.Contains(".") || text.Contains(","))
                return '\0';

            return CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0];
        }

        return '\0';
    }

    private static string FormatNodeTime(float seconds) => FormatFixedDecimals(seconds, TimeDecimalPlaces);
    private static string FormatBurnDuration(float seconds) => FormatFixedDecimals(seconds, BurnDurationDecimalPlaces);
    private static string FormatThrustScale(float scale) => FormatFixedDecimals(scale, ThrustScaleDecimalPlaces);

    private static string FormatFixedDecimals(float value, int decimals)
    {
        var builder = new StringBuilder("0");
        if (decimals > 0)
        {
            builder.Append('.');
            builder.Append('0', decimals);
        }

        return value.ToString(builder.ToString(), CultureInfo.InvariantCulture);
    }

    private Button ResolveSetupNodeButton()
    {
        if (setupButtonResolved)
            return setupNodeButton;

        setupButtonResolved = true;
        if (setupNodeButton != null)
            return setupNodeButton;

        Button[] buttons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button candidate = buttons[i];
            if (candidate == null || candidate == placeNodeButton || candidate == removeNodeButton)
                continue;

            TMP_Text label = candidate.GetComponentInChildren<TMP_Text>(true);
            if (label == null)
                continue;

            string text = label.text;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            text = text.Trim().ToLowerInvariant();
            if (text.Contains("setup"))
            {
                setupNodeButton = candidate;
                break;
            }
        }

        return setupNodeButton;
    }

    public void ShowPreviewManeuverFeedback()
    {
        SetManeuverFeedback(setupNodePreviewMessage);
    }

    public void ShowFinalizedManeuverFeedback()
    {
        SetManeuverFeedback(setupNodeFinalizedMessage);
    }

    public void ClearManeuverFeedback()
    {
        SetManeuverFeedback(string.Empty);
    }

    private void SetManeuverFeedback(string message)
    {
        if (maneuverFeedbackText != null)
            maneuverFeedbackText.text = message ?? string.Empty;
    }
}
