using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

[RequireComponent(typeof(LineRenderer))]
public class TrajectoryRenderer : MonoBehaviour
{
    [Header("Trajectory Prediction Settings")]
    public int predictionSteps = 1000;
    public float predictionDeltaTime = 5f;

    [Header("Line Renderer Settings")]
    public float lineWidth = 3f;
    public Color lineColor = Color.blue;
    public float lineDisableDistance = 50f;

    [Header("References")]
    public TextMeshProUGUI apogeeText;
    public TextMeshProUGUI perigeeText;
    public ThrustController thrustController;


    private LineRenderer predictionLineRenderer;
    private LineRenderer originLineRenderer;
    private LineRenderer apogeeLineRenderer;
    private LineRenderer perigeeLineRenderer;

    private static Material lineMaterial;
    private NBody trackedBody;
    private Camera mainCamera;

    private Coroutine predictionCoroutine;

    // For UI updates
    private float previousApogeeDistance = float.MaxValue;
    private float previousPerigeeDistance = float.MaxValue;

    private bool showPredictionLines;
    private bool showOriginLines;
    private bool showApogeePerigeeLines;

    private static Dictionary<string, Material> materialPool = new Dictionary<string, Material>();


    public CameraMovement cameraMovement;

    private float updateInterval = 2f;
    // Next time we can recalc
    private float nextUpdateTime = 0f;

    void Awake()
    {
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

        predictionLineRenderer = CreateLineRenderer($"{gameObject.name}_Prediction");
        originLineRenderer = CreateLineRenderer($"{gameObject.name}Origin");
        apogeeLineRenderer = CreateApogeePerigeeLineRenderer($"{gameObject.name}_ApogeeLine");
        perigeeLineRenderer = CreateApogeePerigeeLineRenderer($"{gameObject.name}_PerigeeLine");

        ConfigureLineRenderer(predictionLineRenderer, 3f, "#2978FF");
        ConfigureLineRenderer(originLineRenderer, 1f, "#FFFFFF");
        ConfigureLineRenderer(apogeeLineRenderer, 3f, "#FF0000");  // Red for Apogee
        ConfigureLineRenderer(perigeeLineRenderer, 3f, "#00FF00");

        // ConfigureLineRenderer();
        mainCamera = Camera.main;
        showPredictionLines = true;
        showOriginLines = true;
        showApogeePerigeeLines = true;
    }

    void Start()
    {
        if (cameraMovement == null)
        {
            cameraMovement = FindAnyObjectByType<CameraMovement>();
        }
    }

    void FixedUpdate()
    {
        if (cameraMovement != null && cameraMovement.targetBody == trackedBody)
        {
            if (originLineRenderer != null && trackedBody != null && showOriginLines)
            {
                // Ensure the positions are relative to the NBody's position
                originLineRenderer.SetPosition(0, trackedBody.transform.position);
                originLineRenderer.SetPosition(1, Vector3.zero);  // Pointing to the scene origin
                originLineRenderer.enabled = true;
            }
        }
    }

    void OnDestroy()
    {
        if (predictionCoroutine != null)
        {
            StopCoroutine(predictionCoroutine);
        }
    }

    /**
     * Assigns the NBody to be tracked by this TrajectoryRenderer.
     */
    public void SetTrackedBody(NBody body)
    {
        trackedBody = body;

        if (predictionCoroutine != null)
        {
            StopCoroutine(predictionCoroutine);
        }

        if (trackedBody != null)
        {
            predictionCoroutine = StartCoroutine(UpdatePredictedTrajectoryCoroutine());
        }
    }

    public void ResetApogeePerigeeLines()
    {
        if (apogeeLineRenderer != null)
        {
            apogeeLineRenderer.positionCount = 0; // Clear positions
            apogeeLineRenderer.enabled = false;   // Disable the line
        }

        if (perigeeLineRenderer != null)
        {
            perigeeLineRenderer.positionCount = 0; // Clear positions
            perigeeLineRenderer.enabled = false;   // Disable the line
        }
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

    void ConfigureLineRenderer(LineRenderer lineRenderer, float widthMultiplier, string hexColor)
    {
        lineRenderer.useWorldSpace = true;
        lineRenderer.numCapVertices = 2;
        lineRenderer.numCornerVertices = 2;
        lineRenderer.widthMultiplier = widthMultiplier;
        lineRenderer.alignment = LineAlignment.View;

        Material material = GetOrCreateMaterial(hexColor);
        lineRenderer.material = material;

        lineRenderer.startColor = material.color;
        lineRenderer.endColor = material.color;

        lineRenderer.enabled = true;
    }

    /**
     * Configures the LineRenderer with initial settings.
     */
    // private void ConfigureLineRenderer()
    // {
    //     lineRenderer.useWorldSpace = true;
    //     lineRenderer.numCapVertices = 2;
    //     lineRenderer.numCornerVertices = 2;
    //     lineRenderer.widthMultiplier = lineWidth;
    //     lineRenderer.material = lineMaterial;
    //     lineRenderer.startColor = lineColor;
    //     lineRenderer.endColor = lineColor;
    //     lineRenderer.enabled = true;
    // }

    /**
     * Coroutine to update the predicted trajectory.
     */
    private IEnumerator UpdatePredictedTrajectoryCoroutine()
    {
        while (trackedBody != null)
        {
            if (cameraMovement == null || cameraMovement.targetBody != trackedBody)
            {
                predictionLineRenderer.enabled = false;
                originLineRenderer.enabled = false;
                apogeeLineRenderer.enabled = false;
                perigeeLineRenderer.enabled = false;
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            if (!trackedBody.enabled)
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            if (Time.time >= nextUpdateTime)
            {

                // Calculate the predicted trajectory
                List<Vector3> positions = trackedBody.CalculatePredictedTrajectory(predictionSteps, predictionDeltaTime);

                bool isThrusting = thrustController?.isForwardThrustActive == true
                                           || thrustController?.isReverseThrustActive == true
                                           || thrustController?.isLeftThrustActive == true
                                           || thrustController?.isRightThrustActive == true
                                           || thrustController?.isRadialInThrustActive == true
                                           || thrustController?.isRadialOutThrustActive == true;


                AdjustPredictionSettings(Time.timeScale, isThrusting);

                if (positions != null && positions.Count > 0)
                {
                    predictionLineRenderer.positionCount = positions.Count;
                    predictionLineRenderer.SetPositions(positions.ToArray());
                    // lineRenderer.enabled = true;

                    // Optionally, update UI elements for apogee and perigee
                    Vector3 apogeePoint, perigeePoint;
                    float apogeeDistance, perigeeDistance;
                    trackedBody.GetApogeePerigee(positions, out apogeePoint, out perigeePoint, out apogeeDistance, out perigeeDistance);

                    if (cameraMovement != null && cameraMovement.targetBody == trackedBody)
                    {
                        if (apogeeLineRenderer != null)
                        {
                            apogeeLineRenderer.enabled = true;
                            apogeeLineRenderer.positionCount = 2;
                            apogeeLineRenderer.SetPositions(new Vector3[] { apogeePoint, Vector3.zero });
                        }

                        if (perigeeLineRenderer != null)
                        {
                            perigeeLineRenderer.enabled = true;
                            perigeeLineRenderer.positionCount = 2;
                            perigeeLineRenderer.SetPositions(new Vector3[] { perigeePoint, Vector3.zero });
                        }

                        if (apogeeText != null && perigeeText != null)
                        {
                            if (Mathf.Abs(apogeeDistance - previousApogeeDistance) > 1f || Mathf.Abs(perigeeDistance - previousPerigeeDistance) > 1f)
                            {
                                UpdateApogeePerigeeUI(apogeeDistance, perigeeDistance);
                                previousApogeeDistance = apogeeDistance;
                                previousPerigeeDistance = perigeeDistance;
                            }
                        }
                    }
                }

                else
                {
                    predictionLineRenderer.enabled = false;
                }

                nextUpdateTime = Time.time + updateInterval;
            }

            // Handle line visibility based on camera distance
            if (mainCamera != null && trackedBody != null && showPredictionLines)
            {
                float distanceToCamera = Vector3.Distance(mainCamera.transform.position, trackedBody.transform.position);
                predictionLineRenderer.enabled = distanceToCamera > lineDisableDistance;
            }

            yield return new WaitForSeconds(0.1f);
        }
    }

    /**
     * Updates the UI elements for apogee and perigee.
     */
    private void UpdateApogeePerigeeUI(float apogee, float perigee)
    {
        if (apogeeText != null)
        {
            apogeeText.text = $"Apogee: {apogee:F2} km";
        }

        if (perigeeText != null)
        {
            perigeeText.text = $"Perigee: {perigee:F2} km";
        }
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

        // Debug.Log($"Adjusted for {gameObject.name}: predictionSteps = {predictionSteps}, predictionDeltaTime = {predictionDeltaTime}");
    }

    /**
     * Sets the enabled state of specific LineRenderers associated with this NBody.
     * @param showPrediction Whether to show/hide the prediction lines (predictionRenderer, activeRenderer, backgroundRenderer).
     * @param showOrigin Whether to show/hide the origin line.
     */
    public void SetLineVisibility(bool showPrediction, bool showOrigin)
    {
        showPredictionLines = showPrediction;
        showOriginLines = showOrigin;

        if (!showPrediction && predictionLineRenderer != null)
        {
            // Clear out the line so we don't flash old data
            predictionLineRenderer.positionCount = 0;
        }

        if (!showOrigin && originLineRenderer != null)
        {
            originLineRenderer.positionCount = 0;
        }
        else
        {
            originLineRenderer.positionCount = 2;
        }

        if (predictionLineRenderer != null) predictionLineRenderer.enabled = showPrediction;
        if (originLineRenderer != null) originLineRenderer.enabled = showOrigin;
    }

    /// <summary>
    /// Retrieves a material from the pool or creates a new one if it doesn't exist.
    /// </summary>
    /// <param name="hexColor">The hex color of the material.</param>
    /// <returns>A Material with the specified color.</returns>
    private Material GetOrCreateMaterial(string hexColor)
    {
        if (materialPool.ContainsKey(hexColor))
        {
            // Return the existing material
            return materialPool[hexColor];
        }
        else
        {
            // Create a new material and add it to the pool
            Material newMaterial = new Material(Shader.Find("Sprites/Default"));
            if (ColorUtility.TryParseHtmlString(hexColor, out Color colorValue))
            {
                newMaterial.color = colorValue;
            }

            materialPool[hexColor] = newMaterial;  // Store in the pool
            return newMaterial;
        }
    }
}
