using UnityEngine;
using TMPro;
using System.Collections;

/**
 * CameraMovement handles camera positioning and UI updates while tracking celestial bodies.
 * Supports switching between real NBody objects and placeholder objects.
 */
public class CameraMovement : MonoBehaviour
{
    [Header("Tracking Target")]
    public NBody targetBody; // The celestial body to track.
    public Transform targetPlaceholder; // Placeholder object to track when no real NBody is set.
    public CameraController cameraController;

    [Header("Camera Settings")]
    public float distance = 100f; // Default distance from the target.
    public float height = 30f; // Default height from the target.
    public float baseZoomSpeed = 100f; // Base zoom speed for mouse scroll.
    public float maxDistance = 7000f; // Maximum distance from the target.

    [Header("UI References")]
    public TextMeshProUGUI velocityText; // UI element for displaying velocity.
    public TextMeshProUGUI altitudeText; // UI element for displaying altitude.
    public TextMeshProUGUI trackingObjectNameText; // UI element for displaying the tracked object's name.

    private float minDistance = 0.1f; // Minimum distance from the target (adjusted dynamically).
    private float placeholderRadius = 0f; // Radius of the placeholder object.
    private Camera mainCamera; // Main camera reference.

    /**
     * Initializes the main camera and sets the starting position relative to the target.
     */
    void Start()
    {
        mainCamera = GetComponentInChildren<Camera>();

        if (targetBody != null && mainCamera != null)
        {
            // transform.position = targetBody.transform.position;
            // distance = Mathf.Clamp(distance, minDistance, maxDistance);
            // mainCamera.transform.localPosition = new Vector3(0, height, -distance);
            // mainCamera.transform.localRotation = Quaternion.identity;
            // mainCamera.transform.LookAt(transform.position);
            Debug.Log($"Camera initialized at distance: {distance}, position: {mainCamera.transform.localPosition}");
        }
    }

    /**
     * Updates the camera's position, zoom, and UI each frame.
     */
    void LateUpdate()
    {
        if (targetBody == null && targetPlaceholder == null) return;
        if (mainCamera == null) return;

        bool usingPlaceholder = (targetBody == null && targetPlaceholder != null);
        float radius = usingPlaceholder ? placeholderRadius : targetBody.radius;
        transform.position = usingPlaceholder ? targetPlaceholder.position : targetBody.transform.position;

        // Dynamically adjust the minimum distance based on the radius.
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

        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        // Handle scroll-wheel zoom.
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            float sizeMultiplier = Mathf.Clamp(targetBody != null ? targetBody.radius / 20f : 1f, 1f, 20f);  // Boost zoom speed for large bodies
            float distanceFactor = Mathf.Clamp(distance / minDistance, 0.5f, 50f);  // Scale with current distance
            float zoomSpeed = baseZoomSpeed * sizeMultiplier * distanceFactor * 2f;  // Base speed with extra scaling

            distance -= scroll * zoomSpeed;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        // Smoothly move the camera behind/above the pivot.
        Vector3 targetLocalPos = new Vector3(0f, height, -distance);
        mainCamera.transform.localPosition = Vector3.Lerp(
            mainCamera.transform.localPosition,
            targetLocalPos,
            Time.deltaTime * 10f
        );

        // Make the camera look at the pivot.
        mainCamera.transform.LookAt(transform.position);

        if (!usingPlaceholder)
        {
            UpdateVelocityAndAltitudeUI();
        }
    }

    /**
     * Sets the real celestial body as the camera's target.
     */
    public void SetTargetBody(NBody newTarget)
    {
        Debug.Log($"Setting target body: {newTarget.name}");

        // Hide apogee and perigee lines for all other bodies
        foreach (var body in GravityManager.Instance.Bodies)
        {
            if (body != newTarget)  // Skip the current target
            {
                body.SetApogeePerigeeVisibility(false);
            }
        }

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

            // Adjust closer for small objects.
            float closerFraction = targetBody.radius <= 10f ? 0.15f : 0.25f;  // Closer for very small objects.
            float defaultDistance = minDistance + (midpointDistance - minDistance) * closerFraction;
            maxDistance = 4000f;
            // Always reset to this default distance on switching.
            distance = defaultDistance;

            targetBody.SetApogeePerigeeVisibility(true);
            Debug.Log($"Camera target set to {targetBody.name}. Min Distance: {minDistance}, Max Distance: {maxDistance}");
        }
    }

    /**
     * Sets a placeholder object as the camera's target.
     */
    public void SetTargetBodyPlaceholder(Transform planet)
    {
        targetBody = null;
        targetPlaceholder = planet;

        if (planet != null)
        {
            placeholderRadius = planet.localScale.x * 10f;
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
     */
    void UpdateVelocityAndAltitudeUI()
    {
        if (velocityText != null && targetBody != null)
        {
            float velocityMagnitude = targetBody.velocity.magnitude;
            float velocityInMetersPerSecond = velocityMagnitude * 10000f;
            float velocityInMph = velocityInMetersPerSecond * 2.23694f;
            velocityText.text = $"Velocity: {velocityInMetersPerSecond:F2} m/s ({velocityInMph:F2} mph)";
        }

        if (altitudeText != null && targetBody != null)
        {
            float altitude = targetBody.altitude;
            float altitudeInFeet = altitude * 3280.84f;
            altitudeText.text = $"Altitude: {altitude:F2} km ({altitudeInFeet:F0} ft)";
        }

        if (trackingObjectNameText != null && targetBody != null)
        {
            trackingObjectNameText.text = $"{targetBody.name}";
        }
    }

    /**
     * Calculates the minimum distance based on the radius of the object being tracked.
     */
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
     */
    private float CalculateMaxDistance(float radius)
    {
        float minimumMaxDistance = 2000f;  // Ensure you can always zoom out at least this far.

        if (radius <= 0.5f)
        {
            return Mathf.Max(minimumMaxDistance, radius * 500f);  // Small objects need a large zoom-out factor.
        }
        else if (radius > 0.5f && radius <= 100f)
        {
            return Mathf.Max(minimumMaxDistance, radius * 100f);  // Medium-sized objects.
        }
        else
        {
            return Mathf.Max(minimumMaxDistance, radius + 2000f);  // Large planets.
        }
    }
}