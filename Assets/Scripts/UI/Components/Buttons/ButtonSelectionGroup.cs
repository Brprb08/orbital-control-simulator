using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Maintains mutually exclusive selected styling for a small group of buttons.
/// </summary>
public class ButtonSelectionGroup : MonoBehaviour
{
    [SerializeField] private Button[] buttons;
    [SerializeField] private string selectedColorHex = "#2A4E6C";
    [SerializeField] private bool clearEventSelection = true;

    private Color selectedColor = new Color(0.165f, 0.306f, 0.424f, 1f);
    private ColorBlock[] defaultColors;
    private int selectedIndex = -1;

    public int SelectedIndex => selectedIndex;

    private void Awake()
    {
        CaptureDefaults();
        RefreshColors();
    }

    private void OnEnable()
    {
        CaptureDefaults();
        RefreshColors();
    }

    private void OnValidate()
    {
        ColorUtility.TryParseHtmlString(selectedColorHex, out selectedColor);
    }

    public void Select(int index)
    {
        CaptureDefaults();

        selectedIndex = IsValidIndex(index) ? index : -1;
        RefreshColors();
        ClearSelectionFocus();
    }

    public void Clear()
    {
        selectedIndex = -1;
        RefreshColors();
        ClearSelectionFocus();
    }

    private void CaptureDefaults()
    {
        ColorUtility.TryParseHtmlString(selectedColorHex, out selectedColor);

        if (buttons == null)
        {
            defaultColors = null;
            return;
        }

        if (defaultColors != null && defaultColors.Length == buttons.Length)
            return;

        defaultColors = new ColorBlock[buttons.Length];
        for (int i = 0; i < buttons.Length; i++)
            defaultColors[i] = buttons[i] != null ? buttons[i].colors : ColorBlock.defaultColorBlock;
    }

    private void RefreshColors()
    {
        if (buttons == null || defaultColors == null)
            return;

        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null)
                continue;

            ColorBlock colors = defaultColors[i];
            if (i == selectedIndex)
            {
                colors.normalColor = selectedColor;
                colors.highlightedColor = selectedColor;
                colors.selectedColor = selectedColor;
                colors.pressedColor = selectedColor;
            }

            button.colors = colors;
            button.OnDeselect(null);
        }
    }

    private bool IsValidIndex(int index)
    {
        return buttons != null && index >= 0 && index < buttons.Length && buttons[index] != null;
    }

    private void ClearSelectionFocus()
    {
        if (clearEventSelection && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }
}
