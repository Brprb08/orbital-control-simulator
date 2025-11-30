using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Controls simulation time flow (scaling, pause/resume) and keeps related UI in sync.
/// Applies physics step adjustments with time scaling and coordinates UI visibility on pause.
/// </summary>
public class TimeController : MonoBehaviour
{
    [Header("References - UI")]
    public Slider timeSlider;
    public TextMeshProUGUI timeScaleText;
    public Button pauseButton;
    public TextMeshProUGUI pauseButtonText;

    [Header("References - Scripts")]
    public UIManager uIManager;
    public CameraController cameraController;

    [Header("Pause State")]
    private bool isPaused = false;
    private float previousTimeScale = 1.0f;

    private BodyRuntimeCoordinator bodyRuntimeCoordinator;
    private TutorialController tutorialController;
    private SimContext ctx;

    /// <summary>
    /// Injects the simulation context, wires dependencies, and initializes default time settings and UI.
    /// </summary>
    /// <param name="ctx">Active simulation context.</param>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.bodyRuntimeCoordinator = ctx.BodyRuntimeCoordinator;
        this.tutorialController = ctx.TutorialController;
        this.uIManager = ctx.UIManager;
        this.cameraController = ctx.CameraController;

        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;
        Application.targetFrameRate = 60;

        if (timeSlider != null)
        {
            timeSlider.minValue = 1f;
            timeSlider.maxValue = 100f;
            timeSlider.value = Time.timeScale;
            timeSlider.onValueChanged.AddListener(OnTimeScaleChanged);
        }
    }

    /// <summary>
    /// Handles keyboard shortcuts and ignores input while typing into TMP input fields.
    /// </summary>
    void Update()
    {
        if (EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() != null)
        {
            return; // Suppress gameplay input while a text field is focused.
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            SetTimeScale(1.0f);

            if (timeSlider != null)
            {
                timeSlider.value = 1.0f;
            }

            if (timeScaleText != null)
            {
                timeScaleText.text = "1.0x";
            }
        }
    }

    /// <summary>
    /// Responds to time slider changes by applying the new scale and updating UI.
    /// Flags tutorial progress when appropriate.
    /// </summary>
    /// <param name="newTimeScale">Slider-selected time scale.</param>
    public void OnTimeScaleChanged(float newTimeScale)
    {
        SetTimeScale(newTimeScale);

        if (timeScaleText != null)
        {
            timeScaleText.text = $"{newTimeScale:F1}x";
        }

        if (tutorialController.inTutorialMode)
        {
            tutorialController.hasChangedTimeScale = true;
        }
    }

    /// <summary>
    /// Applies a simulation time scale and updates the physics timestep accordingly.
    /// </summary>
    /// <param name="scale">Target time scale.</param>
    public void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;
    }

    /// <summary>
    /// Toggles between paused and running states and updates related UI text.
    /// </summary>
    public void TogglePause()
    {
        if (isPaused)
        {
            Resume();
        }
        else
        {
            if (timeScaleText != null)
            {
                timeScaleText.text = $"Paused";
            }
            Pause();
        }

        UpdatePauseButtonText();
        EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>
    /// Pauses the simulation, disables time controls, and hides context-appropriate UI.
    /// </summary>
    private void Pause()
    {
        timeSlider.interactable = false;
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        // Hide in-simulation UI while paused (context-sensitive to camera mode and thrust UI).
        if (cameraController != null)
        {
            SetUIStateOnPause(false);
        }

        isPaused = true;
        Debug.Log("[TIME CONTROLLER]: Simulation Paused");
    }

    /// <summary>
    /// Resumes the simulation, restores time scale and physics step, and re-enables relevant UI.
    /// </summary>
    private void Resume()
    {
        timeSlider.interactable = true;
        Time.timeScale = previousTimeScale;

        if (timeScaleText != null)
        {
            timeScaleText.text = $"{previousTimeScale:F1}x";
        }

        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        if (cameraController != null)
        {
            SetUIStateOnPause(true);
        }

        uIManager.cameraControls.SetActive(true);

        isPaused = false;
        Debug.Log("[TIME CONTROLLER]: Simulation Resumed");
    }

    /// <summary>
    /// Shows or hides gameplay UI depending on camera mode and thrust UI state while paused/resumed.
    /// </summary>
    /// <param name="show">True to show UI; false to hide.</param>
    private void SetUIStateOnPause(bool show)
    {
        if (cameraController.Mode == CameraMode.Track || cameraController.Mode == CameraMode.Earth)
        {
            if (uIManager.ThrustMode == UIManager.ThrustUiMode.FreeThrust)
            {
                uIManager.thrustButtons.SetActive(show);
            }
            else
            {
                uIManager.maneuverNodePanel.SetActive(show);
            }

            uIManager.attitudeControlPanel.SetActive(show);
            uIManager.burnControlsPanel.SetActive(show);
            uIManager.toggleOptionsPanel.SetActive(show);
            uIManager.dropdown.SetActive(show);
        }
        else
        {
            uIManager.placeTLEPanel.SetActive(show);
            uIManager.placementSelectPanel.SetActive(show);
            uIManager.objectPlacementPanel.SetActive(show);
        }

        uIManager.cameraControls.SetActive(show);
    }

    /// <summary>
    /// Updates the pause button label to match the current state.
    /// </summary>
    private void UpdatePauseButtonText()
    {
        if (pauseButtonText != null)
        {
            pauseButtonText.text = isPaused ? "Resume" : "Pause";
        }
    }
}