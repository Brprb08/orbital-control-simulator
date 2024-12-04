using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(LineRenderer))]
public class NBody : MonoBehaviour
{
    public float mass = 1.0f; // Scaled mass
    public Vector3 velocity = new Vector3(0, 0, 20); // Initial velocity
    public bool isCentralBody = false; // Indicates if this is the central body
    public float radius = 0.5f; // Scaled radius for collision detection

    private Vector3 force = Vector3.zero;
    private LineRenderer trailRenderer;
    private LineRenderer predictionRenderer;
    private List<Vector3> trajectory = new List<Vector3>();
    public int maxTrajectoryPoints = 100;
    public int predictionSteps = 50; // Number of steps to predict
    public float predictionDeltaTime = 0.1f; // Time step for prediction

    void Awake()
    {
        if (GravityManager.Instance != null)
        {
            GravityManager.Instance.RegisterBody(this);
        }
        else
        {
            Debug.LogError("GravityManager instance is null. Ensure GravityManager is in the scene and executes before NBody.");
        }
    }

    void OnDestroy()
    {
        if (GravityManager.Instance != null)
        {
            GravityManager.Instance.DeregisterBody(this);
        }
    }

    void Start()
    {
        // Initialize Trail Renderer
        trailRenderer = GetComponent<LineRenderer>();
        trailRenderer.positionCount = maxTrajectoryPoints;
        trailRenderer.startWidth = 0.05f;
        trailRenderer.endWidth = 0.05f;
        trailRenderer.useWorldSpace = true;

        trajectory.Add(transform.position);

        // Initialize Prediction Renderer
        GameObject predictionObj = new GameObject($"{gameObject.name}_Prediction");
        predictionObj.transform.parent = this.transform;
        predictionRenderer = predictionObj.AddComponent<LineRenderer>();
        predictionRenderer.startWidth = 0.02f;
        predictionRenderer.endWidth = 0.02f;
        predictionRenderer.material = new Material(Shader.Find("Sprites/Default"));
        predictionRenderer.startColor = Color.green;
        predictionRenderer.endColor = Color.green;
        predictionRenderer.positionCount = predictionSteps;

        if (isCentralBody)
        {
            velocity = Vector3.zero; // Central body doesn't move
            Debug.Log($"{gameObject.name} is the central body and will not move.");
        }
        else
        {
            Debug.Log($"{gameObject.name} starts with manually set velocity: {velocity}");
        }
    }

    void FixedUpdate()
    {
        if (isCentralBody) return;

        // Apply forces to update velocity and position
        Vector3 acceleration = force / mass;
        velocity += acceleration * Time.fixedDeltaTime;
        transform.position += velocity * Time.fixedDeltaTime * GravityManager.Instance.scale;

        // Reset force
        force = Vector3.zero;

        // Update trajectory and prediction
        UpdateTrajectory();
        UpdatePredictedTrajectory();
    }

    public void AddForce(Vector3 additionalForce)
    {
        force += additionalForce;
    }

    void UpdateTrajectory()
    {
        trajectory.Add(transform.position);
        if (trajectory.Count > maxTrajectoryPoints)
        {
            trajectory.RemoveAt(0);
        }

        trailRenderer.positionCount = trajectory.Count;
        trailRenderer.SetPositions(trajectory.ToArray());
    }

    void UpdatePredictedTrajectory()
    {
        Vector3 tempPosition = transform.position;
        Vector3 tempVelocity = velocity;
        Vector3 tempForce = Vector3.zero;

        Vector3[] predictedPositions = new Vector3[predictionSteps];

        for (int i = 0; i < predictionSteps; i++)
        {
            tempForce = Vector3.zero;

            foreach (var body in GravityManager.Instance.Bodies)
            {
                if (body != this)
                {
                    Vector3 direction = (body.transform.position - tempPosition) / GravityManager.Instance.scale;
                    float distanceSquared = direction.sqrMagnitude;
                    float minDistance = 1.0f;
                    if (distanceSquared < minDistance * minDistance)
                    {
                        distanceSquared = minDistance * minDistance;
                    }

                    float distance = Mathf.Sqrt(distanceSquared);
                    Vector3 forceDirection = direction.normalized;
                    float forceMagnitude = PhysicsConstants.G * (mass * body.mass) / distanceSquared;
                    tempForce += forceDirection * forceMagnitude;
                }
            }

            Vector3 tempAcceleration = tempForce / mass;
            tempVelocity += tempAcceleration * predictionDeltaTime;
            tempPosition += tempVelocity * predictionDeltaTime;

            predictedPositions[i] = tempPosition * GravityManager.Instance.scale; // Scale back for display
        }

        predictionRenderer.positionCount = predictionSteps;
        predictionRenderer.SetPositions(predictedPositions);
    }
}
