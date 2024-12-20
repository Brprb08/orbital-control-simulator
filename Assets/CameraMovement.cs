using UnityEngine;
using TMPro;

public class CameraMovement : MonoBehaviour
{
    public NBody targetBody;
    public float distance = 100f;
    public float height = 30f;
    public TextMeshProUGUI velocityText; // Assign this in the Inspector
    public TextMeshProUGUI altitudeText; // Assign this in the Inspector

    private bool recentlySwitched = false;
    private float switchCooldown = 0.5f;
    private float switchTimer = 0f;

    void LateUpdate()
    {
        if (targetBody == null)
        {
            // Stop updating the camera position if no target is set
            return;
        }

        if (recentlySwitched)
        {
            switchTimer -= Time.deltaTime;
            if (switchTimer > 0f)
            {
                LockOntoTargetExact();
                UpdateVelocityAndAltitudeUI(); // Update UI even when waiting
                return;
            }
            else
            {
                recentlySwitched = false;
            }
        }

        // Simple tracking using velocity direction
        Vector3 velocityDirection = (targetBody.velocity.magnitude > 0.01f)
            ? targetBody.velocity.normalized
            : targetBody.transform.forward;

        Vector3 desiredPosition = targetBody.transform.position - velocityDirection * distance + Vector3.up * height;
        transform.position = desiredPosition;
        transform.LookAt(targetBody.transform.position);

        UpdateVelocityAndAltitudeUI();
    }

    private void LockOntoTargetExact()
    {
        if (targetBody == null) return;

        Vector3 velocityDirection = (targetBody.velocity.magnitude > 0.01f)
            ? targetBody.velocity.normalized
            : targetBody.transform.forward;

        Vector3 desiredPosition = targetBody.transform.position - velocityDirection * distance + Vector3.up * height;
        transform.position = desiredPosition;
        transform.LookAt(targetBody.transform.position);
    }

    public void SetTargetBody(NBody newTarget)
    {
        targetBody = newTarget;
        recentlySwitched = true;
        switchTimer = switchCooldown;

        if (targetBody != null)
        {
            Vector3 velocityDirection = (targetBody.velocity.magnitude > 0.01f)
                ? targetBody.velocity.normalized
                : targetBody.transform.forward;

            Vector3 snapPosition = targetBody.transform.position - velocityDirection * distance + Vector3.up * height;
            transform.position = snapPosition;
            transform.LookAt(targetBody.transform.position);
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