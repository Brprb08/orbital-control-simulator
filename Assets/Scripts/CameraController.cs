using UnityEngine;
using System.Collections.Generic;
using UnityEngine.EventSystems;

/**
 * CameraController handles the camera movement, tracking, and free camera mode.
 * This script manages the transition between tracking celestial bodies and free movement.
 */
public class CameraController : MonoBehaviour
{
    [Header("References")]
    public CameraMovement cameraMovement; // Controls camera movement.
    public Transform cameraPivotTransform; // The pivot point for camera rotation.
    public Transform cameraTransform; // Main camera, a child of the pivot.

    [Header("Settings")]
    public float sensitivity = 100f; // Mouse sensitivity for camera rotation.

    [Header("Tracking State")]
    private List<NBody> bodies; // List of celestial bodies to track.
    public List<NBody> Bodies => bodies; // Public read-only access to the list of bodies.
    public int currentIndex = 0; // Index of the currently tracked body.
    private bool isFreeCamMode = false; // Whether the camera is in free movement mode.
    private Vector3 defaultLocalPosition; // Default camera position relative to the pivot.
    private bool isSwitchingToFreeCam = false; // Prevents multiple switches.
    private Transform placeholderTarget; // Placeholder for tracking temporary objects.
    private bool isTrackingPlaceholder = false; // Tracks whether the camera is following a placeholder.

    public bool IsFreeCamMode
    {
        get => isFreeCamMode;
        private set
        {
            Debug.Log($"isFreeCamMode changed to {value}. Call stack:\n{System.Environment.StackTrace}");
            isFreeCamMode = value;
        }
    }

    /**
     * Initializes the camera's default position and starts tracking the first celestial body.
     */
    void Start()
    {
        if (cameraTransform != null)
        {
            defaultLocalPosition = cameraTransform.localPosition;
        }

        bodies = GravityManager.Instance.Bodies.FindAll(body => body.CompareTag("Planet"));
        if (bodies.Count > 0 && cameraMovement != null)
        {
            currentIndex = 0;
            cameraMovement.SetTargetBody(bodies[currentIndex]);
            ResetCameraPosition();
            Debug.Log($"Initial camera tracking: {bodies[currentIndex].name}");
        }
    }

    /**
     * Handles camera controls and switching between tracking and free camera mode.
     */
    void Update()
    {
        if (!isFreeCamMode)
        {
            if (Input.GetKeyDown(KeyCode.Tab) && bodies.Count > 0)
            {
                currentIndex = (currentIndex + 1) % bodies.Count;
                cameraMovement.SetTargetBody(bodies[currentIndex]);
                ResetCameraPosition();
                Debug.Log($"Camera now tracking: {bodies[currentIndex].name}");
            }

            if (Input.GetMouseButton(1) && cameraPivotTransform != null)
            {
                float rotationX = Input.GetAxis("Mouse X") * sensitivity * Time.unscaledDeltaTime;
                float rotationY = Input.GetAxis("Mouse Y") * sensitivity * Time.unscaledDeltaTime;
                cameraPivotTransform.Rotate(Vector3.up, rotationX, Space.World);
                cameraPivotTransform.Rotate(Vector3.right, -rotationY, Space.Self);

                Vector3 currentRotation = cameraPivotTransform.eulerAngles;
                float clampedX = ClampAngle(currentRotation.x, -80f, 80f);
                cameraPivotTransform.eulerAngles = new Vector3(clampedX, currentRotation.y, 0);
            }
        }
    }

    /**
     * Clamps an angle between a minimum and maximum value.
     */
    private float ClampAngle(float angle, float min, float max)
    {
        angle = NormalizeAngle(angle);
        return Mathf.Clamp(angle, min, max);
    }

    /**
     * Normalizes an angle to be within -180 to 180 degrees.
     */
    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    /**
     * Switches the camera to free movement mode.
     */
    public void BreakToFreeCam()
    {
        if (isSwitchingToFreeCam)
        {
            return;
        }

        isSwitchingToFreeCam = true;
        if (cameraMovement != null)
        {
            cameraMovement.SetTargetBody(null);
            cameraMovement.enabled = false;
        }

        FreeCamera freeCam = cameraTransform.GetComponent<FreeCamera>();
        if (freeCam != null)
        {
            freeCam.TogglePlacementMode(true);
        }

        isFreeCamMode = true;
        isSwitchingToFreeCam = false;

        EventSystem.current.SetSelectedGameObject(null);
    }

    /**
     * Resets the camera position to its default relative to the pivot.
     */
    private void ResetCameraPosition()
    {
        if (cameraTransform != null)
        {
            Debug.Log($"Resetting Camera to default local position: {defaultLocalPosition}");
            cameraTransform.localPosition = defaultLocalPosition;
            cameraTransform.localRotation = Quaternion.identity;
        }
        else
        {
            Debug.LogError("cameraTransform is null. Ensure it is assigned in the Inspector!");
        }
    }

    /**
     * Resets the pivot's rotation to point at the tracked body.
     */
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

    /**
     * Returns the camera to tracking mode after free movement.
     */
    public void ReturnToTracking()
    {
        if (!isFreeCamMode) return;
        Debug.Log("Returning to Tracking Mode...");

        if (cameraMovement != null)
        {
            cameraMovement.enabled = true;
        }

        if (isTrackingPlaceholder && placeholderTarget != null)
        {
            cameraMovement.SetTargetBodyPlaceholder(placeholderTarget);
        }
        else if (bodies.Count > 0)
        {
            cameraMovement.SetTargetBody(bodies[currentIndex]);
        }

        ResetPivotRotation();
        ResetCameraPosition();

        FreeCamera freeCam = cameraTransform.GetComponent<FreeCamera>();
        if (freeCam != null)
        {
            freeCam.TogglePlacementMode(false);
        }

        isFreeCamMode = false;
        Debug.Log("FreeCam disabled. Tracking resumed.");
    }

    /**
     * Checks if the camera is currently tracking a specific body.
     */
    public bool IsTracking(NBody body)
    {
        return cameraMovement != null && cameraMovement.targetBody == body;
    }

    /**
     * Switches the camera to another valid body if the current one is removed.
     */
    public void SwitchToNextValidBody(NBody removedBody)
    {
        RefreshBodiesList();
        bodies.Remove(removedBody);

        if (bodies.Count > 0)
        {
            currentIndex = Mathf.Clamp(currentIndex, 0, bodies.Count - 1);
            cameraMovement.SetTargetBody(bodies[currentIndex]);
            Debug.Log($"Camera switched to track: {bodies[currentIndex].name}");
        }
        else
        {
            BreakToFreeCam();
            Debug.Log("No valid bodies to track. Switched to FreeCam.");
        }
    }

    /**
     * Sets the camera to track a placeholder object.
     */
    public void SetTargetPlaceholder(Transform placeholder)
    {
        if (placeholder == null) return;

        placeholderTarget = placeholder;
        isTrackingPlaceholder = true;
        cameraMovement.SetTargetBodyPlaceholder(placeholder);

        Debug.Log($"Switched to tracking placeholder planet: {placeholder.name}");
    }

    /**
     * Switches the camera to track a real NBody object.
     */
    public void SwitchToRealNBody(NBody realNBody)
    {
        if (realNBody == null) return;

        isTrackingPlaceholder = false;
        placeholderTarget = null;
        if (cameraMovement != null)
        {
            cameraMovement.SetTargetBody(realNBody);
        }
    }

    /**
     * Refreshes the list of celestial bodies being tracked.
     */
    public void RefreshBodiesList()
    {
        bodies = GravityManager.Instance.Bodies.FindAll(body => body.CompareTag("Planet"));
        Debug.Log($"RefreshBodiesList called. Found {bodies.Count} bodies.");

        if (bodies.Count > 0 && currentIndex >= bodies.Count)
        {
            currentIndex = bodies.Count - 1;
        }
    }
}