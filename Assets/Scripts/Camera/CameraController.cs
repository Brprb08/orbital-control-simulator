using UnityEngine;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using System.Collections;
using TMPro;

/**
* Handles camera movement, setting current tracked body, and switching between cameras.
* This script supports switching between tracking celestial bodies and free movement.
* It also manages trajectory visualization and placeholder tracking for temporary objects.
**/
public class CameraController : MonoBehaviour
{

    public static CameraController Instance { get; private set; }

    [Header("References")]
    public CameraMovement cameraMovement;
    public Transform cameraPivotTransform;
    public Transform cameraTransform;
    public TextMeshProUGUI apogeeText;
    public TextMeshProUGUI perigeeText;

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
    public bool isTrackingPlaceholder = false;
    public TrajectoryRenderer trajectoryRenderer;
    public bool inEarthViewCam = false;
    public NBody previousTrackedBody;

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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /**
    * Initializes the camera's default position and starts tracking the first celestial body.
    * Also initializes the trajectory renderer creatomg component, adding text fields for UI
    *      and setting the TrajectoryRenderer tracked body to follow. 
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

        if (trajectoryRenderer == null)
        {
            GameObject trajectoryObj = new GameObject($"{gameObject.name}_TrajectoryRenderer");
            trajectoryRenderer = trajectoryObj.AddComponent<TrajectoryRenderer>();
            trajectoryRenderer.apogeeText = this.apogeeText;
            trajectoryRenderer.perigeeText = this.perigeeText;
            trajectoryRenderer.SetTrackedBody(bodies[currentIndex]);
        }
    }

    /**
    * Coroutine to initialize the camera after all NBody.Start() methods have executed.
    * Sets the tracked body for the LineVisibilityManger
    **/
    IEnumerator InitializeCamera()
    {
        yield return null; // Wait for all NBody.Start() to finish

        if (LineVisibilityManager.Instance != null)
        {
            LineVisibilityManager.Instance.SetTrackedBody(bodies[currentIndex]);
        }
        ReturnToTracking();

        UpdateDropdownSelection();

        Debug.Log($"Initial camera tracking: {bodies[currentIndex].name}");
    }

    /**
    * Handles input for camera controls and switching between tracking and free camera mode.
    **/
    void Update()
    {
        if (!isFreeCamMode)
        {
            if (Input.GetMouseButton(1) && cameraPivotTransform != null)
            {
                float rotationX = Input.GetAxis("Mouse X") * sensitivity * .01f;
                float rotationY = Input.GetAxis("Mouse Y") * sensitivity * .01f;
                cameraPivotTransform.Rotate(Vector3.up, rotationX, Space.World);
                cameraPivotTransform.Rotate(Vector3.right, -rotationY, Space.Self);

                Vector3 currentRotation = cameraPivotTransform.eulerAngles;
                float clampedX = CameraCalculations.Instance.ClampAngle(currentRotation.x, -80f, 80f);
                cameraPivotTransform.eulerAngles = new Vector3(currentRotation.x, currentRotation.y, 0);
            }
        }
    }

    /**
    * Refreshing the dropdown to have the current tracked body selected.
    * The body is found by name and set as the dropdown value, then refreshed.
    **/
    public void UpdateDropdownSelection()
    {
        if (BodyDropdownManager.Instance.bodyDropdown == null || bodies.Count == 0) return;

        TMP_Dropdown dropdown = BodyDropdownManager.Instance.bodyDropdown;

        string currentBodyName = bodies[currentIndex].name;
        int dropdownIndex = dropdown.options.FindIndex(option => option.text == currentBodyName);

        // Ensure the index is valid before setting it
        if (dropdownIndex != -1)
        {
            dropdown.onValueChanged.RemoveListener(BodyDropdownManager.Instance.HandleDropdownValueChanged);

            dropdown.value = dropdownIndex;
            dropdown.RefreshShownValue();

            dropdown.onValueChanged.AddListener(BodyDropdownManager.Instance.HandleDropdownValueChanged);
            Debug.Log($"Dropdown selection updated to: {dropdown.options[dropdown.value].text}");
        }
        else
        {
            Debug.LogError($"No matching dropdown entry found for body: {currentBodyName}");
        }
    }

    /**
    * Method used to update the current line render for the tracked body.
    **/
    public void UpdateTrajectoryRender(int index)
    {
        trajectoryRenderer.SetTrackedBody(bodies[index]);
        if (LineVisibilityManager.Instance != null)
        {
            LineVisibilityManager.Instance.SetTrackedBody(bodies[index]);
        }

        trajectoryRenderer.orbitIsDirty = true;
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
    * Returns the camera to tracking mode, focusing on a celestial body or placeholder.
    * If placing planet we set the camera to track the placeholder temporarily using the transform
    * If not placing planet the camera is set to current bodies[index]  to track 
    * Target body is set, trajectory render is updated, and camera points to central body direction
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
            Debug.LogError("here");
            cameraMovement.SetTargetBodyPlaceholder(placeholderTarget);
            targetPosition = placeholderTarget.position;
            targetBody = placeholderTarget.GetComponent<NBody>();
        }
        else if (bodies.Count > 0)
        {
            Debug.LogError("there");
            cameraMovement.SetTargetBody(bodies[currentIndex]);
            targetPosition = bodies[currentIndex].transform.position;
            targetBody = bodies[currentIndex];
            trajectoryRenderer.orbitIsDirty = true;
        }
        else
        {
            Debug.LogWarning("No valid bodies to track.");
            return;
        }

        previousTrackedBody = targetBody;

        float distanceMultiplier = 100.0f;  // Adjust for how far back the camera should be
        float radius = (targetBody != null) ? targetBody.radius : 3f;  // Use the body's radius or a default
        float desiredDistance = radius * distanceMultiplier;

        Vector3 directionToTarget = (targetPosition - cameraPivotTransform.position).normalized;
        cameraTransform.position = targetPosition - directionToTarget;
        // * desiredDistance
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

    public void SwitchToEarthCam()
    {
        Debug.LogError(inEarthViewCam);
        if (!inEarthViewCam)
        {
            GameObject centralBody = GameObject.FindWithTag("CentralBody");

            if (centralBody == null)
            {
                Debug.LogError("Central body not found. Ensure it has the correct tag.");
                return;
            }

            NBody nBodyComponent = centralBody.GetComponent<NBody>();

            if (nBodyComponent == null)
            {
                Debug.LogError("NBody component not found on the central body.");
                return;
            }

            cameraMovement.SetTargetEarth(nBodyComponent);
            inEarthViewCam = true;
        }
        else
        {
            Debug.LogError(previousTrackedBody);
            cameraMovement.SetTargetEarth(previousTrackedBody);
            inEarthViewCam = false;
        }
    }

    public void SetInEarthView(bool inEarthCam)
    {
        cameraMovement.inEarthCam = inEarthCam;
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
    * Called from GravityManager after a body is removed from collision.
    * Switches the camera to another valid body if the current one is removed.
    * @param removedBody - Body that has been removed (Collision)
    **/
    public void SwitchToNextValidBody(NBody removedBody)
    {
        RefreshBodiesList();
        if (LineVisibilityManager.Instance != null)
        {
            LineVisibilityManager.Instance.DeregisterNBody(bodies[currentIndex]);
        }
        bodies.Remove(removedBody);


        if (bodies.Count > 0)
        {
            currentIndex = Mathf.Clamp(currentIndex, 0, bodies.Count - 1);

            NBody nextBody = bodies[currentIndex];

            cameraMovement.SetTargetBody(nextBody);

            UpdateTrajectoryRender(currentIndex);

            ReturnToTracking();
            Debug.Log($"Camera switched to track: {nextBody.name}");
        }
        else
        {
            BreakToFreeCam();
            Debug.Log("No valid bodies to track. Switched to FreeCam.");
        }
    }

    public void SwitchToNBody()
    {
        if (bodies.Count > 0)
        {
            currentIndex = Mathf.Clamp(currentIndex, 0, bodies.Count - 1);

            NBody nextBody = bodies[currentIndex];

            cameraMovement.SetTargetBody(nextBody);

            UpdateTrajectoryRender(currentIndex);

            ReturnToTracking();
            Debug.Log($"Camera switched to track: {nextBody.name}");
        }
        else
        {
            BreakToFreeCam();
            Debug.Log("No valid bodies to track. Switched to FreeCam.");
        }
    }

    /**
    * Called from ObjectPlacementManager to set the track temporarily before setting velocity.
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
    * Called from VelocityDragManager to switch to real body to start tracking.
    * @param realNBody - Real object being added to sim to track
    **/
    public void SwitchToRealNBody(NBody realNBody)
    {
        if (realNBody == null) return;

        isTrackingPlaceholder = false;
        placeholderTarget = null;
        previousTrackedBody = realNBody;
        inEarthViewCam = false;
        if (!UIManager.Instance.earthCamPressed)
        {
            UIManager.Instance.OnEarthCamPressed();
        }
        if (cameraMovement != null)
        {
            cameraMovement.inEarthCam = false;
            cameraMovement.SetTargetBody(realNBody);
        }

        UpdateDropdownSelection();
    }

    /**
    * Refreshes the list of celestial bodies currently in GravityManager
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