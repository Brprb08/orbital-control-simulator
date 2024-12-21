using UnityEngine;
using UnityEngine.UI; // For Slider
using TMPro;

public class TimeController : MonoBehaviour
{
    public Slider timeSlider; // Reference to the UI Slider
    public TextMeshProUGUI timeScaleText;
    public Button pauseButton;
    public TextMeshProUGUI pauseButtonText;
    private bool isPaused = false; // Track if the simulation is paused
    private float previousTimeScale = 1.0f;

    void Awake()
    {
        DontDestroyOnLoad(gameObject); // This object will persist between scene reloads
    }

    void Start()
    {
        // Set default time scale
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;

        // Ensure slider's value matches the default time scale
        if (timeSlider != null)
        {
            timeSlider.minValue = 1f; // Minimum speed (slow-motion)
            timeSlider.maxValue = 100f; // Maximum speed (fast-forward)
            timeSlider.value = Time.timeScale;

            // Add listener for slider value changes
            timeSlider.onValueChanged.AddListener(OnTimeScaleChanged);
        }

        Debug.Log($"Time scale set to {Time.timeScale}, fixedDeltaTime = {Time.fixedDeltaTime}");
    }

    void Update()
    {
        // Optional: Reset time scale with a key press
        if (Input.GetKeyDown(KeyCode.R))
        {
            SetTimeScale(1.0f); // Reset to normal speed
        }
    }

    public void OnTimeScaleChanged(float newTimeScale)
    {
        SetTimeScale(newTimeScale);
        if (timeScaleText != null)
        {
            timeScaleText.text = $"Time Scale: {newTimeScale:F1}x";
        }
    }

    private void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        Time.fixedDeltaTime = 0.02f * scale; // Keep physics consistent
        Debug.Log($"Time scale set to {Time.timeScale}, fixedDeltaTime = {Time.fixedDeltaTime}");
    }

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

    private void Pause()
    {
        previousTimeScale = Time.timeScale; // Save the current time scale
        Time.timeScale = 0f; // Pause the simulation
        isPaused = true;
        Debug.Log("Simulation Paused");
    }

    private void Resume()
    {
        Time.timeScale = previousTimeScale; // Restore the previous time scale
        Time.fixedDeltaTime = 0.02f * Time.timeScale; // Adjust fixedDeltaTime
        isPaused = false;
        Debug.Log("Simulation Resumed");
    }

    private void UpdatePauseButtonText()
    {
        if (pauseButtonText != null)
        {
            pauseButtonText.text = isPaused ? "Resume" : "Pause";
        }
    }
}