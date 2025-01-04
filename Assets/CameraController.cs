using UnityEngine;
using System.Collections.Generic;

public class CameraController : MonoBehaviour
{
    public CameraMovement cameraMovement; // Assigned on CameraPivot
    private List<NBody> bodies;
    public int currentIndex = 0;

    private bool isFreeCamMode = false;

    public float sensitivity = 100f;

    public Transform cameraPivotTransform; // CameraPivot GameObject
    public Transform cameraTransform; // Main Camera as a child of CameraPivot
    private Vector3 defaultLocalPosition; // Default offset from the pivot
    private bool isSwitchingToFreeCam = false;
    public List<NBody> Bodies => bodies;  // Public read-only access to the list of bodies
    private Transform placeholderTarget;  // Placeholder being tracked
    private bool isTrackingPlaceholder = false;

    public bool IsFreeCamMode
    {
        get => isFreeCamMode;
        private set
        {
            Debug.Log($"isFreeCamMode changed to {value}. Call stack:\n{System.Environment.StackTrace}");
            isFreeCamMode = value;
        }
    }

    void Start()
    {
        // Save the default local position of the camera relative to the pivot
        if (cameraTransform != null)
        {
            defaultLocalPosition = cameraTransform.localPosition;
        }

        // Initialize planet tracking
        bodies = GravityManager.Instance.Bodies.FindAll(body => body.CompareTag("Planet"));
        if (bodies.Count > 0 && cameraMovement != null)
        {
            currentIndex = 0; // Ensure we start with the first body
            cameraMovement.SetTargetBody(bodies[currentIndex]);
            ResetCameraPosition(); // Reset camera position to default
            Debug.Log($"Initial camera tracking: {bodies[currentIndex].name}");
        }
    }

    void Update()
    {
        Debug.Log($"Current Cam FreeCam=true: {isFreeCamMode}");
        if (!isFreeCamMode)
        {
            // Track Cam Mode
            // Switch planets with Tab key
            if (Input.GetKeyDown(KeyCode.Tab) && bodies.Count > 0)
            {
                currentIndex = (currentIndex + 1) % bodies.Count;
                cameraMovement.SetTargetBody(bodies[currentIndex]);
                ResetCameraPosition(); // Reset the camera position after switching
                Debug.Log($"Camera now tracking: {bodies[currentIndex].name}");
            }

            // Rotate the camera with Mouse Button 1
            if (Input.GetMouseButton(1) && cameraPivotTransform != null)
            {
                float rotationX = Input.GetAxis("Mouse X") * sensitivity * Time.unscaledDeltaTime;
                float rotationY = Input.GetAxis("Mouse Y") * sensitivity * Time.unscaledDeltaTime;

                // Rotate the pivot horizontally (yaw)
                cameraPivotTransform.Rotate(Vector3.up, rotationX, Space.World);

                // Rotate the pivot vertically (pitch) with clamping
                cameraPivotTransform.Rotate(Vector3.right, -rotationY, Space.Self);
                Vector3 currentRotation = cameraPivotTransform.eulerAngles;
                float clampedX = ClampAngle(currentRotation.x, -80f, 80f);
                cameraPivotTransform.eulerAngles = new Vector3(clampedX, currentRotation.y, 0);
            }
        }
    }

    private float ClampAngle(float angle, float min, float max)
    {
        angle = NormalizeAngle(angle);
        return Mathf.Clamp(angle, min, max);
    }

    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    public void BreakToFreeCam()
    {
        if (isSwitchingToFreeCam)
        {
            Debug.Log("here");
            return;
        }

        isSwitchingToFreeCam = true;
        if (cameraMovement != null)
        {
            cameraMovement.SetTargetBody(null); // Stop tracking
            cameraMovement.enabled = false; // Disable tracking
        }

        FreeCamera freeCam = cameraTransform.GetComponent<FreeCamera>();
        if (freeCam != null)
        {
            Debug.Log("hello");
            freeCam.TogglePlacementMode(true);
        }

        isFreeCamMode = true;
        Debug.Log(isFreeCamMode);
        isSwitchingToFreeCam = false;
        Debug.Log("Tracking disabled. FreeCam enabled.");
    }

    private void ResetCameraPosition()
    {
        if (cameraTransform != null)
        {
            // Reset Main Camera to default local position relative to the pivot
            Debug.Log($"Resetting Camera to default local position: {defaultLocalPosition}");
            cameraTransform.localPosition = defaultLocalPosition;

            // Reset Main Camera local rotation
            cameraTransform.localRotation = Quaternion.identity;
        }
        else
        {
            Debug.LogError("cameraTransform is null. Ensure it is assigned in the Inspector!");
        }
    }

    private void ResetPivotRotation()
    {
        if (cameraPivotTransform != null)
        {
            Debug.Log("Resetting CameraPivot rotation to identity (pointing at the planet).");
            cameraPivotTransform.rotation = Quaternion.identity;
        }
        else
        {
            Debug.LogError("cameraPivotTransform is null. Ensure it is assigned in the Inspector!");
        }
    }

    public void ReturnToTracking()
    {
        if (!isFreeCamMode) return;
        Debug.Log("Returning to Tracking Mode...");

        if (cameraMovement != null)
        {
            cameraMovement.enabled = true; // Re-enable tracking
        }

        if (isTrackingPlaceholder && placeholderTarget != null)
        {
            // Track the placeholder if it's still active
            cameraMovement.SetTargetBodyPlaceholder(placeholderTarget);
        }
        else if (bodies.Count > 0)
        {
            cameraMovement.SetTargetBody(bodies[currentIndex]);  // Track the real NBody
        }

        ResetPivotRotation();  // Reset pivot to face the planet
        ResetCameraPosition(); // Reset camera position

        // Disable FreeCam
        FreeCamera freeCam = cameraTransform.GetComponent<FreeCamera>();
        if (freeCam != null)
        {
            freeCam.TogglePlacementMode(false); // Disable FreeCam mode
        }

        isFreeCamMode = false;
        Debug.Log("FreeCam disabled. Tracking resumed.");
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
            // No bodies left, switch to FreeCam
            BreakToFreeCam();
            Debug.Log("No valid bodies to track. Switched to FreeCam.");
        }
    }

    public void SetTargetPlaceholder(Transform placeholder)
    {
        if (placeholder == null) return;

        placeholderTarget = placeholder;
        isTrackingPlaceholder = true;

        // Let CameraMovement handle the placeholder positioning
        cameraMovement.SetTargetBodyPlaceholder(placeholder);

        Debug.Log($"Switched to tracking placeholder planet: {placeholder.name}");
    }

    public void SwitchToRealNBody(NBody realNBody)
    {
        if (realNBody == null) return;

        isTrackingPlaceholder = false;
        placeholderTarget = null;  // Clear placeholder tracking
        if (cameraMovement != null)
        {
            cameraMovement.SetTargetBody(realNBody);
        }
    }

    public void RefreshBodiesList()
    {
        bodies = GravityManager.Instance.Bodies.FindAll(body => body.CompareTag("Planet"));
        Debug.Log($"RefreshBodiesList called. Found {bodies.Count} bodies.");

        if (bodies.Count > 0 && currentIndex >= bodies.Count)
        {
            currentIndex = bodies.Count - 1;  // Set to the last body (likely the newest one)
        }
    }
}

