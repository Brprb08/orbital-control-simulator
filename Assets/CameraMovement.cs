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

    public float baseZoomSpeed = 100f; // Base zoom speed
    public float maxDistance = 1000f; // Maximum distance from the target
    private float minDistance = 0.1f;  // Minimum distance from the target (updated dynamically)

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
        if (targetBody == null || !enabled) return;

        // Ensure the pivot is at the target body's position
        transform.position = targetBody.transform.position;

        // Dynamically adjust min distance based on the target radius
        // Smaller objects allow much closer zoom; larger objects push the camera farther away
        if (targetBody.radius <= 0.5f) // For very small objects
        {
            minDistance = Mathf.Max(0.01f, targetBody.radius * .7f); // Allow extremely close zoom
        }
        else if (targetBody.radius > 0.5f && targetBody.radius <= 100f) // Medium-sized objects
        {
            minDistance = targetBody.radius * 5f; // Maintain proportional zoom for medium objects
        }
        else // Large objects like the moon
        {
            minDistance = targetBody.radius + 400f; // Add a larger buffer for massive objects
        }

        // Clamp the distance to ensure it's within the valid range
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        // Adjust zoom speed dynamically based on the object's size
        float zoomSpeed = baseZoomSpeed * Mathf.Clamp(distance / minDistance, 0.5f, 20f);

        // Handle zoom input
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scrollInput) > 0.01f)
        {
            // Adjust the distance based on zoom input
            distance -= scrollInput * zoomSpeed;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);

            Debug.Log($"Zoom adjusted. New distance: {distance}, Min Distance: {minDistance}");
        }

        // Smoothly move the camera to the new position
        if (mainCamera != null)
        {
            Vector3 targetLocalPosition = new Vector3(0, height, -distance); // Negative Z for proper offset
            mainCamera.transform.localPosition = Vector3.Lerp(mainCamera.transform.localPosition, targetLocalPosition, Time.deltaTime * 10f);

            // Ensure the camera looks at the target
            mainCamera.transform.LookAt(transform.position);
        }

        UpdateVelocityAndAltitudeUI();
    }

    public void SetTargetBody(NBody newTarget)
    {
        targetBody = newTarget;

        if (targetBody != null)
        {
            // Update the pivot position to the new target's position
            transform.position = targetBody.transform.position;

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
    }
}