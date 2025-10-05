using UnityEngine;

public static class UIHelpers
{
    public static bool IsPointerOverTMPDropdown()
    {
        var dropdownList = GameObject.Find("Dropdown List");
        if (dropdownList == null || !dropdownList.activeInHierarchy) return false;

        var rect = dropdownList.GetComponent<RectTransform>();
        return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, null);
    }
}
