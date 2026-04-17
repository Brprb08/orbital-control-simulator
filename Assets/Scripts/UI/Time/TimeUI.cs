using UnityEngine.Events;

public class TimeUI
{
    private readonly UIReferences refs;

    private UnityAction<float> onSliderChanged;
    private UnityAction onPauseClicked;

    public TimeUI(UIReferences refs)
    {
        this.refs = refs;
    }

    public void Initialize(UnityAction<float> onTimeScaleChanged, UnityAction onPausePressed)
    {
        onSliderChanged = onTimeScaleChanged;
        onPauseClicked = onPausePressed;

        if (refs.timeSlider != null)
        {
            refs.timeSlider.minValue = 1f;
            refs.timeSlider.maxValue = 100f;
            refs.timeSlider.SetValueWithoutNotify(1f);
            refs.timeSlider.onValueChanged.AddListener(onSliderChanged);
        }

        if (refs.pauseButton != null)
        {
            refs.pauseButton.onClick.AddListener(onPauseClicked);
        }

        SetSliderInteractable(true);
        SetTimeScaleText(1f);
        SetPauseButtonText(false);
    }

    public void Dispose()
    {
        if (refs.timeSlider != null && onSliderChanged != null)
            refs.timeSlider.onValueChanged.RemoveListener(onSliderChanged);

        if (refs.pauseButton != null && onPauseClicked != null)
            refs.pauseButton.onClick.RemoveListener(onPauseClicked);
    }

    public void SetSliderInteractable(bool interactable)
    {
        if (refs.timeSlider != null)
            refs.timeSlider.interactable = interactable;
    }

    public void SetPauseButtonInteractable(bool interactable)
    {
        if (refs.pauseButton != null)
            refs.pauseButton.interactable = interactable;
    }

    public void SetSliderValue(float value)
    {
        if (refs.timeSlider != null)
            refs.timeSlider.SetValueWithoutNotify(value);
    }

    public void SetTimeScaleText(float scale)
    {
        if (refs.timeScaleText != null)
            refs.timeScaleText.text = $"{scale:F1}x";
    }

    public void SetPausedLabel()
    {
        if (refs.timeScaleText != null)
            refs.timeScaleText.text = "Paused";
    }

    public void SetPauseButtonText(bool paused)
    {
        if (refs.pauseButtonText != null)
            refs.pauseButtonText.text = paused ? "Resume" : "Pause";
    }
}
