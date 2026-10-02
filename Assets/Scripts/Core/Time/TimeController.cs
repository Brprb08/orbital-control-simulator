using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class TimeController : MonoBehaviour
{
    private const float BaseFixedDeltaTime = BodyRuntimeCoordinator.BaseSimulationStep;
    // The editor rejects Time.timeScale above 100; the physics step carries extra warp.
    private const float MaxUnityTimeScale = 100f;

    [SerializeField] private UIRoot uiRoot;
    [SerializeField] private CameraController cameraController;

    [Header("Pause State")]
    private bool isPaused = false;
    private float previousTimeScale = 1.0f;

    private TutorialController tutorialController;
    private BodyRuntimeCoordinator bodyRuntimeCoordinator;
    private BodyService bodyService;
    private TimeUI timeUI;
    private float satelliteMaxTimeScale = 250f;
    private bool temporaryMaxTimeScaleActive;
    private float temporaryMaxTimeScale = float.PositiveInfinity;
    private float temporaryTimeScaleRestoreValue = 1f;
    private bool hasTemporaryTimeScaleRestoreValue;
    private bool planningHold;
    public bool IsPlanningHeld => planningHold;
    public float ActiveTimeScale => isPaused || planningHold ? 0f : previousTimeScale;
    public float SimulationStepSeconds => BaseFixedDeltaTime * previousTimeScale;

    public void Initialize(SimContext ctx)
    {
        if (bodyService != null)
            bodyService.MembershipChanged -= OnBodyMembershipChanged;

        bodyRuntimeCoordinator = ctx.BodyRuntimeCoordinator;
        tutorialController = ctx.TutorialController;
        bodyService = ctx.BodyService;

        if (cameraController == null)
            cameraController = ctx.CameraController;

        if (uiRoot == null)
            uiRoot = ctx.UIRoot;

        timeUI = uiRoot != null ? uiRoot.TimeUI : null;
        timeUI?.Initialize(OnTimeScaleChanged, TogglePause);

        previousTimeScale = 1f;
        Time.timeScale = 1f;
        ApplyFixedDeltaTimeForScale(Time.timeScale);

        if (bodyService != null)
            bodyService.MembershipChanged += OnBodyMembershipChanged;
        OnBodyMembershipChanged();

        Application.targetFrameRate = 60;
    }

    private void OnDestroy()
    {
        if (bodyService != null)
            bodyService.MembershipChanged -= OnBodyMembershipChanged;
    }

    public static float MaxTimeScaleForSatelliteCount(int satelliteCount)
    {
        if (satelliteCount <= 10) return 250f;
        if (satelliteCount <= 25) return 150f;
        if (satelliteCount <= 50) return 100f;
        if (satelliteCount < 300) return 75f;
        return 50f;
    }

    private void OnBodyMembershipChanged()
    {
        int satelliteCount = 0;
        if (bodyService != null)
        {
            foreach (NBody body in bodyService.Bodies)
            {
                if (body != null && !body.isCentralBody)
                    satelliteCount++;
            }
        }

        satelliteMaxTimeScale = MaxTimeScaleForSatelliteCount(satelliteCount);
        float allowedMax = EffectiveMaxTimeScale;
        if (previousTimeScale > allowedMax)
            SetTimeScale(allowedMax);

        timeUI?.SetMaxTimeScale(allowedMax);
    }

    private float EffectiveMaxTimeScale => temporaryMaxTimeScaleActive
        ? Mathf.Min(satelliteMaxTimeScale, temporaryMaxTimeScale)
        : satelliteMaxTimeScale;

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
        scale = Mathf.Min(scale, EffectiveMaxTimeScale);
        previousTimeScale = scale;

        if (!isPaused && !planningHold)
        {
            float unityScale = Mathf.Min(scale, MaxUnityTimeScale);
            Time.timeScale = unityScale;
            ApplyFixedDeltaTimeForScale(unityScale);
            timeUI?.SetTimeScaleText(scale);
        }

        timeUI?.SetSliderValue(scale);
    }

    // Keep the planning snapshot current without hiding the gameplay UI.
    public void BeginPlanningHold()
    {
        if (planningHold) return;
        planningHold = true;
        Time.timeScale = 0f;
        timeUI?.SetSliderInteractable(false);
        timeUI?.SetPauseButtonInteractable(false);
        timeUI?.SetPausedLabel();
    }

    public void EndPlanningHold()
    {
        if (!planningHold) return;
        planningHold = false;
        timeUI?.SetSliderInteractable(!isPaused);
        timeUI?.SetPauseButtonInteractable(true);
        if (!isPaused) SetTimeScale(previousTimeScale);
    }

    public bool BeginTemporaryMaxTimeScale(float maxScale)
    {
        if (!float.IsFinite(maxScale) || maxScale <= 0f)
            return false;

        float currentScale = previousTimeScale;
        bool reducedTimeScale = currentScale > maxScale;

        if (!temporaryMaxTimeScaleActive)
        {
            temporaryTimeScaleRestoreValue = currentScale;
            hasTemporaryTimeScaleRestoreValue = reducedTimeScale;
            temporaryMaxTimeScale = maxScale;
            temporaryMaxTimeScaleActive = true;
        }
        else
        {
            temporaryMaxTimeScale = Mathf.Min(temporaryMaxTimeScale, maxScale);
        }

        if (reducedTimeScale)
            SetTimeScale(maxScale);

        timeUI?.SetMaxTimeScale(EffectiveMaxTimeScale);

        return reducedTimeScale;
    }

    public void EndTemporaryMaxTimeScale()
    {
        if (!temporaryMaxTimeScaleActive)
            return;

        bool shouldRestore = hasTemporaryTimeScaleRestoreValue;
        float restoreValue = temporaryTimeScaleRestoreValue;

        temporaryMaxTimeScaleActive = false;
        temporaryMaxTimeScale = float.PositiveInfinity;
        temporaryTimeScaleRestoreValue = 1f;
        hasTemporaryTimeScaleRestoreValue = false;

        timeUI?.SetMaxTimeScale(EffectiveMaxTimeScale);

        if (shouldRestore)
            SetTimeScale(restoreValue);
    }

    public void TogglePause()
    {
        if (planningHold) return;
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

        Time.timeScale = 0f;
        ApplyFixedDeltaTimeForScale(Mathf.Min(previousTimeScale, MaxUnityTimeScale));

        uiRoot?.SetGameplayUiVisibleForPause(false);

        timeUI?.SetPausedLabel();
        timeUI?.SetPauseButtonText(true);

        isPaused = true;
    }

    private void Resume()
    {
        timeUI?.SetSliderInteractable(true);

        uiRoot?.SetGameplayUiVisibleForPause(true);

        timeUI?.SetPauseButtonText(false);
        isPaused = false;
        SetTimeScale(previousTimeScale);
    }

    private static void ApplyFixedDeltaTimeForScale(float scale)
    {
        Time.fixedDeltaTime = BaseFixedDeltaTime * Mathf.Max(0.0001f, scale);
    }
}
