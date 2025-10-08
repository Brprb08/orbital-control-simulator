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
    public TutorialController tutorialController;
    private ICameraTracker cameraTracker;
    private ObjectPlacementManager objectPlacementManager;

    [Header("Buttons")]
    public Button freeCamButton;
    public Button trackCamButton;
    public Button instructionsButton;

    [Header("Panels")]
    public GameObject objectPlacementPanel;
    public GameObject objectInfoPanel;
    public GameObject thrustButtons;
    public GameObject maneuverNodePanel;
    public GameObject burnControlsPanel;
    public GameObject apogeePerigeePanel;
    public GameObject timeControlsPanel;
    public GameObject instructionsPanel;
    public GameObject toggleOptionsPanel;
    public GameObject dropdown;
    public GameObject placeTLEPanel;
    public GameObject placementSelectPanel;
    public GameObject cameraControls;

    public GameObject placeKeplerPanel;

    [Header("UI - Input Fields")]
    public TMP_InputField nameInputField;
    public TMP_InputField positionInputField;
    public TMP_InputField massInputField;
    public TMP_InputField radiusInputField;
    public TMP_InputField velocityInputField;

    [Header("UI - Buttons")]
    public Button placeObjectButton;
    public Button placementModeButton;
    public Button burnControlButton;

    [Header("UI - Text Displays")]
    public TMP_Text earthCamButtonText;
    public TextMeshProUGUI instructionText;
    public TextMeshProUGUI feedbackText;
    public TextMeshProUGUI apogeeText;
    public TextMeshProUGUI perigeeText;
    public TextMeshProUGUI semiMajorAxisText;
    public TextMeshProUGUI eccentricityText;
    public TextMeshProUGUI orbitalPeriodText;
    public TextMeshProUGUI inclinationText;
    public TextMeshProUGUI raanText;
    public TextMeshProUGUI deltaVText;

    [Header("UI Flags")]
    public bool showInstructionText = false;
    public bool isTracking = false;
    public bool earthCamPressed = true;

    [Header("Tutorial")]
    public GameObject tutorialPanel;
    public Button skipButton;

    private enum PlacementMode { Manual, TLE, Kepler }
    private enum ThrustUiMode { FreeThrust, ManeuverNodes }

    private PlacementMode placementMode = PlacementMode.Manual;
    private ThrustUiMode thrustUiMode = ThrustUiMode.FreeThrust;

    private System.Action<CameraMode> _onModeChangedHandler;
    private System.Action<NBody> _onTrackedBodyHandler;
    private System.Action<Transform> _onTrackedPlaceholderHandler;

    private SimContext ctx;

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        tutorialController = ctx.TutorialController;
        cameraTracker = ctx.CameraTracker;
        objectPlacementManager = ctx.ObjectPlacementManager;

        if (cameraTracker != null)
        {
            // subscribe to the single authoritative mode event
            _onModeChangedHandler = HandleModeChanged;
            cameraTracker.OnModeChanged += _onModeChangedHandler;

            // keep button coherence when tracking target changes
            _onTrackedBodyHandler = _ => EnsureTrackUiConsistency();
            _onTrackedPlaceholderHandler = _ => EnsureTrackUiConsistency();
            cameraTracker.OnTrackedBodyChanged += _onTrackedBodyHandler;
            cameraTracker.OnTrackedPlaceholderChanged += _onTrackedPlaceholderHandler;

            // initial paint based on current mode
            HandleModeChanged(cameraTracker.Mode);
        }

        if (instructionsPanel) instructionsPanel.SetActive(showInstructionText);
        if (cameraControls) cameraControls.SetActive(true);
        if (deltaVText) deltaVText.text = "";

        UpdateInstructionToggleButton();
    }

    private void OnDestroy()
    {
        if (cameraTracker != null)
        {
            if (_onModeChangedHandler != null) cameraTracker.OnModeChanged -= _onModeChangedHandler;
            if (_onTrackedBodyHandler != null) cameraTracker.OnTrackedBodyChanged -= _onTrackedBodyHandler;
            if (_onTrackedPlaceholderHandler != null) cameraTracker.OnTrackedPlaceholderChanged -= _onTrackedPlaceholderHandler;
        }
    }

    // --------- PUBLIC BUTTON HANDLERS (wire these in the Inspector) ----------

    /// <summary>Free Cam button</summary>
    public void OnFreeCamPressed()
    {
        cameraTracker?.BreakToFreeCam(); // UI will update via OnModeChanged
        EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>Track Cam button</summary>
    public void OnTrackCamPressed()
    {
        cameraTracker?.ReturnToTracking(); // UI will update via OnModeChanged
        EventSystem.current.SetSelectedGameObject(null);
    }

    public void ShowFeedbackPanel()
    {
        showInstructionText = !showInstructionText;
        UpdateInstructionToggleButton();
        if (instructionsPanel) instructionsPanel.SetActive(showInstructionText);
        EventSystem.current.SetSelectedGameObject(null);
    }

    public void SwitchPlacementMode()
    {
        placementMode = (PlacementMode)(((int)placementMode + 1) % 3);

        var txt = placementModeButton ? placementModeButton.GetComponentInChildren<TMP_Text>() : null;
        if (txt != null)
        {
            txt.text = placementMode switch
            {
                PlacementMode.Manual => "Mode: Cartesian  (next: TLE)",
                PlacementMode.TLE => "Mode: TLE       (next: Kepler)",
                PlacementMode.Kepler => "Mode: Kepler      (next: Cartesian)",
                _ => txt.text
            };
        }

        feedbackText.text = "";

        if (cameraTracker != null && cameraTracker.Mode == CameraMode.Free)
        {
            objectPlacementManager.ClearAllFields();
            ShowPlacePanels(true);
            ShowPlacementSelect(true);
        }
    }


    public void SwitchBurnMode()
    {
        thrustUiMode = (thrustUiMode == ThrustUiMode.FreeThrust) ? ThrustUiMode.ManeuverNodes : ThrustUiMode.FreeThrust;
        var txt = burnControlButton ? burnControlButton.GetComponentInChildren<TMP_Text>() : null;
        if (txt != null)
            txt.text = thrustUiMode == ThrustUiMode.FreeThrust ? "Use Maneuver Nodes" : "Use Free Thrust";

        if (cameraTracker != null && cameraTracker.Mode != CameraMode.Free)
            ShowThrustPanels(true);
    }

    // --------- CAMERA EVENT HANDLERS (authoritative state) ----------

    private void HandleModeChanged(CameraMode mode)
    {
        // Update EarthCam toggle label
        if (earthCamButtonText != null)
            earthCamButtonText.text = (mode == CameraMode.Earth) ? "Satellite Cam" : "Earth Cam";

        ApplyModeUi(mode);
    }

    // When tracking switches programmatically, keep buttons coherent in Track/Earth
    private void EnsureTrackUiConsistency()
    {
        if (cameraTracker != null && cameraTracker.Mode != CameraMode.Free)
        {
            SetButtonState(trackCamButton, true);
            if (trackCamButton) trackCamButton.interactable = false;
            if (freeCamButton) freeCamButton.interactable = true;
        }
    }

    // --------- CORE UI LAYOUT LOGIC ----------

    private void ApplyModeUi(CameraMode mode)
    {
        bool isFreeCam = (mode == CameraMode.Free);
        bool inEarthCam = (mode == CameraMode.Earth);

        // Buttons …
        SetButtonState(freeCamButton, isFreeCam);
        SetButtonState(trackCamButton, !isFreeCam);
        if (freeCamButton) freeCamButton.interactable = !isFreeCam;
        if (trackCamButton) trackCamButton.interactable = isFreeCam;

        // Instructions
        if (instructionText != null)
            instructionText.text = isFreeCam ? BuildFreeCamInstructions() : BuildTrackCamInstructions();

        if (isFreeCam)
        {
            // FreeCam panels …
            ShowPlacementSelect(true);
            ShowPlacePanels(true);
            ShowThrustPanels(false);
            ShowOrbitInfoPanel(false);
            ShowApogeePerigeePanel(false);
            ShowTimeControlsPanel(false);
            if (toggleOptionsPanel) toggleOptionsPanel.SetActive(false);
            if (dropdown) dropdown.SetActive(false);

            feedbackText.text = "";
            feedbackText.gameObject.SetActive(true);

        }
        else
        {
            // Track/Earth panels …
            ShowPlacementSelect(false);
            ShowPlacePanels(false);
            ShowThrustPanels(true);
            ShowOrbitInfoPanel(true);
            ShowApogeePerigeePanel(true);
            ShowTimeControlsPanel(true);
            objectPlacementManager.ClearAllFields();

            if (toggleOptionsPanel) toggleOptionsPanel.SetActive(true);
            if (dropdown) dropdown.SetActive(true);

            feedbackText.gameObject.SetActive(false);
        }

        if (cameraControls) cameraControls.SetActive(true);

        // Optional: placement button interactivity mirrors FreeCam
        if (placementModeButton) placementModeButton.interactable = isFreeCam;
    }

    private void ShowPlacementSelect(bool show)
    {
        if (placementSelectPanel) placementSelectPanel.SetActive(show);

        bool manual = placementMode == PlacementMode.Manual;

        if (velocityInputField) velocityInputField.interactable = false;

        if (show)
        {
            if (nameInputField) nameInputField.interactable = manual;
            if (positionInputField) positionInputField.interactable = manual;
            if (massInputField) massInputField.interactable = manual;
            if (radiusInputField) radiusInputField.interactable = manual;
            if (placeObjectButton) placeObjectButton.interactable = true;
        }
        else
        {
            if (nameInputField) { nameInputField.text = null; nameInputField.interactable = false; }
            if (positionInputField) { positionInputField.text = null; positionInputField.interactable = false; }
            if (massInputField) { massInputField.text = null; massInputField.interactable = false; }
            if (radiusInputField) { radiusInputField.text = null; radiusInputField.interactable = false; }
            if (placeObjectButton) placeObjectButton.interactable = false;
        }
    }

    // private void ShowPlacePanels(bool show)
    // {
    //     if (!placeTLEPanel || !objectPlacementPanel) return;

    //     if (!show)
    //     {
    //         placeTLEPanel.SetActive(false);
    //         objectPlacementPanel.SetActive(false);
    //         return;
    //     }

    //     if (placementMode == PlacementMode.Manual)
    //     {
    //         placeTLEPanel.SetActive(false);
    //         objectPlacementPanel.SetActive(true);
    //     }
    //     else
    //     {
    //         placeTLEPanel.SetActive(true);
    //         objectPlacementPanel.SetActive(false);
    //     }
    // }

    private void ShowPlacePanels(bool show)
    {
        if (!placeTLEPanel || !objectPlacementPanel || !placeKeplerPanel) return;

        if (!show)
        {
            placeTLEPanel.SetActive(false);
            objectPlacementPanel.SetActive(false);
            placeKeplerPanel.SetActive(false);
            return;
        }

        placeTLEPanel.SetActive(placementMode == PlacementMode.TLE);
        objectPlacementPanel.SetActive(placementMode == PlacementMode.Manual);   // “Cartesian”
        placeKeplerPanel.SetActive(placementMode == PlacementMode.Kepler);
    }

    private void ShowThrustPanels(bool show)
    {
        if (!thrustButtons || !maneuverNodePanel || !burnControlsPanel) return;

        burnControlsPanel.SetActive(show);

        if (!show)
        {
            thrustButtons.SetActive(false);
            maneuverNodePanel.SetActive(false);
            return;
        }

        if (thrustUiMode == ThrustUiMode.FreeThrust)
        {
            thrustButtons.SetActive(true);
            maneuverNodePanel.SetActive(false);
        }
        else
        {
            thrustButtons.SetActive(false);
            maneuverNodePanel.SetActive(true);
        }
    }

    // --------- SMALL HELPERS / DISPLAY FNS ----------

    private string BuildFreeCamInstructions() =>
        "<b>Free Cam Mode Activated!</b>\n\n" +
        "You can freely move to explore or place satellites.\n\n" +
        "\u00A0\u00A0\u00A0\u00A0<b>──────── CONTROLS ────────</b>\n" +
        "- WASD: Move around.\n" +
        "- Right Mouse Button: Rotate the camera.\n" +
        "- Esc Key: Closes the game.\n\n" +
        "\u00A0\u00A0\u00A0\u00A0<b>──────── PLACING A SATELLITE ────────</b>\n" +
        "- Naming is optional (defaults to 'Satellite (n)').\n" +
        "- Set Mass (500 - 1,000,000 kg).\n" +
        "- Set Radius (1-50).\n" +
        "  * Format: 5,45,3\n" +
        "  * No parentheses, negatives, or non-numeric characters.\n" +
        "- Click 'Place Satellite' to spawn.";

    private string BuildTrackCamInstructions() =>
        "<b>Track Cam Mode Activated!</b>\n\n" +
        "\u00A0\u00A0\u00A0\u00A0<b>──────── CONTROLS ────────</b>\n" +
        "- Dropdown Menu: Select the tracked object.\n" +
        "- Esc Key: Closes the game.\n" +
        "- Right Mouse Button: Rotate the camera.\n" +
        "- Mousewheel: Zoom in/out.\n" +
        "- Time Scaler: Adjust time speed (Reset: 'R').\n" +
        "- Earth Cam Button: Toggle 'Earth Cam' or 'Satellite Cam'.\n" +
        "     * Earth Cam: Centers the view on Earth.\n" +
        "     * Satellite Cam: Centers the view on the selected satellite.\n\n" +
        "\u00A0\u00A0\u00A0\u00A0<b>──────── THRUST ────────</b>\n" +
        "- Prograde / Retrograde: Speed up or slow down in orbit.\n" +
        "- Left / Right: Adjust lateral movement (changes inclination).\n" +
        "- Radial In / Radial Out: Thrust toward or away from the planet you're orbiting.\n\n" +
        "\u00A0\u00A0\u00A0\u00A0<b>──────── MANEUVER NODES ──────────</b>\n" +
        "- Select a burn type from the dropdown.\n" +
        "- Click 'Setup' to create a node.\n" +
        "- Use the slider to adjust burn timing.\n" +
        "- Click 'Place' to finalize the maneuver.\n\n" +
        "Switch to Free Cam to explore or place satellites.";

    private void UpdateInstructionToggleButton()
    {
        TMP_Text tmpButtonText = instructionsButton ? instructionsButton.GetComponentInChildren<TMP_Text>() : null;
        if (tmpButtonText != null)
            tmpButtonText.text = showInstructionText ? "Hide Instructions" : "Show Instructions";
    }

    private void SetButtonState(Button button, bool isPressed)
    {
        if (button == null) return;

        ColorBlock colors = button.colors;
        ColorUtility.TryParseHtmlString("#008CDB", out var newColor);
        colors.normalColor = newColor;
        button.colors = colors;

        // Force refresh (optional—can cause focus flicker if overused)
        button.Select();
        button.OnDeselect(null);
    }

    public void ShowApogeePerigeePanel(bool show)
    {
        if (apogeePerigeePanel != null)
            apogeePerigeePanel.SetActive(show);
    }

    private void ShowTimeControlsPanel(bool show)
    {
        if (timeControlsPanel != null)
            timeControlsPanel.SetActive(show);
    }

    private void ShowOrbitInfoPanel(bool show)
    {
        if (objectInfoPanel != null)
            objectInfoPanel.SetActive(show);
    }

    // --------- DATA DISPLAYS ----------

    public void UpdateOrbitUI(float apogee, float perigee, float semiMajorAxis, float eccentricity, float orbitalPeriod, float inclination, float RAAN)
    {
        SetText(apogeeText, "Apogee", apogee);
        SetText(perigeeText, "Perigee", perigee);
        SetText(semiMajorAxisText, "Semi-Major Axis", semiMajorAxis * 10f);
        SetText(eccentricityText, "Eccentricity", eccentricity, "", "F3");
        SetText(orbitalPeriodText, "Orbital Period", orbitalPeriod, "s");
        SetText(inclinationText, "Inclination", inclination, "°", "F1");
        SetText(raanText, "RAAN", RAAN, "°", "F1");
    }

    public void UpdateDeltaV(float deltaV)
    {
        if (deltaVText == null) return;
        if (deltaV != 0f)
            SetText(deltaVText, "DeltaV", deltaV * 1000, "m/s", "F3"); // km/s -> m/s
        else
            deltaVText.text = "";
    }

    private void SetText(TextMeshProUGUI textElement, string label, float value, string unit = "km", string format = "F0")
    {
        if (textElement != null)
            textElement.text = value >= 0 ? $"{label}: {value.ToString(format)} {unit}".Trim() : string.Empty;
    }

    public void OnSkipButtonPressed()
    {
        tutorialController.inTutorialMode = false;
        if (tutorialPanel != null) tutorialPanel.SetActive(false);
    }
}
