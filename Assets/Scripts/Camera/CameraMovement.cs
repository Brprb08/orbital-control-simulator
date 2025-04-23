using UnityEngine;
using TMPro;
using System.Collections;

/**
* CameraMovement handles camera positioning and UI updates while tracking celestial bodies.
* Supports switching between real NBody objects and placeholder objects.
**/
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

    /**
    * Setup the singleton for accessing UIManager
    **/
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // DontDestroyOnLoad(gameObject);
    }

    /**
    * Initializes the main camera and sets the starting position relative to the target.
    **/
    void Start()
    {
        mainCamera = GetComponentInChildren<Camera>();
    }

    /**
    * Updates the camera's position, zoom, and UI each frame.
    **/
    void LateUpdate()
    {
        if (targetBody == null && targetPlaceholder == null) return;
        if (mainCamera == null) return;

        bool usingPlaceholder = (targetBody == null && targetPlaceholder != null);
        float radius = usingPlaceholder ? placeholderBodyRadius : targetBody.radius;

        transform.position = inEarthCam
            ? tempEarthBody.transform.position
            : (usingPlaceholder ? targetPlaceholder.position : targetBody.transform.position);

        // Determine base min distance based on radius
        if (inEarthCam)
        {
            minCameraDistance = 800f;
        }
        else if (radius <= 0.5f)
        {
            minCameraDistance = Mathf.Max(0.01f, radius * 0.7f);
        }
        else if (radius <= 100f)
        {
            minCameraDistance = radius * 5f;
        }
        else
        {
            minCameraDistance = radius + 400f;
        }

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

    /**
    * Configures the camera's distance, position, and zoom boundaries based on the selected celestial body.
    *
    * @param body                The celestial body to focus the camera on.
    * @param togglingEarth       True if we're switching into Earth view mode (used for special zoom behavior).
    * @param closerFraction      How close the camera should default to the body (0 = minDistance, 1 = midpoint).
    * @param customMinMultiplier Optional multiplier to apply to the min camera distance (e.g. 5x for Earth view).
    * @param customMaxOverride   Optional override for max camera distance. Set to -1 to auto-calculate.
    **/
    private void ConfigureCameraForBody(NBody body, bool togglingEarth, float closerFraction, float customMinMultiplier = 1f, float customMaxOverride = -1f)
    {
        if (body == null) return;

        transform.position = body.transform.position;

        if (float.IsNaN(transform.position.x) || float.IsNaN(transform.position.y) || float.IsNaN(transform.position.z))
        {
            Debug.LogError($"[CAMERA MOVEMENT]: Camera transform is NaN after setting target {body.name}");
        }

        minCameraDistance = CameraCalculations.Instance.CalculateMinDistance(body.radius) * customMinMultiplier;
        maxCameraDistance = (customMaxOverride > 0f)
            ? customMaxOverride
            : CameraCalculations.Instance.CalculateMaxDistance(body.radius);

        float midpointDistance = (minCameraDistance + maxCameraDistance) / 2f;

        if (togglingEarth)
        {
            float defaultDistance = minCameraDistance + (midpointDistance - minCameraDistance) * closerFraction;
            maxCameraDistance = 30000f;
            distance = defaultDistance;
        }
        else
        {
            float defaultDistance;
            if (inEarthCam)
            {
                defaultDistance = 2500f;
            }
            else
            {
                defaultDistance = minCameraDistance + (midpointDistance - minCameraDistance) * closerFraction;
            }
            maxCameraDistance = 10000f;

            distance = defaultDistance;
        }

        //Debug.Log($"[CAMERA MOVEMENT]: Camera target set to {body.name}. Min Distance: {minCameraDistance}, Max Distance: {maxCameraDistance}");
    }

    /**
    * Sets the real celestial body as the camera's target.
    * @param newTarget - New target for camera to track
    **/
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

    /**
    * Sets the Earth as the cameras target
    * @param Earth - New target for camera to track
    **/
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

    /**
    * Sets a placeholder object as the camera's target.
    * @param planet - Placeholder for camera to track while object is being placed
    **/
    public void SetTargetBodyPlaceholder(Transform planet)
    {
        targetBody = null;
        targetPlaceholder = planet;

        if (planet != null)
        {
            placeholderBodyRadius = planet.localScale.x * 1f;
            distance = 2f * placeholderBodyRadius;
            height = 0.2f * placeholderBodyRadius;
            //Debug.Log($"[CAMERA MOVEMENT]: Camera now tracks placeholder: {planet.name}, radius={placeholderBodyRadius}");
        }
        else
        {
            Debug.Log("[CAMERA MOVEMENT]: SetTargetBodyPlaceholder called with null. No placeholder assigned.");
        }
    }

    /**
    * Handles all zoom for track cam
    **/
    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            float sizeMultiplier = Mathf.Clamp(targetBody != null ? targetBody.radius / 20f : .4f, 1f, 20f);
            float distanceFactor = Mathf.Clamp(distance * sizeMultiplier * .1f, .5f, 100f);
            float zoomSpeed = baseZoomSpeed * distanceFactor * 3f;

            distance -= scroll * zoomSpeed;
            distance = Mathf.Clamp(distance, minCameraDistance, maxCameraDistance);
        }
    }

    /**
    * Updates the velocity and altitude display in the UI.
    **/
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
            float altitude = targetBody.altitude;
            altitudeText.text = $"Altitude: {altitude * 10:F2} km";
        }

        if (trackingObjectNameText != null && targetBody != null)
        {
            trackingObjectNameText.text = $"{targetBody.name}";
        }
    }
}