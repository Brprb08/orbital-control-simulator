using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class BodyDropdownManager : MonoBehaviour
{
    [Header("References - UI")]
    public TMP_Dropdown bodyDropdown;

    [Header("References - Scripts")]
    public CameraController cameraController;

    private SimContext ctx;

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.cameraController = ctx.CameraController;

        if (bodyDropdown == null)
        {
            Debug.LogError("BodyDropdownManager: Missing reference to TMP_Dropdown.");
            return;
        }

        bodyDropdown.onValueChanged.AddListener(HandleDropdownValueChanged);

    }

    public void HandleDropdownValueChanged(int index)
    {
        int bodyIndex = index - 2;
        // Safety check
        if (index < 0 || index >= cameraController.Bodies.Count)
        {
            Debug.LogWarning("Dropdown selection index out of range.");
            return;
        }

        cameraController.UpdateTrajectoryRender(index);

        cameraController.currentIndex = index;
        cameraController.ReturnToTracking();

        Debug.Log($"Tracking switched to: {cameraController.Bodies[index].name}");
    }
}