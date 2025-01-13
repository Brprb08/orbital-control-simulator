using UnityEngine;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using System.Collections;

/**
* Handles camera movement, tracking of celestial bodies, and free camera mode.
* This script supports switching between tracking celestial bodies and free movement.
* It also manages trajectory visualization and placeholder tracking for temporary objects.
**/
public class CameraController : MonoBehaviour
{
    [Header("References")]
    public CameraMovement cameraMovement;
    public Transform cameraPivotTransform;
    public Transform cameraTransform;
    [Header("Settings")]
    public float sensitivity = 100f;

    [Header("Tracking State")]
    public List<NBody> bodies;
    public List<NBody> Bodies => bodies;
    public int currentIndex = 0;
    private bool isFreeCamMode = false;
    private Vector3 defaultLocalPosition;
    private bool isSwitchingToFreeCam = false;
    private Transform placeholderTarget;
    private bool isTrackingPlaceholder = false;
    public TrajectoryRenderer trajectoryRenderer;

    private float lastTabTime = -1f;
    private float tabCooldown = 1f;

    /**
    * Used by object placement manager to ensure cam is in FreeCam mode when placing.
    **/
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
    * Also initializes the trajectory renderer after a short delay.
    **/
    void Start()
    {
        if (cameraTransform != null)
        {
            defaultLocalPosition = cameraTransform.localPosition;
        }

        bodies = GravityManager.Instance.Bodies.FindAll(body => body.CompareTag("Planet"));
        if (bodies.Count > 0 && cameraMovement != null)
        {
            StartCoroutine(InitializeCamera());
        }

        StartCoroutine(FindTrajectoryRendererWithDelay());
    }


    /**
    * Coroutine to initialize the camera after all NBody.Start() methods have executed.
    **/
    IEnumerator InitializeCamera()
    {
        yield return null; // Wait for all NBody.Start() to finish

        ReturnToTracking();

        Debug.Log($"Initial camera tracking: {bodies[currentIndex].name}");
    }

    /**
    * Coroutine to find the trajectory renderer with a small delay.
    **/
    private IEnumerator FindTrajectoryRendererWithDelay()
    {
        yield return new WaitForSeconds(0.1f);
        trajectoryRenderer = Object.FindFirstObjectByType<TrajectoryRenderer>();

        if (trajectoryRenderer == null)
        {
            Debug.LogError("TrajectoryRenderer not found after delay!");
        }
    }

    /**
    * Handles input for camera controls and switching between tracking and free camera mode.
    **/
    void Update()
    {
        if (!isFreeCamMode)
        {
            if (Time.time - lastTabTime > tabCooldown && Input.GetKeyDown(KeyCode.Tab) && bodies.Count > 0)
            {
                lastTabTime = Time.time;
                currentIndex = (currentIndex + 1) % bodies.Count;
                trajectoryRenderer.SetTrackedBody(bodies[currentIndex]);
                ReturnToTracking();
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
    * @param angle The angle to clamp.
    * @param min The minimum value.
    * @param max The maximum value.
    * @return The clamped angle.
    **/
    private float ClampAngle(float angle, float min, float max)
    {
        angle = NormalizeAngle(angle);
        return Mathf.Clamp(angle, min, max);
    }

    /**
    * Normalizes an angle to be within -180 to 180 degrees.
    * @param angle The angle to normalize.
    * @return The normalized angle.
    **/
    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    /**
    * Switches the camera to free movement mode, disabling tracking.
    **/
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
    * Resets the camera's local position and rotation relative to the pivot.
    **/
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
    * Resets the pivot's rotation to the default orientation.
    **/
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
    * Returns the camera to tracking mode, focusing on a celestial body or placeholder.
    **/
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

        float distanceMultiplier = 100.0f;  // Adjust for how far back the camera should be
        float radius = (targetBody != null) ? targetBody.radius : 3f;  // Use the body's radius or a default
        float desiredDistance = radius * distanceMultiplier;

        Vector3 directionToTarget = (targetPosition - cameraPivotTransform.position).normalized;
        cameraTransform.position = targetPosition - directionToTarget * desiredDistance;

        PointCameraTowardCentralBody(centralBody.transform, targetPosition);

        FreeCamera freeCam = cameraTransform.GetComponent<FreeCamera>();
        if (freeCam != null)
        {
            freeCam.TogglePlacementMode(false);
        }

        isFreeCamMode = false;
        Debug.Log($"Camera positioned {desiredDistance} units away from {targetBody?.name ?? "the placeholder"}. Tracking resumed.");
    }

    /**
    * Points camera at tracked NBody object with Central body as the center background.
    * @param centralBody - CentralBody of the sim
    * @param targetPosition - Current object camera is tracking
    **/
    private void PointCameraTowardCentralBody(Transform centralBody, Vector3 targetPosition)
    {
        // Vector pointing from the tracked object to the central body
        Vector3 directionToCentralBody = (centralBody.position - targetPosition).normalized;

        Vector3 forwardDirection = -(targetPosition - centralBody.position).normalized;

        Quaternion targetRotation = Quaternion.LookRotation(forwardDirection, Vector3.up);

        float pitchAngle = 10f;
        Quaternion pitchAdjustment = Quaternion.Euler(pitchAngle, 0f, 0f);

        cameraPivotTransform.rotation = targetRotation * pitchAdjustment;

        Debug.Log($"Camera pivot adjusted to point at {centralBody.name} behind target, with upward tilt.");
    }

    /**
    * Checks if the camera is currently tracking a specific body.
    * @param body - Current NBody we are tracking
    **/
    public bool IsTracking(NBody body)
    {
        return cameraMovement != null && cameraMovement.targetBody == body;
    }

    /**
    * Switches the camera to another valid body if the current one is removed.
    * @param removedBody - Body that has been removed (Collision)
    **/
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
    * @param radius - Placeholder object to track temporarily
    **/
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
    * @param realNBody - Real object being added to sim to track
    **/
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
    **/
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