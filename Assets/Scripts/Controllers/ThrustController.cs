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
    public static ThrustController Instance { get; private set; }

    [Header("Thrust Settings")]
    public float maxForwardThrustMagnitude = 10f;
    public float maxReverseThrustMagnitude = 10f;
    public float maxLateralThrustMagnitude = 10f;
    public float maxRadialThrustMagnitude = 10f;
    public float thrustRampUpTime = 2f;

    [Header("Visual Feedback")]
    public ParticleSystem thrustParticles;

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

    private bool thrustStopped = false;

    public bool IsThrusting
    {
        get
        {
            return isForwardThrustActive
                || isReverseThrustActive
                || isLeftThrustActive
                || isRightThrustActive
                || isRadialInThrustActive
                || isRadialOutThrustActive;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

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

        if (thrustParticles == null)
        {
            thrustParticles = GameObject.Find("Particle System").GetComponent<ParticleSystem>();

            if (thrustParticles == null)
            {
                Debug.LogError("ThrustController: No Particle System found in the scene!");
            }
        }

        thrustParticles.Stop();
        thrustParticles.Clear();
    }

    void FixedUpdate()
    {
        if (cameraController == null) return;

        NBody currentTargetBody = cameraController.cameraMovement.targetBody;
        if (currentTargetBody == null) return;

        Vector3 planetUp = currentTargetBody.transform.position.normalized;
        Vector3 rightThrust = Vector3.Cross(planetUp, currentTargetBody.velocity.normalized);
        Vector3 leftThrust = -rightThrust;

        bool isThrusting = false;

        if (isForwardThrustActive)
        {
            ApplyThrust(currentTargetBody, maxForwardThrustMagnitude, currentTargetBody.velocity.normalized);
            isThrusting = true;
        }
        else if (isReverseThrustActive)
        {
            ApplyThrust(currentTargetBody, maxReverseThrustMagnitude, -currentTargetBody.velocity.normalized);
            isThrusting = true;
        }
        else if (isRightThrustActive)
        {
            ApplyThrust(currentTargetBody, maxLateralThrustMagnitude, rightThrust);
            isThrusting = true;
        }
        else if (isLeftThrustActive)
        {
            ApplyThrust(currentTargetBody, maxLateralThrustMagnitude, leftThrust);
            isThrusting = true;
        }
        else if (isRadialInThrustActive)
        {
            ApplyThrust(currentTargetBody, maxRadialThrustMagnitude, -planetUp);
            isThrusting = true;
        }
        else if (isRadialOutThrustActive)
        {
            ApplyThrust(currentTargetBody, maxRadialThrustMagnitude, planetUp);
            isThrusting = true;
        }

        if (!isThrusting)
        {
            thrustParticles.Stop();
            thrustStopped = true;
        }
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
        float scaledMagnitude = magnitude / 10f;

        Debug.Log($"Applying Scaled Force: {scaledMagnitude} N in direction: {adjustedThrustDirection}, Mass: {targetBody.mass}");

        targetBody.AddForce(adjustedThrustDirection * scaledMagnitude);

        UpdateThrustParticleSystem(targetBody, adjustedThrustDirection);

        trajectoryRenderer = FindFirstObjectByType<TrajectoryRenderer>();
        trajectoryRenderer.orbitIsDirty = true;
    }

    private void UpdateThrustParticleSystem(NBody targetBody, Vector3 thrustDirection)
    {
        if (thrustParticles == null)
        {
            Debug.LogError("ThrustController: thrustParticles is null! Ensure the particle system is assigned.");
            return;
        }
        if (!thrustParticles) return;

        // Set the position of the particle system to the target body's position
        thrustParticles.transform.position = targetBody.transform.position;

        // Rotate the particle system to align with the opposite of the thrust direction
        thrustParticles.transform.rotation = Quaternion.LookRotation(-thrustDirection, targetBody.transform.up);

        if (!thrustParticles.isPlaying || thrustStopped)
        {
            thrustParticles.Clear();
            thrustParticles.Play();
            thrustStopped = false;
        }
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
    public void StartForwardThrust() => isForwardThrustActive = true;
    public void StopForwardThrust() => isForwardThrustActive = false;
    public void StartReverseThrust() => isReverseThrustActive = true;
    public void StopReverseThrust() => isReverseThrustActive = false;
    public void StartLeftThrust() => isLeftThrustActive = true;
    public void StopLeftThrust() => isLeftThrustActive = false;
    public void StartRightThrust() => isRightThrustActive = true;
    public void StopRightThrust() => isRightThrustActive = false;
    public void StartRadialInThrust() => isRadialInThrustActive = true;
    public void StopRadialInThrust() => isRadialInThrustActive = false;
    public void StartRadialOutThrust() => isRadialOutThrustActive = true;
    public void StopRadialOutThrust() => isRadialOutThrustActive = false;
}