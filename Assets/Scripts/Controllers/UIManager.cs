using UnityEngine;
using UnityEngine.UI;
using TMPro;

/**
* UIManager class manages the user interface for switching between free camera and tracking camera modes.
* It controls the visibility of UI panels and highlights the active button.
**/
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Buttons")]
    public Button freeCamButton;
    public Button trackCamButton;
    public Button feedbackButton;

    [Header("Panels")]
    public GameObject objectPlacementPanel;
    public GameObject objectInfoPanel;
    public GameObject thrustButtons;
    public GameObject apogeePerigeePanel;
    public GameObject feedbackPanel;
    public GameObject toggleOptionsPanel;

    [Header("UI")]
    public TMP_InputField nameInputField;
    public TMP_InputField massInputField;
    public TMP_InputField radiusInputField;
    public Button placeObjectButton;
    public TextMeshProUGUI apogeeText;
    public TextMeshProUGUI perigeeText;

    public CameraController cameraController;
    public TMP_InputField velocityInputField;


    public TextMeshProUGUI feedbackText;
    public bool showFeedbackText = true;

    private bool isTracking = true;

    /**
    * Setup the singleton for accessing UIManager
    **/
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /**
    * Initializes the UI elements and sets the default button states.
    **/
    private void Start()
    {
        feedbackText.text =
    "Welcome to the Orbit Simulator!\n\n" +
    "You’re in Track Cam mode, following the planet’s orbit.\n" +
    "• Use Tab to switch the object you are tracking.\n" +
    "• Use right mouse button to rotate the camera around.\n" +
    "• Zoom in and out with mousewheel.\n" +
    "• Use the Time Scaler to speed up or slow down time, as well as reset with 'R'.\n" +
    "• Monitor Altitude and Velocity values to observe orbital behaviors.\n" +
    "• Switch to Free Cam to explore or place satellites.";
        ShowObjectPlacementPanel(false);
        ShowPanel(true);
        SetButtonState(freeCamButton, false);
        SetButtonState(trackCamButton, true);
        trackCamButton.Select();
        trackCamButton.interactable = false;

        UpdateButtonText();
    }

    /**
    * Handles the Free Cam button press event.
    **/
    public void OnFreeCamPressed()
    {
        feedbackText.text =
    "Free Cam Mode Activated!\n\n" +
    "• You can freely move to explore or place satellites.\n" +
    "• Use WASD to move around, and mouse button 2 to rotate camera.\n" +
    "• To place a satellite:\n" +
    "     • Naming is optional; defaults to \"Satellite (n)\".\n" +
    "     • Set the Mass (500 - 5.0e23 kg).\n" +
    "     • Set the Radius (1-50). Radius is set as follows: 5,45,3\n" +
    "     • Click \"Place Satellite\" to spawn it.";
        ShowObjectPlacementPanel(true);
        ShowPanel(false);
        SetButtonState(freeCamButton, true);
        SetButtonState(trackCamButton, false);
        ShowThrustButtonsPanel(false);
        ShowApogeePerigeePanel(false);

        freeCamButton.interactable = false;
        trackCamButton.interactable = true;

        isTracking = false;

        if (velocityInputField != null)
        {
            velocityInputField.interactable = false;
        }

        if (nameInputField != null && massInputField != null && radiusInputField != null)
        {
            nameInputField.interactable = true;

            massInputField.interactable = true;

            radiusInputField.interactable = true;

            placeObjectButton.interactable = true;
        }
    }

    /**
    * Handles the Track Cam button press event.
    **/
    public void OnTrackCamPressed()
    {
        feedbackText.text =
        "Track Cam Mode Activated!\n\n" +
        "• Use Tab to switch the object you are tracking.\n" +
    "• Use right mouse button to the rotate camera around.\n" +
    "• Zoom in and out with mousewheel.\n" +
    "• Use the Time Scaler to speed up or slow down time, as well as reset with 'R'.\n" +
    "• Monitor Altitude and Velocity values to observe orbital behaviors.\n" +
    "• Switch to Free Cam to explore or place satellites.";
        ShowObjectPlacementPanel(false);
        ShowPanel(true);
        SetButtonState(freeCamButton, false);
        SetButtonState(trackCamButton, true);
        ShowThrustButtonsPanel(true);
        ShowApogeePerigeePanel(true);

        trackCamButton.interactable = false;
        freeCamButton.interactable = true;

        isTracking = true;

        if (velocityInputField != null)
        {
            velocityInputField.interactable = false;
        }

        if (nameInputField != null && massInputField != null && radiusInputField != null)
        {
            nameInputField.text = null;
            nameInputField.interactable = false;

            massInputField.text = null;
            massInputField.interactable = false;

            radiusInputField.text = null;
            radiusInputField.interactable = false;

            placeObjectButton.interactable = false;
        }
    }

    /**
    * Toggles the visibility of the object placement panel.
    * @param showObjectPlacementPanel - Displays the object placement panel on canvas
    * @param ShowThrustButtonsPanel - Displays the Thrust buttons panel on canvas
    * @param showApogeePerigeePanel - Displays the Apogee Perigee panel on canvas
    * @param showPanel - Displays the Velocity/Altitude panel on canvas
    **/
    public void ShowSelectPanels(bool showObjectPlacementPanel, bool showThrustButtonsPanel)
    {
        // If were tracking and any of the booleans are false
        if (!showObjectPlacementPanel)
        {
            toggleOptionsPanel.SetActive(false);
            freeCamButton.interactable = false;
            trackCamButton.interactable = false;
        }
        else
        {
            toggleOptionsPanel.SetActive(true);
            if (isTracking)
            {
                freeCamButton.interactable = true;
            }
            else
            {
                trackCamButton.interactable = true;
            }
        }
        if (!freeCamButton.interactable)
        {
            ShowObjectPlacementPanel(showObjectPlacementPanel);
        }
        ShowThrustButtonsPanel(showThrustButtonsPanel);
    }

    /**
    * Toggles the visibility of the object placement panel.
    * @param show - True to show the panel, false to hide it.
    **/
    private void ShowObjectPlacementPanel(bool show)
    {
        objectPlacementPanel.SetActive(show);
    }

    /**
    * Toggles the visibility of the object placement panel.
    * @param show - True to show the panel, false to hide it.
    **/
    private void ShowThrustButtonsPanel(bool show)
    {
        thrustButtons.SetActive(show);
    }

    /**
    * Toggles the visibility of the apogee and perigee panel.
    * @param show - True to show the panel, false to hide it.
    **/
    public void ShowApogeePerigeePanel(bool show)
    {
        apogeePerigeePanel.SetActive(show);
    }

    /**
    * Toggles the visibility of the general UI panel.
    * @param show - True to show the panel, false to hide it.
    **/
    private void ShowPanel(bool show)
    {
        objectInfoPanel.SetActive(show);
    }

    public void ShowFeedbackPanel()
    {
        showFeedbackText = !showFeedbackText;
        UpdateButtonText(); // Update the button text when toggling
        feedbackPanel.SetActive(showFeedbackText);
    }

    private void UpdateButtonText()
    {
        TMP_Text tmpButtonText = feedbackButton.GetComponentInChildren<TMP_Text>();
        if (tmpButtonText != null)
        {
            tmpButtonText.text = showFeedbackText ? "Hide Instructions" : "Show Instructions";
        }
    }

    /**
    * Updates the visual state of a button.
    * @param button - The button to update.
    * @param isPressed - True if the button is active/pressed, false otherwise.
    **/
    private void SetButtonState(Button button, bool isPressed)
    {
        ColorBlock colors = button.colors;
        Color newColor;

        if (isPressed)
        {
            ColorUtility.TryParseHtmlString("#2A4E6C", out newColor); // Dark blue for active state.
        }
        else
        {
            ColorUtility.TryParseHtmlString("#1B2735", out newColor); // Purple for inactive state.
        }

        colors.normalColor = newColor;
        button.colors = colors;

        button.Select();
        button.OnDeselect(null); // Force the button to refresh its visual state.
    }
}
