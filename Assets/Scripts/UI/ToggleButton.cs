using UnityEngine;
using UnityEngine.UI;

/**
* Sets the color of a button based on if it is pressed held or active
**/
public class ToggleButton : MonoBehaviour
{
    [Header("References")]
    private Button button;
    private LineVisibilityManager manager;

    [Header("Button Colors")]
    public string activeColorHex = "#150B28";   // Dark blue for active state
    public string inactiveColorHex = "#1B2735"; // Purple for inactive state

    [Header("Button States")]
    private Color activeColor;
    private Color inactiveColor;
    private bool isOn = false;

    [Header("Line Type")]
    public LineVisibilityManager.LineType lineType;

    void Awake()
    {
        button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(ToggleState);
        }

        if (button == null)
        {
            Debug.LogError("ToggleButton: No Button component found on this GameObject.");
            return;
        }

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

        manager = LineVisibilityManager.Instance;
        if (manager == null)
        {
            GameObject gravityManager = GameObject.Find("Controller"); // Ensure name matches in the hierarchy
            if (gravityManager != null)
                manager = gravityManager.GetComponent<LineVisibilityManager>();

            if (manager == null)
                Debug.LogError("ToggleButton: No LineVisibilityManager found in the scene.");
        }

        if (manager == null)
        {
            Debug.LogError("ToggleButton: No LineVisibilityManager instance found in the scene.");
        }

        UpdateButtonColor();
    }

    void Start()
    {
        if (manager != null)
        {
            isOn = manager.GetInitialLineState(lineType);
        }
        else
        {
            isOn = true;  // Default to on state if manager is missing
        }

        UpdateButtonColor();
    }

    /**
    * This method should be linked to the Button's OnClick event via the Unity Inspector.
    * It toggles the button's state and updates visibility.
    **/
    public void ToggleState()
    {
        isOn = !isOn;

        // Notify the LineVisibilityManager
        if (manager != null)
        {
            manager.SetLineVisibility(lineType, isOn);
        }

        // Immediately fetch the actual state after applying to avoid desync
        if (manager != null)
        {
            isOn = manager.GetInitialLineState(lineType);
        }

        UpdateButtonColor();
    }

    /**
    * Updates the button's color based on its current state.
    **/
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

    /**
    * Sets the button's state programmatically.
    * @param state - Desired state
    **/
    public void SetState(bool state)
    {
        isOn = state;
        UpdateButtonColor();
    }

    /**
    * Gets the current state of the button.
    **/
    public bool GetState()
    {
        return isOn;
    }
}