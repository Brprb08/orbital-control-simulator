using UnityEngine;

public class CameraMovement : MonoBehaviour
{
    public Transform target; // The planet to follow
    public float distance = 100f; // Distance from the target
    public float height = 30f; // Height above the target
    public float smoothSpeed = 0.05f; // Smoothness of the camera's movement
    public float rotationSmoothSpeed = 5f; // Smoothness of the camera's rotation
    public float lookAheadDistance = 20f; // Distance ahead to look based on velocity

    private Rigidbody targetRigidbody; // For calculating velocity-based look direction

    void Start()
    {
        // Ensure the target has a Rigidbody (optional but recommended)
        if (target != null)
        {
            targetRigidbody = target.GetComponent<Rigidbody>();
            if (targetRigidbody == null)
            {
                Debug.LogWarning("Target does not have a Rigidbody! Using forward direction instead.");
            }
        }
    }

    void LateUpdate()
    {
        if (target == null)
        {
            Debug.LogError("CameraMovement: No target assigned!");
            return;
        }

        // Calculate the desired position of the camera relative to the target
        Vector3 desiredPosition = target.position - target.forward.normalized * distance + Vector3.up * height;

        // Smoothly move the camera to the desired position
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);

        // Determine the look-ahead direction
        Vector3 velocityDirection;
        if (targetRigidbody != null && targetRigidbody.linearVelocity.magnitude > 0.1f)
        {
            // Use the velocity vector for the look-ahead direction
            velocityDirection = targetRigidbody.linearVelocity.normalized;
        }
        else
        {
            // Fallback to the target's forward direction if no velocity
            velocityDirection = target.forward.normalized;
        }

        // Calculate the look-at point based on the look-ahead distance
        Vector3 lookTarget = target.position + velocityDirection * lookAheadDistance;

        // Smoothly rotate the camera to look at the calculated target
        Quaternion targetRotation = Quaternion.LookRotation(lookTarget - transform.position);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSmoothSpeed * Time.deltaTime);
    }
}