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

    [Header("Apogee and Perigee Lines")]
    // Red = Apogee, Green = Perigee
    private LineRenderer apogeeLineRenderer;
    private LineRenderer perigeeLineRenderer;
    private CameraMovement cameraMovement;

    public Vector3 force = Vector3.zero;

    private Coroutine predictionCoroutine;
    private static Material lineMaterial;
    private bool showPredictionLines = true;
    public float lineDisableDistance = 50f;

    [Header("Thrust Feedback")]
    public ParticleSystem thrustParticles;
    float normalDelay = 0.01f;
    private ThrustController thrustController;

    [Header("UI Elements")]
    public TextMeshProUGUI apogeeText;
    public TextMeshProUGUI perigeeText;

    private float previousApogeeDistance = float.MaxValue;
    private float previousPerigeeDistance = float.MaxValue;

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
        predictionRenderer = CreateLineRenderer($"{gameObject.name}_Prediction");
        originLineRenderer = CreateLineRenderer($"{gameObject.name}Origin");
        apogeeLineRenderer = CreateApogeePerigeeLineRenderer($"{gameObject.name}_ApogeeLine");
        perigeeLineRenderer = CreateApogeePerigeeLineRenderer($"{gameObject.name}_PerigeeLine");

        ConfigureLineRenderer(predictionRenderer, 3f, "#2978FF");
        ConfigureLineRenderer(apogeeLineRenderer, 3f, "#FF0000");  // Red for Apogee
        ConfigureLineRenderer(perigeeLineRenderer, 3f, "#00FF00");
        ConfigureLineRenderer(originLineRenderer, 1f, "#FFFFFF");

        SetLineVisibility(showPredictionLines, true);

        SetApogeePerigeeVisibility(false);

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
        return lineRenderer;
    }

    /**
     * Creates an apogee and perigee LineRenderer with the specified name.
     */
    private LineRenderer CreateApogeePerigeeLineRenderer(string name)
    {
        GameObject obj = new GameObject(name);
        obj.transform.parent = null;
        LineRenderer lineRenderer = obj.AddComponent<LineRenderer>();
        return lineRenderer;
    }

    /**
     * Configures a LineRenderer with specific width and color.
     */
    void ConfigureLineRenderer(LineRenderer lineRenderer, float widthMultiplier, string hexColor)
    {
        lineRenderer.useWorldSpace = true;
        lineRenderer.numCapVertices = 2;
        lineRenderer.numCornerVertices = 2;
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
        Debug.DrawLine(transform.position, transform.position + additionalForce * 1e-6f, Color.green, 0.5f);
    }

    /**
     * Asynchronously updates the predicted trajectory for the NBody.
     */
    IEnumerator UpdatePredictedTrajectoryCoroutine()
    {
        float loopThresholdDistance = 5f;
        float dynamicAngleThreshold = Mathf.Max(5f, velocity.magnitude / 100f);
        int significantStepThreshold = predictionSteps / 4;
        int currentPredictionSteps = predictionSteps;
        float thrustBufferTime = 0.1f;
        float thrustingDelayMultiplier = 5f;

        float highestAltitude = float.MinValue;
        float lowestAltitude = float.MaxValue;
        while (true) // Infinite loop until stopped.
        {
            if (this == null || gameObject == null)
            {
                yield break; // Exit the coroutine if the object is destroyed.
            }

            NBody targetBody = thrustController?.cameraController?.cameraMovement?.targetBody;

            bool isThrusting = thrustController?.isForwardThrustActive == true
                           || thrustController?.isReverseThrustActive == true
                           || thrustController?.isLeftThrustActive == true
                           || thrustController?.isRightThrustActive == true
                           || thrustController?.isRadialInThrustActive == true
                           || thrustController?.isRadialOutThrustActive == true;

            bool shouldApplyThrust = isThrusting && (targetBody == this);
            // AdjustPredictionSettings(Time.timeScale, isThrusting);
            float currentDelay = shouldApplyThrust ? normalDelay * thrustingDelayMultiplier : normalDelay;

            if (isThrusting)
            {
                yield return new WaitForSecondsRealtime(thrustBufferTime);  // Brief delay to allow state to stabilize
            }

            if (!showPredictionLines)
            {
                yield return new WaitForSecondsRealtime(0.1f);
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
                    yield return new WaitForSecondsRealtime(0.1f);
                    continue;
                }
            }

            highestAltitude = float.MinValue;
            lowestAltitude = float.MaxValue;

            Vector3 initialPosition = transform.position;
            Vector3 initialVelocity = velocity;
            Dictionary<NBody, Vector3> bodyPositions = GravityManager.Instance.Bodies
                .Where(body => body != null && body.gameObject != null && body != this)
                .ToDictionary(body => body, body => body.transform.position);

            bool isTrackedBody = (targetBody == this);

            Vector3 currentThrustImpulse = Vector3.zero;
            float currentThrustDuration = thrustController.GetThrustDuration();

            if (isThrusting && targetBody == this)
            {
                currentThrustImpulse = thrustController.GetCurrentThrustImpulse();
            }

            List<Vector3> positions = new List<Vector3>();
            positions.Add(initialPosition);

            bool closedLoopDetected = false;
            bool collisionDetected = false;


            Vector3 apogeePoint = Vector3.zero;
            Vector3 perigeePoint = Vector3.zero;

            for (int i = 1; i < predictionSteps; i++)
            {
                if (this == null || gameObject == null)
                {
                    yield break; // Stop immediately if object is null.
                }

                Vector3 thrustToApply = currentThrustImpulse;
                if (currentThrustDuration > 0f)
                {
                    // Apply thrust only for a certain duration in prediction
                    // For example, apply thrust for the next 5 seconds
                    float thrustApplicationTime = 5f;
                    if (i * predictionDeltaTime > thrustApplicationTime)
                    {
                        thrustToApply = Vector3.zero;
                    }
                }

                OrbitalState newState = RungeKuttaStep(new OrbitalState(initialPosition, initialVelocity), predictionDeltaTime, bodyPositions, thrustToApply);
                Vector3 nextPosition = newState.position;
                Vector3 nextVelocity = newState.velocity;

                float altitude = nextPosition.magnitude;

                if (altitude > highestAltitude)
                {
                    highestAltitude = Mathf.Max(highestAltitude, altitude);
                    apogeePoint = nextPosition;
                }

                if (altitude < lowestAltitude)
                {
                    lowestAltitude = Mathf.Min(lowestAltitude, altitude);
                    perigeePoint = nextPosition;
                }

                // else
                // {
                //     // Hide the lines for non-tracked bodies
                //     if (apogeeLineRenderer != null) apogeeLineRenderer.enabled = false;
                //     if (perigeeLineRenderer != null) perigeeLineRenderer.enabled = false;
                // }

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
                    if (angleDifference < dynamicAngleThreshold)
                    {
                        // Debug.Log($"Loop detected after {i} steps for {gameObject.name}!");
                        closedLoopDetected = true;
                        break;
                    }
                }
            }

            float apogeeDistance = ((highestAltitude) - 637.1f) * 10000f;  // Convert from Unity units to km.
            float perigeeDistance = ((lowestAltitude) - 637.1f) * 10000f;  // Convert from Unity units to km.

            // Update UI After Loop
            if (isTrackedBody)
            {
                if (Mathf.Abs(apogeeDistance - previousApogeeDistance) > 1f || Mathf.Abs(perigeeDistance - previousPerigeeDistance) > 1f)
                {
                    UpdateApogeePerigeeUI(apogeeDistance, perigeeDistance);
                    previousApogeeDistance = apogeeDistance;
                    previousPerigeeDistance = perigeeDistance;
                }
            }

            // Update the LineRenderers with the calculated positions
            if (predictionRenderer != null)
            {
                predictionRenderer.positionCount = positions.Count;
                predictionRenderer.SetPositions(positions.ToArray());
                predictionRenderer.enabled = true;
            }

            if (apogeeLineRenderer != null)
            {
                apogeeLineRenderer.positionCount = 2;
                apogeeLineRenderer.SetPositions(new Vector3[] { apogeePoint, Vector3.zero });
            }

            if (perigeeLineRenderer != null)
            {
                perigeeLineRenderer.positionCount = 2;
                perigeeLineRenderer.SetPositions(new Vector3[] { perigeePoint, Vector3.zero });
            }

            yield return new WaitForSecondsRealtime(currentDelay); // Wait to avoid freezing the frame.

            if (closedLoopDetected || collisionDetected)
            {
                yield return null;
            }
        }
    }

    private Vector3 CatmullRomSpline(List<Vector3> points, int index, float alpha)
    {
        // Clamp the index values to avoid out-of-bounds access.
        int p0Index = Mathf.Clamp(index - 1, 0, points.Count - 1);
        int p1Index = Mathf.Clamp(index, 0, points.Count - 1);
        int p2Index = Mathf.Clamp(index + 1, 0, points.Count - 1);
        int p3Index = Mathf.Clamp(index + 2, 0, points.Count - 1);

        Vector3 p0 = points[p0Index];
        Vector3 p1 = points[p1Index];
        Vector3 p2 = points[p2Index];
        Vector3 p3 = points[p3Index];

        // Compute the Catmull-Rom spline using the formula.
        float t0 = 0.0f;
        float t1 = GetT(t0, p0, p1, alpha);
        float t2 = GetT(t1, p1, p2, alpha);
        float t3 = GetT(t2, p2, p3, alpha);

        float t = Mathf.Lerp(t1, t2, 0.5f); // Interpolation point between t1 and t2.
        Vector3 a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1;
        Vector3 a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2;
        Vector3 a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3;

        Vector3 b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2;
        Vector3 b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3;

        return (t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2;
    }

    private float GetT(float t, Vector3 p0, Vector3 p1, float alpha)
    {
        float distance = Vector3.Distance(p0, p1);
        return t + Mathf.Pow(distance, alpha);
    }

    /**
     * Adjusts the trajectory prediction settings based on time scale.
     */
    public void AdjustPredictionSettings(float timeScale, bool isThrusting)
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
            predictionSteps = 2000;
            predictionDeltaTime = 20f;
        }
        else if (timeScale <= 100f)
        {
            predictionSteps = 2000;
            predictionDeltaTime = 30f;
        }

        Debug.Log($"Adjusted for {gameObject.name}: predictionSteps = {predictionSteps}, predictionDeltaTime = {predictionDeltaTime}");
    }

    /**
     * Computes the gravitational acceleration for a given position.
     */
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
    private OrbitalState RungeKuttaStep(OrbitalState currentState, float deltaTime, Dictionary<NBody, Vector3> bodyPositions, Vector3 thrustImpulse = default)
    {
        OrbitalState k1 = CalculateDerivatives(currentState, bodyPositions, thrustImpulse);
        OrbitalState k2 = CalculateDerivatives(new OrbitalState(
            currentState.position + k1.position * (deltaTime / 2f),
            currentState.velocity + k1.velocity * (deltaTime / 2f)
        ), bodyPositions, thrustImpulse);

        OrbitalState k3 = CalculateDerivatives(new OrbitalState(
            currentState.position + k2.position * (deltaTime / 2f),
            currentState.velocity + k2.velocity * (deltaTime / 2f)
        ), bodyPositions, thrustImpulse);

        OrbitalState k4 = CalculateDerivatives(new OrbitalState(
            currentState.position + k3.position * deltaTime,
            currentState.velocity + k3.velocity * deltaTime
        ), bodyPositions, thrustImpulse);

        Vector3 newPosition = currentState.position + (deltaTime / 6f) * (k1.position + 2f * k2.position + 2f * k3.position + k4.position);
        Vector3 newVelocity = currentState.velocity + (deltaTime / 6f) * (k1.velocity + 2f * k2.velocity + 2f * k3.velocity + k4.velocity);
        // Debug.Log($"New Velocity: {newVelocity}");
        return new OrbitalState(newPosition, newVelocity);
    }

    private OrbitalState RungeKutta2Step(OrbitalState currentState, float deltaTime, Dictionary<NBody, Vector3> bodyPositions, Vector3 thrustImpulse = default)
    {
        // Calculate the first derivative (k1)
        OrbitalState k1 = CalculateDerivatives(currentState, bodyPositions, thrustImpulse);

        // Calculate the midpoint state using k1
        OrbitalState midState = new OrbitalState(
            currentState.position + k1.position * (deltaTime / 2f),
            currentState.velocity + k1.velocity * (deltaTime / 2f)
        );

        // Calculate the second derivative (k2) using the midpoint
        OrbitalState k2 = CalculateDerivatives(midState, bodyPositions, thrustImpulse);

        // Update position and velocity using k2
        Vector3 newPosition = currentState.position + deltaTime * k2.position;
        Vector3 newVelocity = currentState.velocity + deltaTime * k2.velocity;

        return new OrbitalState(newPosition, newVelocity);
    }


    /**
     * Calculates derivatives for the Runge-Kutta integration.
     */
    private OrbitalState CalculateDerivatives(OrbitalState state, Dictionary<NBody, Vector3> bodyPositions, Vector3 thrustImpulse = default)
    {
        Vector3 acceleration = ComputeAccelerationFromData(state.position, bodyPositions, thrustImpulse);
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

    public void SetApogeePerigeeVisibility(bool isVisible)
    {
        if (apogeeLineRenderer != null)
        {
            apogeeLineRenderer.enabled = isVisible;
        }
        if (perigeeLineRenderer != null)
        {
            perigeeLineRenderer.enabled = isVisible;
        }
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

    private void UpdateApogeePerigeeUI(float apogee, float perigee)
    {
        if (apogeeText != null)
        {
            apogeeText.text = $"Apogee: {apogee / 1000f:F2} km";
        }

        if (perigeeText != null)
        {
            perigeeText.text = $"Perigee: {perigee / 1000f:F2} km";
        }
    }
}