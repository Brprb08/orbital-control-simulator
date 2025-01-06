using UnityEngine;
using UnityEngine.UI; // Required for Button and ColorBlock
using TMPro;

/**
 * UIManager class manages the user interface for switching between free camera and tracking camera modes.
 * It controls the visibility of UI panels and highlights the active button.
 */
public class UIManager : MonoBehaviour
{
    [Header("Buttons")]
    public Button freeCamButton; // Button to switch to free camera mode.
    public Button trackCamButton; // Button to switch to tracking camera mode.

    [Header("Panels")]
    public GameObject objectPlacementPanel; // UI panel for object placement controls.
    public GameObject panel; // General UI panel for velocity and altitude display.

    [Header("UI")]
    public TMP_InputField velocityInputField; // Input field for entering velocity.

    /**
     * Initializes the UI elements and sets the default button states.
     */
    private void Start()
    {
        ShowObjectPlacementPanel(false);
        ShowPanel(true);
        SetButtonState(freeCamButton, false); // Default state for Free Cam button.
        SetButtonState(trackCamButton, true); // Default state for Track Cam button.

        trackCamButton.Select(); // Pre-select Track Cam as the active button.
    }

    /**
     * Handles the Free Cam button press event.
     */
    public void OnFreeCamPressed()
    {
        ShowObjectPlacementPanel(true);
        ShowPanel(false);
        SetButtonState(freeCamButton, true);
        SetButtonState(trackCamButton, false);

        if (velocityInputField != null)
        {
            velocityInputField.interactable = false;
        }
    }

    /**
     * Handles the Track Cam button press event.
     */
    public void OnTrackCamPressed()
    {
        ShowObjectPlacementPanel(false);
        ShowPanel(true);
        SetButtonState(freeCamButton, false);
        SetButtonState(trackCamButton, true);

        if (velocityInputField != null)
        {
            velocityInputField.interactable = false;
        }
    }

    /**
     * Toggles the visibility of the object placement panel.
     * @param show True to show the panel, false to hide it.
     */
    private void ShowObjectPlacementPanel(bool show)
    {
        objectPlacementPanel.SetActive(show);
    }

    /**
     * Toggles the visibility of the general UI panel.
     * @param show True to show the panel, false to hide it.
     */
    private void ShowPanel(bool show)
    {
        panel.SetActive(show);
    }

    /**
     * Updates the visual state of a button.
     * @param button The button to update.
     * @param isPressed True if the button is active/pressed, false otherwise.
     */
    private void SetButtonState(Button button, bool isPressed)
    {
        ColorBlock colors = button.colors;
        Color newColor;

        if (isPressed)
        {
            ColorUtility.TryParseHtmlString("#150B28", out newColor); // Dark blue for active state.
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
