using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using System.Linq;

[RequireComponent(typeof(LineRenderer))]
public class NBody : MonoBehaviour
{
    public float mass = 5.0e21f;
    public Vector3 velocity = new Vector3(0, 0, 20);
    public bool isCentralBody = false;
    public float radius = 637.1f;

    private Vector3 force = Vector3.zero;
    private LineRenderer predictionRenderer;
    public int predictionSteps = 1000;  // Total prediction points
    public float predictionDeltaTime = 5f;  // Time between steps in seconds
    private LineRenderer originLineRenderer;
    private bool isPredictionLineActive = false;

    private int coarsePredictionSteps = 200;  // Coarse steps for faster prediction
    private int refinementFrequency = 5;  // How often to refine (e.g., every 5 coarse updates)
    private int frameCounter = 0;  // Track frame intervals
    private LineRenderer activeRenderer;  // The current displayed line
    private LineRenderer backgroundRenderer;

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

        // GameObject predictionObj = new GameObject($"{gameObject.name}_Prediction");
        // predictionObj.transform.parent = this.transform;
        // predictionRenderer = predictionObj.AddComponent<LineRenderer>();
        activeRenderer = CreateLineRenderer($"{gameObject.name}_ActivePrediction");
        backgroundRenderer = CreateLineRenderer($"{gameObject.name}_BackgroundPrediction");

        GameObject originLineObj = new GameObject($"{gameObject.name}_OriginLine");
        originLineObj.transform.parent = this.transform;
        originLineRenderer = originLineObj.AddComponent<LineRenderer>();

        originLineRenderer.positionCount = 2;

        // ConfigureLineRenderer(predictionRenderer);
        // ConfigureMaterial(true, predictionRenderer, "#2978FF");
        ConfigureMaterial(false, originLineRenderer, "#FFFFFF");

        // predictionRenderer.positionCount = 0;
        // predictionRenderer.enabled = false;

        if (isCentralBody)
        {
            velocity = Vector3.zero;
            Debug.Log($"{gameObject.name} is the central body and will not move.");
        }

        await UpdatePredictedTrajectoryAsync();
    }

    private LineRenderer CreateLineRenderer(string name)
    {
        GameObject obj = new GameObject(name);
        obj.transform.parent = null;
        LineRenderer lineRenderer = obj.AddComponent<LineRenderer>();
        ConfigureLineRenderer(lineRenderer, 2f, "#2978FF");
        return lineRenderer;
    }

    void ConfigureLineRenderer(LineRenderer lineRenderer, float widthMultiplier, string hexColor)
    {
        lineRenderer.useWorldSpace = true;
        lineRenderer.numCapVertices = 10;
        lineRenderer.numCornerVertices = 10;
        lineRenderer.widthMultiplier = widthMultiplier;
        lineRenderer.alignment = LineAlignment.View;

        // Apply material and color
        Color colorValue;
        if (ColorUtility.TryParseHtmlString(hexColor, out colorValue))
        {
            Material lineMaterial = new Material(Shader.Find("Sprites/Default"));  // Transparent-friendly shader
            lineMaterial.color = colorValue;
            lineRenderer.material = lineMaterial;
            lineRenderer.startColor = colorValue;
            lineRenderer.endColor = colorValue;
        }
        lineRenderer.enabled = true;
    }

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

        force = Vector3.zero;  // Reset force
    }

    public void AddForce(Vector3 additionalForce)
    {
        force += additionalForce;
    }

    async Task UpdatePredictedTrajectoryAsync()
    {
        int batchSize = 500;  // Number of points calculated per batch
        int delayBetweenBatches = 10;  // Milliseconds to pause between each batch
        float loopThresholdDistance = 5f;  // Distance threshold to detect a loop
        float angleThreshold = 30f;  // Angular threshold for direction comparison
        int significantStepThreshold = predictionSteps / 4;  // Only check for loops after 25% of the steps
        int currentPredictionSteps = predictionSteps;  // Track changes in predictionSteps

        while (true)
        {
            Vector3 initialPosition = transform.position;
            Vector3 initialVelocity = velocity;
            Dictionary<NBody, Vector3> bodyPositions = GravityManager.Instance.Bodies.ToDictionary(body => body, body => body.transform.position);

            Vector3[] positions = new Vector3[predictionSteps];
            bool closedLoopDetected = false;

            for (int i = 0; i < predictionSteps;)
            {
                // Check if `predictionSteps` changed and reset if necessary
                if (currentPredictionSteps != predictionSteps)
                {
                    currentPredictionSteps = predictionSteps;
                    positions = new Vector3[predictionSteps];
                    i = 0;  // Restart the calculation
                    Debug.Log($"Prediction steps updated to {predictionSteps}. Restarting trajectory calculation.");
                }

                int currentBatchSize = Mathf.Min(batchSize, predictionSteps - i);  // Prevent overflow
                for (int j = 0; j < currentBatchSize; j++, i++)
                {
                    OrbitalState newState = RungeKuttaStep(new OrbitalState(initialPosition, initialVelocity), predictionDeltaTime, bodyPositions);
                    initialPosition = newState.position;
                    initialVelocity = newState.velocity;

                    if (i < positions.Length)  // Safety check to prevent out-of-bounds
                    {
                        positions[i] = initialPosition;
                    }

                    // Check for loop closure only after significant steps
                    if (i > significantStepThreshold && Vector3.Distance(initialPosition, positions[0]) < loopThresholdDistance)
                    {
                        float angleDifference = Vector3.Angle(velocity.normalized, initialVelocity.normalized);
                        if (angleDifference < angleThreshold)  // Directions are nearly aligned
                        {
                            Debug.Log($"Loop detected after {i} steps for {gameObject.name}!");
                            closedLoopDetected = true;
                            break;
                        }
                    }
                }

                if (closedLoopDetected)
                    break;  // Exit loop when a closed orbit is detected

                activeRenderer.positionCount = i;
                activeRenderer.SetPositions(positions.Take(i).ToArray());
                activeRenderer.enabled = true;

                await Task.Delay(delayBetweenBatches);  // Allow Unity to update other processes
            }

            Debug.Log(closedLoopDetected ? $"Closed loop detected for {gameObject.name}!" : $"No loop detected for {gameObject.name}.");

            // Keep the rendered line visible but pause recalculation until the planet moves significantly
            Vector3 previousPosition = transform.position;
            while (Vector3.Distance(previousPosition, transform.position) < loopThresholdDistance)
            {
                await Task.Delay(100);  // Wait until the planet moves enough to require recalculation
            }

            Debug.Log($"Object moved significantly. Recalculating {gameObject.name}'s orbit.");
        }
    }

    private Vector3[] PredictTrajectory(Vector3 initialPosition, Vector3 initialVelocity, int steps, Dictionary<NBody, Vector3> bodyPositions)
    {
        Vector3 tempPosition = initialPosition;
        Vector3 tempVelocity = initialVelocity;
        Vector3[] positions = new Vector3[steps];

        for (int i = 0; i < steps; i++)
        {
            OrbitalState newState = RungeKuttaStep(new OrbitalState(tempPosition, tempVelocity), predictionDeltaTime, bodyPositions);
            tempPosition = newState.position;
            tempVelocity = newState.velocity;
            positions[i] = tempPosition;
        }

        return positions;
    }

    private Vector3[] SimplifyPositions(Vector3[] positions, int targetPointCount)
    {
        if (positions.Length <= targetPointCount) return positions;

        List<Vector3> simplifiedPositions = new List<Vector3>();
        float step = (float)positions.Length / targetPointCount;

        for (int i = 0; i < targetPointCount; i++)
        {
            simplifiedPositions.Add(positions[Mathf.RoundToInt(i * step)]);
        }

        return simplifiedPositions.ToArray();
    }

    public void AdjustPredictionSettings(float timeScale)
    {
        if (timeScale <= 1f) // Scenario 1: Real-time 1x (close inspection, detailed orbit)
        {
            predictionSteps = 3000;  // High resolution for smooth prediction
            predictionDeltaTime = 0.5f;  // Small time step for detailed lines
        }
        else if (timeScale <= 10f) // Scenario 2: Medium speeds (moderate zoom-out)
        {
            predictionSteps = 1500;  // Fewer steps since more distance is covered
            predictionDeltaTime = 10f;  // Slightly larger time step
        }
        else if (timeScale <= 50f) // Scenario 3: High speeds (farther zoom-out)
        {
            predictionSteps = 800;  // Reduced steps, but enough for clear orbit
            predictionDeltaTime = 50f;  // Larger time slices
        }
        else if (timeScale <= 100f) // Very high speeds (maximum zoom-out)
        {
            predictionSteps = 600;  // Minimal steps to avoid too many calculations
            predictionDeltaTime = 100f;  // Coarse time slices for large distances
        }

        Debug.Log($"Adjusted for {gameObject.name}: predictionSteps = {predictionSteps}, predictionDeltaTime = {predictionDeltaTime}");
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

    private OrbitalState CalculateDerivatives(OrbitalState state, Dictionary<NBody, Vector3> bodyPositions)
    {
        Vector3 acceleration = ComputeAccelerationFromData(state.position, bodyPositions);
        return new OrbitalState(state.velocity, acceleration);
    }
}