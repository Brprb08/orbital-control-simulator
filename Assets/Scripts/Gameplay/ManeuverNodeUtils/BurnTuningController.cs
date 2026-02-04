using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class BurnTuningController : MonoBehaviour
{
    [Header("UI")]
    public Slider burnDurationSlider;
    public Slider thrustPowerSlider;
    public TMP_Text burnDurationLabel;
    public TMP_Text thrustPowerLabel;

    [Header("Defaults")]
    public float defaultBurnDuration = 20f;
    public float minBurnDuration = 1f;
    public float maxBurnDuration = 120f;

    public float defaultThrustScale = 1f;
    public float minThrustScale = 0.1f;
    public float maxThrustScale = 3f;

    public float BurnDuration { get; private set; }
    public float ThrustScale { get; private set; }

    public event Action<float> BurnDurationChanged;
    public event Action<float> ThrustScaleChanged;

    void Awake()
    {
        SetupBurnDurationSlider();
        SetupThrustPowerSlider();
        UpdateBurnDurationLabel();
        UpdateThrustPowerLabel();
    }

    void OnDestroy()
    {
        if (burnDurationSlider != null)
            burnDurationSlider.onValueChanged.RemoveListener(OnBurnDurationSliderChanged);

        if (thrustPowerSlider != null)
            thrustPowerSlider.onValueChanged.RemoveListener(OnThrustPowerSliderChanged);
    }

    void SetupBurnDurationSlider()
    {
        if (burnDurationSlider == null)
            return;

        burnDurationSlider.minValue = minBurnDuration;
        burnDurationSlider.maxValue = maxBurnDuration;
        burnDurationSlider.wholeNumbers = false;

        BurnDuration = defaultBurnDuration;
        burnDurationSlider.value = BurnDuration;

        burnDurationSlider.onValueChanged.RemoveAllListeners();
        burnDurationSlider.onValueChanged.AddListener(OnBurnDurationSliderChanged);
    }

    void SetupThrustPowerSlider()
    {
        if (thrustPowerSlider == null)
            return;

        thrustPowerSlider.minValue = minThrustScale;
        thrustPowerSlider.maxValue = maxThrustScale;
        thrustPowerSlider.wholeNumbers = false;

        ThrustScale = defaultThrustScale;
        thrustPowerSlider.value = ThrustScale;

        thrustPowerSlider.onValueChanged.RemoveAllListeners();
        thrustPowerSlider.onValueChanged.AddListener(OnThrustPowerSliderChanged);
    }

    void OnBurnDurationSliderChanged(float value)
    {
        BurnDuration = value;
        UpdateBurnDurationLabel();
        BurnDurationChanged?.Invoke(BurnDuration);
    }

    void OnThrustPowerSliderChanged(float value)
    {
        ThrustScale = value;
        UpdateThrustPowerLabel();
        ThrustScaleChanged?.Invoke(ThrustScale);
    }

    void UpdateBurnDurationLabel()
    {
        if (burnDurationLabel != null)
            burnDurationLabel.text = $"{BurnDuration:0.0} s";
    }

    void UpdateThrustPowerLabel()
    {
        if (thrustPowerLabel != null)
            thrustPowerLabel.text = $"{ThrustScale:0.00}x";
    }

    public void SetSlidersInteractable(bool interactable)
    {
        if (burnDurationSlider != null)
            burnDurationSlider.interactable = interactable;

        if (thrustPowerSlider != null)
            thrustPowerSlider.interactable = interactable;
    }
}
