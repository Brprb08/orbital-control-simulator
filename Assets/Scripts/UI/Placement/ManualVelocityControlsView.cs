using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns manual velocity input, slider, visibility, and feedback text plumbing.
/// </summary>
public sealed class ManualVelocityControlsView
{
    private readonly TMP_InputField velocityInputField;
    private readonly Slider speedSlider;
    private readonly Button setVelocityButton;
    private readonly TextMeshProUGUI feedbackText;
    private readonly GameObject orbitIntentControlsRoot;
    private readonly float intentSpeedTrimRange;
    private readonly System.Action<float> speedChanged;
    private readonly System.Action<string> velocityTextChanged;
    private readonly System.Func<string> launchPreviewProvider;

    private bool capturedSpeedSliderBounds;
    private float defaultSpeedSliderMin;
    private float defaultSpeedSliderMax;
    private bool suppressSpeedSliderEvents;

    public ManualVelocityControlsView(
        TMP_InputField velocityInputField,
        Slider speedSlider,
        Button setVelocityButton,
        TextMeshProUGUI feedbackText,
        GameObject orbitIntentControlsRoot,
        float intentSpeedTrimRange,
        System.Action<float> speedChanged,
        System.Action<string> velocityTextChanged,
        System.Func<string> launchPreviewProvider)
    {
        this.velocityInputField = velocityInputField;
        this.speedSlider = speedSlider;
        this.setVelocityButton = setVelocityButton;
        this.feedbackText = feedbackText;
        this.orbitIntentControlsRoot = orbitIntentControlsRoot;
        this.intentSpeedTrimRange = intentSpeedTrimRange;
        this.speedChanged = speedChanged;
        this.velocityTextChanged = velocityTextChanged;
        this.launchPreviewProvider = launchPreviewProvider;
    }

    public void Initialize()
    {
        if (speedSlider != null)
        {
            CaptureSpeedSliderBounds();
            speedSlider.onValueChanged.AddListener(OnSpeedSliderChanged);
            speedSlider.interactable = false;
        }

        if (velocityInputField != null)
        {
            velocityInputField.onValueChanged.AddListener(OnVelocityInputChanged);
            velocityInputField.interactable = false;
        }

        if (setVelocityButton != null)
            setVelocityButton.interactable = false;
    }

    public void Dispose()
    {
        if (speedSlider != null)
            speedSlider.onValueChanged.RemoveListener(OnSpeedSliderChanged);

        if (velocityInputField != null)
            velocityInputField.onValueChanged.RemoveListener(OnVelocityInputChanged);
    }

    public void SetPendingInteractable(bool pending)
    {
        if (velocityInputField != null)
            velocityInputField.interactable = pending;

        if (speedSlider != null)
            speedSlider.interactable = pending;

        if (setVelocityButton != null)
            setVelocityButton.interactable = false;
    }

    public void SyncVelocityInput(Vector3 velocity)
    {
        if (velocityInputField == null)
            return;

        velocityInputField.onValueChanged.RemoveListener(OnVelocityInputChanged);
        velocityInputField.text = FormatVelocityForUI(velocity);
        velocityInputField.onValueChanged.AddListener(OnVelocityInputChanged);
        velocityInputField.interactable = true;
    }

    public void SyncSpeedSlider(bool useOrbitTrim, float trimScale, float sliderSpeed)
    {
        if (speedSlider == null)
            return;

        suppressSpeedSliderEvents = true;
        try
        {
            if (useOrbitTrim)
                ConfigureSpeedSliderForOrbitTrim();
            else
                RestoreDefaultSpeedSliderBounds();

            float sliderValue = useOrbitTrim ? trimScale : sliderSpeed;
            speedSlider.SetValueWithoutNotify(Mathf.Clamp(sliderValue, speedSlider.minValue, speedSlider.maxValue));
            speedSlider.interactable = true;
        }
        finally
        {
            suppressSpeedSliderEvents = false;
        }
    }

    public void SyncVelocityInputFromSlider(Vector3 velocity)
    {
        if (velocityInputField == null || velocity == Vector3.zero)
            return;

        velocityInputField.onValueChanged.RemoveListener(OnVelocityInputChanged);
        velocityInputField.text = FormatVelocityForUI(velocity);
        velocityInputField.onValueChanged.AddListener(OnVelocityInputChanged);
    }

    public void RefreshSetVelocityButton(bool canApply)
    {
        if (setVelocityButton != null)
            setVelocityButton.interactable = canApply;
    }

    public void SetVelocityControlsVisible(bool visible)
    {
        UIHelpers.SetActive(velocityInputField != null ? velocityInputField.gameObject : null, visible);
        UIHelpers.SetActive(setVelocityButton != null ? setVelocityButton.gameObject : null, visible);
        UIHelpers.SetActive(orbitIntentControlsRoot, visible);

        GameObject sliderRoot = null;
        if (speedSlider != null)
        {
            Transform sliderTransform = speedSlider.transform;
            sliderRoot = sliderTransform.parent != null && sliderTransform.parent.name == "Slider_Velocity"
                ? sliderTransform.parent.gameObject
                : sliderTransform.gameObject;
        }

        UIHelpers.SetActive(sliderRoot, visible);

        Transform panelRoot = velocityInputField != null ? velocityInputField.transform.parent : null;
        UIHelpers.SetChildActive(panelRoot, "Txt_Velocity", visible);
        UIHelpers.SetChildActive(panelRoot, "VelocityLabel", visible);
    }

    public void ResetVelocityControls()
    {
        UIHelpers.ClearInput(velocityInputField, clearSelection: false);
        UIHelpers.SetInteractable(velocityInputField, false);

        RestoreDefaultSpeedSliderBounds();

        if (speedSlider != null)
            speedSlider.SetValueWithoutNotify(0f);

        UIHelpers.SetInteractable(speedSlider, false);
        UIHelpers.SetInteractable(setVelocityButton, false);
    }

    public void SetFeedback(string message, bool appendLaunchPreview = true)
    {
        if (feedbackText != null)
            feedbackText.text = BuildVelocityFeedback(message, appendLaunchPreview);
    }

    public static string FormatVelocityForUI(Vector3 velocity)
    {
        return $"{(velocity.x * 10f):F2}, {(velocity.z * 10f):F2}, {(velocity.y * 10f):F2}";
    }

    public static bool TryParseVelocityFromUI(string inputText, out Vector3 velocity)
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

    private void OnSpeedSliderChanged(float value)
    {
        if (suppressSpeedSliderEvents)
            return;

        speedChanged?.Invoke(value);
    }

    private void OnVelocityInputChanged(string inputText)
    {
        velocityTextChanged?.Invoke(inputText);
    }

    private void CaptureSpeedSliderBounds()
    {
        if (speedSlider == null || capturedSpeedSliderBounds)
            return;

        defaultSpeedSliderMin = speedSlider.minValue;
        defaultSpeedSliderMax = speedSlider.maxValue;
        capturedSpeedSliderBounds = true;
    }

    private void ConfigureSpeedSliderForOrbitTrim()
    {
        if (speedSlider == null)
            return;

        CaptureSpeedSliderBounds();
        float range = Mathf.Max(0.05f, intentSpeedTrimRange);
        speedSlider.minValue = Mathf.Max(0.01f, 1f - range);
        speedSlider.maxValue = 1f + range;
    }

    private void RestoreDefaultSpeedSliderBounds()
    {
        if (speedSlider == null || !capturedSpeedSliderBounds)
            return;

        speedSlider.minValue = defaultSpeedSliderMin;
        speedSlider.maxValue = defaultSpeedSliderMax;
    }

    private string BuildVelocityFeedback(string message, bool appendLaunchPreview)
    {
        string baseMessage = message ?? string.Empty;

        if (!appendLaunchPreview)
            return baseMessage;

        string preview = launchPreviewProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(preview))
            return baseMessage;

        return string.IsNullOrWhiteSpace(baseMessage)
            ? preview
            : $"{baseMessage}\n{preview}";
    }
}
