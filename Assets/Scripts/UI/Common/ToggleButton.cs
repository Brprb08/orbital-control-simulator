using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Button))]
/// <summary>
/// Handles a UI button that toggles visibility for specific line types (Prediction, Origin, or Apogee/Perigee).
/// Integrates with LineVisibilityController and updates its visual state using color changes.
/// </summary>
public class ToggleButton : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LineVisibilityController controller; // Assigned in Inspector when possible
    private Button button;

    [Header("Button Colors")]
    [SerializeField] private string activeColorHex = "#2A4E6C";
    [SerializeField] private string inactiveColorHex = "#1B2735";
    private Color activeColor, inactiveColor;

    [Header("Line Type")]
    public LineVisibilityController.LineType lineType;

    private bool isOn;
    private SimContext ctx;

    /// <summary>
    /// Injects the simulation context used for resolving the line visibility controller.
    /// </summary>
    public void Initialize(SimContext ctx) => this.ctx = ctx;

    /// <summary>
    /// Initializes button references and color settings.
    /// </summary>
    void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(ToggleState);

        if (!ColorUtility.TryParseHtmlString(activeColorHex, out activeColor)) activeColor = Color.gray;
        if (!ColorUtility.TryParseHtmlString(inactiveColorHex, out inactiveColor)) inactiveColor = Color.white;
    }

    /// <summary>
    /// Resolves controller reference and sets initial button color state.
    /// </summary>
    void Start()
    {
        EnsureController();
        isOn = controller != null ? controller.GetInitialLineState(lineType) : true;
        UpdateButtonColor();
    }

    /// <summary>
    /// Ensures that a valid LineVisibilityController reference exists, resolving through context or fallback lookup.
    /// </summary>
    private void EnsureController()
    {
        if (controller != null) return;

        if (ctx != null) controller = ctx.LineVisibilityController;

#if UNITY_2023_1_OR_NEWER
        if (controller == null) controller = FindFirstObjectByType<LineVisibilityController>();
#else
        if (controller == null) controller = FindObjectOfType<LineVisibilityController>();
#endif

        if (controller == null)
            Debug.LogError($"[{nameof(ToggleButton)}] No {nameof(LineVisibilityController)} found.");
    }

    /// <summary>
    /// Toggles the current visibility state for the assigned line type and updates the button color.
    /// </summary>
    public void ToggleState()
    {
        isOn = !isOn;
        if (controller != null)
            controller.SetLineVisibility(lineType, isOn);

        UpdateButtonColor();

        if (EventSystem.current)
            EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>
    /// Updates the button’s color to reflect its active/inactive state.
    /// </summary>
    private void UpdateButtonColor()
    {
        if (!button) return;
        var colors = button.colors;
        colors.normalColor = isOn ? activeColor : inactiveColor;
        colors.highlightedColor = colors.normalColor;
        colors.selectedColor = colors.normalColor;
        button.colors = colors;
    }

    /// <summary>
    /// Manually sets the button’s state and updates its appearance.
    /// </summary>
    public void SetState(bool state)
    {
        isOn = state;
        UpdateButtonColor();
    }

    /// <summary>
    /// Returns whether the toggle button is currently active.
    /// </summary>
    public bool GetState() => isOn;
}
