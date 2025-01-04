using UnityEngine;
using System.Collections;
using TMPro;

public class CameraMovement : MonoBehaviour
{
    public NBody targetBody; // The celestial body to track
    public float distance = 100f; // Default distance from the target
    public float height = 30f;   // Default height from the target

    public TextMeshProUGUI velocityText; // Assign in Inspector
    public TextMeshProUGUI altitudeText; // Assign in Inspector
    public TextMeshProUGUI trackingObjectNameText; // Assign in Inspector

    public float baseZoomSpeed = 100f; // Base zoom speed
    public float maxDistance = 1000f; // Maximum distance from the target
    private float minDistance = 0.1f;  // Minimum distance from the target (updated dynamically)
    public Transform targetPlaceholder;   // <--- ADD THIS
    private float placeholderRadius = 0f;
    private Camera mainCamera;


    void Start()
    {
        mainCamera = GetComponentInChildren<Camera>();

        if (targetBody != null && mainCamera != null)
        {
            // Move the pivot to the target's position
            transform.position = targetBody.transform.position;

            // Calculate the starting distance
            distance = Mathf.Clamp(distance, minDistance, maxDistance);

            // Explicitly set the camera position to avoid glitches
            mainCamera.transform.localPosition = new Vector3(0, height, -distance);
            mainCamera.transform.localRotation = Quaternion.identity;

            // Force the camera to look at the target body
            mainCamera.transform.LookAt(transform.position);

            Debug.Log($"Camera initialized at distance: {distance}, position: {mainCamera.transform.localPosition}");
        }
    }

    void LateUpdate()
    {
        if (targetBody == null && targetPlaceholder == null) return;
        if (mainCamera == null) return;

        // 2) Decide which target to track and figure out radius
        bool usingPlaceholder = (targetBody == null && targetPlaceholder != null);
        float radius;
        if (!usingPlaceholder)
        {
            // Tracking a real NBody
            radius = targetBody.radius;
            // Place pivot at NBody position
            transform.position = targetBody.transform.position;
        }
        else
        {
            // Tracking a placeholder
            radius = placeholderRadius;
            // Place pivot at placeholder position
            transform.position = targetPlaceholder.position;
        }

        // 3) Compute minDistance from the radius (like your existing logic)
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

        // Clamp distance
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        // 4) Scroll-wheel zoom
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            float zoomSpeed = baseZoomSpeed * Mathf.Clamp(distance / minDistance, 0.5f, 20f);
            distance -= scroll * zoomSpeed;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        // 5) Smoothly move camera behind/above pivot (this transform)
        Vector3 targetLocalPos = new Vector3(0f, height, -distance);
        mainCamera.transform.localPosition = Vector3.Lerp(
            mainCamera.transform.localPosition,
            targetLocalPos,
            Time.deltaTime * 10f
        );

        // 6) Make the camera look at the pivot (which is at planet center)
        mainCamera.transform.LookAt(transform.position);

        // 7) Update UI only if we have a real body
        if (!usingPlaceholder)
        {
            UpdateVelocityAndAltitudeUI();
        }
    }

    public void SetTargetBody(NBody newTarget)
    {
        targetBody = newTarget;

        if (targetBody != null)
        {
            // Update the pivot position to the new target's position
            transform.position = targetBody.transform.position;
            if (float.IsNaN(transform.position.x) || float.IsNaN(transform.position.y) || float.IsNaN(transform.position.z))
            {
                Debug.LogError($"[ERROR] Camera transform is NaN after setting target {targetBody.name}");
            }

            // Dynamically adjust min distance based on the target radius
            if (targetBody.radius <= 0.5f)
            {
                minDistance = Mathf.Max(0.1f, targetBody.radius * 10f);
            }
            else if (targetBody.radius > 0.5f && targetBody.radius <= 100f)
            {
                minDistance = targetBody.radius * 2f;
            }
            else
            {
                minDistance = targetBody.radius + 400f;
            }

            // Clamp the distance to ensure it's within the valid range
            distance = Mathf.Clamp(distance, minDistance, maxDistance);

            Debug.Log($"Camera target set to {targetBody.name}. Min Distance: {minDistance}, Max Distance: {maxDistance}");
        }
    }

    public void SetTargetBodyPlaceholder(Transform planet)
    {
        targetBody = null;
        targetPlaceholder = planet;

        // Compute placeholder radius from localScale (like your real NBody logic)
        if (planet != null)
        {
            float scaleX = planet.localScale.x;
            placeholderRadius = scaleX * 10f;
            distance = 2f * placeholderRadius;
            height = 0.2f * placeholderRadius;

            Debug.Log($"Camera now tracks placeholder: {planet.name}, radius={placeholderRadius}");
        }
        else
        {
            Debug.Log("SetTargetBodyPlaceholder called with null. No placeholder assigned.");
        }
    }
    void UpdateVelocityAndAltitudeUI()
    {
        if (velocityText != null && targetBody != null)
        {
            float velocityMagnitude = targetBody.velocity.magnitude; // units/s
            float velocityInMetersPerSecond = velocityMagnitude * 10000f;
            float velocityInMph = velocityInMetersPerSecond * 2.23694f;
            velocityText.text = $"Velocity: {velocityInMetersPerSecond:F2} m/s ({velocityInMph:F2} mph)";
        }

        if (altitudeText != null && targetBody != null)
        {
            float altitude = targetBody.altitude; // Get altitude from NBody
            float altitudeInFeet = altitude * 3280.84f; // Convert km to feet
            altitudeText.text = $"Altitude: {altitude:F2} km ({altitudeInFeet:F0} ft)";
        }

        if (trackingObjectNameText != null)
        {
            trackingObjectNameText.text = $"{targetBody.name}";
        }
    }
}