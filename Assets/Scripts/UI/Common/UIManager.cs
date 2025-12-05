using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Manages top-level UI state for camera modes, placement modes, thrust UIs, and instructional panels.
/// Subscribes to camera events, shows/hides panels, updates button visuals, and formats orbit readouts.
/// </summary>
public class UIManager : MonoBehaviour
{
    public TutorialController tutorialController;
    private ICameraTracker cameraTracker;
    private ObjectPlacementManager objectPlacementManager;
    public NBodyVectorOverlayController vectorOverlayController;

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
    public GameObject randomPlacementPanel;
    public GameObject cameraControls;
    public GameObject placeKeplerPanel;
    public GameObject confirmRemoveSatPanel;
    public GameObject attitudeControlPanel;

    [Header("UI - Input Fields")]
    public TMP_InputField nameInputField;
    public TMP_InputField positionInputField;
    public TMP_InputField massInputField;
    public TMP_InputField radiusInputField;
    public TMP_InputField velocityInputField;

    [Header("UI - Buttons")]
    public Button placeObjectButton;
    public Button placementModeButton;
    public Button randomSatelliteButton;
    public Button burnControlButton;
    public Button removePreManeuverLineButton;
    public Button vectorToggleButton;

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
    public TextMeshProUGUI meanAnomalyText;
    public TextMeshProUGUI deltaVText;
    public TextMeshProUGUI timeToPerigeeText;
    public TextMeshProUGUI timeToApogeeText;
    public TextMeshProUGUI vectorToggleButtonText;

    [Header("UI Flags")]
    public bool showInstructionText = false;
    public bool isTracking = false;
    public bool earthCamPressed = true;
    private bool _vectorsVisible = true;

    [Header("Tutorial")]
    public GameObject tutorialPanel;
    public Button skipButton;

    private enum PlacementMode { Manual, TLE, Kepler }
    public enum ThrustUiMode { FreeThrust, ManeuverNodes }

    private PlacementMode placementMode = PlacementMode.Manual;
    private ThrustUiMode thrustUiMode = ThrustUiMode.FreeThrust;
    public ThrustUiMode ThrustMode => thrustUiMode;

    private System.Action<CameraMode> _onModeChangedHandler;
    private System.Action<NBody> _onTrackedBodyHandler;
    private System.Action<Transform> _onTrackedPlaceholderHandler;

    private SimContext ctx;

    /// <summary>
    /// Injects dependencies, subscribes to camera events, and initializes initial UI state.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        tutorialController = ctx.TutorialController;
        cameraTracker = ctx.CameraTracker;
        objectPlacementManager = ctx.ObjectPlacementManager;

        if (cameraTracker != null)
        {
            _onModeChangedHandler = HandleModeChanged;
            cameraTracker.OnModeChanged += _onModeChangedHandler;

            _onTrackedBodyHandler = _ => HandleTrackedBodyChanged();
            _onTrackedPlaceholderHandler = _ => HandleTrackedBodyChanged();
            cameraTracker.OnTrackedBodyChanged += _onTrackedBodyHandler;
            cameraTracker.OnTrackedPlaceholderChanged += _onTrackedPlaceholderHandler;

            HandleModeChanged(cameraTracker.Mode);
        }

        if (instructionsPanel) instructionsPanel.SetActive(showInstructionText);
        if (cameraControls) cameraControls.SetActive(true);
        if (deltaVText) deltaVText.text = "";
        if (removePreManeuverLineButton) removePreManeuverLineButton.gameObject.SetActive(false);

        if (vectorToggleButton != null)
        {
            vectorToggleButton.onClick.AddListener(OnVectorTogglePressed);
        }

        if (vectorOverlayController != null)
        {
            _vectorsVisible = vectorOverlayController.showVectors;
        }

        UpdateInstructionToggleButton();
    }

    /// <summary>
    /// Unsubscribes from camera events on destruction.
    /// </summary>
    private void OnDestroy()
    {
        if (cameraTracker != null)
        {
            if (_onModeChangedHandler != null) cameraTracker.OnModeChanged -= _onModeChangedHandler;
            if (_onTrackedBodyHandler != null) cameraTracker.OnTrackedBodyChanged -= _onTrackedBodyHandler;
            if (_onTrackedPlaceholderHandler != null) cameraTracker.OnTrackedPlaceholderChanged -= _onTrackedPlaceholderHandler;
        }
    }

    // --------- PUBLIC BUTTON HANDLERS ----------

    /// <summary>
    /// Switches to Free Cam; UI updates via camera mode event.
    /// </summary>
    public void OnFreeCamPressed()
    {
        cameraTracker?.BreakToFreeCam();
        EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>
    /// Switches back to tracking mode; UI updates via camera mode event.
    /// </summary>
    public void OnTrackCamPressed()
    {
        cameraTracker?.ReturnToTracking();
        EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>
    /// Toggles the instructions panel and updates the toggle button label.
    /// </summary>
    public void ShowFeedbackPanel()
    {
        showInstructionText = !showInstructionText;
        UpdateInstructionToggleButton();
        if (instructionsPanel) instructionsPanel.SetActive(showInstructionText);
        EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>
    /// Cycles placement mode (Manual ↔ TLE ↔ Kepler), updates labels, and shows relevant panels in Free Cam.
    /// </summary>
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
            if (objectPlacementManager != null)
            {
                objectPlacementManager.ClearAllFields();
            }
            ShowPlacePanels(true);
            ShowPlacementSelect(true);
        }
    }

    /// <summary>
    /// Toggles thrust UI between Free Thrust and Maneuver Nodes; shows relevant panels when not in Free Cam.
    /// </summary>
    public void SwitchBurnMode()
    {
        thrustUiMode = (thrustUiMode == ThrustUiMode.FreeThrust) ? ThrustUiMode.ManeuverNodes : ThrustUiMode.FreeThrust;
        var txt = burnControlButton ? burnControlButton.GetComponentInChildren<TMP_Text>() : null;
        if (txt != null)
            txt.text = thrustUiMode == ThrustUiMode.FreeThrust ? "Use Maneuver Nodes" : "Use Free Thrust";

        if (cameraTracker != null && cameraTracker.Mode != CameraMode.Free)
            ShowThrustPanels(true);
    }

    // --------- CAMERA EVENT HANDLERS ----------

    /// <summary>
    /// Handles camera mode changes and reapplies the UI layout.
    /// </summary>
    private void HandleModeChanged(CameraMode mode)
    {
        if (earthCamButtonText != null)
            earthCamButtonText.text = (mode == CameraMode.Earth) ? "Satellite Cam" : "Earth Cam";

        placementModeButton.interactable = mode == CameraMode.Free;

        ApplyModeUi(mode);
    }

    private void HandleTrackedBodyChanged()
    {
        if (removePreManeuverLineButton) removePreManeuverLineButton.gameObject.SetActive(false);
        EnsureTrackUiConsistency();
    }
    /// <summary>
    /// Ensures button interactivity reflects tracking state when targets change.
    /// </summary>
    private void EnsureTrackUiConsistency()
    {
        if (cameraTracker != null && cameraTracker.Mode != CameraMode.Free)
        {
            SetButtonState(trackCamButton, true);
            if (trackCamButton) trackCamButton.interactable = false;
            if (freeCamButton) freeCamButton.interactable = true;
        }
    }

    /// <summary>
    /// Applies panel visibility, instructions text, and button states for the given camera mode.
    /// </summary>
    private void ApplyModeUi(CameraMode mode)
    {
        bool isFreeCam = (mode == CameraMode.Free);
        bool inEarthCam = (mode == CameraMode.Earth);

        SetButtonState(freeCamButton, isFreeCam);
        SetButtonState(trackCamButton, !isFreeCam);
        if (freeCamButton) freeCamButton.interactable = !isFreeCam;
        if (trackCamButton) trackCamButton.interactable = isFreeCam;

        if (instructionText != null)
            instructionText.text = isFreeCam ? BuildFreeCamInstructions() : BuildTrackCamInstructions();

        if (isFreeCam)
        {
            ShowPlacementSelect(true);
            ShowPlacePanels(true);
            ShowThrustPanels(false);
            ShowOrbitInfoPanel(false);
            ShowApogeePerigeePanel(false);
            ShowTimeControlsPanel(false);
            attitudeControlPanel.SetActive(false);
            if (toggleOptionsPanel) toggleOptionsPanel.SetActive(false);
            if (confirmRemoveSatPanel) confirmRemoveSatPanel.SetActive(false);
            if (dropdown) dropdown.SetActive(false);
            if (removePreManeuverLineButton) removePreManeuverLineButton.gameObject.SetActive(false);

            if (feedbackText != null)
            {
                feedbackText.text = "";
                feedbackText.gameObject.SetActive(true);
            }
        }
        else
        {
            ShowPlacementSelect(false);
            ShowPlacePanels(false);
            ShowThrustPanels(true);
            ShowOrbitInfoPanel(true);
            ShowApogeePerigeePanel(true);
            ShowTimeControlsPanel(true);
            attitudeControlPanel.SetActive(true);
            if (objectPlacementManager != null)
            {
                objectPlacementManager.ClearAllFields();
            }

            if (toggleOptionsPanel) toggleOptionsPanel.SetActive(true);
            if (dropdown) dropdown.SetActive(true);

            if (feedbackText != null)
                feedbackText.gameObject.SetActive(false);
        }

        if (cameraControls) cameraControls.SetActive(true);

        if (placementModeButton) placementModeButton.interactable = isFreeCam;
        if (randomSatelliteButton) randomSatelliteButton.interactable = isFreeCam;
        if (vectorToggleButton) vectorToggleButton.gameObject.SetActive(!isFreeCam);
    }

    /// <summary>
    /// Shows/hides the placement selection group and toggles field interactivity based on mode.
    /// </summary>
    private void ShowPlacementSelect(bool show)
    {
        if (placementSelectPanel) placementSelectPanel.SetActive(show);
        if (randomPlacementPanel) randomPlacementPanel.SetActive(show);

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

    /// <summary>
    /// Switches between Manual, TLE, and Kepler placement subpanels.
    /// </summary>
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
        objectPlacementPanel.SetActive(placementMode == PlacementMode.Manual);
        placeKeplerPanel.SetActive(placementMode == PlacementMode.Kepler);
    }

    /// <summary>
    /// Shows either Free Thrust controls or Maneuver Node controls depending on the current thrust UI mode.
    /// </summary>
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

    // --------- HELPERS / DISPLAY ----------

    /// <summary>
    /// Returns the instructional text for Free Cam mode.
    /// </summary>
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

    /// <summary>
    /// Returns the instructional text for Track/Earth Cam modes.
    /// </summary>
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

    /// <summary>
    /// Updates the label for the Instructions toggle button.
    /// </summary>
    private void UpdateInstructionToggleButton()
    {
        TMP_Text tmpButtonText = instructionsButton ? instructionsButton.GetComponentInChildren<TMP_Text>() : null;
        if (tmpButtonText != null)
            tmpButtonText.text = showInstructionText ? "Hide Instructions" : "Show Instructions";
    }

    /// <summary>
    /// Applies a pressed-color style to a button and forces a light refresh.
    /// </summary>
    private void SetButtonState(Button button, bool isPressed)
    {
        if (button == null) return;

        ColorBlock colors = button.colors;
        ColorUtility.TryParseHtmlString("#008CDB", out var newColor);
        colors.normalColor = newColor;
        button.colors = colors;

        button.Select();
        button.OnDeselect(null);
    }

    /// <summary>
    /// Shows or hides the Apogee/Perigee panel.
    /// </summary>
    public void ShowApogeePerigeePanel(bool show)
    {
        if (apogeePerigeePanel != null)
            apogeePerigeePanel.SetActive(show);
    }

    /// <summary>
    /// Shows or hides the time controls panel.
    /// </summary>
    private void ShowTimeControlsPanel(bool show)
    {
        if (timeControlsPanel != null)
            timeControlsPanel.SetActive(show);
    }

    /// <summary>
    /// Shows or hides the orbit info panel.
    /// </summary>
    private void ShowOrbitInfoPanel(bool show)
    {
        if (objectInfoPanel != null)
            objectInfoPanel.SetActive(show);
    }

    // --------- DATA DISPLAYS ----------

    /// <summary>
    /// Updates orbit statistics text fields with formatted values.
    /// </summary>
    public void UpdateOrbitUI(float apogee, float perigee, float semiMajorAxis, float eccentricity, float orbitalPeriod, float inclination, float RAAN, float meanAnomaly, float timeToPerigee, float timeToApogee)
    {
        SetText(apogeeText, "Apogee", apogee);
        SetText(perigeeText, "Perigee", perigee);
        SetText(semiMajorAxisText, "Semi-Major Axis", semiMajorAxis * 10f);
        SetText(eccentricityText, "Eccentricity", eccentricity, "", "F3");
        SetText(orbitalPeriodText, "Orbital Period", orbitalPeriod, "s");
        SetText(inclinationText, "Inclination", inclination, "°", "F1");
        SetText(raanText, "RAAN", RAAN, "°", "F1");
        SetText(meanAnomalyText, "Mean Anomaly", meanAnomaly, "rad", "F2");

        var (valPeri, unitPeri) = TimeFormatUtils.GetBestTimeUnit(timeToPerigee);
        var (valApo, unitApo) = TimeFormatUtils.GetBestTimeUnit(timeToApogee);
        SetText(timeToPerigeeText, "Time to Perigee", valPeri, unitPeri, "F2");
        SetText(timeToApogeeText, "Time to Apogee", valApo, unitApo, "F2");
    }

    /// <summary>
    /// Updates Delta-V readout (km/s → m/s) or clears it when zero.
    /// </summary>
    public void UpdateDeltaV(float deltaV)
    {
        if (deltaVText == null) return;
        if (deltaV != 0f)
            SetText(deltaVText, "DeltaV", deltaV * 1000, "m/s", "F3");
        else
            deltaVText.text = "";
    }

    /// <summary>
    /// Helper to format and assign label/value text with an optional unit.
    /// </summary>
    private void SetText(TextMeshProUGUI textElement, string label, float value, string unit = "km", string format = "F0")
    {
        if (textElement != null)
            textElement.text = value >= 0 ? $"{label}: {value.ToString(format)} {unit}".Trim() : string.Empty;
    }

    /// <summary>
    /// Skips the tutorial and hides the panel.
    /// </summary>
    public void OnSkipButtonPressed()
    {
        tutorialController.inTutorialMode = false;
        if (tutorialPanel != null) tutorialPanel.SetActive(false);
    }

    /// <summary>
    /// Toggles all orbit vector lines on/off via the NBodyVectorOverlayController.
    /// </summary>
    public void OnVectorTogglePressed()
    {
        if (vectorOverlayController == null)
            return;

        vectorOverlayController.ToggleFromUI();

        _vectorsVisible = vectorOverlayController.showVectors;

        UpdateVectorToggleButtonLabel();

        EventSystem.current.SetSelectedGameObject(null);
    }

    private void UpdateVectorToggleButtonLabel()
    {
        if (vectorToggleButton == null) return;

        if (vectorToggleButtonText == null) return;

        vectorToggleButtonText.text = _vectorsVisible ? "Hide Vectors" : "Show Vectors";
    }
}
