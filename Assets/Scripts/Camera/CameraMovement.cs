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
    public float maxDistance = 50000f;

    [Header("UI References")]
    public TextMeshProUGUI velocityText;
    public TextMeshProUGUI altitudeText;
    public TextMeshProUGUI trackingObjectNameText;

    [Header("Camera Variables")]
    private float minDistance = 0.1f;
    private float placeholderRadius = 0f;
    private Camera mainCamera;
    public bool inEarthView = false;
    public NBody tempEarthBody;

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
        DontDestroyOnLoad(gameObject);
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
        float radius = usingPlaceholder ? placeholderRadius : targetBody.radius;
        if (inEarthView)
        {
            transform.position = tempEarthBody.transform.position;
        }
        else
        {
            transform.position = usingPlaceholder ? targetPlaceholder.position : targetBody.transform.position;
        }

        if (radius <= 0.5f)
        {
            minDistance = Mathf.Max(0.01f, radius * 0.7f);
        }
        else if (radius > 0.5f && radius <= 100f)
        {
            minDistance = radius * 5f;
        }
        else
        {
            minDistance = radius + 400f;
        }

        if (inEarthView)
        {
            minDistance = 800f;
        }
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            float sizeMultiplier = Mathf.Clamp(targetBody != null ? targetBody.radius / 20f : .4f, 1f, 20f);
            float distanceFactor = Mathf.Clamp(distance * sizeMultiplier * .1f, .5f, 100f);
            Debug.LogError(distanceFactor);
            float zoomSpeed = baseZoomSpeed * distanceFactor * 3f;

            distance -= scroll * zoomSpeed;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

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
    * Sets the real celestial body as the camera's target.
    * @param newTarget - New target for camera to track
    **/
    public void SetTargetBody(NBody newTarget)
    {
        targetBody = newTarget;
        targetPlaceholder = null;

        if (targetBody != null)
        {
            transform.position = targetBody.transform.position;

            if (float.IsNaN(transform.position.x) || float.IsNaN(transform.position.y) || float.IsNaN(transform.position.z))
            {
                Debug.LogError($"[ERROR] Camera transform is NaN after setting target {targetBody.name}");
            }

            minDistance = CalculateMinDistance(targetBody.radius);
            maxDistance = CalculateMaxDistance(targetBody.radius);
            float midpointDistance = (minDistance + maxDistance) / 2f;

            float closerFraction = targetBody.radius <= 10f ? 0.15f : 0.25f;

            float defaultDistance;
            if (inEarthView)
            {
                defaultDistance = 2500f;
            }
            else
            {
                defaultDistance = minDistance + (midpointDistance - minDistance) * closerFraction;
            }
            maxDistance = 10000f;

            distance = defaultDistance;

            Debug.Log($"Camera target set to {targetBody.name}. Min Distance: {minDistance}, Max Distance: {maxDistance}");
        }
    }

    // Sets the earth as the camera track
    public void SetTargetBodyTemp(NBody newTarget)
    {
        if (inEarthView)
        {
            inEarthView = false;
            targetBody = newTarget;
        }
        else
        {
            inEarthView = true;
            tempEarthBody = newTarget;
        }
        targetPlaceholder = null;

        if (newTarget != null)
        {
            transform.position = targetBody.transform.position;

            if (float.IsNaN(transform.position.x) || float.IsNaN(transform.position.y) || float.IsNaN(transform.position.z))
            {
                Debug.LogError($"[ERROR] Camera transform is NaN after setting target {tempEarthBody.name}");
            }

            minDistance = CalculateMinDistance(tempEarthBody.radius) * 5;
            maxDistance = CalculateMaxDistance(tempEarthBody.radius);
            float midpointDistance = (minDistance + maxDistance) / 2f;

            float closerFraction = tempEarthBody.radius <= 10f ? 0.15f : 0.25f;
            float defaultDistance = minDistance + (midpointDistance - minDistance) * closerFraction;
            maxDistance = 30000f;

            distance = defaultDistance;

            Debug.Log($"Camera target set to {tempEarthBody.name}. Min Distance: {minDistance}, Max Distance: {maxDistance}");
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
            placeholderRadius = planet.localScale.x * 1f;
            distance = 2f * placeholderRadius;
            height = 0.2f * placeholderRadius;
            Debug.Log($"Camera now tracks placeholder: {planet.name}, radius={placeholderRadius}");
        }
        else
        {
            Debug.Log("SetTargetBodyPlaceholder called with null. No placeholder assigned.");
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
            // float velocityInMph = velocityInMetersPerSecond * 2.23694f;
            velocityText.text = $"Velocity: {velocityInMetersPerSecond:F2} m/s";
        }

        if (altitudeText != null && targetBody != null)
        {
            float altitude = targetBody.altitude;
            // float altitudeInFeet = altitude * 3280.84f;
            altitudeText.text = $"Altitude: {altitude * 10:F2} km";
        }

        if (trackingObjectNameText != null && targetBody != null)
        {
            trackingObjectNameText.text = $"{targetBody.name}";
        }
    }

    /**
    * Calculates the minimum distance based on the radius of the object being tracked.
    * @param radius - Radius of object being tracked by camera
    **/
    private float CalculateMinDistance(float radius)
    {
        if (radius <= 0.5f)
        {
            return Mathf.Max(0.4f, radius * 10f);
        }
        else if (radius > 0.5f && radius <= 100f)
        {
            return radius * 4f;
        }
        else
        {
            return radius + 400f;
        }
    }

    /**
    * Calculates the maximum distance based on the radius of the object being tracked.
    * @param radius - Radius of object being tracked by camera
    **/
    private float CalculateMaxDistance(float radius)
    {
        float minimumMaxDistance = 2000f;

        if (radius <= 0.5f)
        {
            return Mathf.Max(minimumMaxDistance, radius * 500f);  // Small objects
        }
        else if (radius > 0.5f && radius <= 100f)
        {
            return Mathf.Max(minimumMaxDistance, radius * 100f);  // Medium objects
        }
        else
        {
            return Mathf.Max(minimumMaxDistance, radius + 2000f);  // Large onjects
        }
    }
}