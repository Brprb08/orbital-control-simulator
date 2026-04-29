using UnityEngine;

/// <summary>
/// Handles camera positioning, rotation, and zoom for a focus supplied by CameraController.
/// </summary>
public class CameraMovement : MonoBehaviour
{
    [Header("Applied Focus")]
    [SerializeField] private NBody focusBody;
    [SerializeField] private Transform focusPlaceholder;
    [SerializeField] private NBody earthFocusBody;
    public Transform cameraPivotTransform;
    public Transform cameraTransform;

    [Header("Camera Distance + Zoom")]
    public float distance = 100f;
    public float height = 30f;
    public float baseZoomSpeed = 40f;
    public float maxCameraDistance = 50000f;
    [SerializeField, Min(0f)] private float closeZoomHeightRatio = 0.25f;
    private float minCameraDistance = 0.1f;

    [Header("Camera State")]
    [SerializeField] private bool inEarthFocus = false;
    [SerializeField] private bool isFreeCamMode = false;
    private Camera mainCamera;

    public Camera MainCamera => mainCamera;
    public bool IsFreeCamMode => isFreeCamMode;

    [Header("UI Input Guards")]
    private GameObject dropdownList;

    [Header("Constants")]
    private float yaw = 0f;
    private float pitch = 20f;
    public float sensitivity = 100f;

    private bool isRightMouseHeld = false;
    /// <summary>Initializes references from the simulation context.</summary>
    public void Initialize(SimContext ctx)
    {
        mainCamera = GetComponentInChildren<Camera>();
    }

    void Update()
    {
        if (!isFreeCamMode)
        {
            if (Input.GetMouseButtonDown(1))
            {
                // Sync rotation on initial press
                Vector3 currentEuler = cameraPivotTransform.rotation.eulerAngles;
                yaw = currentEuler.y;
                pitch = currentEuler.x;
                if (pitch > 180f) pitch -= 360f; // normalize once
                isRightMouseHeld = true;
            }

            if (Input.GetMouseButtonUp(1))
            {
                isRightMouseHeld = false;
            }

            if (isRightMouseHeld)
            {
                yaw += Input.GetAxis("Mouse X") * sensitivity * 0.01f;
                pitch -= Input.GetAxis("Mouse Y") * sensitivity * 0.01f;
                pitch = Mathf.Clamp(pitch, -80f, 80f);

                Quaternion targetRotation = Quaternion.Euler(pitch, yaw, 0f);
                cameraPivotTransform.rotation = targetRotation;
            }

            HandleZoom();
        }
    }

    /// <summary>
    /// Positions the camera after other updates have run.
    /// </summary>
    void LateUpdate()
    {
        bool usingEarthTarget = inEarthFocus && earthFocusBody != null;
        bool usingPlaceholder = !usingEarthTarget && focusBody == null && focusPlaceholder != null;

        if (mainCamera == null || (!usingEarthTarget && focusBody == null && !usingPlaceholder)) return;

        float cameraDistanceRadius = usingEarthTarget
            ? earthFocusBody.cameraDistanceRadius
            : (usingPlaceholder ? focusPlaceholder.localScale.x : focusBody.cameraDistanceRadius);

        transform.position = usingEarthTarget
            ? earthFocusBody.transform.position
            : (usingPlaceholder ? focusPlaceholder.position : focusBody.transform.position);

        if (usingPlaceholder)
        {
            maxCameraDistance = CameraZoomSettingsFactory.PlaceholderMaxCameraDistance;
        }

        minCameraDistance = CalculateMinCameraDistance(cameraDistanceRadius);

        float effectiveHeight = CalculateEffectiveHeight(height, distance, closeZoomHeightRatio);
        Vector3 targetLocalPos = new Vector3(0f, effectiveHeight, -distance);

        mainCamera.transform.localPosition = Vector3.Lerp(
            mainCamera.transform.localPosition,
            targetLocalPos,
            0.2f
        );

        mainCamera.transform.LookAt(transform.position);
    }

    /// <summary>
    /// Applies a body focus chosen by CameraController.
    /// </summary>
    public void ApplyBodyFocus(NBody body, float? defaultDistanceOverride = null)
    {
        focusBody = body;
        focusPlaceholder = null;
        inEarthFocus = false;
        earthFocusBody = null;

        if (focusBody == null)
            return;

        ApplyZoomSettings(CameraZoomSettingsFactory.ForBody(focusBody, defaultDistanceOverride));
        ApplyFocusPosition(focusBody);
    }

    /// <summary>
    /// Applies an Earth focus chosen by CameraController.
    /// </summary>
    public void ApplyEarthFocus(NBody earth)
    {
        inEarthFocus = true;
        earthFocusBody = earth;
        focusPlaceholder = null;

        if (earth == null)
            return;

        ApplyZoomSettings(CameraZoomSettingsFactory.ForEarth(earth));
        ApplyFocusPosition(earth);
    }

    public void ClearEarthFocus()
    {
        inEarthFocus = false;
        earthFocusBody = null;
    }

    /// <summary>
    /// Applies a placeholder focus chosen by CameraController.
    /// </summary>
    public void ApplyPlaceholderFocus(Transform placeholder)
    {
        focusBody = null;
        focusPlaceholder = placeholder;
        inEarthFocus = false;
        earthFocusBody = null;

        if (placeholder != null)
        {
            ApplyZoomSettings(CameraZoomSettingsFactory.ForPlaceholder(placeholder));
        }
        else
        {
            Debug.Log("[CAMERA MOVEMENT]: ApplyPlaceholderFocus called with null. No placeholder assigned.");
        }
    }

    public void ClearFocus()
    {
        focusBody = null;
        focusPlaceholder = null;
        earthFocusBody = null;
        inEarthFocus = false;
    }

    public void SetFreeCamMode(bool enabled)
    {
        isFreeCamMode = enabled;
    }

    /// <summary>
    /// Orients the camera to look from the tracked target toward the central body, with a pitch adjustment.
    /// </summary>
    /// <param name="centralBodyPos">World position of the central body.</param>
    /// <param name="targetPosition">World position of the tracked target.</param>
    public void PointCameraTowardCentralBody(Vector3 centralBodyPos, Vector3 targetPosition)
    {
        Vector3 directionToTarget = (targetPosition - cameraPivotTransform.position).normalized;
        cameraTransform.position = targetPosition - directionToTarget;

        Vector3 forwardDirection = -(targetPosition - centralBodyPos).normalized;
        Quaternion targetRotation = Quaternion.LookRotation(forwardDirection, Vector3.up);

        float pitchAngle = 10f;
        Quaternion pitchAdjustment = Quaternion.Euler(pitchAngle, 0f, 0f);

        cameraPivotTransform.rotation = targetRotation * pitchAdjustment;
    }

    /// <summary>
    /// Handles mouse-wheel zoom with bounds based on the current target size.
    /// </summary>
    void HandleZoom()
    {
        if (UIHelpers.IsPointerOverTMPDropdown())
            return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) <= 0.01f) return;

        NBody zoomBody = inEarthFocus ? earthFocusBody : focusBody;
        float sizeMultiplier = Mathf.Clamp(zoomBody != null ? zoomBody.cameraDistanceRadius / 20f : 0.4f, 1f, 15f);
        float distanceFactor = Mathf.Clamp(distance * sizeMultiplier * 0.1f, 0.5f, 100f);
        float zoomSpeed = baseZoomSpeed * distanceFactor * 3f;

        // No Time.deltaTime so zoom works at timeScale = 0
        distance = Mathf.Clamp(distance - scroll * zoomSpeed, minCameraDistance, maxCameraDistance);
    }

    /// <summary>
    /// Returns true if the pointer is over an open TMP dropdown list (prevents zoom).
    /// </summary>
    public bool IsPointerOverDropdown()
    {
        if (dropdownList == null)
        {
            dropdownList = GameObject.Find("Dropdown List");
        }
        else if (!dropdownList.activeInHierarchy)
        {
            dropdownList = null;
        }

        if (dropdownList == null) return false;

        RectTransform rect = dropdownList.GetComponent<RectTransform>();
        return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, null);
    }

    /// <summary>
    /// Calculates the minimum camera distance based on target radius and mode.
    /// </summary>
    private float CalculateMinCameraDistance(float radius)
    {
        return CameraZoomSettingsFactory.CalculateRuntimeMinDistance(radius, inEarthFocus);
    }

    private void ApplyZoomSettings(CameraZoomSettings settings)
    {
        minCameraDistance = settings.MinDistance;
        maxCameraDistance = settings.MaxDistance;
        distance = settings.DefaultDistance;
        if (settings.Height.HasValue)
            height = settings.Height.Value;
    }

    public static float CalculateEffectiveHeight(float configuredHeight, float currentDistance, float heightRatio)
    {
        float maxHeight = Mathf.Max(0f, currentDistance) * Mathf.Max(0f, heightRatio);
        return Mathf.Min(configuredHeight, maxHeight);
    }

    private void ApplyFocusPosition(NBody body)
    {
        if (body == null) return;
        transform.position = body.transform.position;

        if (float.IsNaN(transform.position.x) || float.IsNaN(transform.position.y) || float.IsNaN(transform.position.z))
        {
            Debug.LogError($"[CAMERA MOVEMENT]: Camera transform is NaN after applying focus {body.name}");
        }
    }

}
