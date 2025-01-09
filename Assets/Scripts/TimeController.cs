using UnityEngine;
using UnityEngine.UI; // For Slider
using TMPro;
using UnityEngine.EventSystems;

/**
 * TimeController class manages the simulation time scale, pause/resume states, and user interface elements.
 * It allows the user to control the time flow using a slider and pause button.
 */
public class TimeController : MonoBehaviour
{
    public Slider timeSlider; // Reference to the UI Slider for controlling time scale.
    public TextMeshProUGUI timeScaleText; // UI text displaying the current time scale.
    public Button pauseButton; // Button to pause or resume the simulation.
    public TextMeshProUGUI pauseButtonText; // Text component of the pause button.
    private bool isPaused = false; // Track if the simulation is paused.
    private float previousTimeScale = 1.0f; // Store the previous time scale before pausing.

    /**
     * Ensures this object persists between scene reloads.
     */
    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    /**
     * Initializes the time scale and configures the UI elements.
     */
    void Start()
    {
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;

        if (timeSlider != null)
        {
            timeSlider.minValue = 1f;
            timeSlider.maxValue = 100f;
            timeSlider.value = Time.timeScale;
            timeSlider.onValueChanged.AddListener(OnTimeScaleChanged);
        }

        Debug.Log($"Time scale set to {Time.timeScale}, fixedDeltaTime = {Time.fixedDeltaTime}");
    }

    /**
     * Checks for user input to reset the time scale.
     */
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
                timeScaleText.text = "Time Scale: 1.0x";
            }
        }
    }

    /**
     * Handles changes to the time scale from the slider.
     * @param newTimeScale The new time scale value from the slider.
     */
    public void OnTimeScaleChanged(float newTimeScale)
    {
        SetTimeScale(newTimeScale);

        if (timeScaleText != null)
        {
            timeScaleText.text = $"Time Scale: {newTimeScale:F1}x";
        }
    }

    /**
     * Sets the time scale and updates the physics time step.
     * @param scale The new time scale value.
     */
    private void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        Time.fixedDeltaTime = 0.02f * scale;
        Debug.Log($"Time scale set to {Time.timeScale}, fixedDeltaTime = {Time.fixedDeltaTime}");

        GravityManager gravityManager = GravityManager.Instance;
        if (gravityManager != null)
        {
            foreach (NBody nBody in gravityManager.Bodies)
            {
                if (nBody != null)
                {
                    nBody.AdjustPredictionSettings(scale, false);
                }
            }
        }
        else
        {
            Debug.LogWarning("GravityManager instance not found.");
        }
    }

    /**
     * Toggles the simulation between paused and resumed states.
     */
    public void TogglePause()
    {
        if (isPaused)
        {
            Resume();
        }
        else
        {
            Pause();
        }
        UpdatePauseButtonText();
    }

    /**
     * Pauses the simulation and saves the current time scale.
     */
    private void Pause()
    {
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        isPaused = true;
        Debug.Log("Simulation Paused");
    }

    /**
     * Resumes the simulation and restores the previous time scale.
     */
    private void Resume()
    {
        Time.timeScale = previousTimeScale;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;
        isPaused = false;
        Debug.Log("Simulation Resumed");
    }

    /**
     * Updates the pause button text to reflect the current state.
     */
    private void UpdatePauseButtonText()
    {
        if (pauseButtonText != null)
        {
            pauseButtonText.text = isPaused ? "Resume" : "Pause";
        }
    }
}