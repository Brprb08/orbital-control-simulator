using UnityEngine;
using System.Collections.Generic;

public class CameraController : MonoBehaviour
{
    public CameraMovement cameraMovement; // Assign in Inspector
    private List<NBody> bodies;
    private int currentIndex = 0;

    private bool isFreeCamMode = false;

    void Start()
    {
        bodies = GravityManager.Instance.Bodies.FindAll(body => body.CompareTag("Planet"));
        if (bodies.Count > 0 && cameraMovement != null)
        {
            // Initially set the first body as target
            cameraMovement.SetTargetBody(bodies[currentIndex]);
        }
    }

    void Update()
    {
        if (!isFreeCamMode)
        {
            // Press Tab to cycle through bodies in tracking mode
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                currentIndex = (currentIndex + 1) % bodies.Count;
                cameraMovement.SetTargetBody(bodies[currentIndex]);
                Debug.Log($"Camera now tracking: {bodies[currentIndex].name}");
            }
        }
    }

    public void BreakToFreeCam()
    {
        if (cameraMovement != null)
        {
            cameraMovement.SetTargetBody(null); // Stop tracking
            cameraMovement.enabled = false; // Disable tracking completely
        }

        FreeCamera freeCam = cameraMovement.GetComponent<FreeCamera>();
        if (freeCam != null)
        {
            freeCam.TogglePlacementMode(true);
        }

        isFreeCamMode = true;
        Debug.Log("Tracking camera disabled. FreeCam enabled.");
    }

    public void ReturnToTracking()
    {
        if (cameraMovement != null)
        {
            cameraMovement.enabled = true; // Enable tracking
            cameraMovement.SetTargetBody(bodies[currentIndex]); // Resume tracking the last body
        }

        FreeCamera freeCam = cameraMovement.GetComponent<FreeCamera>();
        if (freeCam != null)
        {
            freeCam.TogglePlacementMode(false);
        }

        isFreeCamMode = false;
        Debug.Log("FreeCam disabled. Tracking resumed.");
    }
}