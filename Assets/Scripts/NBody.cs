using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using System.Linq;

/**
 * NBody class represents a celestial body in the gravitational system.
 * It simulates gravitational interactions, velocity, and trajectory prediction.
 */
[RequireComponent(typeof(LineRenderer))]
public class NBody : MonoBehaviour
{
    [Header("Celestial Body Properties")]
    public float mass = 5.0e21f;
    public Vector3 velocity = new Vector3(0, 0, 20);
    public bool isCentralBody = false;
    public float radius = 637.1f;

    [Header("Trajectory Prediction Settings")]
    public int predictionSteps = 1000;
    public float predictionDeltaTime = 5f;

    [Header("Line Renderer References")]
    private LineRenderer predictionRenderer;
    private LineRenderer originLineRenderer;
    private LineRenderer activeRenderer;
    private LineRenderer backgroundRenderer;

    private bool isPredictionLineActive = false;

    private Vector3 force = Vector3.zero;

    private int coarsePredictionSteps = 200;
    private int refinementFrequency = 5;
    private int frameCounter = 0;

    /**
     * Called when the script instance is being loaded.
     * Registers this NBody with the GravityManager.
     */
    void Awake()
    {
        if (GravityManager.Instance != null)
        {
            GravityManager.Instance.RegisterBody(this);
        }
    }

    /**
     * Called when the GameObject is destroyed.
     * Deregisters this NBody from the GravityManager.
     */
    void OnDestroy()
    {
        if (GravityManager.Instance != null)
        {
            GravityManager.Instance.DeregisterBody(this);
        }
    }

    /**
     * Start method initializes line renderers and sets up trajectory predictions.
     */
    async void Start()
    {
        activeRenderer = CreateLineRenderer($"{gameObject.name}_ActivePrediction");
        backgroundRenderer = CreateLineRenderer($"{gameObject.name}_BackgroundPrediction");

        GameObject originLineObj = new GameObject($"{gameObject.name}_OriginLine");
        originLineObj.transform.parent = this.transform;
        originLineRenderer = originLineObj.AddComponent<LineRenderer>();
        originLineRenderer.positionCount = 2;
        ConfigureMaterial(false, originLineRenderer, "#FFFFFF");

        if (isCentralBody)
        {
            velocity = Vector3.zero;
            Debug.Log($"{gameObject.name} is the central body and will not move.");
        }

        await UpdatePredictedTrajectoryAsync();
    }

    /**
     * Creates a LineRenderer with the specified name.
     */
    private LineRenderer CreateLineRenderer(string name)
    {
        GameObject obj = new GameObject(name);
        obj.transform.parent = null;
        LineRenderer lineRenderer = obj.AddComponent<LineRenderer>();
        ConfigureLineRenderer(lineRenderer, 2f, "#2978FF");
        return lineRenderer;
    }

    /**
     * Configures a LineRenderer with specific width and color.
     */
    void ConfigureLineRenderer(LineRenderer lineRenderer, float widthMultiplier, string hexColor)
    {
        lineRenderer.useWorldSpace = true;
        lineRenderer.numCapVertices = 10;
        lineRenderer.numCornerVertices = 10;
        lineRenderer.widthMultiplier = widthMultiplier;
        lineRenderer.alignment = LineAlignment.View;

        Color colorValue;
        if (ColorUtility.TryParseHtmlString(hexColor, out colorValue))
        {
            Material lineMaterial = new Material(Shader.Find("Sprites/Default"));
            lineMaterial.color = colorValue;
            lineRenderer.material = lineMaterial;
            lineRenderer.startColor = colorValue;
            lineRenderer.endColor = colorValue;
        }
        lineRenderer.enabled = true;
    }

    /**
     * Configures the material and color for a LineRenderer.
     */
    void ConfigureMaterial(bool isPredictionLine, LineRenderer lineRenderer, string hexColor)
    {
        Color colorValue;
        Material lineMaterial = new Material(Shader.Find("Unlit/Color"));
        if (ColorUtility.TryParseHtmlString(hexColor, out colorValue))
        {
            lineMaterial.color = colorValue;
            lineRenderer.material = lineMaterial;
            lineRenderer.startColor = colorValue;
            lineRenderer.endColor = colorValue;
        }
    }

    /**
     * FixedUpdate is called at a consistent interval and updates the NBody's physics state.
     */
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
            OrbitalState newState = RungeKuttaStep(currentState, Time.fixedDeltaTime, bodyPositions);

            velocity = newState.velocity;
            transform.position = newState.position;
        }

        if (originLineRenderer != null)
        {
            originLineRenderer.SetPosition(0, transform.position);
            originLineRenderer.SetPosition(1, Vector3.zero);
        }

        force = Vector3.zero;
    }

    /**
     * Adds a force vector to this NBody.
     */
    public void AddForce(Vector3 additionalForce)
    {
        force += additionalForce;
    }

    /**
     * Asynchronously updates the predicted trajectory for the NBody.
     */
    async Task UpdatePredictedTrajectoryAsync()
    {
        int batchSize = 500;
        int delayBetweenBatches = 10;
        float loopThresholdDistance = 5f;
        float angleThreshold = 30f;
        int significantStepThreshold = predictionSteps / 4;
        int currentPredictionSteps = predictionSteps;

        while (true)
        {
            Vector3 initialPosition = transform.position;
            Vector3 initialVelocity = velocity;
            Dictionary<NBody, Vector3> bodyPositions = GravityManager.Instance.Bodies.ToDictionary(body => body, body => body.transform.position);

            Vector3[] positions = new Vector3[predictionSteps];
            bool closedLoopDetected = false;

            for (int i = 0; i < predictionSteps;)
            {
                if (currentPredictionSteps != predictionSteps)
                {
                    currentPredictionSteps = predictionSteps;
                    positions = new Vector3[predictionSteps];
                    i = 0;
                    Debug.Log($"Prediction steps updated to {predictionSteps}. Restarting trajectory calculation.");
                }

                int currentBatchSize = Mathf.Min(batchSize, predictionSteps - i);
                for (int j = 0; j < currentBatchSize; j++, i++)
                {
                    OrbitalState newState = RungeKuttaStep(new OrbitalState(initialPosition, initialVelocity), predictionDeltaTime, bodyPositions);
                    initialPosition = newState.position;
                    initialVelocity = newState.velocity;

                    if (i < positions.Length)
                    {
                        positions[i] = initialPosition;
                    }

                    if (i > significantStepThreshold && Vector3.Distance(initialPosition, positions[0]) < loopThresholdDistance)
                    {
                        float angleDifference = Vector3.Angle(velocity.normalized, initialVelocity.normalized);
                        if (angleDifference < angleThreshold)
                        {
                            Debug.Log($"Loop detected after {i} steps for {gameObject.name}!");
                            closedLoopDetected = true;
                            break;
                        }
                    }
                }

                if (closedLoopDetected)
                    break;

                activeRenderer.positionCount = i;
                activeRenderer.SetPositions(positions.Take(i).ToArray());
                activeRenderer.enabled = true;

                await Task.Delay(delayBetweenBatches);
            }

            Debug.Log(closedLoopDetected ? $"Closed loop detected for {gameObject.name}!" : $"No loop detected for {gameObject.name}.");

            Vector3 previousPosition = transform.position;
            while (Vector3.Distance(previousPosition, transform.position) < loopThresholdDistance)
            {
                await Task.Delay(100);
            }

            Debug.Log($"Object moved significantly. Recalculating {gameObject.name}'s orbit.");
        }
    }

    /**
     * Adjusts the trajectory prediction settings based on time scale.
     */
    public void AdjustPredictionSettings(float timeScale)
    {
        if (timeScale <= 1f)
        {
            predictionSteps = 3000;
            predictionDeltaTime = 0.5f;
        }
        else if (timeScale <= 10f)
        {
            predictionSteps = 1500;
            predictionDeltaTime = 10f;
        }
        else if (timeScale <= 50f)
        {
            predictionSteps = 800;
            predictionDeltaTime = 50f;
        }
        else if (timeScale <= 100f)
        {
            predictionSteps = 600;
            predictionDeltaTime = 100f;
        }

        Debug.Log($"Adjusted for {gameObject.name}: predictionSteps = {predictionSteps}, predictionDeltaTime = {predictionDeltaTime}");
    }

    /**
     * Computes the gravitational acceleration for a given position.
     */
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

    /**
     * Returns the altitude above the reference central body.
     */
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

    /**
     * OrbitalState struct holds the position and velocity for Runge-Kutta calculations.
     */
    private struct OrbitalState
    {
        public Vector3 position;
        public Vector3 velocity;

        public OrbitalState(Vector3 position, Vector3 velocity)
        {
            this.position = position;
            this.velocity = velocity;
        }
    }

    /**
     * Performs a single Runge-Kutta (RK4) step to update position and velocity.
     */
    private OrbitalState RungeKuttaStep(OrbitalState currentState, float deltaTime, Dictionary<NBody, Vector3> bodyPositions)
    {
        OrbitalState k1 = CalculateDerivatives(currentState, bodyPositions);
        OrbitalState k2 = CalculateDerivatives(new OrbitalState(
            currentState.position + k1.position * (deltaTime / 2f),
            currentState.velocity + k1.velocity * (deltaTime / 2f)
        ), bodyPositions);

        OrbitalState k3 = CalculateDerivatives(new OrbitalState(
            currentState.position + k2.position * (deltaTime / 2f),
            currentState.velocity + k2.velocity * (deltaTime / 2f)
        ), bodyPositions);

        OrbitalState k4 = CalculateDerivatives(new OrbitalState(
            currentState.position + k3.position * deltaTime,
            currentState.velocity + k3.velocity * deltaTime
        ), bodyPositions);

        Vector3 newPosition = currentState.position + (deltaTime / 6f) * (k1.position + 2f * k2.position + 2f * k3.position + k4.position);
        Vector3 newVelocity = currentState.velocity + (deltaTime / 6f) * (k1.velocity + 2f * k2.velocity + 2f * k3.velocity + k4.velocity);

        return new OrbitalState(newPosition, newVelocity);
    }

    /**
     * Calculates derivatives for the Runge-Kutta integration.
     */
    private OrbitalState CalculateDerivatives(OrbitalState state, Dictionary<NBody, Vector3> bodyPositions)
    {
        Vector3 acceleration = ComputeAccelerationFromData(state.position, bodyPositions);
        return new OrbitalState(state.velocity, acceleration);
    }
}
