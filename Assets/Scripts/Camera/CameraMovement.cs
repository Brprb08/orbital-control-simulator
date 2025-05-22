using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Handles camera positioning, zoom, and UI updates while tracking celestial bodies.
/// Supports tracking both real NBody objects and placeholder objects during placement.
/// </summary>
public class CameraMovement : MonoBehaviour
{
    public static CameraMovement Instance { get; private set; }

    [Header("Tracking Target")]
    public NBody targetBody;
    public Transform targetPlaceholder;

    [Header("Camera Settings")]
    public float distance = 100f;
    public float height = 30f;
    public float baseZoomSpeed = 40f;
    public float maxCameraDistance = 50000f;
    private float minCameraDistance = 0.1f;

    // Placeholders are used when placing a body before it becomes an NBody
    private float placeholderBodyRadius = 0f;
    private Camera mainCamera;
    public bool inEarthCam = false;
    public NBody tempEarthBody;


    [Header("UI References")]
    public TextMeshProUGUI velocityText;
    public TextMeshProUGUI altitudeText;
    public TextMeshProUGUI trackingObjectNameText;

    [Header("Constants")]
    private const float EarthCamMinDistance = 750f;
    private const float EarthCamDefaultDistance = 2000f;
    private const float PlaceholderMaxCameraDistance = 800f;

    public TMP_Dropdown dropdown;
    private GameObject dropdownList;

    /// <summary>
    /// Sets up the singleton instance for camera control.
    /// </summary>
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
    /// Initializes the main camera reference.
    /// </summary>
    void Start()
    {
        mainCamera = GetComponentInChildren<Camera>();
    }

    /// <summary>
    /// Updates the camera position and UI each frame after all updates are processed.
    /// </summary>
    void LateUpdate()
    {
        if (mainCamera == null || (targetBody == null && targetPlaceholder == null)) return;

        bool usingPlaceholder = (targetBody == null && targetPlaceholder != null);
        float cameraDistanceRadius = usingPlaceholder ? placeholderBodyRadius : targetBody.cameraDistanceRadius;

        transform.position = inEarthCam
            ? tempEarthBody.transform.position
            : (usingPlaceholder ? targetPlaceholder.position : targetBody.transform.position);


        if (usingPlaceholder)
        {
            maxCameraDistance = PlaceholderMaxCameraDistance;
        }

        minCameraDistance = CalculateMinCameraDistance(cameraDistanceRadius);

        HandleZoom();

        Vector3 targetLocalPos = new Vector3(0f, height, -distance);

        mainCamera.transform.localPosition = Vector3.Lerp(
            mainCamera.transform.localPosition,
            targetLocalPos,
            Time.deltaTime * 10f
        );

        mainCamera.transform.LookAt(transform.position);

        if (!usingPlaceholder)
        {
            UpdateVelocityAndAltitudeUI();
        }
    }

    /// <summary>
    /// Configures camera zoom limits and default positioning for a given NBody.
    /// </summary>
    /// <param name="body">The celestial body to focus on.</param>
    /// <param name="togglingEarth">Whether this is a toggle into Earth view mode.</param>
    /// <param name="closerFraction">Fraction used to place the camera closer to the object.</param>
    /// <param name="customMinMultiplier">Optional multiplier for minimum camera distance.</param>
    /// <param name="customMaxOverride">Optional override for max camera distance.</param>
    private void ConfigureCameraForBody(NBody body, bool togglingEarth, float closerFraction, float customMinMultiplier = 1f, float customMaxOverride = -1f)
    {
        if (body == null) return;

        transform.position = body.transform.position;

        if (float.IsNaN(transform.position.x) || float.IsNaN(transform.position.y) || float.IsNaN(transform.position.z))
        {
            Debug.LogError($"[CAMERA MOVEMENT]: Camera transform is NaN after setting target {body.name}");
        }

        minCameraDistance = CameraCalculations.CalculateMinDistance(body.radius) * customMinMultiplier;
        maxCameraDistance = (customMaxOverride > 0f)
            ? customMaxOverride
            : CameraCalculations.CalculateMaxDistance(body.radius);

        float midpointDistance = (minCameraDistance + maxCameraDistance) / 2f;

        if (togglingEarth)
        {
            // float defaultDistance = minCameraDistance + (midpointDistance - minCameraDistance) * closerFraction;
            maxCameraDistance = 30000f;
            distance = EarthCamDefaultDistance;
        }
        else
        {
            float defaultDistance;
            if (inEarthCam)
            {
                defaultDistance = 1000f;
            }
            else
            {
                defaultDistance = minCameraDistance + (midpointDistance - minCameraDistance) * closerFraction;
            }
            maxCameraDistance = 10000f;

            distance = defaultDistance;
        }
    }

    /// <summary>
    /// Sets an NBody object as the target for the camera to follow.
    /// </summary>
    /// <param name="newTarget">The celestial body to track.</param>
    public void SetTargetBody(NBody newTarget)
    {

        targetBody = newTarget;
        targetPlaceholder = null;

        if (targetBody != null)
        {
            float closerFraction = targetBody.radius <= 10f ? 0.15f : 0.25f;
            float earthViewOverride = inEarthCam ? 2500f : -1f;

            ConfigureCameraForBody(targetBody, false, closerFraction, 1f, earthViewOverride > 0 ? 10000f : -1f);
            if (earthViewOverride > 0) distance = earthViewOverride;
        }
    }

    /// <summary>
    /// Switches camera into or out of Earth tracking mode.
    /// </summary>
    /// <param name="earth">The Earth body to track.</param>
    public void SetTargetEarth(NBody earth)
    {
        inEarthCam = !inEarthCam;

        tempEarthBody = earth;

        targetPlaceholder = null;

        if (earth != null)
        {
            float closerFraction = earth.radius <= 10f ? 0.15f : 0.25f;
            float customMinMultiplier = 5f;
            float customMaxOverride = 30000f;

            ConfigureCameraForBody(earth, true, closerFraction, customMinMultiplier, customMaxOverride);
        }
    }

    /// <summary>
    /// Sets a placeholder transform as the camera's target (during placement).
    /// </summary>
    /// <param name="planet">The transform of the placeholder object.</param>
    public void SetTargetBodyPlaceholder(Transform planet)
    {
        targetBody = null;
        targetPlaceholder = planet;

        if (planet != null)
        {
            placeholderBodyRadius = planet.localScale.x * 1f;
            distance = 10f * placeholderBodyRadius;
            height = 0.2f * placeholderBodyRadius;
        }
        else
        {
            Debug.Log("[CAMERA MOVEMENT]: SetTargetBodyPlaceholder called with null. No placeholder assigned.");
        }
    }

    /// <summary>
    /// Handles scroll-wheel zooming and enforces camera distance constraints.
    /// </summary>
    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            if (IsPointerOverDropdown())
                return;
            float sizeMultiplier = Mathf.Clamp(targetBody != null ? targetBody.cameraDistanceRadius / 20f : .4f, 1f, 20f);
            float distanceFactor = Mathf.Clamp(distance * sizeMultiplier * .1f, .5f, 100f);
            float zoomSpeed = baseZoomSpeed * distanceFactor * 3f;

            distance -= scroll * zoomSpeed;
            distance = Mathf.Clamp(distance, minCameraDistance, maxCameraDistance);
        }
    }

    /// <summary>
    /// Makes sure pointer is not over dropdown when zooming, if it is zooming is not allowed.
    /// </summary>
    public bool IsPointerOverDropdown()
    {
        if (dropdownList == null)
        {
            dropdownList = GameObject.Find("Dropdown List"); // uses this name
        }
        else
        {
            if (!dropdownList.activeInHierarchy)
                dropdownList = null;
        }

        if (dropdownList == null) return false;

        RectTransform rect = dropdownList.GetComponent<RectTransform>();
        return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, null);
    }

    /// <summary>
    /// Calculates min distance for camera based off object type and raduis.
    /// </summary>
    private float CalculateMinCameraDistance(float radius)
    {
        if (inEarthCam) return EarthCamMinDistance;
        if (radius <= 0.5f) return Mathf.Max(0.01f, radius * 0.7f);
        if (radius <= 100f) return radius * 5f;
        return radius + 400f;
    }

    /// <summary>
    /// Updates the velocity, altitude, and tracked object name in the UI.
    /// </summary>
    void UpdateVelocityAndAltitudeUI()
    {
        if (velocityText != null && targetBody != null)
        {
            float velocityMagnitude = targetBody.velocity.magnitude;
            float velocityInMetersPerSecond = velocityMagnitude * 10000f;
            velocityText.text = $"Velocity: {velocityInMetersPerSecond:F2} m/s";
        }

        if (altitudeText != null && targetBody != null)
        {
            float altitude = (float)targetBody.altitude;
            altitudeText.text = $"Altitude: {altitude * 10:F3} km";
        }

        if (trackingObjectNameText != null && targetBody != null)
        {
            trackingObjectNameText.text = $"{targetBody.name}";
        }
    }
}