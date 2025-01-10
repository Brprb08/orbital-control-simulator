using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class ThrustController : MonoBehaviour
{
    [Header("Thrust Settings")]
    public float maxForwardThrustMagnitude = 10f; // Adjust based on simulation scale
    public float maxReverseThrustMagnitude = 10f;
    public float maxLateralThrustMagnitude = 10f; // For left/right thrust
    public float maxRadialThrustMagnitude = 10f;
    public float thrustRampUpTime = 2f;

    [Header("Visual Feedback")]
    public ParticleSystem forwardThrustParticles;
    public ParticleSystem reverseThrustParticles;
    public ParticleSystem leftThrustParticles;
    public ParticleSystem rightThrustParticles;
    public ParticleSystem radialInThrustParticles;
    public ParticleSystem radialOutThrustParticles;

    public bool isForwardThrustActive = false;
    public bool isReverseThrustActive = false;
    public bool isLeftThrustActive = false;
    public bool isRightThrustActive = false;
    public bool isRadialInThrustActive = false;
    public bool isRadialOutThrustActive = false;

    private Coroutine thrustCoroutine = null;

    private float currentThrustFactor = 0f;

    [Header("References")]
    public CameraController cameraController;

    private float thrustFactor = 1f;
    private float thrustDuration = 0f;
    private float desiredAcceleration = 5f;

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

    void FixedUpdate()
    {
        if (cameraController == null) return;

        NBody currentTargetBody = cameraController.cameraMovement.targetBody;
        if (currentTargetBody == null) return;

        Vector3 planetUp = currentTargetBody.transform.position.normalized;
        Vector3 rightThrust = Vector3.Cross(planetUp, currentTargetBody.velocity.normalized);
        Vector3 leftThrust = -rightThrust;

        bool anyThrustActive = false;

        if (isForwardThrustActive)
        {
            ApplyThrust(currentTargetBody, maxForwardThrustMagnitude, currentTargetBody.velocity.normalized);
            anyThrustActive = true;
        }

        if (isReverseThrustActive)
        {
            ApplyThrust(currentTargetBody, -maxReverseThrustMagnitude, -currentTargetBody.velocity.normalized);
            anyThrustActive = true;
        }

        if (isRightThrustActive)
        {
            ApplyThrust(currentTargetBody, maxLateralThrustMagnitude, rightThrust);
            anyThrustActive = true;
        }

        if (isLeftThrustActive)
        {
            ApplyThrust(currentTargetBody, maxLateralThrustMagnitude, leftThrust);
            anyThrustActive = true;
        }

        if (isRadialInThrustActive)
        {
            ApplyThrust(currentTargetBody, maxRadialThrustMagnitude, -planetUp);
            anyThrustActive = true;
        }

        if (isRadialOutThrustActive)
        {
            ApplyThrust(currentTargetBody, maxRadialThrustMagnitude, planetUp);
            anyThrustActive = true;
        }

        // Update thrust duration
        if (anyThrustActive)
        {
            thrustDuration += Time.fixedDeltaTime;
        }
        else
        {
            thrustDuration = 0f;
        }
    }

    public float GetThrustDuration()
    {
        return thrustDuration;
    }

    private void ApplyThrust(NBody targetBody, float magnitude, Vector3 thrustDirection, float rampedThrustFactor = 1f)
    {
        if (targetBody == null) return;

        Vector3 adjustedThrustDirection = thrustDirection.normalized;

        // Calculate the actual acceleration, scaled to account for 1 unit = 10 km
        float scaledMagnitude = magnitude / 10f;  // Divide by 10,000 to match simulation scale

        Debug.Log($"Applying Scaled Force: {scaledMagnitude} N in direction: {adjustedThrustDirection}, Mass: {targetBody.mass}");

        // Apply the scaled force
        targetBody.AddForce(adjustedThrustDirection * scaledMagnitude);
    }

    public Vector3 GetCurrentThrustImpulse()
    {
        Vector3 totalForce = Vector3.zero;

        NBody currentTargetBody = cameraController.cameraMovement.targetBody;
        if (currentTargetBody == null) return Vector3.zero;

        Vector3 planetUp = currentTargetBody.transform.position.normalized;
        Vector3 rightThrust = Vector3.Cross(planetUp, currentTargetBody.velocity.normalized).normalized;
        Vector3 leftThrust = -rightThrust;

        if (isForwardThrustActive)
            totalForce += currentTargetBody.velocity.normalized * maxForwardThrustMagnitude * thrustFactor;

        if (isReverseThrustActive)
            totalForce += -currentTargetBody.velocity.normalized * maxReverseThrustMagnitude * thrustFactor;

        if (isRightThrustActive)
            totalForce += rightThrust * maxLateralThrustMagnitude * thrustFactor;

        if (isLeftThrustActive)
            totalForce += leftThrust * maxLateralThrustMagnitude * thrustFactor;

        if (isRadialInThrustActive)
            totalForce += -planetUp * maxRadialThrustMagnitude * thrustFactor;

        if (isRadialOutThrustActive)
            totalForce += planetUp * maxRadialThrustMagnitude * thrustFactor;
        return totalForce;
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
    public void StartRadialInThrust() { isRadialInThrustActive = true; if (radialInThrustParticles) radialInThrustParticles.Play(); }
    public void StopRadialInThrust() { isRadialInThrustActive = false; if (radialInThrustParticles) radialInThrustParticles.Stop(); }
    public void StartRadialOutThrust() { isRadialOutThrustActive = true; if (radialOutThrustParticles) radialOutThrustParticles.Play(); }
    public void StopRadialOutThrust() { isRadialOutThrustActive = false; if (radialOutThrustParticles) radialOutThrustParticles.Stop(); }
    #endregion
}