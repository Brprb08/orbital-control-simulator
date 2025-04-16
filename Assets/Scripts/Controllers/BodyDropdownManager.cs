using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class BodyDropdownManager : MonoBehaviour
{
    public static BodyDropdownManager Instance { get; private set; }

    [Header("UI References")]
    public TMP_Dropdown bodyDropdown;

    [Header("Scene References")]
    public CameraController cameraController;

    public TrajectoryRenderer trajectoryRenderer;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        if (bodyDropdown == null)
        {
            Debug.LogError("BodyDropdownManager: Missing reference to TMP_Dropdown.");
            return;
        }

        if (cameraController == null)
        {
            Debug.LogError("BodyDropdownManager: Could not find CameraController in the scene.");
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