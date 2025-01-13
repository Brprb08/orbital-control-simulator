using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using System.Linq;
using TMPro;

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
    public float radius = 637.1f;
    public Vector3 force = Vector3.zero;

    [Header("Trajectory Prediction Settings")]
    public int predictionSteps = 1000;
    public float predictionDeltaTime = 5f;
    private static Material lineMaterial;
    private TrajectoryRenderer trajectoryRenderer;
    public float tolerance = 0f;

    [Header("Thrust Feedback")]
    private ThrustController thrustController;

    [Header("UI Elements")]
    public TextMeshProUGUI apogeeText;
    public TextMeshProUGUI perigeeText;

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

        if (lineMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("Shader 'Sprites/Default' not found. Please ensure it exists in your project.");
            }
            else
            {
                lineMaterial = new Material(shader);
            }
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
            Debug.Log($"{gameObject.name} is the central body and will not move.");
        }

        thrustController = GravityManager.Instance.GetComponent<ThrustController>();
        if (thrustController == null)
        {
            Debug.LogError("NBody: ThrustController not found on GravityManager.");
        }

        if (GetComponentInChildren<TrajectoryRenderer>() == null)
        {
            GameObject trajectoryObj = new GameObject($"{gameObject.name}_TrajectoryRenderer");
            trajectoryObj.transform.parent = this.transform;
            trajectoryRenderer = trajectoryObj.AddComponent<TrajectoryRenderer>();
            trajectoryRenderer.apogeeText = this.apogeeText;
            trajectoryRenderer.perigeeText = this.perigeeText;
            trajectoryRenderer.predictionSteps = 1000;
            trajectoryRenderer.predictionDeltaTime = 5f;
            trajectoryRenderer.lineWidth = 3f;
            trajectoryRenderer.lineColor = Color.blue;
            trajectoryRenderer.lineDisableDistance = 50f;

            // Assign this NBody to TrajectoryRenderer
            trajectoryRenderer.SetTrackedBody(this);
        }
    }

    /**
    * FixedUpdate is called at a consistent interval and updates the NBody's physics state.
    **/
    void FixedUpdate()
    {
        if (float.IsNaN(transform.position.x) || float.IsNaN(transform.position.y) || float.IsNaN(transform.position.z))
        {
            Debug.LogError($"[ERROR] {name} has NaN transform.position! velocity={velocity}, force={force}");
        }

        if (mass <= 1e-6f)
        {
            force = Vector3.zero;
            return;
        }

        if (isCentralBody)
        {
            float earthRotationRate = 360f / (24f * 60f * 60f);
            transform.Rotate(Vector3.up, earthRotationRate * Time.fixedDeltaTime);
        }
        else
        {
            Dictionary<NBody, Vector3> bodyPositions = new Dictionary<NBody, Vector3>();
            foreach (var body in GravityManager.Instance.Bodies)
            {
                bodyPositions[body] = body.transform.position;
            }

            OrbitalState currentState = new OrbitalState(transform.position, velocity);
            OrbitalState newState = RungeKuttaStep(currentState, Time.fixedDeltaTime, bodyPositions, true);

            velocity = newState.velocity;
            transform.position = newState.position;

            // Check for collisions.
            foreach (var body in GravityManager.Instance.Bodies)
            {
                if (body == this) continue;

                float distance = Vector3.Distance(transform.position, body.transform.position);
                float collisionThreshold = radius + body.radius; // Consider body radii for more accurate collisions.

                // Debug.Log("Distance: " + distance + "  Collision Threshold: " + collisionThreshold);
                if (body.isCentralBody && distance < collisionThreshold)
                {
                    Debug.Log($"[COLLISION] {body.name} collided with central body {body.name}");
                    GravityManager.Instance.HandleCollision(this, body);
                    return; // Stop further updates for this frame.
                }
            }
        }
        force = Vector3.zero;
    }

    /**
    * Calculates the predicted trajectory.
    * This method is called by the TrajectoryRenderer.
    * @param steps - Number of future prediciton line steps to render
    * @param deltaTime - Current deltaTime of the simulation
    **/
    public List<Vector3> CalculatePredictedTrajectory(int steps, float deltaTime)
    {
        List<Vector3> positions = new List<Vector3>();
        positions.Add(transform.position);

        Vector3 initialPosition = transform.position;
        Vector3 initialVelocity = velocity;

        Dictionary<NBody, Vector3> bodyPositions = new Dictionary<NBody, Vector3>();
        foreach (var body in GravityManager.Instance.Bodies)
        {
            if (body != this)
            {
                bodyPositions[body] = body.transform.position;
            }
        }

        bool collisionDetected = false;

        for (int i = 1; i < steps; i++)
        {
            OrbitalState newState = RungeKuttaStep(new OrbitalState(initialPosition, initialVelocity), deltaTime, bodyPositions, false);
            Vector3 nextPosition = newState.position;
            Vector3 nextVelocity = newState.velocity;

            if (!float.IsNaN(nextPosition.x) && !float.IsInfinity(nextPosition.x))
            {
                positions.Add(nextPosition);
                initialPosition = nextPosition;
                initialVelocity = nextVelocity;
            }
            else
            {
                Debug.LogWarning("Invalid trajectory point detected; skipping.");
                break;
            }

            // Collision Detection
            Collider[] hitColliders = Physics.OverlapSphere(nextPosition, radius * 0.1f); // Adjust the radius as needed
            foreach (var hitCollider in hitColliders)
            {
                NBody hitBody = hitCollider.GetComponent<NBody>();
                if (hitBody != null && hitBody != this)
                {
                    float distance = Vector3.Distance(nextPosition, hitBody.transform.position);
                    float collisionThreshold = radius + hitBody.radius;

                    if (distance < collisionThreshold)
                    {
                        Debug.Log($"[Collision Detected] {gameObject.name} will collide with {hitBody.gameObject.name} at step {i}");
                        collisionDetected = true;
                        break;
                    }
                }
            }

            if (collisionDetected)
            {
                // Stop adding further points
                break;
            }
        }

        return positions;
    }

    /**
    * Adds a force vector to this NBody.
    * @param additionalForce - Additional force being applied to object (Thrust)
    **/
    public void AddForce(Vector3 additionalForce)
    {
        force += additionalForce;
        Debug.DrawLine(transform.position, transform.position + additionalForce * 1e-6f, Color.green, 0.5f);
    }

    /**
    * Performs a single Runge-Kutta (RK4) step to update position and velocity.
    * Calculates derivatives for the Runge-Kutta integration.
    * @param state - The position and velocity of NBody object
    * @param bodyPositions - Current positions of all NBody objects
    * @param isTrajectory - If True RK4 is used for precise object motion, otherwise RK2 for line render
    * @param thrustImpulse - The current thrust value applied to object
    **/
    private OrbitalState RungeKuttaStep(OrbitalState currentState, float deltaTime, Dictionary<NBody, Vector3> bodyPositions, bool isTrajectory, Vector3 thrustImpulse = default)
    {
        OrbitalState k1;
        OrbitalState k2;
        if (isTrajectory)
        {
            OrbitalState k3;
            OrbitalState k4;
            k1 = CalculateDerivatives(currentState, bodyPositions, thrustImpulse);
            k2 = CalculateDerivatives(new OrbitalState(
                currentState.position + k1.position * (deltaTime / 2f),
                currentState.velocity + k1.velocity * (deltaTime / 2f)
            ), bodyPositions, thrustImpulse);

            k3 = CalculateDerivatives(new OrbitalState(
                currentState.position + k2.position * (deltaTime / 2f),
                currentState.velocity + k2.velocity * (deltaTime / 2f)
            ), bodyPositions, thrustImpulse);

            k4 = CalculateDerivatives(new OrbitalState(
                currentState.position + k3.position * deltaTime,
                currentState.velocity + k3.velocity * deltaTime
            ), bodyPositions, thrustImpulse);

            Vector3 newPosition = currentState.position + (deltaTime / 6f) * (k1.position + 2f * k2.position + 2f * k3.position + k4.position);
            Vector3 newVelocity = currentState.velocity + (deltaTime / 6f) * (k1.velocity + 2f * k2.velocity + 2f * k3.velocity + k4.velocity);
            // Debug.Log($"New Velocity: {newVelocity}");
            return new OrbitalState(newPosition, newVelocity);
        }
        else
        {
            k1 = CalculateDerivatives(currentState, bodyPositions, thrustImpulse);

            // Calculate the midpoint state using k1
            OrbitalState midState = new OrbitalState(
                currentState.position + k1.position * (deltaTime / 2f),
                currentState.velocity + k1.velocity * (deltaTime / 2f)
            );

            // Calculate the second derivative (k2) using the midpoint
            k2 = CalculateDerivatives(midState, bodyPositions, thrustImpulse);

            // Update position and velocity using k2
            Vector3 newPosition = currentState.position + deltaTime * k2.position;
            Vector3 newVelocity = currentState.velocity + deltaTime * k2.velocity;

            return new OrbitalState(newPosition, newVelocity);
        }
    }

    /**
    * Calculates derivatives for the Runge-Kutta integration.
    * @param state - The position and velocity of NBody object
    * @param bodyPositions - Current positions of all NBody objects
    * @param thrustImpulse - The current thrust value applied to object
    **/
    private OrbitalState CalculateDerivatives(OrbitalState state, Dictionary<NBody, Vector3> bodyPositions, Vector3 thrustImpulse = default)
    {
        Vector3 acceleration = ComputeAccelerationFromData(state.position, bodyPositions, thrustImpulse);
        return new OrbitalState(state.velocity, acceleration);
    }

    /**
    * Computes the gravitational acceleration for a given position.
    * @param position - Current position of runge-kutta step
    * @param bodyPositions - Current positions of all NBody objects
    * @param thrustImpulse - The current thrust value applied to object
    **/
    private Vector3 ComputeAccelerationFromData(Vector3 position, Dictionary<NBody, Vector3> bodyPositions, Vector3 thrustImpulse = default)
    {
        Vector3 totalForce = Vector3.zero;
        float minDistance = 0.001f;  // Prevent divide-by-zero issues.

        foreach (var body in bodyPositions.Keys)
        {
            if (body != this)
            {
                Vector3 direction = bodyPositions[body] - position;
                float distanceSquared = Mathf.Max(direction.sqrMagnitude, minDistance * minDistance);  // Avoid zero distances.

                float forceMagnitude = PhysicsConstants.G * (mass * body.mass) / distanceSquared;
                // forceMagnitude = Mathf.Min(forceMagnitude, 1e10f);  // Clamp max force.

                totalForce += direction.normalized * forceMagnitude;
            }
        }

        // Incorporate external forces (e.g., thrust) into acceleration
        Vector3 externalAcceleration = (force / mass) + (thrustImpulse / mass);

        // Total acceleration is gravitational acceleration plus external acceleration
        Vector3 totalAcceleration = (totalForce / mass) + externalAcceleration;
        // Debug.Log($"Total Acceleration: {totalAcceleration}, External Acceleration: {externalAcceleration}");
        return totalAcceleration;
    }

    /**
    * Extracts apogee and perigee from the predicted positions.
    * @param positions - Line render positions of orbit path
    * @return apogeePoint - The line render position farthest from central body
    * @return perigeePoint - The line render position closest to central body
    * @return apogeeDistance - The farthest distance in km from the central body
    * @return perigeeDistance - The closest distance in km from the central body
    **/
    public void GetApogeePerigee(List<Vector3> positions, out Vector3 apogeePoint, out Vector3 perigeePoint, out float apogeeDistance, out float perigeeDistance)
    {
        apogeePoint = Vector3.zero;
        perigeePoint = Vector3.zero;
        apogeeDistance = float.MinValue;
        perigeeDistance = float.MaxValue;

        foreach (var pos in positions)
        {
            float altitude = pos.magnitude;
            if (altitude > apogeeDistance + tolerance)
            {
                apogeeDistance = altitude;
                apogeePoint = pos;
            }
            if (altitude < perigeeDistance - tolerance)
            {
                perigeeDistance = altitude;
                perigeePoint = pos;
            }
        }

        // Convert to desired units if necessary
        apogeeDistance = ((apogeeDistance) - 637.1f) * 10; // Example conversion
        perigeeDistance = ((perigeeDistance) - 637.1f) * 10;
    }

    /**
    * Adjusts the trajectory prediction settings based on time scale.
    * @param timeScale - Current time scale of the simulation
    **/
    public void AdjustPredictionSettings(float timeScale)
    {
        // predictionDeltaTime = Time.fixedDeltaTime;
        if (timeScale <= 1f)
        {
            predictionSteps = 1000;
            predictionDeltaTime = 5f;
        }
        else if (timeScale <= 10f)
        {
            predictionSteps = 2000;
            predictionDeltaTime = 5f;
        }
        else if (timeScale <= 50f)
        {
            predictionSteps = 3000;
            predictionDeltaTime = 8f;
        }
        else if (timeScale <= 100f)
        {
            predictionSteps = 3000;
            predictionDeltaTime = 10f;
        }
    }

    /**
    * Returns the altitude above the reference central body.
    **/
    public float altitude
    {
        get
        {
            float distanceFromCenter = transform.position.magnitude;
            float distanceInKm = distanceFromCenter * 10f;
            float earthRadiusKm = 6371f;
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