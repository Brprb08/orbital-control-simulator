using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Manages the simulation's time flow, including time scale adjustment,
/// pause/resume functionality, and relevant UI elements.
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

    [Header("Pause State")]
    private bool isPaused = false;
    private float previousTimeScale = 1.0f; // Stores the previous time scale before pausing

    private GravityManager gravityManager;
    private SimContext ctx;

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.gravityManager = ctx.GravityManager;
        this.uIManager = ctx.UIManager;

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
    /// Checks for user input (reset time scale) and handles input lock during UI focus.
    /// </summary>
    void Update()
    {
        if (EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() != null)
        {
            return; // Don't allow WASD movement or camera control while typing.
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
    /// Called when the time scale slider value changes.
    /// Updates time scale and text label.
    /// </summary>
    /// <param name="newTimeScale">The new time scale value from the slider.</param>
    public void OnTimeScaleChanged(float newTimeScale)
    {
        SetTimeScale(newTimeScale);

        if (timeScaleText != null)
        {
            timeScaleText.text = $"{newTimeScale:F1}x";
        }
    }

    /// <summary>
    /// Sets the simulation time scale and updates related physics settings.
    /// </summary>
    /// <param name="scale">The new time scale to apply.</param>
    public void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        if (gravityManager != null)
        {
            foreach (NBody nBody in gravityManager.Bodies)
            {
                if (nBody != null)
                {
                    nBody.AdjustPredictionSettings(scale);
                }
            }
        }
        else
        {
            Debug.LogWarning("[TIME CONTROLLER]: GravityManager instance not found.");
        }
    }

    /// <summary>
    /// Toggles the simulation between paused and running states.
    /// Updates UI and simulation accordingly.
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
    /// Pauses the simulation and disables user input for the time slider.
    /// </summary>
    private void Pause()
    {
        timeSlider.interactable = false;
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        // FIX
        uIManager.objectPlacementPanel.SetActive(false);
        uIManager.thrustButtons.SetActive(false);
        uIManager.maneuverNodePanel.SetActive(false);
        uIManager.burnControlsPanel.SetActive(false);
        uIManager.cameraControls.SetActive(false);
        uIManager.toggleOptionsPanel.SetActive(false);
        uIManager.dropdown.SetActive(false);
        uIManager.placeTLEPanel.SetActive(false);
        uIManager.placementSelectPanel.SetActive(false);

        isPaused = true;
        Debug.Log("[TIME CONTROLLER]: Simulation Paused");
    }

    /// <summary>
    /// Resumes the simulation and restores the previous time scale.
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

        if (uIManager.isTracking)
        {
            uIManager.OnTrackCamPressed();
        }
        else
        {
            uIManager.OnFreeCamPressed();
        }
        uIManager.cameraControls.SetActive(true);

        isPaused = false;
        Debug.Log("[TIME CONTROLLER]: Simulation Resumed");
    }

    /// <summary>
    /// Updates the pause/resume button text based on current simulation state.
    /// </summary>
    private void UpdatePauseButtonText()
    {
        if (pauseButtonText != null)
        {
            pauseButtonText.text = isPaused ? "Resume" : "Pause";
        }
    }
}