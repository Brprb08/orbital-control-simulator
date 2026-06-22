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
    [SerializeField] private string previewHorizonLimitMessage = "Preview is approximate beyond 48 hours.";

    private bool allowNodeSlider = true;
    private float nextNodeSliderAllowed;
    private bool setupButtonResolved;
    private float nodeTimeSampleDelta;

    private NumericControlBinding nodeTimeControl;
    private NumericControlBinding burnDurationControl;
    private NumericControlBinding thrustScaleControl;

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
        SetupControls(defaultBurnDuration, defaultThrustScale);

        if (placeNodeButton != null)
            placeNodeButton.interactable = false;

        if (removeNodeButton != null)
            removeNodeButton.interactable = false;

        SetBurnTuningInteractable(false);
        SetNodeTimeSliderInteractable(false);
        SetSetupNodeButtonInteractable(true);
        ClearManeuverFeedback();
    }

    public void Dispose()
    {
        nodeTimeControl?.Dispose();
        burnDurationControl?.Dispose();
        thrustScaleControl?.Dispose();
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

    private void SetupControls(float defaultBurnDuration, float defaultThrustScale)
    {
        nodeTimeControl = new NumericControlBinding(
            slider: nodeTimeSlider,
            input: nodeTimeInputField,
            decreaseButton: nodeTimeDecreaseButton,
            increaseButton: nodeTimeIncreaseButton,
            label: null,
            decimals: TimeDecimalPlaces,
            suffix: string.Empty,
            stepProvider: () => 1f,
            inputToSlider: SecondsToNodeIndex,
            sliderToInput: NodeIndexToSeconds,
            quantize: seconds => QuantizeTimeSeconds(seconds, GetNodeTimeSampleDelta()),
            canEdit: () => allowNodeSlider,
            configureStepButton: ConfigureStepButton,
            onValueChanged: HandleNodeTimeControlChanged);
        nodeTimeControl.Initialize(0f, 0f, 1f, startInteractable: false, clearInput: true);

        burnDurationControl = new NumericControlBinding(
            slider: burnDurationSlider,
            input: burnDurationInputField,
            decreaseButton: burnDurationDecreaseButton,
            increaseButton: burnDurationIncreaseButton,
            label: burnDurationLabel,
            decimals: BurnDurationDecimalPlaces,
            suffix: " s",
            stepProvider: GetBurnDurationStep,
            inputToSlider: value => value,
            sliderToInput: value => value,
            quantize: QuantizeBurnDuration,
            canEdit: null,
            configureStepButton: ConfigureStepButton,
            onValueChanged: OnBurnDurationControlChanged);
        BurnDuration = Mathf.Clamp(defaultBurnDuration, minBurnDuration, maxBurnDuration);
        burnDurationControl.Initialize(BurnDuration, minBurnDuration, maxBurnDuration, startInteractable: true, clearInput: false);

        thrustScaleControl = new NumericControlBinding(
            slider: thrustScaleSlider,
            input: thrustScaleInputField,
            decreaseButton: thrustScaleDecreaseButton,
            increaseButton: thrustScaleIncreaseButton,
            label: thrustScaleLabel,
            decimals: ThrustScaleDecimalPlaces,
            suffix: "x",
            stepProvider: GetThrustScaleStep,
            inputToSlider: value => value,
            sliderToInput: value => value,
            quantize: QuantizeThrustScale,
            canEdit: null,
            configureStepButton: ConfigureStepButton,
            onValueChanged: OnThrustScaleControlChanged);
        ThrustScale = Mathf.Clamp(defaultThrustScale, minThrustScale, maxThrustScale);
        thrustScaleControl.Initialize(ThrustScale, minThrustScale, maxThrustScale, startInteractable: true, clearInput: false);
    }

    public void SetupNodeSlider(ManeuverNode node)
    {
        if (!allowNodeSlider || node == null || nodeTimeSlider == null)
            return;

        var trajectory = node.trajectorySnapshot;
        if (trajectory == null || trajectory.Count < 2)
            return;

        float dt = Mathf.Max(1e-5f, node.snapshotDeltaTime);
        float floatIndex = (node.burnTime - node.snapshotStartTime) / dt;
        nodeTimeSampleDelta = dt;

        nodeTimeControl.Initialize(
            value: Mathf.Clamp(floatIndex, 0f, trajectory.Count - 1),
            min: 0f,
            max: trajectory.Count - 1,
            startInteractable: true,
            clearInput: false);
    }

    public void SetNodeSliderValueWithoutNotify(float value)
    {
        nodeTimeControl?.SetValueWithoutNotify(value);
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
        nodeTimeControl?.Reset(clearInput: true);
        nodeTimeSampleDelta = 0f;

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
        nodeTimeControl?.SetInteractable(active);
    }

    public void SetPlaceButtonInteractable(bool active)
    {
        if (placeNodeButton != null)
            placeNodeButton.interactable = active;
    }

    public void SetBurnTuningInteractable(bool interactable)
    {
        burnDurationControl?.SetInteractable(interactable);
        thrustScaleControl?.SetInteractable(interactable);
    }

    private void HandleNodeTimeControlChanged(float sliderValue)
    {
        if (Time.unscaledTime < nextNodeSliderAllowed)
            return;

        nextNodeSliderAllowed = Time.unscaledTime + sliderUpdateMinInterval;
        NodeTimeSliderChanged?.Invoke(sliderValue);
    }

    private void OnBurnDurationControlChanged(float value)
    {
        BurnDuration = value;
        BurnDurationChanged?.Invoke(BurnDuration);
    }

    private void OnThrustScaleControlChanged(float value)
    {
        ThrustScale = value;
        ThrustScaleChanged?.Invoke(ThrustScale);
    }

    private float SecondsToNodeIndex(float seconds)
    {
        float sampleDt = GetNodeTimeSampleDelta();
        if (sampleDt <= 0f)
            return nodeTimeSlider != null ? nodeTimeSlider.value : 0f;

        return seconds / sampleDt;
    }

    private float NodeIndexToSeconds(float index)
    {
        return index * GetNodeTimeSampleDelta();
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

        repeatButton.SetTiming(
            stepButtonInitialDelay,
            stepButtonRepeatInterval,
            stepButtonFastRepeatInterval,
            stepButtonAccelerationDelay
        );
        repeatButton.Configure(action);
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

    public void ShowPreviewHorizonLimitFeedback()
    {
        SetManeuverFeedback(previewHorizonLimitMessage);
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

    private sealed class NumericControlBinding
    {
        private readonly Slider slider;
        private readonly TMP_InputField input;
        private readonly Button decreaseButton;
        private readonly Button increaseButton;
        private readonly TMP_Text label;
        private readonly int decimals;
        private readonly string suffix;
        private readonly Func<float> stepProvider;
        private readonly Func<float, float> inputToSlider;
        private readonly Func<float, float> sliderToInput;
        private readonly Func<float, float> quantize;
        private readonly Func<bool> canEdit;
        private readonly Action<Button, Action> configureStepButton;
        private readonly Action<float> onValueChanged;

        private bool suppressSync;
        private float value;

        public NumericControlBinding(
            Slider slider,
            TMP_InputField input,
            Button decreaseButton,
            Button increaseButton,
            TMP_Text label,
            int decimals,
            string suffix,
            Func<float> stepProvider,
            Func<float, float> inputToSlider,
            Func<float, float> sliderToInput,
            Func<float, float> quantize,
            Func<bool> canEdit,
            Action<Button, Action> configureStepButton,
            Action<float> onValueChanged)
        {
            this.slider = slider;
            this.input = input;
            this.decreaseButton = decreaseButton;
            this.increaseButton = increaseButton;
            this.label = label;
            this.decimals = decimals;
            this.suffix = suffix;
            this.stepProvider = stepProvider;
            this.inputToSlider = inputToSlider;
            this.sliderToInput = sliderToInput;
            this.quantize = quantize;
            this.canEdit = canEdit;
            this.configureStepButton = configureStepButton;
            this.onValueChanged = onValueChanged;
        }

        public void Initialize(float value, float min, float max, bool startInteractable, bool clearInput)
        {
            if (slider != null)
            {
                slider.wholeNumbers = false;
                slider.minValue = min;
                slider.maxValue = max;
                slider.onValueChanged.RemoveAllListeners();
                slider.SetValueWithoutNotify(Mathf.Clamp(value, min, max));
                slider.onValueChanged.AddListener(OnSliderChanged);
            }

            this.value = slider != null ? slider.value : value;

            if (input != null)
            {
                input.onValueChanged.RemoveListener(OnInputChanged);
                input.onEndEdit.RemoveListener(OnInputEndEdit);
                input.contentType = TMP_InputField.ContentType.DecimalNumber;
                input.onValidateInput = ValidateInputCharacter;
                input.SetTextWithoutNotify(clearInput ? string.Empty : FormatInputFromSliderValue(this.value));
                input.onValueChanged.AddListener(OnInputChanged);
                input.onEndEdit.AddListener(OnInputEndEdit);
            }

            configureStepButton?.Invoke(decreaseButton, OnDecreaseClicked);
            configureStepButton?.Invoke(increaseButton, OnIncreaseClicked);

            RefreshLabel();
            SetInteractable(startInteractable);
        }

        public void Dispose()
        {
            if (slider != null)
                slider.onValueChanged.RemoveListener(OnSliderChanged);

            if (input != null)
            {
                input.onValueChanged.RemoveListener(OnInputChanged);
                input.onEndEdit.RemoveListener(OnInputEndEdit);
            }

            ClearStepButton(decreaseButton);
            ClearStepButton(increaseButton);
        }

        public void Reset(bool clearInput)
        {
            if (slider != null)
            {
                slider.interactable = false;
                slider.onValueChanged.RemoveAllListeners();
            }

            if (input != null)
            {
                input.interactable = false;
                if (clearInput)
                    input.SetTextWithoutNotify(string.Empty);
            }

            SetButtonInteractable(decreaseButton, false);
            SetButtonInteractable(increaseButton, false);
        }

        public void SetInteractable(bool active)
        {
            bool interactable = active && (canEdit == null || canEdit());

            if (slider != null)
                slider.interactable = interactable;

            if (input != null)
                input.interactable = interactable;

            SetButtonInteractable(decreaseButton, interactable);
            SetButtonInteractable(increaseButton, interactable);
        }

        public void SetValueWithoutNotify(float nextValue)
        {
            if (slider != null)
            {
                slider.SetValueWithoutNotify(nextValue);
                value = slider.value;
            }
            else
            {
                value = nextValue;
            }

            SyncInput(force: false);
            RefreshLabel();
        }

        private void OnSliderChanged(float sliderValue)
        {
            if (suppressSync)
                return;

            SetCurrentValue(sliderValue, preserveTypedText: false, notify: true);
        }

        private void OnInputChanged(string text)
        {
            if (!CanRespondToInput(text, out float parsed))
                return;

            SetValueFromParsedInput(parsed, preserveTypedText: true);
        }

        private void OnInputEndEdit(string text)
        {
            if (!TryParseFloat(text, out float parsed))
            {
                SyncInput(force: true);
                return;
            }

            SetValueFromParsedInput(parsed, preserveTypedText: false);
        }

        private void OnDecreaseClicked()
        {
            Step(-1f);
        }

        private void OnIncreaseClicked()
        {
            Step(1f);
        }

        private void Step(float direction)
        {
            if (slider == null || (canEdit != null && !canEdit()))
                return;

            float step = stepProvider != null ? stepProvider() : 1f;
            float nextValue = Mathf.Clamp(
                slider.value + Mathf.Sign(direction) * step,
                slider.minValue,
                slider.maxValue
            );

            SetCurrentValue(nextValue, preserveTypedText: false, notify: true);
        }

        private bool CanRespondToInput(string text, out float parsed)
        {
            parsed = 0f;
            if (slider == null || (canEdit != null && !canEdit()))
                return false;

            return TryParseFloat(text, out parsed);
        }

        private void SetValueFromParsedInput(float parsedInputValue, bool preserveTypedText)
        {
            float inputValue = quantize != null ? quantize(parsedInputValue) : parsedInputValue;
            float sliderValue = inputToSlider != null ? inputToSlider(inputValue) : inputValue;

            if (slider != null)
                sliderValue = Mathf.Clamp(sliderValue, slider.minValue, slider.maxValue);

            SetCurrentValue(sliderValue, preserveTypedText, notify: true);
        }

        private void SetCurrentValue(float sliderValue, bool preserveTypedText, bool notify)
        {
            if (slider != null)
            {
                suppressSync = true;
                slider.SetValueWithoutNotify(sliderValue);
                suppressSync = false;
                value = slider.value;
            }
            else
            {
                value = sliderValue;
            }

            if (!preserveTypedText)
                SyncInput(force: true);

            RefreshLabel();

            if (notify)
                onValueChanged?.Invoke(value);
        }

        private void SyncInput(bool force)
        {
            if (input == null)
                return;

            if (!force && input.isFocused)
                return;

            input.SetTextWithoutNotify(FormatInputFromSliderValue(value));
        }

        private void RefreshLabel()
        {
            if (label != null)
                label.text = $"{FormatSliderValue(value)}{suffix}";
        }

        private string FormatInputFromSliderValue(float sliderValue)
        {
            float inputValue = sliderToInput != null ? sliderToInput(sliderValue) : sliderValue;
            return FormatFixedDecimals(inputValue, decimals);
        }

        private string FormatSliderValue(float sliderValue)
        {
            float displayValue = sliderToInput != null ? sliderToInput(sliderValue) : sliderValue;
            return FormatFixedDecimals(displayValue, decimals);
        }

        private char ValidateInputCharacter(string text, int charIndex, char addedChar)
        {
            return ValidateDecimalCharacter(text, charIndex, addedChar, decimals);
        }

        private static void SetButtonInteractable(Button button, bool interactable)
        {
            if (button != null)
                button.interactable = interactable;
        }

        private static void ClearStepButton(Button button)
        {
            if (button == null)
                return;

            HoldRepeatButton repeatButton = button.GetComponent<HoldRepeatButton>();
            repeatButton?.Clear();
        }
    }
}
