using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class TimeController : MonoBehaviour
{
    [SerializeField] private UIRoot uiRoot;
    [SerializeField] private CameraController cameraController;

    [Header("Pause State")]
    private bool isPaused = false;
    private float previousTimeScale = 1.0f;

    private TutorialController tutorialController;
    private BodyRuntimeCoordinator bodyRuntimeCoordinator;
    private TimeUI timeUI;

    public void Initialize(SimContext ctx)
    {
        bodyRuntimeCoordinator = ctx.BodyRuntimeCoordinator;
        tutorialController = ctx.TutorialController;

        if (cameraController == null)
            cameraController = ctx.CameraController;

        if (uiRoot == null)
            uiRoot = ctx.UIRoot;

        timeUI = uiRoot.TimeUI;
        timeUI.Initialize(OnTimeScaleChanged, TogglePause);

        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;
        Application.targetFrameRate = 60;
    }

    private void Update()
    {
        if (EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.R))
            SetTimeScale(1.0f);
    }

    public void OnTimeScaleChanged(float newTimeScale)
    {
        SetTimeScale(newTimeScale);

        if (tutorialController != null && tutorialController.inTutorialMode)
            tutorialController.hasChangedTimeScale = true;
    }

    public void SetTimeScale(float scale)
    {
        previousTimeScale = scale;

        if (!isPaused)
        {
            Time.timeScale = scale;
            Time.fixedDeltaTime = 0.02f;
            timeUI?.SetTimeScaleText(scale);
        }

        timeUI?.SetSliderValue(scale);
    }

    public void TogglePause()
    {
        if (bodyRuntimeCoordinator != null && bodyRuntimeCoordinator.IsNodeBurnInProgress)
        {
            EventSystem.current?.SetSelectedGameObject(null);
            return;
        }

        if (isPaused) Resume();
        else Pause();

        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void Pause()
    {
        timeUI?.SetSliderInteractable(false);

        if (Time.timeScale > 0f)
            previousTimeScale = Time.timeScale;

        Time.timeScale = 0f;
        Time.fixedDeltaTime = 0.02f;

        uiRoot?.SetGameplayUiVisibleForPause(false);

        timeUI?.SetPausedLabel();
        timeUI?.SetPauseButtonText(true);

        isPaused = true;
    }

    private void Resume()
    {
        timeUI?.SetSliderInteractable(true);

        Time.timeScale = previousTimeScale;
        Time.fixedDeltaTime = 0.02f;

        uiRoot?.SetGameplayUiVisibleForPause(true);

        timeUI?.SetPauseButtonText(false);
        timeUI?.SetSliderValue(previousTimeScale);
        timeUI?.SetTimeScaleText(previousTimeScale);

        isPaused = false;
    }
}
