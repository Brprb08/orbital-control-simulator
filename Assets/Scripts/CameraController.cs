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
                ReturnToTracking();
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
        Debug.Log("Returning to Tracking Mode...");

        if (cameraMovement != null)
        {
            cameraMovement.enabled = true;
        }

        GameObject centralBody = GameObject.FindWithTag("CentralBody");

        if (centralBody == null)
        {
            Debug.LogError("Central body not found. Ensure it has the correct tag.");
            return;
        }

        NBody targetBody = null;
        Vector3 targetPosition;

        if (isTrackingPlaceholder && placeholderTarget != null)
        {
            cameraMovement.SetTargetBodyPlaceholder(placeholderTarget);
            targetPosition = placeholderTarget.position;
            targetBody = placeholderTarget.GetComponent<NBody>();
        }
        else if (bodies.Count > 0)
        {
            cameraMovement.SetTargetBody(bodies[currentIndex]);
            targetPosition = bodies[currentIndex].transform.position;
            targetBody = bodies[currentIndex];
        }
        else
        {
            Debug.LogWarning("No valid bodies to track.");
            return;
        }

        float distanceMultiplier = 10.0f;  // Adjust for how far back the camera should be
        float radius = (targetBody != null) ? targetBody.radius : 1f;  // Use the body's radius or a default
        float desiredDistance = radius * distanceMultiplier;

        Vector3 directionToTarget = (targetPosition - cameraPivotTransform.position).normalized;
        cameraTransform.position = targetPosition - directionToTarget * desiredDistance;

        // Adjust the camera's orientation
        PointCameraTowardCentralBody(centralBody.transform, targetPosition);

        FreeCamera freeCam = cameraTransform.GetComponent<FreeCamera>();
        if (freeCam != null)
        {
            freeCam.TogglePlacementMode(false);
        }

        isFreeCamMode = false;
        Debug.Log($"Camera positioned {desiredDistance} units away from {targetBody?.name ?? "the placeholder"}. Tracking resumed.");
    }

    private void PointCameraTowardCentralBody(Transform centralBody, Vector3 targetPosition)
    {
        // Vector pointing FROM the tracked object TO the central body (Earth)
        Vector3 directionToCentralBody = (centralBody.position - targetPosition).normalized;

        // Desired camera forward direction: Opposite of the direction to Earth (i.e., facing the object)
        Vector3 forwardDirection = -(targetPosition - centralBody.position).normalized;

        // Calculate the base rotation that makes the camera look at the object, with Earth behind
        Quaternion targetRotation = Quaternion.LookRotation(forwardDirection, Vector3.up);

        // Add a small upward pitch (positive X-axis rotation)
        float pitchAngle = 10f;  // Adjust for desired tilt (in degrees)
        Quaternion pitchAdjustment = Quaternion.Euler(pitchAngle, 0f, 0f);

        // Combine the rotations
        cameraPivotTransform.rotation = targetRotation * pitchAdjustment;

        Debug.Log($"Camera pivot adjusted to align with {centralBody.name} behind target, with upward tilt.");
    }
    private int GetClosestBodyIndexToLastPosition()
    {
        if (bodies.Count == 0) return -1;

        Vector3 lastPlaceholderPosition = placeholderTarget != null ? placeholderTarget.position : Vector3.zero;
        float closestDistance = float.MaxValue;
        int closestIndex = -1;

        for (int i = 0; i < bodies.Count; i++)
        {
            float distance = Vector3.Distance(lastPlaceholderPosition, bodies[i].transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = i;
            }
        }

        return closestIndex;
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