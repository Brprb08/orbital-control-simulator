using UnityEngine;

/// <summary>
/// Small UI utility for detecting if the mouse is currently over an open TMP dropdown list.
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
}
