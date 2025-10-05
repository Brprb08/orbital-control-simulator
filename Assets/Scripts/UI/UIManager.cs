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

    private enum PlacementMode { Manual, TLE }
    private enum ThrustUiMode { FreeThrust, ManeuverNodes }

    private PlacementMode placementMode = PlacementMode.Manual;
    private ThrustUiMode thrustUiMode = ThrustUiMode.FreeThrust;

    private SimContext ctx;

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        tutorialController = ctx.TutorialController;
        cameraTracker = ctx.CameraTracker;
        objectPlacementManager = ctx.ObjectPlacementManager;

        // Subscribe to camera state – single source of truth
        if (cameraTracker != null)
        {
            cameraTracker.OnEarthViewChanged += HandleEarthViewChanged;
            cameraTracker.OnFreeModeChanged += HandleFreeModeChanged;
            cameraTracker.OnTrackedBodyChanged += _ => EnsureTrackUiConsistency();
            cameraTracker.OnTrackedPlaceholderChanged += _ => EnsureTrackUiConsistency();
        }

        // Base UI state (assume starting in Track Cam)
        ApplyModeUi(isFreeCam: cameraTracker?.IsFree == true, inEarthCam: cameraTracker?.IsEarthView == true);

        instructionsPanel.SetActive(showInstructionText);
        cameraControls.SetActive(true);
        deltaVText.text = "";

        UpdateInstructionToggleButton();
    }

    private void OnDestroy()
    {
        if (cameraTracker != null)
        {
            cameraTracker.OnEarthViewChanged -= HandleEarthViewChanged;
            cameraTracker.OnFreeModeChanged -= HandleFreeModeChanged;
            cameraTracker.OnTrackedBodyChanged -= _ => EnsureTrackUiConsistency();
            cameraTracker.OnTrackedPlaceholderChanged -= _ => EnsureTrackUiConsistency();
        }
    }

    // --------- PUBLIC BUTTON HANDLERS (wire these in the Inspector) ----------

    /// <summary>Free Cam button</summary>
    public void OnFreeCamPressed()
    {
        // Tell camera to switch; UI updates will come from events
        cameraTracker?.BreakToFreeCam();
        // The rest of the UI (panels) is controlled by HandleFreeModeChanged
        EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>Track Cam button</summary>
    public void OnTrackCamPressed()
    {
        // Return to tracking prior target (body/placeholder)
        cameraTracker?.ReturnToTracking();
        EventSystem.current.SetSelectedGameObject(null);
    }

    public void ShowFeedbackPanel()
    {
        showInstructionText = !showInstructionText;
        UpdateInstructionToggleButton();
        instructionsPanel.SetActive(showInstructionText);
        EventSystem.current.SetSelectedGameObject(null);
    }

    // public void SwitchPlacementMode()
    // {
    //     placementMode = (placementMode == PlacementMode.Manual) ? PlacementMode.TLE : PlacementMode.Manual;
    //     var placementModeButtonText = placementModeButton.GetComponentInChildren<TMP_Text>();
    //     if (placementModeButtonText != null)
    //         placementModeButtonText.text = placementMode == PlacementMode.Manual ? "Switch to TLE Input" : "Switch to Manual Input";

    //     // Only visible in FreeCam
    //     if (cameraTracker != null && cameraTracker.IsFree)
    //         ShowPlacePanels(true);
    // }

    public void SwitchPlacementMode()
    {
        placementMode = (placementMode == PlacementMode.Manual) ? PlacementMode.TLE : PlacementMode.Manual;

        var placementModeButtonText = placementModeButton.GetComponentInChildren<TMP_Text>();
        if (placementModeButtonText != null)
            placementModeButtonText.text = placementMode == PlacementMode.Manual ? "Switch to TLE Input" : "Switch to Manual Input";

        // Only visible in FreeCam
        if (cameraTracker != null && cameraTracker.IsFree)
        {
            ShowPlacePanels(true);
            // Also refresh input interactivity to match the new mode
            ShowPlacementSelect(true);
        }
    }

    public void SwitchBurnMode()
    {
        thrustUiMode = (thrustUiMode == ThrustUiMode.FreeThrust) ? ThrustUiMode.ManeuverNodes : ThrustUiMode.FreeThrust;
        var txt = burnControlButton.GetComponentInChildren<TMP_Text>();
        if (txt != null)
            txt.text = thrustUiMode == ThrustUiMode.FreeThrust ? "Use Maneuver Nodes" : "Use Free Thrust";

        // Only visible in TrackCam
        if (cameraTracker != null && !cameraTracker.IsFree)
            ShowThrustPanels(true);
    }

    // --------- CAMERA EVENT HANDLERS (authoritative state) ----------

    private void HandleEarthViewChanged(bool inEarth)
    {
        if (earthCamButtonText != null)
            earthCamButtonText.text = inEarth ? "Satellite Cam" : "Earth Cam";

        // Reapply current overall mode to panels (keeps things consistent)
        ApplyModeUi(isFreeCam: cameraTracker?.IsFree == true, inEarthCam: inEarth);
    }

    private void HandleFreeModeChanged(bool isFree)
    {
        // Reapply UI for Free/Track – EarthCam text handled by its own event
        ApplyModeUi(isFreeCam: isFree, inEarthCam: cameraTracker?.IsEarthView == true);
    }

    // When tracking switches programmatically, keep buttons coherent
    private void EnsureTrackUiConsistency()
    {
        if (cameraTracker != null && !cameraTracker.IsFree)
        {
            SetButtonState(trackCamButton, true);
            trackCamButton.interactable = false;
            if (freeCamButton != null) freeCamButton.interactable = true;
        }
    }

    // --------- CORE UI LAYOUT LOGIC ----------

    private void ApplyModeUi(bool isFreeCam, bool inEarthCam)
    {
        // Buttons …
        SetButtonState(freeCamButton, isFreeCam);
        SetButtonState(trackCamButton, !isFreeCam);
        freeCamButton.interactable = !isFreeCam;
        trackCamButton.interactable = isFreeCam;

        // 👇 NEW: swap instructions here
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
            toggleOptionsPanel.SetActive(false);
            dropdown.SetActive(false);
        }
        else
        {
            // TrackCam panels …
            ShowPlacementSelect(false);
            ShowPlacePanels(false);
            ShowThrustPanels(true);
            ShowOrbitInfoPanel(true);
            ShowApogeePerigeePanel(true);
            ShowTimeControlsPanel(true);
            toggleOptionsPanel.SetActive(true);
            dropdown.SetActive(true);
        }

        cameraControls.SetActive(true);
    }


    private void ShowPlacementSelect(bool show)
    {
        if (placementSelectPanel != null)
            placementSelectPanel.SetActive(show);

        // Inputs & button interactivity in placement mode
        bool manual = placementMode == PlacementMode.Manual;

        if (show)
        {
            if (velocityInputField != null) velocityInputField.interactable = false;

            if (nameInputField) nameInputField.interactable = manual;
            if (positionInputField) positionInputField.interactable = manual;
            if (massInputField) massInputField.interactable = manual;
            if (radiusInputField) radiusInputField.interactable = manual;
            if (placeObjectButton) placeObjectButton.interactable = true;
        }
        else
        {
            // Clear & lock when leaving placement
            if (velocityInputField != null) velocityInputField.interactable = false;

            if (nameInputField) { nameInputField.text = null; nameInputField.interactable = false; }
            if (positionInputField) { positionInputField.text = null; positionInputField.interactable = false; }
            if (massInputField) { massInputField.text = null; massInputField.interactable = false; }
            if (radiusInputField) { radiusInputField.text = null; radiusInputField.interactable = false; }
            if (placeObjectButton) placeObjectButton.interactable = false;
        }
    }

    private void ShowPlacePanels(bool show)
    {
        // Only meaningful in FreeCam
        if (placeTLEPanel == null || objectPlacementPanel == null) return;

        if (!show)
        {
            placeTLEPanel.SetActive(false);
            objectPlacementPanel.SetActive(false);
            return;
        }

        if (placementMode == PlacementMode.Manual)
        {
            placeTLEPanel.SetActive(false);
            objectPlacementPanel.SetActive(true);
        }
        else
        {
            placeTLEPanel.SetActive(true);
            objectPlacementPanel.SetActive(false);

            // objectPlacementManager.ClearManualPlacementCompletely();
        }
    }

    private void ShowThrustPanels(bool show)
    {
        // Only meaningful in TrackCam
        if (thrustButtons == null || maneuverNodePanel == null || burnControlsPanel == null) return;

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
        TMP_Text tmpButtonText = instructionsButton.GetComponentInChildren<TMP_Text>();
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

        // Force refresh
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
        SetText(inclinationText, "Inclination", inclination, "°");
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
