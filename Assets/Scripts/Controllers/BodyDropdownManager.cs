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
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // Ensure references are assigned
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

        // Add a listener to handle changes in dropdown selection
        bodyDropdown.onValueChanged.AddListener(HandleDropdownValueChanged);
    }

    public void HandleDropdownValueChanged(int index)
    {
        // If you added a default prompt option at index 0, ignore selection 0.
        // if (index == 0)
        // {
        //     Debug.Log("No body selected.");
        //     return;
        // }

        // Adjust for the extra prompt option
        int bodyIndex = index - 2;
        Debug.LogError(index);
        // Safety check
        if (index < 0 || index >= cameraController.Bodies.Count)
        {
            Debug.LogWarning("Dropdown selection index out of range.");
            return;
        }

        // cameraController.UpdateDropdownSelection();
        cameraController.UpdateTrajectoryRender(index);

        // Update the currentIndex in CameraController and track the chosen body.
        cameraController.currentIndex = index;
        cameraController.ReturnToTracking();

        Debug.Log($"Tracking switched to: {cameraController.Bodies[index].name}");
    }
}