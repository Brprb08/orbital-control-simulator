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

    public void RefreshBodiesList()
    {
        bodies = GravityManager.Instance.Bodies.FindAll(body => body.CompareTag("Planet"));
        Debug.Log($"RefreshBodiesList called. Found {bodies.Count} bodies.");

        if (bodies.Count > 0 && (currentIndex < 0 || currentIndex >= bodies.Count))
        {
            currentIndex = 0;
            Debug.Log($"Resetting currentIndex to 0.");
        }
    }

    public bool IsTracking(NBody body)
    {
        return cameraMovement != null && cameraMovement.targetBody == body;
    }

    public void SwitchToNextValidBody(NBody removedBody)
    {
        // Refresh the list of bodies
        RefreshBodiesList();

        // Remove the destroyed body from the list
        bodies.Remove(removedBody);

        if (bodies.Count > 0)
        {
            // Switch to another valid body
            currentIndex = Mathf.Clamp(currentIndex, 0, bodies.Count - 1);
            cameraMovement.SetTargetBody(bodies[currentIndex]);
            Debug.Log($"Camera switched to track: {bodies[currentIndex].name}");
        }
        else
        {
            // No bodies left, switch to free cam
            BreakToFreeCam();
            Debug.Log("No valid bodies to track. Switched to FreeCam.");
        }
    }
}