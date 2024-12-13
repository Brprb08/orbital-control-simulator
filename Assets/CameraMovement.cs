using UnityEngine;

public class CameraMovement : MonoBehaviour
{
    public NBody targetBody; // Reference to the NBody script for accessing predictionRenderer
    public float distance = 100f; // Distance behind the target
    public float height = 30f; // Height above the target
    public float smoothSpeed = 0.1f; // Smoothness of camera movement
    public float rotationSmoothSpeed = 0.1f; // Smoothness of camera rotation

    private LineRenderer predictionLine; // Reference to the prediction line renderer

    void Start()
    {
        if (targetBody != null)
        {
            predictionLine = targetBody.GetComponentInChildren<LineRenderer>();
            if (predictionLine == null)
            {
                Debug.LogError("CameraMovement: Prediction LineRenderer is not found in the target NBody!");
            }
        }
        else
        {
            Debug.LogError("CameraMovement: Target NBody is not assigned!");
        }
    }

    void LateUpdate()
    {
        if (targetBody == null)
        {
            Debug.LogError("CameraMovement: Target NBody is not assigned!");
            return;
        }

        // Step 1: Lock onto the target if prediction line is incomplete
        if (predictionLine == null || predictionLine.positionCount < 2)
        {
            LockOntoTarget();
            return;
        }

        // Step 2: Calculate trajectory direction from the prediction line
        Vector3 trajectoryDirection = GetPredictionDirection();
        if (trajectoryDirection == Vector3.zero)
        {
            Debug.LogWarning("CameraMovement: Trajectory direction is zero, falling back to target forward direction.");
            trajectoryDirection = targetBody.transform.forward;
        }

        // Step 3: Calculate the desired camera position behind the target
        Vector3 desiredPosition = targetBody.transform.position - trajectoryDirection.normalized * distance + Vector3.up * height;

        // Debugging the desired position
        Debug.DrawLine(targetBody.transform.position, desiredPosition, Color.blue, 0.1f);

        // Smoothly move the camera to the desired position
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);

        // Step 4: Make the camera look forward along the prediction line
        Vector3 lookTarget = GetPredictionLookTarget();
        Quaternion targetRotation = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);

        // Debugging the look target
        Debug.DrawLine(transform.position, lookTarget, Color.green, 0.1f);

        // Smoothly rotate the camera
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSmoothSpeed);
    }

    // Lock onto the target when the prediction line is not ready
    private void LockOntoTarget()
    {
        // Step 1: Calculate the fallback direction
        Vector3 fallbackDirection;

        // Use the target body's velocity if available
        if (targetBody.velocity.magnitude > 0.01f)
        {
            fallbackDirection = targetBody.velocity.normalized;
        }
        else
        {
            // Fall back to transform.forward if velocity is too small
            fallbackDirection = targetBody.transform.forward;
        }

        // Step 2: Position the camera directly behind the target
        Vector3 desiredPosition = targetBody.transform.position - fallbackDirection * distance + Vector3.up * height;

        // Smoothly move to the fallback position
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);

        // Step 3: Make the camera look at the target
        Quaternion targetRotation = Quaternion.LookRotation(targetBody.transform.position - transform.position, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSmoothSpeed);

        // Debugging fallback behavior
        Debug.DrawLine(transform.position, targetBody.transform.position, Color.yellow, 0.1f);
        Debug.Log("CameraMovement: Locking onto target while waiting for prediction line.");
    }

    // Calculate trajectory direction from the prediction line
    private Vector3 GetPredictionDirection()
    {
        // Use the first two points of the prediction line to calculate the direction
        Vector3 startPoint = predictionLine.GetPosition(0);
        Vector3 nextPoint = predictionLine.GetPosition(1);
        return (nextPoint - startPoint).normalized;
    }

    // Get the look target farther along the prediction line
    private Vector3 GetPredictionLookTarget()
    {
        // Use a point farther along the prediction line
        int lookIndex = Mathf.Min(10, predictionLine.positionCount - 1); // Adjust index to avoid out-of-bounds
        return predictionLine.GetPosition(lookIndex);
    }
}