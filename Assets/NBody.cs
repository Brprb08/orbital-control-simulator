using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;

[RequireComponent(typeof(LineRenderer))]
public class NBody : MonoBehaviour
{
    public float mass = 5.0e21f; // Example scaled mass
    public Vector3 velocity = new Vector3(0, 0, 20); // Initial velocity (scaled units/s)
    public bool isCentralBody = false;
    public float radius = 680f; // Approx Earth radius in scaled units (1 unit = 10 km)

    private Vector3 force = Vector3.zero;
    private LineRenderer trailRenderer;
    private LineRenderer predictionRenderer;
    private List<Vector3> trajectory = new List<Vector3>();
    public int maxTrajectoryPoints = 100;
    public int predictionSteps = 50; // Number of steps for trajectory prediction
    public float predictionDeltaTime = 0.1f; // Delta time for trajectory prediction
    public int frameCounter = 0;
    public int updateFrequency = 2000;
    private Vector3[] predictedPositions;
    private bool predictionDirty = true;
    private TextMeshProUGUI velocityText;
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

        velocityText = GameObject.Find("VelocityText").GetComponent<TextMeshProUGUI>();
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

        // Configure line renderer properties
        ConfigureLineRenderer(predictionRenderer);
        ConfigureMaterial(predictionRenderer);
        // Optionally add depth bias or glow:
        // ConfigureMaterialWithDepthBias(predictionRenderer);

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
        // Add width curve for consistent thickness
        AnimationCurve widthCurve = new AnimationCurve();
        widthCurve.AddKey(0.0f, 0.5f); // Start width
        widthCurve.AddKey(1.0f, 0.5f); // End width
        lineRenderer.widthCurve = widthCurve;

        // Ensure the line uses world space coordinates
        lineRenderer.useWorldSpace = true;

        // Add a slight offset for better visibility
        lineRenderer.startWidth = 0.5f;
        lineRenderer.endWidth = 0.5f;
    }

    void ConfigureMaterial(LineRenderer lineRenderer)
    {
        // Create a new material with a shader that supports double-sided rendering
        Material lineMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));
        lineMaterial.SetColor("_TintColor", Color.green); // Set the color to bright green
        lineRenderer.material = lineMaterial;

        // Set line color (if shader supports direct color settings)
        lineRenderer.startColor = Color.green;
        lineRenderer.endColor = Color.green;

        // Optional: Make the line glow (depending on the shader used)
        // lineMaterial.SetFloat("_Glow", 1.0f);
    }

    void FixedUpdate()
    {
        if (isCentralBody)
        {
            // Spin the Earth on its axis
            float earthRotationRate = 360f / (24f * 60f * 60f); // degrees per second (approx 0.0041667 deg/s)
            transform.Rotate(Vector3.up, earthRotationRate * Time.fixedDeltaTime);
        }
        else
        {
            Vector3 acceleration = force / mass;
            velocity += acceleration * Time.fixedDeltaTime;
            transform.position += velocity * Time.fixedDeltaTime;
        }

        force = Vector3.zero;
        UpdateTrajectory();

        UpdateUI();
    }

    void UpdateUI()
    {
        if (velocityText != null)
        {
            float velocityMagnitude = velocity.magnitude;
            velocityText.text = $"Velocity: {velocityMagnitude:F2} m/s";
        }

        // if (altitudeText != null && isCentralBody == false)
        // {
        //     float altitude = Vector3.Distance(transform.position, Vector3.zero) - radius; // Distance from "Earth" (assumes Earth is at 0,0,0)
        //     altitudeText.text = $"Altitude: {altitude:F2} km";
        // }
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
            // Cache Unity-specific data on the main thread
            Vector3 initialPosition = transform.position;
            Vector3 initialVelocity = velocity;

            // Cache updated positions of all bodies on the main thread
            var bodyPositions = new Dictionary<NBody, Vector3>();
            foreach (var body in GravityManager.Instance.Bodies)
            {
                bodyPositions[body] = body.transform.position;
            }

            // Perform trajectory calculation asynchronously
            Vector3[] calculatedPositions = await Task.Run(() =>
            {
                Vector3 tempPosition = initialPosition;
                Vector3 tempVelocity = initialVelocity;
                Vector3[] positions = new Vector3[predictionSteps];

                for (int i = 0; i < predictionSteps; i++)
                {
                    // RK4 integration
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

            // Update the line renderer on the main thread
            predictionRenderer.positionCount = calculatedPositions.Length;
            predictionRenderer.SetPositions(calculatedPositions);

            await Task.Delay(30); // Wait a short time before recalculating to avoid overloading the CPU
        }
    }


    // void UpdatePredictedTrajectory()
    // {
    //     Vector3 tempPosition = transform.position;
    //     Vector3 tempVelocity = velocity;
    //     Vector3[] predictedPositions = new Vector3[predictionSteps];

    //     for (int i = 0; i < predictionSteps; i++)
    //     {
    //         // Use RK4 integration for the prediction step
    //         Vector3 k1Vel = tempVelocity;
    //         Vector3 k1Acc = ComputeAcceleration(tempPosition);

    //         Vector3 k2Vel = tempVelocity + k1Acc * (predictionDeltaTime / 2f);
    //         Vector3 k2Acc = ComputeAcceleration(tempPosition + k1Vel * (predictionDeltaTime / 2f));

    //         Vector3 k3Vel = tempVelocity + k2Acc * (predictionDeltaTime / 2f);
    //         Vector3 k3Acc = ComputeAcceleration(tempPosition + k2Vel * (predictionDeltaTime / 2f));

    //         Vector3 k4Vel = tempVelocity + k3Acc * predictionDeltaTime;
    //         Vector3 k4Acc = ComputeAcceleration(tempPosition + k3Vel * predictionDeltaTime);

    //         // Update velocity and position using RK4
    //         tempVelocity += (k1Acc + 2f * k2Acc + 2f * k3Acc + k4Acc) * (predictionDeltaTime / 6f);
    //         tempPosition += (k1Vel + 2f * k2Vel + 2f * k3Vel + k4Vel) * (predictionDeltaTime / 6f);

    //         // Populate predicted positions array
    //         predictedPositions[i] = tempPosition;
    //     }

    //     // Update the line renderer to match predicted trajectory
    //     predictionRenderer.positionCount = predictionSteps;
    //     predictionRenderer.SetPositions(predictedPositions);
    // }

    Vector3 ComputeAccelerationFromData(Vector3 position, Dictionary<NBody, Vector3> bodyPositions)
    {
        Vector3 totalForce = Vector3.zero;
        foreach (var body in bodyPositions.Keys)
        {
            if (body != this)
            {
                Vector3 direction = bodyPositions[body] - position;
                float distance = direction.magnitude;
                float distanceInUnits = distance * 10f; // Convert units to km
                float distanceSquared = distanceInUnits * distanceInUnits;
                float forceMagnitude = (6.67430e-11f * mass * body.mass) / distanceSquared;
                totalForce += direction.normalized * forceMagnitude;
            }
        }
        return totalForce / mass;
    }
}