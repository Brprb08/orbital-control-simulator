using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class ManeuverNodeUIController : MonoBehaviour
{
    [Header("Node UI")]
    public Slider nodeTimeSlider;
    public TMP_Dropdown burnDropdown;
    public Button placeNodeButton;
    public Button removeNodeButton;

    [Header("Burn Tuning UI")]
    public Slider burnDurationSlider;
    public Slider thrustScaleSlider;
    public TMP_Text burnDurationLabel;
    public TMP_Text thrustScaleLabel;

    [Header("Burn Duration Settings")]
    [SerializeField] private float minBurnDuration = 1f;
    [SerializeField] private float maxBurnDuration = 120f;

    [Header("Thrust Scale Settings")]
    [SerializeField] private float minThrustScale = 0.1f;
    [SerializeField] private float maxThrustScale = 3f;

    [Header("UX Settings")]
    [SerializeField] private float sliderUpdateMinInterval = 0.02f;

    private bool allowNodeSlider = true;
    private float nextNodeSliderAllowed;

    public float BurnDuration { get; private set; }
    public float ThrustScale { get; private set; }

    public event Action<float> NodeTimeSliderChanged;
    public event Action<float> BurnDurationChanged;
    public event Action<float> ThrustScaleChanged;

    public void Initialize(float defaultBurnDuration, float defaultThrustScale, bool allowNodeSlider)
    {
        this.allowNodeSlider = allowNodeSlider;

        PopulateBurnDropdown();
        SetupNodeTimeSlider();
        SetupBurnDurationSlider(defaultBurnDuration);
        SetupThrustScaleSlider(defaultThrustScale);

        UpdateBurnDurationLabel();
        UpdateThrustScaleLabel();

        if (placeNodeButton != null)
            placeNodeButton.interactable = false;

        if (removeNodeButton != null)
            removeNodeButton.interactable = false;

        SetBurnTuningInteractable(false);
    }

    public void Dispose()
    {
        if (nodeTimeSlider != null)
            nodeTimeSlider.onValueChanged.RemoveAllListeners();

        if (burnDurationSlider != null)
            burnDurationSlider.onValueChanged.RemoveListener(OnBurnDurationSliderChanged);

        if (thrustScaleSlider != null)
            thrustScaleSlider.onValueChanged.RemoveListener(OnThrustScaleSliderChanged);
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

        nodeTimeSlider.onValueChanged.RemoveAllListeners();
        nodeTimeSlider.SetValueWithoutNotify(Mathf.Clamp(floatIndex, 0f, nodeTimeSlider.maxValue));
        nodeTimeSlider.onValueChanged.AddListener(HandleNodeTimeSliderChanged);
        nodeTimeSlider.interactable = true;
    }

    public void SetNodeSliderValueWithoutNotify(float value)
    {
        if (nodeTimeSlider != null)
            nodeTimeSlider.SetValueWithoutNotify(value);
    }

    public void SetEditingEnabled(bool enabled)
    {
        if (nodeTimeSlider != null)
            nodeTimeSlider.interactable = enabled && allowNodeSlider;

        SetBurnTuningInteractable(enabled);

        if (placeNodeButton != null)
            placeNodeButton.interactable = enabled;

        if (removeNodeButton != null)
            removeNodeButton.interactable = enabled;
    }

    public void ResetEditingUI()
    {
        if (nodeTimeSlider != null)
        {
            nodeTimeSlider.interactable = false;
            nodeTimeSlider.onValueChanged.RemoveAllListeners();
        }

        SetBurnTuningInteractable(false);

        if (placeNodeButton != null)
            placeNodeButton.interactable = false;

        if (removeNodeButton != null)
            removeNodeButton.interactable = false;
    }

    public void SetNodeTimeSliderInteractable(bool active)
    {
        if (nodeTimeSlider != null)
            nodeTimeSlider.interactable = active && allowNodeSlider;
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
    }

    private void HandleNodeTimeSliderChanged(float value)
    {
        if (Time.unscaledTime < nextNodeSliderAllowed)
            return;

        nextNodeSliderAllowed = Time.unscaledTime + sliderUpdateMinInterval;
        NodeTimeSliderChanged?.Invoke(value);
    }

    private void OnBurnDurationSliderChanged(float value)
    {
        BurnDuration = value;
        UpdateBurnDurationLabel();
        BurnDurationChanged?.Invoke(BurnDuration);
    }

    private void OnThrustScaleSliderChanged(float value)
    {
        ThrustScale = value;
        UpdateThrustScaleLabel();
        ThrustScaleChanged?.Invoke(ThrustScale);
    }

    private void UpdateBurnDurationLabel()
    {
        if (burnDurationLabel != null)
            burnDurationLabel.text = $"{BurnDuration:0.0} s";
    }

    private void UpdateThrustScaleLabel()
    {
        if (thrustScaleLabel != null)
            thrustScaleLabel.text = $"{ThrustScale:0.00}x";
    }
}