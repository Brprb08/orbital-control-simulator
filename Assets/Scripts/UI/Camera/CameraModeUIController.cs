using UnityEngine;
using UnityEngine.UI;

public class CameraModeUIController
{
    private readonly UIReferences refs;

    public CameraModeUIController(UIReferences refs)
    {
        this.refs = refs;
    }

    public void Apply(ICameraTracker cameraTracker, bool showManualVelocityUi)
    {
        CameraMode mode = cameraTracker != null ? cameraTracker.Mode : CameraMode.Free;
        bool isFreeCam = mode == CameraMode.Free;
        bool isEarthView = cameraTracker != null && cameraTracker.IsEarthView;
        bool isPendingManualVelocity = isFreeCam && showManualVelocityUi;
        bool showEarthButton = !isFreeCam || isPendingManualVelocity;

        if (refs.earthCamButtonText != null)
            refs.earthCamButtonText.text = isEarthView ? "Satellite Cam" : "Earth Cam";

        SetButtonState(refs.freeCamButton, isFreeCam);
        SetButtonState(refs.trackCamButton, !isFreeCam);

        if (refs.freeCamButton != null)
            refs.freeCamButton.interactable = !isFreeCam && !isPendingManualVelocity;

        if (refs.trackCamButton != null)
            refs.trackCamButton.interactable = isFreeCam && !isPendingManualVelocity;

        if (refs.instructionText != null)
            refs.instructionText.text = isFreeCam
                ? BuildFreeCamInstructions()
                : BuildTrackCamInstructions();

        if (isFreeCam && !showManualVelocityUi)
        {
            SetActive(refs.objectInfoPanel, false);
            SetActive(refs.apogeePerigeePanel, false);
            SetActive(refs.timeControlsPanel, true);
            SetActive(refs.toggleOptionsPanel, false);
            SetActive(refs.dropdown, false);

            if (refs.removePreManeuverLineButton != null)
                refs.removePreManeuverLineButton.gameObject.SetActive(false);

            if (refs.feedbackText != null)
            {
                refs.feedbackText.text = "";
                refs.feedbackText.gameObject.SetActive(true);
            }
            if (refs.trackedSatellites != null)
                refs.trackedSatellites.gameObject.SetActive(false);
        }
        else
        {
            SetActive(refs.objectInfoPanel, !isFreeCam);
            SetActive(refs.apogeePerigeePanel, !isFreeCam);
            SetActive(refs.timeControlsPanel, true);
            SetActive(refs.toggleOptionsPanel, !isFreeCam);
            SetActive(refs.dropdown, showEarthButton);

            if (refs.feedbackText != null)
                refs.feedbackText.gameObject.SetActive(isFreeCam);
            if (refs.trackedSatellites != null)
                refs.trackedSatellites.gameObject.SetActive(!isFreeCam);
        }

        if (refs.earthView != null)
            refs.earthView.gameObject.SetActive(showEarthButton);

        if (refs.removeSatellite != null)
            refs.removeSatellite.gameObject.SetActive(!isFreeCam && !isPendingManualVelocity);

        SetActive(refs.confirmRemoveSatPanel, false);

        if (refs.freeCamButton != null)
            refs.freeCamButton.gameObject.SetActive(true);

        if (refs.trackCamButton != null)
            refs.trackCamButton.gameObject.SetActive(true);

        if (isPendingManualVelocity)
        {
            if (refs.freeCamButton != null)
                refs.freeCamButton.interactable = false;

            if (refs.trackCamButton != null)
                refs.trackCamButton.interactable = false;
        }

        SetContainerChromeVisible(refs.dropdown, !isPendingManualVelocity);

        SetActive(refs.cameraControls, true);
    }

    public void HandleTrackedBodyChanged(ICameraTracker cameraTracker)
    {
        if (cameraTracker != null && cameraTracker.switchedToPrevTrackedSat)
            return;

        if (refs.removePreManeuverLineButton != null)
            refs.removePreManeuverLineButton.gameObject.SetActive(false);

        EnsureTrackUiConsistency(cameraTracker);
    }

    private void EnsureTrackUiConsistency(ICameraTracker cameraTracker)
    {
        if (cameraTracker == null || cameraTracker.Mode == CameraMode.Free)
            return;

        SetButtonState(refs.trackCamButton, true);

        if (refs.trackCamButton != null)
            refs.trackCamButton.interactable = false;

        if (refs.freeCamButton != null)
            refs.freeCamButton.interactable = true;
    }

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

    private void SetActive(GameObject go, bool show)
    {
        if (go != null)
            go.SetActive(show);
    }

    private void SetContainerChromeVisible(GameObject go, bool visible)
    {
        if (go == null)
            return;

        var graphic = go.GetComponent<Graphic>();
        if (graphic != null)
            graphic.enabled = visible;
    }
}
