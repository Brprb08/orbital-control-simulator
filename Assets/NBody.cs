using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
// using Unity.Jobs; // Unity Job System for parallelism
// using Unity.Burst; // Burst Compiler for performance

[RequireComponent(typeof(LineRenderer))]
public class NBody : MonoBehaviour
{
    public float mass = 5.0e21f;
    public Vector3 velocity = new Vector3(0, 0, 20);
    public bool isCentralBody = false;
    public float radius = 637.1f;

    private Vector3 force = Vector3.zero;
    private LineRenderer trailRenderer;
    private LineRenderer predictionRenderer;
    public List<Vector3> trajectory = new List<Vector3>();
    public int maxTrajectoryPoints = 0;
    public int predictionSteps = 10000;
    public float predictionDeltaTime = 5f;
    private Vector3[] predictedPositions;
    private LineRenderer originLineRenderer;

    void Awake()
    {

    }

    void OnDestroy()
    {
        if (GravityManager.Instance != null)
        {
            GravityManager.Instance.DeregisterBody(this);
        }
    }

    async void Start()
    {
        if (GravityManager.Instance != null)
        {
            GravityManager.Instance.RegisterBody(this);
        }
        else
        {
            Debug.LogError("GravityManager instance is null. Ensure GravityManager is in the scene.");
        }

        trailRenderer = GetComponent<LineRenderer>();
        trailRenderer.positionCount = maxTrajectoryPoints;
        trajectory.Add(transform.position);

        GameObject predictionObj = new GameObject($"{gameObject.name}_Prediction");
        predictionObj.transform.parent = this.transform;
        predictionRenderer = predictionObj.AddComponent<LineRenderer>();

        GameObject originLineObj = new GameObject($"{gameObject.name}_OriginLine");
        originLineObj.transform.parent = this.transform;
        originLineRenderer = originLineObj.AddComponent<LineRenderer>();

        originLineRenderer.positionCount = 2;

        ConfigureLineRenderer(predictionRenderer);
        ConfigureMaterial(predictionRenderer);

        predictionRenderer.positionCount = predictionSteps;

        if (isCentralBody)
        {
            velocity = Vector3.zero;
            Debug.Log($"{gameObject.name} is the central body and will not move.");
        }

        await UpdatePredictedTrajectoryAsync();
    }

    void ConfigureLineRenderer(LineRenderer lineRenderer)
    {
        lineRenderer.useWorldSpace = true;
        lineRenderer.numCapVertices = 5;
        lineRenderer.numCornerVertices = 5;
    }

    void ConfigureMaterial(LineRenderer lineRenderer)
    {
        Material lineMaterial = new Material(Shader.Find("Unlit/Color"));
        lineMaterial.color = Color.green;
        lineRenderer.material = lineMaterial;
        lineRenderer.startColor = Color.green;
        lineRenderer.endColor = Color.green;
    }

    void FixedUpdate()
    {
        if (isCentralBody)
        {
            float earthRotationRate = 360f / (24f * 60f * 60f);
            transform.Rotate(Vector3.up, earthRotationRate * Time.fixedDeltaTime);
        }
        else
        {
            Vector3 acceleration = force / mass;
            velocity += acceleration * Time.fixedDeltaTime;
            transform.position += velocity * Time.fixedDeltaTime;
        }

        if (originLineRenderer != null)
        {
            originLineRenderer.SetPosition(0, transform.position);
            originLineRenderer.SetPosition(1, Vector3.zero);
        }

        force = Vector3.zero;
    }

    public void AddForce(Vector3 additionalForce)
    {
        force += additionalForce;
    }

    async Task UpdatePredictedTrajectoryAsync()
    {
        while (true)
        {
            Vector3 initialPosition = transform.position;
            Vector3 initialVelocity = velocity;

            var bodyPositions = new Dictionary<NBody, Vector3>();
            foreach (var body in GravityManager.Instance.Bodies)
            {
                bodyPositions[body] = body.transform.position;
            }

            Vector3[] calculatedPositions = await Task.Run(() =>
            {
                Vector3 tempPosition = initialPosition;
                Vector3 tempVelocity = initialVelocity;
                Vector3[] positions = new Vector3[predictionSteps];

                for (int i = 0; i < predictionSteps; i++)
                {
                    Vector3 acceleration = ComputeAccelerationFromData(tempPosition, bodyPositions);
                    tempVelocity += acceleration * predictionDeltaTime;
                    tempPosition += tempVelocity * predictionDeltaTime;
                    positions[i] = tempPosition;
                }

                return positions;
            });

            if (predictionRenderer == null)
            {
                Debug.LogWarning("Prediction Renderer has been destroyed. Exiting UpdatePredictedTrajectoryAsync.");
                break;
            }

            predictionRenderer.positionCount = calculatedPositions.Length;
            predictionRenderer.SetPositions(calculatedPositions);

            await Task.Delay(500); // Lower frequency for prediction updates
        }
    }

    Vector3 ComputeAccelerationFromData(Vector3 position, Dictionary<NBody, Vector3> bodyPositions)
    {
        Vector3 totalForce = Vector3.zero;
        foreach (var body in bodyPositions.Keys)
        {
            if (body != this)
            {
                Vector3 direction = bodyPositions[body] - position;
                float distanceSquared = direction.sqrMagnitude;
                if (distanceSquared < Mathf.Epsilon) continue;

                float forceMagnitude = PhysicsConstants.G * (mass * body.mass) / distanceSquared;
                totalForce += direction.normalized * forceMagnitude;
            }
        }
        return totalForce / mass;
    }

    public float altitude
    {
        get
        {
            float distanceFromCenter = transform.position.magnitude;
            float distanceInKm = distanceFromCenter * 10f;
            float earthRadiusKm = 6378f;
            return distanceInKm - earthRadiusKm;
        }
    }
}