using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using System.Linq;
using TMPro;
using System;

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

        tcc = FindFirstObjectByType<TrajectoryComputeController>();
        if (!tcc)
        {
            Debug.LogError("[NBODY]: No TrajectoryComputeController found in scene!");
        }
        Debug.Log($"[NBODY]: {gameObject.name} Start Pos: {transform.position}, Vel: {velocity}");

    }

    /**
    * FixedUpdate is called at a consistent interval and updates the NBody's physics state.
    **/
    void FixedUpdate()
    {
        if (float.IsNaN(transform.position.x) || float.IsNaN(transform.position.y) || float.IsNaN(transform.position.z))
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
            float earthRotationRate = 360f / (24f * 60f * 60f);
            transform.Rotate(Vector3.up, -earthRotationRate * Time.fixedDeltaTime);
        }
        else
        {
            List<NBody> bodies = GravityManager.Instance.Bodies;
            int numBodies = bodies.Count;

            Vector3[] positions = new Vector3[numBodies];
            Vector3[] velocities = new Vector3[numBodies];
            float[] masses = new float[numBodies];

            for (int i = 0; i < numBodies; i++)
            {
                positions[i] = bodies[i].transform.position;
                velocities[i] = bodies[i].velocity;
                masses[i] = bodies[i].mass;
            }

            Vector3 thrustImpulse = force;
            force = Vector3.zero;

            Vector3 tempPosition = transform.position;
            Vector3 tempVelocity = velocity;

            NativePhysics.RungeKuttaSingle(ref tempPosition, ref tempVelocity, mass, positions, masses, numBodies, Time.fixedDeltaTime, ref thrustImpulse);

            transform.position = tempPosition;
            velocity = tempVelocity;

            NBody earth = GravityManager.Instance.CentralBody;
            if (earth != null && earth != this)
            {
                float distance = Vector3.Distance(transform.position, earth.transform.position);
                float collisionThreshold = radius + earth.radius;

                if (distance < collisionThreshold)
                {
                    Debug.Log($"[NBODY]: [COLLISION] {name} collided with Earth");
                    GravityManager.Instance.HandleCollision(this, earth);
                    return;
                }
            }
        }
        force = Vector3.zero;
    }

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

        if (tcc == null)
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
        float maxDeltaTime = 3f;

        float adjustedDelta = baseDeltaTime * (1 + distance / 1000f) / (1 + speed / 10f);
        adjustedDelta = Mathf.Clamp(adjustedDelta, minDeltaTime, maxDeltaTime);

        predictionDeltaTime = adjustedDelta;
    }

    /**
    * Returns the altitude above the reference central body.
    **/
    public float altitude
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
