using UnityEngine;
using UnityEngine.UI;

public class ToggleButton : MonoBehaviour
{
    // Reference to the Button component
    private Button button;

    // Current state of the button
    private bool isOn = false;

    // Colors for active and inactive states (using hex codes)
    [Header("Button Colors")]
    [Tooltip("Hex color for the active (pressed) state.")]
    public string activeColorHex = "#150B28";   // Dark blue for active state

    [Tooltip("Hex color for the inactive (unpressed) state.")]
    public string inactiveColorHex = "#1B2735"; // Purple for inactive state

    // Parsed Color values
    private Color activeColor;
    private Color inactiveColor;

    // Reference to the LineVisibilityManager
    private LineVisibilityManager manager;

    // The type of line this button controls
    public enum LineType
    {
        Prediction, // Controls predictionRenderer, activeRenderer, backgroundRenderer
        Origin      // Controls originLineRenderer
    }

    [Header("Line Type")]
    public LineVisibilityManager.LineType lineType;

    void Awake()
    {
        // Get the Button component
        button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(ToggleState);  // Add listener programmatically
        }

        if (button == null)
        {
            Debug.LogError("ToggleButton: No Button component found on this GameObject.");
            return;
        }

        // Parse the hex color strings to Color objects
        if (!ColorUtility.TryParseHtmlString(activeColorHex, out activeColor))
        {
            Debug.LogError($"ToggleButton: Failed to parse activeColorHex '{activeColorHex}'. Using default Color.gray.");
            activeColor = Color.gray;
        }

        if (!ColorUtility.TryParseHtmlString(inactiveColorHex, out inactiveColor))
        {
            Debug.LogError($"ToggleButton: Failed to parse inactiveColorHex '{inactiveColorHex}'. Using default Color.white.");
            inactiveColor = Color.white;
        }

        // Find the LineVisibilityManager instance
        manager = LineVisibilityManager.Instance;
        if (manager == null)
        {
            // Attempt to find LineVisibilityManager attached to GravityManager
            GameObject gravityManager = GameObject.Find("GravityManager"); // Ensure name matches in the hierarchy
            if (gravityManager != null)
                manager = gravityManager.GetComponent<LineVisibilityManager>();

            if (manager == null)
                Debug.LogError("ToggleButton: No LineVisibilityManager found in the scene.");
        }

        if (manager == null)
        {
            Debug.LogError("ToggleButton: No LineVisibilityManager instance found in the scene.");
        }

        // Initialize button color based on initial state
        UpdateButtonColor();
    }

    void Start()
    {
        // Get initial visibility state from the LineVisibilityManager
        if (manager != null)
        {
            isOn = manager.GetInitialLineState(lineType);  // Get initial state
        }
        else
        {
            isOn = true;  // Default to "on" state if manager is missing
        }

        UpdateButtonColor();  // Update button visual state to match the initial state
    }

    /// <summary>
    /// This method should be linked to the Button's OnClick event via the Unity Inspector.
    /// It toggles the button's state and updates visibility.
    /// </summary>
    public void ToggleState()
    {
        // Toggle the button's intended state
        isOn = !isOn;

        // Notify the LineVisibilityManager
        if (manager != null)
        {
            manager.SetLineVisibility(lineType, isOn);  // Apply visibility change
        }

        // Immediately fetch the actual state after applying to avoid desync
        if (manager != null)
        {
            isOn = manager.GetInitialLineState(lineType);  // Re-fetch the current visibility state
        }

        // Update the button's color
        UpdateButtonColor();
    }

    /// <summary>
    /// Updates the button's color based on its current state.
    /// </summary>
    void UpdateButtonColor()
    {
        if (button == null)
            return;

        ColorBlock colors = button.colors;
        Color newColor;

        if (isOn)
        {
            ColorUtility.TryParseHtmlString(activeColorHex, out newColor); // Active state color
        }
        else
        {
            ColorUtility.TryParseHtmlString(inactiveColorHex, out newColor); // Inactive state color
        }

        colors.normalColor = newColor;
        button.colors = colors;
        button.Select();
        button.OnDeselect(null);
    }

    /// <summary>
    /// Sets the button's state programmatically.
    /// </summary>
    /// <param name="state">Desired state.</param>
    public void SetState(bool state)
    {
        isOn = state;
        UpdateButtonColor();
    }

    /// <summary>
    /// Gets the current state of the button.
    /// </summary>
    /// <returns>True if on, false otherwise.</returns>
    public bool GetState()
    {
        return isOn;
    }
}