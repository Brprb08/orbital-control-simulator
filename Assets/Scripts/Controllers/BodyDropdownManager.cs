using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class BodyDropdownManager : MonoBehaviour
{
    [Header("References - UI")]
    public TMP_Dropdown bodyDropdown;
    private GameObject dropdownList;

    [Header("References - Scripts")]
    public CameraController cameraController;
    public CameraMovement cameraMovement;
    private GravityManager gravityManager;
    private ICameraTracker cameraTracker;
    // public CameraMode cameraMode;
    public TutorialController tutorialController;

    private SimContext ctx;

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.cameraController = ctx.CameraController;
        this.cameraMovement = ctx.CameraMovement;
        this.tutorialController = ctx.TutorialController;
        this.gravityManager = ctx.GravityManager;
        this.cameraTracker = ctx.CameraTracker;

        if (bodyDropdown == null)
        {
            Debug.LogError("BodyDropdownManager: Missing TMP_Dropdown reference.");
            return;
        }

        // UI -> Sim
        bodyDropdown.onValueChanged.AddListener(HandleDropdownValueChanged);

        // Sim -> UI
        if (cameraController != null)
        {
            cameraController.OnTrackedBodyChanged += OnTrackedBodyChanged;
            cameraController.OnTrackedPlaceholderChanged += _ => SetDropdownNoSelection();
            cameraController.OnFreeModeChanged += isFree =>
            {
                if (isFree) SetDropdownNoSelection();
            };
        }

        UpdateDropdownSelection();
    }

    public void HandleDropdownValueChanged(int index)
    {
        if (bodyDropdown == null) return;
        if (index < 0 || index >= bodyDropdown.options.Count) return;

        string selectedName = bodyDropdown.options[index].text;
        if (string.IsNullOrEmpty(selectedName)) return;

        // Find body by name (no LINQ)
        NBody target = null;
        var list = gravityManager != null ? gravityManager.Bodies : null;
        if (list != null)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].name == selectedName)
                {
                    target = list[i];
                    break;
                }
            }
        }

        if (target == null)
        {
            Debug.LogWarning($"[BodyDropdown] No NBody named '{selectedName}' found.");
            return;
        }

        cameraTracker?.TrackBody(target);

        if (tutorialController != null && tutorialController.inTutorialMode)
            tutorialController.hasSwitchedSatellites = true;

        Debug.Log($"[BodyDropdown] Tracking switched to: {selectedName}");
    }

    public void UpdateDropdownSelection()
    {
        if (bodyDropdown == null || gravityManager == null) return;

        // Ensure dropdown has entries for all current planets (no LINQ)
        var bodies = gravityManager.Bodies;
        if (bodies != null)
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (b == null) continue;
                if (!b.CompareTag("Planet")) continue;

                bool exists = false;
                for (int j = 0; j < bodyDropdown.options.Count; j++)
                {
                    if (bodyDropdown.options[j].text == b.name) { exists = true; break; }
                }
                if (!exists)
                {
                    bodyDropdown.options.Add(new TMP_Dropdown.OptionData(b.name));
                }
            }
            bodyDropdown.RefreshShownValue();
        }

        // Align dropdown to the tracked body (or clear if free/placeholder)
        var tracked = cameraTracker != null ? cameraTracker.CurrentBody : null;
        if (tracked == null)
        {
            SetDropdownNoSelection();
            return;
        }

        int idx = -1;
        for (int i = 0; i < bodyDropdown.options.Count; i++)
        {
            if (bodyDropdown.options[i].text == tracked.name) { idx = i; break; }
        }

        if (idx >= 0)
        {
            bodyDropdown.onValueChanged.RemoveListener(HandleDropdownValueChanged);
            bodyDropdown.value = idx;
            bodyDropdown.RefreshShownValue();
            bodyDropdown.onValueChanged.AddListener(HandleDropdownValueChanged);
        }
        else
        {
            SetDropdownNoSelection();
            Debug.LogWarning($"[BodyDropdown] Couldn’t match dropdown option for tracked body '{tracked.name}'.");
        }
    }

    private void OnTrackedBodyChanged(NBody body)
    {
        if (body == null) { SetDropdownNoSelection(); return; }
        UpdateDropdownSelection();
    }

    private void SetDropdownNoSelection()
    {
        if (bodyDropdown == null) return;
        bodyDropdown.onValueChanged.RemoveListener(HandleDropdownValueChanged);
        // If you want a visible placeholder, you can insert one as option[0] like "--".
        bodyDropdown.RefreshShownValue();
        bodyDropdown.onValueChanged.AddListener(HandleDropdownValueChanged);
    }

    /// <summary>
    /// Prevent mouse-wheel zoom when the dropdown popup is open.
    /// </summary>
    public bool IsPointerOverDropdown()
    {
        if (dropdownList == null)
        {
            dropdownList = GameObject.Find("Dropdown List"); // TMP creates this at runtime
        }
        else if (!dropdownList.activeInHierarchy)
        {
            dropdownList = null;
        }

        if (dropdownList == null) return false;

        RectTransform rect = dropdownList.GetComponent<RectTransform>();
        return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, null);
    }
}