using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class ThrustController : MonoBehaviour
{
    [Header("Thrust Settings")]
    public float maxForwardThrustMagnitude = 10f; // Adjust based on simulation scale
    public float maxReverseThrustMagnitude = 10f;
    public float maxLateralThrustMagnitude = 10f; // For left/right thrust
    public float thrustRampUpTime = 2f;

    [Header("Visual Feedback")]
    public ParticleSystem forwardThrustParticles;
    public ParticleSystem reverseThrustParticles;
    public ParticleSystem leftThrustParticles;
    public ParticleSystem rightThrustParticles;

    public bool isForwardThrustActive = false;
    public bool isReverseThrustActive = false;
    public bool isLeftThrustActive = false;
    public bool isRightThrustActive = false;

    private Coroutine thrustCoroutine = null;

    [Header("References")]
    public CameraController cameraController;

    void Start()
    {
        if (cameraController == null)
        {
            cameraController = GravityManager.Instance.GetComponent<CameraController>();
            if (cameraController == null)
            {
                Debug.LogError("ThrustController: CameraController reference not set and not found on GravityManager.");
            }
        }
    }

    void Update()
    {
        if (cameraController == null) return;

        NBody currentTargetBody = cameraController.cameraMovement.targetBody;
        if (currentTargetBody == null) return;

        Vector3 planetUp = currentTargetBody.transform.position.normalized;  // Local "up" for correct lateral calculation
        Vector3 rightThrust = Vector3.Cross(planetUp, currentTargetBody.velocity.normalized);
        Vector3 leftThrust = -rightThrust;

        if (isForwardThrustActive)
            ApplyThrust(currentTargetBody, maxForwardThrustMagnitude, currentTargetBody.velocity.normalized);

        if (isReverseThrustActive)
            ApplyThrust(currentTargetBody, -maxReverseThrustMagnitude, currentTargetBody.velocity.normalized);

        if (isRightThrustActive)
            ApplyThrust(currentTargetBody, maxLateralThrustMagnitude, rightThrust);

        if (isLeftThrustActive)
            ApplyThrust(currentTargetBody, maxLateralThrustMagnitude, leftThrust);
    }

    private void ApplyThrust(NBody targetBody, float magnitude, Vector3 thrustDirection)
    {
        if (targetBody.velocity == Vector3.zero)
        {
            Debug.LogWarning($"ThrustController: {targetBody.name} has zero velocity. Thrust direction undefined.");
            return;
        }

        // Use the true normal vector to apply pure lateral thrust
        Vector3 positionRelativeToCenter = targetBody.transform.position.normalized;
        Vector3 velocityDirection = targetBody.velocity.normalized;

        // "Normal" thrust direction: perpendicular to orbit
        Vector3 normalDirection = Vector3.Cross(positionRelativeToCenter, velocityDirection).normalized;

        // Apply normal thrust (for inclination change)
        Vector3 adjustedThrustDirection = thrustDirection.normalized;
        float thrustFactor = 10f;
        float scaledMagnitude = magnitude * thrustFactor;

        targetBody.AddForce(adjustedThrustDirection * scaledMagnitude);
        Debug.DrawRay(targetBody.transform.position, adjustedThrustDirection * scaledMagnitude * 1e6f, Color.blue, 0.1f);
    }

    #region UI Button Handlers
    public void StartForwardThrust() { isForwardThrustActive = true; if (forwardThrustParticles) forwardThrustParticles.Play(); }
    public void StopForwardThrust() { isForwardThrustActive = false; if (forwardThrustParticles) forwardThrustParticles.Stop(); }
    public void StartReverseThrust() { isReverseThrustActive = true; if (reverseThrustParticles) reverseThrustParticles.Play(); }
    public void StopReverseThrust() { isReverseThrustActive = false; if (reverseThrustParticles) reverseThrustParticles.Stop(); }
    public void StartLeftThrust() { isLeftThrustActive = true; if (leftThrustParticles) leftThrustParticles.Play(); }
    public void StopLeftThrust() { isLeftThrustActive = false; if (leftThrustParticles) leftThrustParticles.Stop(); }
    public void StartRightThrust() { isRightThrustActive = true; if (rightThrustParticles) rightThrustParticles.Play(); }
    public void StopRightThrust() { isRightThrustActive = false; if (rightThrustParticles) rightThrustParticles.Stop(); }
    #endregion
}