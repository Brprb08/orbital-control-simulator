using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/**
* TimeController class manages the simulation time scale, pause/resume states, and user interface elements.
* It allows the user to control the time flow using a slider and pause button.
**/
public class TimeController : MonoBehaviour
{
    public Slider timeSlider;
    public TextMeshProUGUI timeScaleText;
    public Button pauseButton;
    public TextMeshProUGUI pauseButtonText;
    public UIManager uIManager;
    private bool isPaused = false;
    private float previousTimeScale = 1.0f; // Store the previous time scale before pausing.

    /**
    * Ensures this object persists between scene reloads.
    **/
    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }


    /**
    * Initializes the time scale and configures the UI elements.
    **/
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

        if (uIManager == null)
        {
            uIManager = GravityManager.Instance.GetComponent<UIManager>();
            if (uIManager == null)
            {
                Debug.LogError("TimeController: UIManager reference not set and not found on GravityManager.");
            }
        }

        Debug.Log($"Time scale set to {Time.timeScale}, fixedDeltaTime = {Time.fixedDeltaTime}");
    }

    /**
    * Checks for user input to reset the time scale.
    **/
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
    * @param newTimeScale - The new time scale value from the slider.
    **/
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
    * @param scale - The new time scale value.
    **/
    private void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        Time.fixedDeltaTime = 0.02f * scale;

        GravityManager gravityManager = GravityManager.Instance;
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
            Debug.LogWarning("GravityManager instance not found.");
        }
    }

    /**
    * Toggles the simulation between paused and resumed states.
    **/
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
                timeScaleText.text = $"Time Scale: Paused";
            }
            Pause();
        }
        UpdatePauseButtonText();
        EventSystem.current.SetSelectedGameObject(null);
    }

    /**
    * Pauses the simulation and saves the current time scale.
    **/
    private void Pause()
    {
        timeSlider.interactable = false;
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        uIManager.ShowSelectPanels(false, false);
        isPaused = true;
        Debug.Log("Simulation Paused");
    }

    /**
    * Resumes the simulation and restores the previous time scale.
    **/
    private void Resume()
    {
        timeSlider.interactable = true;
        Time.timeScale = previousTimeScale;
        if (timeScaleText != null)
        {
            timeScaleText.text = $"Time Scale: {previousTimeScale:F1}x";
        }
        Time.fixedDeltaTime = 0.02f * Time.timeScale;
        uIManager.ShowSelectPanels(true, true);
        isPaused = false;
        Debug.Log("Simulation Resumed");
    }

    /**
    * Updates the pause button text to reflect the current state.
    **/
    private void UpdatePauseButtonText()
    {
        if (pauseButtonText != null)
        {
            pauseButtonText.text = isPaused ? "Resume" : "Pause";
        }
    }
}