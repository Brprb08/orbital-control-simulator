using UnityEngine;
using UnityEngine.UI; // Required for Button and ColorBlock
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("Buttons")]
    public Button freeCamButton; // "BreakToFreeCam" Button
    public Button trackCamButton; // "BreakToTrackCam" Button

    [Header("Panels")]
    public GameObject objectPlacementPanel; // "ObjectPlacementPanel" containing Radius/Velocity UI
    public GameObject panel;

    [Header("UI")]
    public TMP_InputField velocityInputField;

    private void Start()
    {
        // Initialize the UI state
        ShowObjectPlacementPanel(false); // Hide the Radius/Velocity panel initially
        ShowPanel(true);
        // Set initial states for the buttons
        SetButtonState(freeCamButton, false); // Default unpressed state for Free Cam
        SetButtonState(trackCamButton, true); // Start Track Cam as pressed (Purple)

        // Pre-assign Track Cam as the "active" button
        trackCamButton.Select();
    }

    public void OnFreeCamPressed()
    {
        ShowObjectPlacementPanel(true); // Show Radius/Velocity panel
        ShowPanel(false); // Show Velocity and Altitude
        SetButtonState(freeCamButton, true); // Highlight Free Cam
        SetButtonState(trackCamButton, false); // Reset Track Cam

        if (velocityInputField != null)
        {
            velocityInputField.interactable = false; // Disable when switching back to tracking
        }
    }

    public void OnTrackCamPressed()
    {
        ShowObjectPlacementPanel(false); // Hide Radius/Velocity panel
        ShowPanel(true); // Show Velocity and Altitude
        SetButtonState(freeCamButton, false); // Reset Free Cam
        SetButtonState(trackCamButton, true); // Highlight Track Cam

        if (velocityInputField != null)
        {
            velocityInputField.interactable = false; // Disable when switching back to tracking
        }
    }

    private void ShowObjectPlacementPanel(bool show)
    {
        objectPlacementPanel.SetActive(show);
    }

    private void ShowPanel(bool show)
    {
        panel.SetActive(show);
    }

    private void SetButtonState(Button button, bool isPressed)
    {
        ColorBlock colors = button.colors;

        Color newColor;

        // Use hex codes for colors
        if (isPressed)
        {
            ColorUtility.TryParseHtmlString("#150B28", out newColor); // Dark blue
        }
        else
        {
            ColorUtility.TryParseHtmlString("#1B2735", out newColor); // Purple
        }

        colors.normalColor = newColor;

        // Reassign the modified ColorBlock back to the button
        button.colors = colors;

        // Force the button to refresh its visual state
        button.Select(); // Select the button
        button.OnDeselect(null);
    }
}