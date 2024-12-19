using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;

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
    private List<Vector3> trajectory = new List<Vector3>();
    public int maxTrajectoryPoints = 100;
    public int predictionSteps = 50;
    public float predictionDeltaTime = 0.1f;
    private Vector3[] predictedPositions;
    private LineRenderer originLineRenderer;

    void Awake()
    {
        if (GravityManager.Instance != null)
        {
            GravityManager.Instance.RegisterBody(this);
        }
        else
        {
            Debug.LogError("GravityManager instance is null. Ensure GravityManager is in the scene.");
        }
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

        GameObject originLineObj = new GameObject($"{gameObject.name}_OriginLine");
        originLineObj.transform.parent = this.transform;
        originLineRenderer = originLineObj.AddComponent<LineRenderer>();

        originLineRenderer.positionCount = 2;
        originLineRenderer.startWidth = 0.02f;
        originLineRenderer.endWidth = 0.02f;
        originLineRenderer.material = new Material(Shader.Find("Unlit/Color"));
        originLineRenderer.material.color = Color.blue;
        originLineRenderer.useWorldSpace = true;

        ConfigureLineRenderer(predictionRenderer);
        ConfigureMaterial(predictionRenderer);

        predictionRenderer.positionCount = predictionSteps;

        if (isCentralBody)
        {
            velocity = Vector3.zero;
            Debug.Log($"{gameObject.name} is the central body and will not move.");
        }
        else
        {
            Debug.Log($"{gameObject.name} starts with velocity: {velocity}");
        }

        await UpdatePredictedTrajectoryAsync();
    }

    void ConfigureLineRenderer(LineRenderer lineRenderer)
    {
        AnimationCurve widthCurve = new AnimationCurve();
        widthCurve.AddKey(0.0f, 0.5f);
        widthCurve.AddKey(1.0f, 0.5f);
        lineRenderer.widthCurve = widthCurve;
        lineRenderer.useWorldSpace = true;
        lineRenderer.startWidth = 0.5f;
        lineRenderer.endWidth = 0.5f;
    }

    void ConfigureMaterial(LineRenderer lineRenderer)
    {
        Material lineMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));
        lineMaterial.SetColor("_TintColor", Color.green);
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
        UpdateTrajectory();
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
                    Vector3 k1Vel = tempVelocity;
                    Vector3 k1Acc = ComputeAccelerationFromData(tempPosition, bodyPositions);

                    Vector3 k2Vel = tempVelocity + k1Acc * (predictionDeltaTime / 2f);
                    Vector3 k2Acc = ComputeAccelerationFromData(tempPosition + k1Vel * (predictionDeltaTime / 2f), bodyPositions);

                    Vector3 k3Vel = tempVelocity + k2Acc * (predictionDeltaTime / 2f);
                    Vector3 k3Acc = ComputeAccelerationFromData(tempPosition + k2Vel * (predictionDeltaTime / 2f), bodyPositions);

                    Vector3 k4Vel = tempVelocity + k3Acc * predictionDeltaTime;
                    Vector3 k4Acc = ComputeAccelerationFromData(tempPosition + k3Vel * predictionDeltaTime, bodyPositions);

                    tempVelocity += (k1Acc + 2f * k2Acc + 2f * k3Acc + k4Acc) * (predictionDeltaTime / 6f);
                    tempPosition += (k1Vel + 2f * k2Vel + 2f * k3Vel + k4Vel) * (predictionDeltaTime / 6f);

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

            await Task.Delay(30);
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
                float distanceInUnits = direction.magnitude;
                float distanceSquared = distanceInUnits * distanceInUnits;
                float forceMagnitude = PhysicsConstants.G * (mass * body.mass) / distanceSquared;
                totalForce += direction.normalized * forceMagnitude;
            }
        }
        return totalForce / mass;
    }
}