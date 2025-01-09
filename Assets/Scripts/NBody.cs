using UnityEngine;
using System.Collections;
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

    [Tooltip("Renderer for the predicted trajectory.")]
    private LineRenderer predictionRenderer;

    [Tooltip("Renderer for the origin line.")]
    private LineRenderer originLineRenderer;

    [Tooltip("Renderer for the active prediction line.")]
    private LineRenderer activeRenderer;

    [Tooltip("Renderer for the background prediction line.")]
    private LineRenderer backgroundRenderer;
    public Vector3 force = Vector3.zero;

    private Coroutine predictionCoroutine;
    private static Material lineMaterial;
    private bool showPredictionLines = true;
    public float lineDisableDistance = 50f;

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
     * Called when the GameObject is destroyed.
     * Deregisters this NBody from the GravityManager.
     */
    void OnDestroy()
    {
        if (predictionCoroutine != null)
        {
            StopCoroutine(predictionCoroutine);
        }
    }

    /**
     * Start method initializes line renderers and sets up trajectory predictions.
     */
    void Start()
    {
        activeRenderer = CreateLineRenderer($"{gameObject.name}_ActivePrediction");
        backgroundRenderer = CreateLineRenderer($"{gameObject.name}_BackgroundPrediction");

        GameObject originLineObj = new GameObject($"{gameObject.name}_OriginLine");
        originLineObj.transform.parent = this.transform;
        originLineRenderer = originLineObj.AddComponent<LineRenderer>();
        originLineRenderer.positionCount = 2;
        ConfigureMaterial(false, originLineRenderer, "#FFFFFF");

        SetLineVisibility(showPredictionLines, true);

        if (isCentralBody)
        {
            velocity = Vector3.zero;
            Debug.Log($"{gameObject.name} is the central body and will not move.");
        }

        predictionCoroutine = StartCoroutine(UpdatePredictedTrajectoryCoroutine());
    }

    /**
     * Creates a LineRenderer with the specified name.
     */
    private LineRenderer CreateLineRenderer(string name)
    {
        GameObject obj = new GameObject(name);
        obj.transform.parent = this.transform;
        LineRenderer lineRenderer = obj.AddComponent<LineRenderer>();
        ConfigureLineRenderer(lineRenderer, 10f, "#2978FF");
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
    IEnumerator UpdatePredictedTrajectoryCoroutine()
    {
        float delayBetweenBatches = 0.01f; // Time delay in seconds (coroutine-friendly)
        float loopThresholdDistance = 5f;
        float angleThreshold = 5f;
        int significantStepThreshold = predictionSteps / 4;
        int currentPredictionSteps = predictionSteps;

        while (true) // Infinite loop until stopped.
        {
            if (this == null || gameObject == null)
            {
                yield break; // Exit the coroutine if the object is destroyed.
            }

            if (!showPredictionLines)
            {
                yield return new WaitForSeconds(0.1f);
                continue;  // Don't update positions if lines are hidden
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                float distanceToCamera = Vector3.Distance(mainCamera.transform.position, transform.position);

                // Disable or enable line renderers based on distance.
                if (distanceToCamera > lineDisableDistance)
                {
                    // Show line renderers when far away.
                    SetLinesEnabled(showPredictionLines);
                }
                else
                {
                    SetLinesEnabled(false);
                    yield return new WaitForSeconds(0.1f);
                    continue;
                }
            }

            Vector3 initialPosition = transform.position;
            Vector3 initialVelocity = velocity;
            Dictionary<NBody, Vector3> bodyPositions = GravityManager.Instance.Bodies
                .Where(body => body != null && body.gameObject != null && body != this)
                .ToDictionary(body => body, body => body.transform.position);

            List<Vector3> positions = new List<Vector3>();
            positions.Add(initialPosition);

            bool closedLoopDetected = false;
            bool collisionDetected = false;

            for (int i = 1; i < predictionSteps; i++)
            {
                if (this == null || gameObject == null)
                {
                    yield break; // Stop immediately if object is null.
                }

                OrbitalState newState = RungeKuttaStep(new OrbitalState(initialPosition, initialVelocity), predictionDeltaTime, bodyPositions);
                Vector3 nextPosition = newState.position;
                Vector3 nextVelocity = newState.velocity;

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

                // Add the valid next position
                if (!float.IsNaN(nextPosition.x) && !float.IsInfinity(nextPosition.x))
                {
                    positions.Add(nextPosition);
                    initialPosition = nextPosition;
                    initialVelocity = nextVelocity;
                }
                else
                {
                    Debug.LogWarning("Invalid trajectory point detected; skipping.");
                }


                // Loop Detection
                if (i > significantStepThreshold && Vector3.Distance(nextPosition, positions[0]) < loopThresholdDistance)
                {
                    float angleDifference = Vector3.Angle(velocity.normalized, newState.velocity.normalized);
                    if (angleDifference < angleThreshold)
                    {
                        // Debug.Log($"Loop detected after {i} steps for {gameObject.name}!");
                        closedLoopDetected = true;
                        break;
                    }
                }
            }

            // Update the LineRenderers with the calculated positions
            if (activeRenderer != null)
            {
                activeRenderer.positionCount = positions.Count;
                activeRenderer.SetPositions(positions.ToArray());
                activeRenderer.enabled = true;
            }

            if (backgroundRenderer != null)
            {
                backgroundRenderer.positionCount = positions.Count;
                backgroundRenderer.SetPositions(positions.ToArray());
                backgroundRenderer.enabled = true;
            }

            // Optionally, you can also handle the origin line here if needed

            yield return new WaitForSeconds(delayBetweenBatches); // Wait to avoid freezing the frame.

            if (closedLoopDetected || collisionDetected)
            {
                Debug.Log($"Stopping prediction for {gameObject.name} due to loop or collision.");
                yield return null; // Optionally wait or handle accordingly
            }
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
            predictionSteps = 2000;
            predictionDeltaTime = 5f;
        }
        else if (timeScale <= 50f)
        {
            predictionSteps = 1500;
            predictionDeltaTime = 20f;
        }
        else if (timeScale <= 100f)
        {
            predictionSteps = 1000;
            predictionDeltaTime = 30f;
        }

        Debug.Log($"Adjusted for {gameObject.name}: predictionSteps = {predictionSteps}, predictionDeltaTime = {predictionDeltaTime}");
    }

    /**
     * Computes the gravitational acceleration for a given position.
     */
    Vector3 ComputeAccelerationFromData(Vector3 position, Dictionary<NBody, Vector3> bodyPositions)
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
                forceMagnitude = Mathf.Min(forceMagnitude, 1e10f);  // Clamp max force.

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

    /**
     * Sets the enabled state of specific LineRenderers associated with this NBody.
     * @param showPrediction Whether to show/hide the prediction lines (predictionRenderer, activeRenderer, backgroundRenderer).
     * @param showOrigin Whether to show/hide the origin line.
     */
    public void SetLineVisibility(bool showPrediction, bool showOrigin)
    {
        showPredictionLines = showPrediction;
        if (predictionRenderer != null) predictionRenderer.enabled = showPrediction;
        if (activeRenderer != null) activeRenderer.enabled = showPrediction;
        if (backgroundRenderer != null) backgroundRenderer.enabled = showPrediction;
        if (originLineRenderer != null) originLineRenderer.enabled = showOrigin;
    }

    /**
     * Sets the enabled state of all LineRenderers associated with this NBody.
     * @param enabled Whether to enable or disable all lines.
     */
    public void SetLinesEnabled(bool enabled)
    {
        if (predictionRenderer != null)
            predictionRenderer.enabled = enabled;

        if (activeRenderer != null)
            activeRenderer.enabled = enabled;

        if (backgroundRenderer != null)
            backgroundRenderer.enabled = enabled;

        if (originLineRenderer != null)
            originLineRenderer.enabled = enabled;
    }
}
