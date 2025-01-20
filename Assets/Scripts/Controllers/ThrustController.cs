using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/**
* Manages the thrust system for a spacecraft or object, allowing for various directional thrusts 
* including forward, reverse, lateral, and radial directions. It provides visual feedback through
* particle systems and manages thrust force to the NBody object.
* 
* The class also handles UI button input to toggle thrust activation and tracks the duration 
* for any thrust that is applied.
**/
public class ThrustController : MonoBehaviour
{
    [Header("Thrust Settings")]
    public float maxForwardThrustMagnitude = 10f;
    public float maxReverseThrustMagnitude = 10f;
    public float maxLateralThrustMagnitude = 10f;
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

    [Header("References")]
    public CameraController cameraController;
    public TrajectoryRenderer trajectoryRenderer;

    private float thrustFactor = 1f;
    private float thrustDuration = 0f;

    public bool IsThrusting { get; private set; } = false;

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
            ApplyThrust(currentTargetBody, maxReverseThrustMagnitude, -currentTargetBody.velocity.normalized);
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

        IsThrusting = anyThrustActive;

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

    /**
    * Returns the total duration (in seconds) for any thrust that has been applied.
    * @return - The duration of active thrust.
    **/
    public float GetThrustDuration()
    {
        return thrustDuration;
    }

    /**
    * Applies a thrust force to the specified NBody object in a given direction and magnitude.
    * @param targetBody - The NBody to which the force is applied.
    * @param magnitude - The magnitude of the thrust force.
    * @param thrustDirection - The direction in which the thrust is applied.
    * @param rampedThrustFactor - An optional scaling factor for ramping the thrust.
    **/
    private void ApplyThrust(NBody targetBody, float magnitude, Vector3 thrustDirection, float rampedThrustFactor = 1f)
    {
        if (targetBody == null) return;

        Vector3 adjustedThrustDirection = thrustDirection.normalized;

        // Calculate the actual acceleration, scaled to account for 1 unit = 10 km
        float scaledMagnitude = magnitude / 10f;  // Divide by 10,000 to match simulation scale

        Debug.Log($"Applying Scaled Force: {scaledMagnitude} N in direction: {adjustedThrustDirection}, Mass: {targetBody.mass}");

        // Apply the scaled force
        targetBody.AddForce(adjustedThrustDirection * scaledMagnitude);

        trajectoryRenderer = FindObjectOfType<TrajectoryRenderer>();
        trajectoryRenderer.orbitIsDirty = true;
    }

    /**
    * Calculates and returns the current total thrust impulse applied as a Vector3.
    * @return - The current total thrust force in the form of a Vector3.
    **/
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

    // UI Button Handlers
    public void StartForwardThrust()
    {
        isForwardThrustActive = true;
        if (forwardThrustParticles)
        {
            forwardThrustParticles.Play();
        }
    }

    public void StopForwardThrust()
    {
        isForwardThrustActive = false;
        if (forwardThrustParticles)
        {
            forwardThrustParticles.Stop();
        }

        trajectoryRenderer = FindObjectOfType<TrajectoryRenderer>();
        trajectoryRenderer.orbitIsDirty = true;
        trajectoryRenderer.StartCoroutine(trajectoryRenderer.RecomputeTrajectory());
    }

    public void StartReverseThrust()
    {
        isReverseThrustActive = true;
        if (reverseThrustParticles)
        {
            reverseThrustParticles.Play();
        }
    }

    public void StopReverseThrust()
    {
        isReverseThrustActive = false;
        if (reverseThrustParticles)
        {
            reverseThrustParticles.Stop();
        }

        trajectoryRenderer = FindObjectOfType<TrajectoryRenderer>();
        trajectoryRenderer.orbitIsDirty = true;
        trajectoryRenderer.StartCoroutine(trajectoryRenderer.RecomputeTrajectory());
    }

    public void StartLeftThrust()
    {
        isLeftThrustActive = true;
        if (leftThrustParticles)
        {
            leftThrustParticles.Play();
        }
    }

    public void StopLeftThrust()
    {
        isLeftThrustActive = false;
        if (leftThrustParticles)
        {
            leftThrustParticles.Stop();
        }

        trajectoryRenderer = FindObjectOfType<TrajectoryRenderer>();
        trajectoryRenderer.orbitIsDirty = true;
        trajectoryRenderer.StartCoroutine(trajectoryRenderer.RecomputeTrajectory());
    }

    public void StartRightThrust()
    {
        isRightThrustActive = true;
        if (rightThrustParticles)
        {
            rightThrustParticles.Play();
        }
    }

    public void StopRightThrust()
    {
        isRightThrustActive = false;
        if (rightThrustParticles)
        {
            rightThrustParticles.Stop();
        }

        trajectoryRenderer = FindObjectOfType<TrajectoryRenderer>();
        trajectoryRenderer.orbitIsDirty = true;
        trajectoryRenderer.StartCoroutine(trajectoryRenderer.RecomputeTrajectory());
    }

    public void StartRadialInThrust()
    {
        isRadialInThrustActive = true;
        if (radialInThrustParticles)
        {
            radialInThrustParticles.Play();
        }
    }

    public void StopRadialInThrust()
    {
        isRadialInThrustActive = false;
        if (radialInThrustParticles)
        {
            radialInThrustParticles.Stop();
        }

        trajectoryRenderer = FindObjectOfType<TrajectoryRenderer>();
        trajectoryRenderer.orbitIsDirty = true;
        trajectoryRenderer.StartCoroutine(trajectoryRenderer.RecomputeTrajectory());
    }

    public void StartRadialOutThrust()
    {
        isRadialOutThrustActive = true;
        if (radialOutThrustParticles)
        {
            radialOutThrustParticles.Play();
        }
    }

    public void StopRadialOutThrust()
    {
        isRadialOutThrustActive = false;
        if (radialOutThrustParticles)
        {
            radialOutThrustParticles.Stop();
        }

        trajectoryRenderer = FindObjectOfType<TrajectoryRenderer>();
        trajectoryRenderer.orbitIsDirty = true;
        trajectoryRenderer.StartCoroutine(trajectoryRenderer.RecomputeTrajectory());
    }
}