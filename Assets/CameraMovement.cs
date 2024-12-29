using UnityEngine;
using TMPro;

public class CameraMovement : MonoBehaviour
{
    public NBody targetBody;
    public float distance = 100f; // Default distance
    public float height = 30f;   // Default height

    public TextMeshProUGUI velocityText; // Assign in Inspector
    public TextMeshProUGUI altitudeText; // Assign in Inspector

    // private bool recentlySwitched = false;
    private float switchCooldown = 0.5f;
    private float switchTimer = 0f;
    public float minDistance = 50f;  // Minimum distance from the planet
    public float maxDistance = 300f; // Maximum distance from the planet
    public float zoomSpeed = 10f;

    void Start()
    {
        Transform cameraTransform = GetComponentInChildren<Camera>()?.transform;

        if (targetBody != null && cameraTransform != null)
        {
            // Move the pivot to the target's position
            transform.position = targetBody.transform.position;

            // Calculate the starting distance based on the camera's local position
            distance = cameraTransform.localPosition.z; // Use Z-axis for distance
            distance = Mathf.Clamp(distance, minDistance, maxDistance); // Clamp to ensure valid range

            // Explicitly set the camera position to avoid glitches
            cameraTransform.localPosition = new Vector3(0, height, distance);
            cameraTransform.localRotation = Quaternion.identity;

            // Force the camera to look at the target body
            cameraTransform.LookAt(transform.position);

            Debug.Log($"Camera initialized at distance: {distance}, position: {cameraTransform.localPosition}");
        }
    }

    void LateUpdate()
    {
        if (targetBody == null || !enabled) return;

        // Ensure the pivot is at the target body's position
        transform.position = targetBody.transform.position;

        // Handle zoom input
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scrollInput) > 0.01f)
        {
            // Adjust distance based on zoom input
            distance -= scrollInput * zoomSpeed;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            Debug.Log($"Zoom adjusted. New distance: {distance}");
        }

        // Adjust the camera's position relative to the pivot
        Transform cameraTransform = GetComponentInChildren<Camera>()?.transform;
        if (cameraTransform != null)
        {
            // Directly set the position based on height and distance
            Vector3 targetLocalPosition = new Vector3(0, height, distance);
            cameraTransform.localPosition = Vector3.Lerp(cameraTransform.localPosition, targetLocalPosition, Time.deltaTime * 10f);

            // Ensure the camera looks at the target
            cameraTransform.LookAt(transform.position);
        }

        UpdateVelocityAndAltitudeUI();
    }

    private void LockOntoTargetExact()
    {
        if (targetBody == null) return;

        transform.position = targetBody.transform.position;
    }

    public void SetTargetBody(NBody newTarget)
    {
        targetBody = newTarget;
        // recentlySwitched = true;
        switchTimer = switchCooldown;

        if (targetBody != null)
        {
            transform.position = targetBody.transform.position;
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