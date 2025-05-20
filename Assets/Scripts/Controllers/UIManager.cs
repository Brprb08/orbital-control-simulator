using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Manages the user interface for switching between Free Cam and Track Cam modes.
/// Controls the visibility of panels and highlights active buttons.
/// </summary>
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
    public GameObject dropdown;

    [Header("UI")]
    public TMP_InputField nameInputField;
    public TMP_InputField positionInputField;
    public TMP_InputField massInputField;
    public TMP_InputField radiusInputField;
    public Button placeObjectButton;
    public TMP_InputField velocityInputField;
    public TMP_Text earthCamButtonText;
    public TextMeshProUGUI instructionText;
    public TextMeshProUGUI apogeeText;
    public TextMeshProUGUI perigeeText;
    public TextMeshProUGUI semiMajorAxisText;
    public TextMeshProUGUI eccentricityText;
    public TextMeshProUGUI orbitalPeriodText;
    public TextMeshProUGUI inclinationText;
    public TextMeshProUGUI raanText;

    [Header("UI Flags")]
    public bool showInstructionText = false;
    private bool isTracking = true;
    public bool earthCamPressed = true;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Initializes UI elements and sets default button states on startup.
    /// </summary>
    private void Start()
    {
        instructionText.text =
    "<b>Welcome to the Orbit Simulator!</b>\n" +
    "<b>Track Cam Mode Activated!</b>\n\n" +
    "<b>CONTROLS:</b>\n" +
    "- Dropdown Menu: Select the tracked object.\n" +
    "- Esc Key: Closes the game.\n" +
    "- Right Mouse Button: Rotate the camera.\n" +
    "- Mousewheel: Zoom in/out.\n" +
    "- Time Scaler: Adjust time speed (Reset: 'R').\n" +
    "- Earth Cam Button: Toggle 'Earth Cam' or 'Satellite Cam'.\n" +
    "     * Earth Cam: Centers the view on Earth.\n" +
    "     * Satellite Cam: Centers the view on the selected satellite.\n\n" +
    "<b>THRUST:</b>\n" +
    "- Prograde / Retrograde: Speed up or slow down in orbit.\n" +
    "- Left / Right: Adjust lateral movement (changes inclination).\n" +
    "- Radial In / Radial Out: Thrust toward or away from the planet you're orbiting.\n\n" +
    "Switch to Free Cam to explore or place satellites.";



        ShowObjectPlacementPanel(false);
        ShowPanel(true);
        SetButtonState(freeCamButton, false);
        SetButtonState(trackCamButton, true);
        trackCamButton.Select();
        trackCamButton.interactable = false;

        feedbackPanel.SetActive(showInstructionText);
        UpdateButtonText();
    }

    /// <summary>
    /// Handles the "Free Cam" button press event.
    /// Switches UI and controls into free camera placement mode.
    /// </summary>
    public void OnFreeCamPressed()
    {
        instructionText.text =
        "<b>Free Cam Mode Activated!</b>\n\n" +
        "You can freely move to explore or place satellites.\n\n" +
        "<b>CONTROLS:</b>\n" +
        "- WASD: Move around.\n" +
        "- Right Mouse Button: Rotate the camera.\n" +
        "- Esc Key: Closes the game.\n\n" +
        "<b>PLACING A SATELLITE:</b>\n" +
        "- Naming is optional (defaults to 'Satellite (n)').\n" +
        "- Set Mass (500 - 1,000,000 kg).\n" +
        "- Set Radius (1-50).\n" +
        "  * Format: 5,45,3\n" +
        "  * No parentheses, negatives, or non-numeric characters.\n" +
        "- Click 'Place Satellite' to spawn.";

        ShowObjectPlacementPanel(true);
        ShowPanel(false);
        SetButtonState(freeCamButton, true);
        SetButtonState(trackCamButton, false);
        ShowThrustButtonsPanel(false);
        ShowApogeePerigeePanel(false);

        toggleOptionsPanel.SetActive(false);
        dropdown.SetActive(false);

        freeCamButton.interactable = false;
        trackCamButton.interactable = true;

        isTracking = false;

        if (velocityInputField != null)
        {
            velocityInputField.interactable = false;
        }

        if (nameInputField != null && massInputField != null && radiusInputField != null && positionInputField != null)
        {
            nameInputField.interactable = true;

            positionInputField.interactable = true;

            massInputField.interactable = true;

            radiusInputField.interactable = true;

            placeObjectButton.interactable = true;
        }
        EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>
    /// Handles the "Track Cam" button press event.
    /// Switches UI and controls into tracking mode.
    /// </summary>
    public void OnTrackCamPressed()
    {
        instructionText.text =
    "<b>Track Cam Mode Activated!</b>\n\n" +
    "<b>CONTROLS:</b>\n" +
    "- Dropdown Menu: Select the tracked object.\n" +
    "- Esc Key: Closes the game.\n" +
    "- Right Mouse Button: Rotate the camera.\n" +
    "- Mousewheel: Zoom in/out.\n" +
    "- Time Scaler: Adjust time speed (Reset: 'R').\n" +
    "- Earth Cam Button: Toggle 'Earth Cam' or 'Satellite Cam'.\n" +
    "     * Earth Cam: Centers the view on Earth.\n" +
    "     * Satellite Cam: Centers the view on the selected satellite.\n\n" +
    "<b>THRUST:</b>\n" +
    "- Prograde / Retrograde: Speed up or slow down in orbit.\n" +
    "- Left / Right: Adjust lateral movement (changes inclination).\n" +
    "- Radial In / Radial Out: Thrust toward or away from the planet you're orbiting.\n\n" +
    "Switch to Free Cam to explore or place satellites.";


        ShowObjectPlacementPanel(false);
        ShowPanel(true);
        SetButtonState(freeCamButton, false);
        SetButtonState(trackCamButton, true);
        ShowThrustButtonsPanel(true);
        ShowApogeePerigeePanel(true);

        toggleOptionsPanel.SetActive(true);
        dropdown.SetActive(true);

        trackCamButton.interactable = false;
        freeCamButton.interactable = true;

        isTracking = true;

        if (velocityInputField != null)
        {
            velocityInputField.interactable = false;
        }

        if (nameInputField != null && massInputField != null && radiusInputField != null && positionInputField != null)
        {
            nameInputField.text = null;
            nameInputField.interactable = false;

            positionInputField.text = null;
            positionInputField.interactable = false;

            massInputField.text = null;
            massInputField.interactable = false;

            radiusInputField.text = null;
            radiusInputField.interactable = false;

            placeObjectButton.interactable = false;
        }
        EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>
    /// Updates the Earth Cam button text when toggled.
    /// </summary>
    public void OnEarthCamPressed()
    {
        if (earthCamPressed)
        {
            earthCamButtonText.text = "Satellite Cam";
            earthCamPressed = false;
        }
        else
        {
            earthCamButtonText.text = "Earth Cam";
            earthCamPressed = true;
        }
    }

    /// <summary>
    /// Shows or hides multiple panels depending on placement/tracking mode.
    /// </summary>
    /// <param name="showObjectPlacementPanel">Whether to show the object placement panel.</param>
    /// <param name="showThrustButtonsPanel">Whether to show the thrust buttons panel.</param>
    public void ShowSelectPanels(bool showObjectPlacementPanel, bool showThrustButtonsPanel, bool showDropdownSection)
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

        if (!showDropdownSection)
        {
            dropdown.SetActive(false);
        }
        else
        {
            dropdown.SetActive(true);
        }
    }

    /// <summary>
    /// Toggles the visibility of the object placement panel.
    /// </summary>
    /// <param name="show">True to show, false to hide.</param>
    private void ShowObjectPlacementPanel(bool show)
    {
        objectPlacementPanel.SetActive(show);
    }

    /// <summary>
    /// Toggles the visibility of the thrust buttons panel.
    /// </summary>
    /// <param name="show">True to show, false to hide.</param>
    private void ShowThrustButtonsPanel(bool show)
    {
        thrustButtons.SetActive(show);
    }

    /// <summary>
    /// Toggles the visibility of the apogee and perigee panel.
    /// </summary>
    /// <param name="show">True to show, false to hide.</param>
    public void ShowApogeePerigeePanel(bool show)
    {
        apogeePerigeePanel.SetActive(show);
    }

    /// <summary>
    /// Toggles the visibility of the general object info panel.
    /// </summary>
    /// <param name="show">True to show, false to hide.</param>
    private void ShowPanel(bool show)
    {
        objectInfoPanel.SetActive(show);
    }

    /// <summary>
    /// Toggles visibility of the feedback/instructions panel.
    /// </summary>
    public void ShowFeedbackPanel()
    {
        showInstructionText = !showInstructionText;
        UpdateButtonText(); // Update the button text when toggling
        feedbackPanel.SetActive(showInstructionText);
        EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>
    /// Updates the feedback button's text to reflect visibility state.
    /// </summary>
    private void UpdateButtonText()
    {
        TMP_Text tmpButtonText = feedbackButton.GetComponentInChildren<TMP_Text>();
        if (tmpButtonText != null)
        {
            tmpButtonText.text = showInstructionText ? "Hide Instructions" : "Show Instructions";
        }
    }

    /// <summary>
    /// Updates the visual state (color) of a button.
    /// </summary>
    /// <param name="button">The button to modify.</param>
    /// <param name="isPressed">True if the button is active/selected.</param>
    private void SetButtonState(Button button, bool isPressed)
    {
        ColorBlock colors = button.colors;
        Color newColor;

        if (isPressed)
        {
            ColorUtility.TryParseHtmlString("#008CDB", out newColor); // Dark blue for active state.
        }
        else
        {
            ColorUtility.TryParseHtmlString("#008CDB", out newColor); // Purple for inactive state.
        }

        colors.normalColor = newColor;
        button.colors = colors;

        button.Select();
        button.OnDeselect(null); // Force the button to refresh its visual state.
    }

    /// <summary>
    /// Updates orbit-related UI fields like apogee, perigee, and eccentricity.
    /// </summary>
    /// <param name="apogee">Apogee in km.</param>
    /// <param name="perigee">Perigee in km.</param>
    /// <param name="semiMajorAxis">Semi-major axis in km.</param>
    /// <param name="eccentricity">Orbital eccentricity (unitless).</param>
    /// <param name="orbitalPeriod">Orbital period in seconds.</param>
    /// <param name="inclination">Inclination in degrees.</param>
    /// <param name="RAAN">Right Ascension of Ascending Node in degrees.</param>
    public void UpdateOrbitUI(float apogee, float perigee, float semiMajorAxis, float eccentricity, float orbitalPeriod, float inclination, float RAAN)
    {
        SetText(apogeeText, "Apogee", apogee);
        SetText(perigeeText, "Perigee", perigee);
        SetText(semiMajorAxisText, "Semi-Major Axis", semiMajorAxis * 10f);
        SetText(eccentricityText, "Eccentricity", eccentricity, "", "F3");
        SetText(orbitalPeriodText, "Orbital Period", orbitalPeriod, "s");
        SetText(inclinationText, "Inclination", inclination, "°");
        SetText(raanText, "RAAN", RAAN, "°", "F1");
    }

    /// <summary>
    /// Sets formatted text to a UI element with optional unit and precision.
    /// </summary>
    /// <param name="textElement">UI element to update.</param>
    /// <param name="label">Label for the field ("Apogee").</param>
    /// <param name="value">Numerical value.</param>
    /// <param name="unit">Unit of measurement (default: "km").</param>
    /// <param name="format">String format (default: "F0").</param>
    private void SetText(TextMeshProUGUI textElement, string label, float value, string unit = "km", string format = "F0")
    {
        if (textElement != null)
            textElement.text = value >= 0 ? $"{label}: {value.ToString(format)} {unit}".Trim() : string.Empty;
    }
}
