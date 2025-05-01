using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using System.Linq;
using TMPro;
using System;
using Unity.Mathematics;

/**
* NBody class represents a celestial body in the gravitational system.
* It simulates gravitational interactions, velocity, and trajectory prediction.
**/
[RequireComponent(typeof(LineRenderer))]
public class NBody : MonoBehaviour
{
    [Header("Celestial Body Properties")]
    public float mass = 5.0e21f;
    public Vector3 velocity = new Vector3(0, 0, 20);
    public bool isCentralBody = false;
    public float radius = 637.8137f;
    public Vector3 force = Vector3.zero;
    public float centralBodyMass = 5.972e24f;

    [Header("Trajectory Prediction Settings")]
    public float predictionDeltaTime = .5f;

    [Header("Thrust Feedback")]
    private ThrustController thrustController;

    [Header("References")]
    private TrajectoryComputeController tcc;

    [Header("Atmosphere & Drag")]
    [Tooltip("Sea-level density (kg/km³)")]
    public float atmosphericDensity0 = 1.225e9f;
    [Tooltip("Scale height (km)")]
    public float atmosphericScaleHeight = 8.5f;
    [Tooltip("Dimensionless drag coefficient")]
    public float dragCoefficient = 2.2f;

    public double3 truePosition;
    public double3 trueVelocity;

    /**
    * Called when the script instance is being loaded.
    * Registers this NBody with the GravityManager.
    **/
    void Awake()
    {
        if (GravityManager.Instance != null)
        {
            GravityManager.Instance.RegisterBody(this);
        }
    }

    /**
    * Start method initializes line renderers and sets up trajectory predictions.
    **/
    void Start()
    {
        if (isCentralBody)
        {
            velocity = Vector3.zero;
            Debug.Log($"[NBODY]: {gameObject.name} is the central body and will not move.");
        }

        thrustController = GravityManager.Instance.GetComponent<ThrustController>();
        if (thrustController == null)
        {
            Debug.LogError("[NBody]: ThrustController not found on GravityManager.");
        }

        Debug.Log($"[NBODY]: {gameObject.name} Start Pos: {transform.position}, Vel: {velocity}");

        truePosition = new double3(transform.position.x, transform.position.y, transform.position.z);
        trueVelocity = new double3(velocity.x, velocity.y, velocity.z);
    }

    /**
    * FixedUpdate is called at a consistent interval and updates the NBody's physics state.
    **/
    void FixedUpdate()
    {
        if (HasNaNPosition())
        {
            Debug.LogError($"[NBODY]: {name} has NaN transform.position! velocity={velocity}, force={force}");
        }

        if (mass <= 1e-6f)
        {
            force = Vector3.zero;
            return;
        }

        if (isCentralBody)
        {
            RotateCentralBody();
        }
        else
        {
            SimulateOrbitalMotion();
        }

        force = Vector3.zero;
    }

    /**
    * Checks if the current position has any NaN (not-a-number) values.
    * This usually means something blew up in the physics sim.
    **/
    bool HasNaNPosition()
    {
        Vector3 pos = transform.position;
        return float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z);
    }

    /**
    * Rotates the central body (Earth) to simulate its daily spin.
    * Only applies to objects tagged as Centralbody.
    **/
    void RotateCentralBody()
    {
        const float earthRotationRate = 360f / (24f * 60f * 60f);
        transform.Rotate(Vector3.up, -earthRotationRate * Time.fixedDeltaTime);
    }

    /**
    * Simulates the object's orbital movement using physics and multiple substeps.
    * Handles gravity forces and updates position and velocity based on the result.
    **/
    void SimulateOrbitalMotion()
    {
        List<NBody> bodies = GravityManager.Instance.Bodies;
        int numBodies = bodies.Count;

        var positions = new Vector3[numBodies];
        var velocities = new Vector3[numBodies];
        var masses = new float[numBodies];

        for (int i = 0; i < numBodies; i++)
        {
            NBody body = bodies[i];
            positions[i] = body.transform.position;
            velocities[i] = body.velocity;
            masses[i] = body.mass;
        }

        const float dtMax = 0.002f;
        int substeps = Mathf.CeilToInt(Time.fixedDeltaTime / dtMax);
        float dt = Time.fixedDeltaTime / substeps;

        float crossSectionArea = Mathf.PI * radius * radius;
        Vector3 thrustImpulse = force;
        for (int s = 0; s < substeps; s++)
        {
            NativePhysics.DormandPrinceSingle(ref truePosition, ref trueVelocity, mass, positions, masses, numBodies, dt, thrustImpulse, dragCoefficient, crossSectionArea);
        }

        transform.position = new Vector3(
            (float)truePosition.x,
            (float)truePosition.y,
            (float)truePosition.z
        );

        velocity = new Vector3(
            (float)trueVelocity.x,
            (float)trueVelocity.y,
            (float)trueVelocity.z
        );

        CheckCollisionWithEarth();
    }

    /**
    * Checks if this object has collided with the central body.
    * If so, it gets removed.
    **/
    void CheckCollisionWithEarth()
    {
        NBody earth = GravityManager.Instance.CentralBody;
        if (earth == null || earth == this) return;

        float distance = Vector3.Distance(transform.position, earth.transform.position);
        float collisionThreshold = radius + earth.radius;

        if (distance < collisionThreshold)
        {
            Debug.Log($"[NBODY]: [COLLISION] {name} collided with Earth");
            GravityManager.Instance.HandleCollision(this, earth);
        }
    }

    /**
    * Cleanup when this object is destroyed.
    **/
    private void OnDestroy()
    {
        if (LineVisibilityManager.Instance != null)
        {
            LineVisibilityManager.Instance.DeregisterNBody(this);
        }
    }

    /**
    * Calculates the predicted trajectory of an orbit by passing calculation to the GPU
    * @param steps - Number of prediction line render points to render
    * @param deltaTime - Time step for simulation
    * @param onComplete - Async call from GPU to let know that calculations are complete
    **/
    public void CalculatePredictedTrajectoryGPU_Async(
        int steps,
        float deltaTime,
        Action<List<Vector3>> onComplete
    )
    {
        var otherBodies = GravityManager.Instance.Bodies.Where(b => b != this).ToList();
        Vector3[] otherPositions = otherBodies.Select(b => b.transform.position).ToArray();
        float[] otherMasses = otherBodies.Select(b => b.mass).ToArray();

        if (tcc == null && (tcc = TrajectoryComputeController.Instance) == null)
        {
            Debug.LogError("[NBODY]: TrajectoryComputeController (tcc) is null. Ensure it is assigned before calling this method.");
            onComplete?.Invoke(null);
            return;
        }

        tcc.CalculateTrajectoryGPU_Async(
            startPos: transform.position,
            startVel: velocity,
            bodyMass: mass,
            otherBodyPositions: otherPositions,
            otherBodyMasses: otherMasses,
            dt: deltaTime,
            steps: steps,
            onComplete: (positionsArray) =>
            {
                // Called when GPU readback is complete
                if (positionsArray == null)
                {
                    // Means there was an error in readback
                    onComplete?.Invoke(new List<Vector3>());
                }
                else
                {
                    onComplete?.Invoke(new List<Vector3>(positionsArray));
                }
            }
        );
    }

    /**
    * Adds a force vector to this NBody.
    * @param additionalForce - Additional force being applied to object (Thrust)
    **/
    public void AddForce(Vector3 additionalForce)
    {
        force += additionalForce;
    }

    /**
    * Adjusts the trajectory prediction settings based on time scale.
    * @param timeScale - Current time scale of the simulation
    **/
    public void AdjustPredictionSettings(float timeScale)
    {
        float distance = transform.position.magnitude;
        float speed = 300f;

        float baseDeltaTime = 0.5f;
        float minDeltaTime = 0.5f;
        float maxDeltaTime = 1f;

        float adjustedDelta = baseDeltaTime * (1 + distance / 1000f) / (1 + speed / 10f);
        adjustedDelta = Mathf.Clamp(adjustedDelta, minDeltaTime, maxDeltaTime);

        predictionDeltaTime = adjustedDelta;
    }

    /**
    * Returns the altitude above the reference central body.
    **/
    public double altitude
    {
        get
        {

            float distanceFromCenter = transform.position.magnitude;
            float distanceInKm = distanceFromCenter;
            float earthRadiusKm = 637.8137f;
            return distanceInKm - earthRadiusKm;
        }
    }

    /**
    * OrbitalState struct holds the position and velocity for Runge-Kutta calculations.
    **/
    public struct OrbitalState
    {
        public Vector3 position;
        public Vector3 velocity;

        public OrbitalState(Vector3 position, Vector3 velocity)
        {
            this.position = position;
            this.velocity = velocity;
        }
    }
}
