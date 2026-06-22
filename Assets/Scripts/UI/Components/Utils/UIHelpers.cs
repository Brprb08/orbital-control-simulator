using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Small null-safe UI operations shared by lightweight UI controllers.
/// </summary>
public static class UIHelpers
{
    /// <summary>
    /// Returns true if the pointer is inside the active TMP dropdown popup (named "Dropdown List").
    /// </summary>
    public static bool IsPointerOverTMPDropdown()
    {
        var dropdownList = GameObject.Find("Dropdown List");
        if (dropdownList == null || !dropdownList.activeInHierarchy) return false;

        var rect = dropdownList.GetComponent<RectTransform>();
        return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, null);
    }

    public static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    public static void SetChildActive(Transform root, string childName, bool active)
    {
        if (root == null)
            return;

        Transform child = root.Find(childName);
        if (child != null)
            SetActive(child.gameObject, active);
    }

    public static void SetInteractable(Selectable selectable, bool interactable)
    {
        if (selectable != null)
            selectable.interactable = interactable;
    }

    public static void SetInteractable(bool interactable, params Selectable[] selectables)
    {
        if (selectables == null)
            return;

        for (int i = 0; i < selectables.Length; i++)
            SetInteractable(selectables[i], interactable);
    }

    public static void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value ?? string.Empty;
    }

    public static void ClearInput(TMP_InputField input, bool clearSelection = true)
    {
        if (input == null)
            return;

        input.text = string.Empty;

        if (clearSelection)
            ClearSelection();
    }

    public static void ClearInputs(bool clearSelection, params TMP_InputField[] inputs)
    {
        if (inputs == null)
            return;

        for (int i = 0; i < inputs.Length; i++)
            ClearInput(inputs[i], clearSelection: false);

        if (clearSelection)
            ClearSelection();
    }

    public static void ClearSelection()
    {
        EventSystem.current?.SetSelectedGameObject(null);
    }
}
