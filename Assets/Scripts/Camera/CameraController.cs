using UnityEngine;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using System.Collections;
using TMPro;

/// <summary>
/// Handles camera movement, setting the current tracked body, and switching between cameras.
/// Supports switching between tracking celestial bodies and free movement.
/// Also manages trajectory visualization and placeholder tracking for temporary objects.
/// </summary>
public class CameraController : MonoBehaviour
{

    public static CameraController Instance { get; private set; }

    [Header("References")]
    public CameraMovement cameraMovement;
    public Transform cameraPivotTransform;
    public Transform cameraTransform;
    public TextMeshProUGUI apogeeText;
    public TextMeshProUGUI perigeeText;
    private LineVisibilityManager lineVisibilityManager;
    private GravityManager gravityManager;
    private BodyDropdownManager bodyDropdownManager;

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

    /// <summary>
    /// Used by object placement manager to ensure camera is in FreeCam mode when placing.
    /// </summary>
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

    /// <summary>
    /// Initializes the camera's default position and starts tracking the first celestial body.
    /// Initializes the trajectory renderer and UI text fields.
    /// </summary>
    void Start()
    {
        if (cameraTransform != null)
        {
            defaultLocalPosition = cameraTransform.localPosition;
        }

        if (gravityManager == null)
        {
            gravityManager = GravityManager.Instance;
        }

        bodies = gravityManager.Bodies.FindAll(body => body.CompareTag("Planet"));
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

        lineVisibilityManager = LineVisibilityManager.Instance;
        if (lineVisibilityManager == null)
        {
            Debug.LogError("LineVisibilityManager instance is not set.");
        }

        gravityManager = GravityManager.Instance;
        if (gravityManager == null)
        {
            Debug.LogError("LineVisibilityManager instance is not set.");
        }

        bodyDropdownManager = BodyDropdownManager.Instance;
        if (bodyDropdownManager == null)
        {
            Debug.LogError("LineVisibilityManager instance is not set.");
        }
    }

    /// <summary>
    /// Coroutine to initialize the camera after all NBody.Start() methods have executed.
    /// </summary>
    IEnumerator InitializeCamera()
    {
        yield return null; // Wait for all NBody.Start() to finish

        if (lineVisibilityManager != null)
        {
            lineVisibilityManager.SetTrackedBody(bodies[currentIndex]);
        }
        ReturnToTracking();

        UpdateDropdownSelection();

        Debug.Log($"[CAMERA CONTROLLER]: Initial camera tracking: {bodies[currentIndex].name}");
    }

    /// <summary>
    /// Handles input for camera controls and switching between tracking and free camera mode.
    /// </summary>
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
                float clampedX = CameraCalculations.ClampAngle(currentRotation.x, -80f, 80f);
                cameraPivotTransform.eulerAngles = new Vector3(currentRotation.x, currentRotation.y, 0);
            }
        }
    }

    /// <summary>
    /// Refreshes the dropdown to reflect the currently tracked body.
    /// </summary>
    public void UpdateDropdownSelection()
    {
        if (bodyDropdownManager.bodyDropdown == null || bodies.Count == 0) return;

        TMP_Dropdown dropdown = bodyDropdownManager.bodyDropdown;

        string currentBodyName = bodies[currentIndex].name;
        int dropdownIndex = dropdown.options.FindIndex(option => option.text == currentBodyName);

        // Ensure the index is valid before setting it
        if (dropdownIndex != -1)
        {
            dropdown.onValueChanged.RemoveListener(bodyDropdownManager.HandleDropdownValueChanged);

            dropdown.value = dropdownIndex;
            dropdown.RefreshShownValue();

            dropdown.onValueChanged.AddListener(bodyDropdownManager.HandleDropdownValueChanged);
            //Debug.Log($"[CAMERA CONTROLLER]: Dropdown selection updated to: {dropdown.options[dropdown.value].text}");
        }
        else
        {
            Debug.LogError($"[CAMERA CONTROLLER]: No matching dropdown entry found for body: {currentBodyName}");
        }
    }

    /// <summary>
    /// Updates the trajectory renderer for the specified body index.
    /// </summary>
    public void UpdateTrajectoryRender(int index)
    {
        trajectoryRenderer.SetTrackedBody(bodies[index]);
        if (lineVisibilityManager != null)
        {
            lineVisibilityManager.SetTrackedBody(bodies[index]);
        }

        trajectoryRenderer.orbitIsDirty = true;
    }

    /// <summary>
    /// Switches the camera to free movement mode, disabling tracking.
    /// </summary>
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

    /// <summary>
    /// Returns the camera to tracking mode, focusing on a celestial body or placeholder.
    /// </summary>
    public void ReturnToTracking()
    {
        cameraMovement.enabled = true;

        GameObject centralBody = GameObject.FindWithTag("CentralBody");

        if (centralBody == null)
        {
            Debug.LogError("[CAMERA CONTROLLER]: Central body not found. Ensure it has the correct tag.");
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
            trajectoryRenderer.orbitIsDirty = true;
        }
        else
        {
            Debug.LogWarning("[CAMERA CONTROLLER]: No valid bodies to track.");
            return;
        }

        previousTrackedBody = targetBody;

        float distanceMultiplier = 100.0f;  // Adjust for how far back the camera should be
        float radius = (targetBody != null) ? targetBody.radius : 3f;  // Use the body's radius or a default
        float desiredDistance = radius * distanceMultiplier;

        Vector3 directionToTarget = (targetPosition - cameraPivotTransform.position).normalized;
        cameraTransform.position = targetPosition - directionToTarget;

        PointCameraTowardCentralBody(centralBody.transform, targetPosition);

        FreeCamera freeCam = cameraTransform.GetComponent<FreeCamera>();
        if (freeCam != null)
        {
            freeCam.TogglePlacementMode(false);
        }

        isFreeCamMode = false;
    }

    /// <summary>
    /// Points the camera toward the central body with a pitch adjustment.
    /// </summary>
    /// <param name="centralBody">The transform of the central body.</param>
    /// <param name="targetPosition">The position of the currently tracked object.</param>
    private void PointCameraTowardCentralBody(Transform centralBody, Vector3 targetPosition)
    {
        // Vector pointing from the tracked object to the central body
        Vector3 directionToCentralBody = (centralBody.position - targetPosition).normalized;

        Vector3 forwardDirection = -(targetPosition - centralBody.position).normalized;

        Quaternion targetRotation = Quaternion.LookRotation(forwardDirection, Vector3.up);

        float pitchAngle = 10f;
        Quaternion pitchAdjustment = Quaternion.Euler(pitchAngle, 0f, 0f);

        cameraPivotTransform.rotation = targetRotation * pitchAdjustment;
    }

    /// <summary>
    /// Toggles the camera between Earth view and the previously tracked body.
    /// </summary>
    public void SwitchToEarthCam()
    {
        if (!inEarthViewCam)
        {
            GameObject centralBody = GameObject.FindWithTag("CentralBody");

            if (centralBody == null)
            {
                Debug.LogError("[CAMERA CONTROLLER]: Central body not found. Ensure it has the correct tag.");
                return;
            }

            NBody nBodyComponent = centralBody.GetComponent<NBody>();

            if (nBodyComponent == null)
            {
                Debug.LogError("[CAMERA CONTROLLER]: NBody component not found on the central body.");
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

    /// <summary>
    /// Sets the Earth view tracking state.
    /// Used in ObjectPlacementManager.
    /// </summary>
    public void SetInEarthView(bool inEarthCam)
    {
        cameraMovement.inEarthCam = inEarthCam;
    }

    /// <summary>
    /// Checks if the camera is currently tracking the specified body.
    /// Used in ObjectPlacementManager.
    /// </summary>
    /// <param name="body">The body to check against.</param>
    public bool IsTracking(NBody body)
    {
        return cameraMovement != null && cameraMovement.targetBody == body;
    }

    /// <summary>
    /// Switches to a new valid celestial body after one is removed (collision).
    /// Used in GravityManager.
    /// </summary>
    /// <param name="removedBody">The body that was removed.</param>
    public void SwitchToNextValidBody(NBody removedBody)
    {
        RefreshBodiesList();
        if (lineVisibilityManager != null)
        {
            lineVisibilityManager.DeregisterNBody(bodies[currentIndex]);
        }
        bodies.Remove(removedBody);


        if (bodies.Count > 0)
        {
            currentIndex = Mathf.Clamp(currentIndex, 0, bodies.Count - 1);

            NBody nextBody = bodies[currentIndex];

            cameraMovement.SetTargetBody(nextBody);

            UpdateTrajectoryRender(currentIndex);

            ReturnToTracking();
            Debug.Log($"[CAMERA CONTROLLER]: Camera switched to track: {nextBody.name}");
        }
        else
        {
            BreakToFreeCam();
            Debug.Log("[CAMERA CONTROLLER]: No valid bodies to track. Switched to FreeCam.");
        }
    }

    /// <summary>
    /// Sets a placeholder target for tracking during object placement.
    /// Used in ObjectPlacementManager.
    /// </summary>
    /// <param name="placeholder">The placeholder transform to track.</param>
    public void SetTargetPlaceholder(Transform placeholder)
    {
        if (placeholder == null) return;

        placeholderTarget = placeholder;
        isTrackingPlaceholder = true;
        cameraMovement.SetTargetBodyPlaceholder(placeholder);
    }

    /// <summary>
    /// Switches the camera from tracking a placeholder to tracking the real object.
    /// Used in VelocityDragManager.
    /// </summary>
    /// <param name="realNBody">The real NBody to track.</param>
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

    /// <summary>
    /// Refreshes the list of celestial bodies from the GravityManager.
    /// Used in GravityManager, ObjectPlacementManager, VelocityDragManager
    /// </summary>
    public void RefreshBodiesList()
    {
        bodies = gravityManager.Bodies.FindAll(body => body.CompareTag("Planet"));

        if (bodies.Count > 0 && currentIndex >= bodies.Count)
        {
            currentIndex = bodies.Count - 1;
        }
    }
}