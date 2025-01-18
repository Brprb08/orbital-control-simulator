using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

/** 
* Handles the rendering of trajectory prediction lines for celestial bodies.
* This includes prediction lines, origin lines, and apogee/perigee indicators.
* The class also updates the UI elements for apogee and perigee distances
* and toggles line visibility based on user inputs and simulation state.
**/
[RequireComponent(typeof(LineRenderer))]
public class TrajectoryRenderer : MonoBehaviour
{
    [Header("Trajectory Prediction Settings")]
    public int predictionSteps = 5000;
    public float predictionDeltaTime = 5f;

    [Header("Line Renderer Settings")]
    public float lineWidth = 3f;
    public Color lineColor = Color.blue;
    private LineRenderer predictionLineRenderer;
    private LineRenderer originLineRenderer;
    private LineRenderer apogeeLineRenderer;
    private LineRenderer perigeeLineRenderer;
    public float lineDisableDistance = 50f;
    private static Material lineMaterial;
    private static Dictionary<string, Material> materialPool = new Dictionary<string, Material>();

    [Header("References")]
    public TextMeshProUGUI apogeeText;
    public TextMeshProUGUI perigeeText;
    public ThrustController thrustController;
    public CameraMovement cameraMovement;
    private Camera mainCamera;
    private NBody trackedBody;
    public ProceduralLineRenderer proceduralLine;

    [Header("UI Updates")]
    private float previousApogeeDistance = float.MaxValue;
    private float previousPerigeeDistance = float.MaxValue;

    private bool showPredictionLines;
    private bool showOriginLines;
    private bool showApogeePerigeeLines;

    [Header("Coroutine")]
    private Coroutine predictionCoroutine;

    private float updateInterval = 2f;
    private float updateIntervalApogeePerigee = 10f;
    private float nextUpdateTime = 1f;
    private float apogeePerigeeUpdateTime = 1f;
    public float apogeeDistance = 0f;
    public float perigeeDistance = 0f;
    public bool justSwitchedTrack = false;


    private Dictionary<int, List<Vector3>> orbitChunks = new Dictionary<int, List<Vector3>>();
    private List<Vector3> accumulatedPositions = new List<Vector3>();
    private int lastChunkIndex = 0; // Track which chunk we are displaying
    private const int chunkSize = 100;

    [Header("Optimizations")]
    public bool useLOD = true;             // Toggle LOD on/off
    public float lodDistanceThreshold = 5000f;  // Example threshold for LOD
    // public float nonThrustRecomputeInterval = 2f; // Wait 2 seconds if not thrusting
    public float maxRecomputeInterval = 5f;      // Cap how long to wait
    public bool orbitIsDirty = true;     // Thrusting => orbit dirty

    private float nonThrustRecomputeInterval = 120f;


    /**
    * Initializes line renderers and sets up materials
    **/
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
        apogeeLineRenderer = CreateLineRenderer($"{gameObject.name}_ApogeeLine");
        perigeeLineRenderer = CreateLineRenderer($"{gameObject.name}_PerigeeLine");

        ConfigureLineRenderer(predictionLineRenderer, 3f, "#2978FF");
        ConfigureLineRenderer(originLineRenderer, 1f, "#FFFFFF");
        ConfigureLineRenderer(apogeeLineRenderer, 3f, "#FF0000");  // Red for Apogee
        ConfigureLineRenderer(perigeeLineRenderer, 3f, "#00FF00"); // Green for Perigee

        mainCamera = Camera.main;
        showPredictionLines = true;
        showOriginLines = true;
        showApogeePerigeeLines = true;
    }

    /** 
    * Sets the cameraMovement reference if null 
    **/
    void Start()
    {
        if (cameraMovement == null)
        {
            cameraMovement = FindAnyObjectByType<CameraMovement>();
        }

        if (thrustController == null)
        {
            thrustController = FindObjectOfType<ThrustController>();
        }

        proceduralLine = FindObjectOfType<ProceduralLineRenderer>();
    }

    /** 
    * Stops the prediction coroutine when this object is destroyed 
    **/
    void OnDestroy()
    {
        if (predictionCoroutine != null)
        {
            StopCoroutine(predictionCoroutine);
        }
    }

    /**
    * Assigns the NBody to be tracked by this TrajectoryRenderer.
    * @param body - Nbody the line renders switch to.
    **/
    public void SetTrackedBody(NBody body)
    {
        trackedBody = body;

        if (trackedBody != null)
        {
            predictionCoroutine = StartCoroutine(RecomputeTrajectory());
        }
    }

    /** 
    * Clears the apogee and perigee line positions and disables them 
    **/
    public void ResetApogeePerigeeLines()
    {
        apogeeDistance = float.MinValue;
        perigeeDistance = float.MaxValue;
        previousApogeeDistance = 0f;
        previousPerigeeDistance = 0f;
        justSwitchedTrack = true;

        if (apogeeLineRenderer != null)
        {
            apogeeLineRenderer.positionCount = 0;
            apogeeLineRenderer.enabled = false;
        }

        if (perigeeLineRenderer != null)
        {
            perigeeLineRenderer.positionCount = 0;
            perigeeLineRenderer.enabled = false;
        }
    }

    /**
    * Creates a LineRenderer with the specified name.
    * @param name - Game object name
    **/
    private LineRenderer CreateLineRenderer(string name)
    {
        GameObject obj = new GameObject(name);
        obj.transform.parent = this.transform;
        LineRenderer lineRenderer = obj.AddComponent<LineRenderer>();
        return lineRenderer;
    }

    /** 
    * Configures the visual settings for a LineRenderer 
    * @param lineRender - Line render being configured
    * @param widthMultiplier - How wide to set the line render
    * @param hexColor - Color of the line render
    **/
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
    * Coroutine to update the predicted trajectory.
    **/
    // private IEnumerator UpdatePredictedTrajectoryCoroutine()
    // {
    //     while (trackedBody != null)
    //     {
    //         if (cameraMovement == null || cameraMovement.targetBody != trackedBody)
    //         {
    //             predictionLineRenderer.enabled = false;
    //             originLineRenderer.enabled = false;
    //             apogeeLineRenderer.enabled = false;
    //             perigeeLineRenderer.enabled = false;
    //             yield return new WaitForSeconds(0.5f);
    //             continue;
    //         }

    //         if (!trackedBody.enabled)
    //         {
    //             yield return new WaitForSeconds(0.5f);
    //             continue;
    //         }

    //         if (Time.time >= nextUpdateTime)
    //         {

    //             List<Vector3> positions = new List<Vector3>();
    //             // Calculate the predicted trajectory
    //             // List<Vector3> positions = trackedBody.CalculatePredictedTrajectory(predictionSteps, predictionDeltaTime);

    //             if (!orbitChunks.ContainsKey(lastChunkIndex))
    //             {
    //                 // Calculate a new trajectory chunk if not cached
    //                 List<Vector3> chunkPositions = trackedBody.CalculatePredictedTrajectory(chunkSize, predictionDeltaTime);
    //                 orbitChunks[lastChunkIndex] = chunkPositions;
    //                 positions.AddRange(chunkPositions);
    //             }
    //             else
    //             {
    //                 // Use the cached chunk
    //                 positions.AddRange(orbitChunks[lastChunkIndex]);
    //             }

    //             lastChunkIndex++;

    //             if (lastChunkIndex * chunkSize >= predictionSteps)
    //             {
    //                 lastChunkIndex = 0; // Restart from the beginning
    //                 orbitChunks.Clear(); // Optionally clear cache for periodic orbits
    //             }



    //             if (positions.Count > 0)
    //             {
    //                 predictionLineRenderer.positionCount = positions.Count;
    //                 predictionLineRenderer.SetPositions(positions.ToArray());

    //             }
    //             else
    //             {
    //                 predictionLineRenderer.enabled = false;
    //             }

    //             AdjustPredictionSettings(Time.timeScale);

    //             nextUpdateTime = Time.time + updateInterval;
    //         }

    //         if (showApogeePerigeeLines && trackedBody != null && Time.time >= apogeePerigeeUpdateTime)
    //         {
    //             // Get world-space positions for apogee and perigee
    //             trackedBody.GetOrbitalApogeePerigee(trackedBody.centralBodyMass, out Vector3 apogeePosition, out Vector3 perigeePosition);

    //             // Update apogee line
    //             if (apogeeLineRenderer != null)
    //             {
    //                 apogeeLineRenderer.enabled = true;
    //                 apogeeLineRenderer.positionCount = 2;
    //                 apogeeLineRenderer.SetPositions(new Vector3[] { apogeePosition, Vector3.zero });
    //             }

    //             // Update perigee line
    //             if (perigeeLineRenderer != null)
    //             {
    //                 perigeeLineRenderer.enabled = true;
    //                 perigeeLineRenderer.positionCount = 2;
    //                 perigeeLineRenderer.SetPositions(new Vector3[] { perigeePosition, Vector3.zero });
    //             }

    //             // Optionally update the UI
    //             if (apogeeText != null && perigeeText != null)
    //             {

    //                 float apogeeAltitude = (apogeePosition.magnitude - 637.1f) * 10f; // Convert to kilometers
    //                 float perigeeAltitude = (perigeePosition.magnitude - 637.1f) * 10f; // Convert to kilometers

    //                 UpdateApogeePerigeeUI(apogeeAltitude, perigeeAltitude);
    //             }

    //             apogeePerigeeUpdateTime = Time.time + updateIntervalApogeePerigee;
    //         }

    //         if (cameraMovement != null && cameraMovement.targetBody == trackedBody)
    //         {
    //             if (originLineRenderer != null && trackedBody != null && showOriginLines)
    //             {
    //                 originLineRenderer.enabled = true;
    //                 originLineRenderer.positionCount = 2;
    //                 originLineRenderer.SetPositions(new Vector3[] { trackedBody.transform.position, Vector3.zero });
    //             }
    //         }

    //         if (mainCamera != null && trackedBody != null && showPredictionLines)
    //         {
    //             float distanceToCamera = Vector3.Distance(mainCamera.transform.position, trackedBody.transform.position);
    //             predictionLineRenderer.enabled = distanceToCamera > lineDisableDistance;
    //         }

    //         yield return new WaitForSeconds(0.1f);
    //     }
    // }

    public IEnumerator RecomputeTrajectory()
    {
        Vector3 lastPosition = trackedBody.transform.position;
        float positionChangeThreshold = 0f;  // Minimum positional change (in units) to trigger recompute
        while (true)
        {
            if (trackedBody == null)
                yield return new WaitForSeconds(0.5f);

            if (cameraMovement == null || cameraMovement.targetBody != trackedBody)
            {
                predictionLineRenderer.enabled = false;
                originLineRenderer.enabled = false;
                apogeeLineRenderer.enabled = false;
                perigeeLineRenderer.enabled = false;
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            float eccentricity, semiMajorAxis, orbitalPeriod = 0f;
            trackedBody.ComputeOrbitalElements(out semiMajorAxis, out eccentricity, trackedBody.centralBodyMass);
            bool isElliptical = eccentricity < 1f;

            bool isThrusting = thrustController != null && thrustController.IsThrusting;
            Debug.LogError(isThrusting);

            if (isThrusting || orbitIsDirty)
            {


                if (isElliptical)
                {
                    // Compute orbital period using Kepler's Third Law
                    float gravitationalParameter = PhysicsConstants.G * trackedBody.centralBodyMass;
                    orbitalPeriod = 2f * Mathf.PI * Mathf.Sqrt(Mathf.Pow(semiMajorAxis, 3) / gravitationalParameter);

                    // Adjust prediction steps to cover the full orbital loop
                    predictionSteps = Mathf.Clamp(
        Mathf.CeilToInt(orbitalPeriod / predictionDeltaTime),
        1, // Minimum number of steps to ensure at least one step
        30000 // Maximum number of steps
    );
                }
                else
                {
                    // For hyperbolic orbits, use a fixed number of steps
                    predictionSteps = 5000;
                }

                if (isThrusting)
                {
                    predictionSteps = 5000;
                }
                // Vector3 currentPosition = trackedBody.transform.position;
                // float positionChange = (currentPosition - lastPosition).magnitude;

                // Recompute if the position changed significantly or after a periodic interval
                // if (positionChange > positionChangeThreshold || (isThrusting) || Time.time >= nextUpdateTime)
                // {
                // 1) Clear old line data
                predictionLineRenderer.positionCount = 0;

                // 2) Calculate entire orbit in a single call
                List<Vector3> newTrajectoryPoints = trackedBody.CalculatePredictedTrajectoryGPU(predictionSteps, predictionDeltaTime);
                proceduralLine.UpdateLine(newTrajectoryPoints.ToArray());

                // 3) Apply them to the line renderer
                // if (newTrajectoryPoints.Count > 0)
                // {
                //     predictionLineRenderer.positionCount = newTrajectoryPoints.Count;
                //     predictionLineRenderer.SetPositions(newTrajectoryPoints.ToArray());
                //     predictionLineRenderer.enabled = true;
                // }
                // else
                // {
                //     predictionLineRenderer.enabled = false;
                // }

                // lastPosition = currentPosition;  // Update position
                // nextUpdateTime = Time.time + updateInterval;
                // }

                if (showApogeePerigeeLines && trackedBody != null && Time.time >= apogeePerigeeUpdateTime)
                {
                    // Get world-space positions for apogee and perigee
                    trackedBody.GetOrbitalApogeePerigee(trackedBody.centralBodyMass, out Vector3 apogeePosition, out Vector3 perigeePosition);

                    // Update apogee line
                    if (apogeeLineRenderer != null)
                    {
                        apogeeLineRenderer.enabled = true;
                        apogeeLineRenderer.positionCount = 2;
                        apogeeLineRenderer.SetPositions(new Vector3[] { apogeePosition, Vector3.zero });
                    }

                    // Update perigee line
                    if (perigeeLineRenderer != null)
                    {
                        perigeeLineRenderer.enabled = true;
                        perigeeLineRenderer.positionCount = 2;
                        perigeeLineRenderer.SetPositions(new Vector3[] { perigeePosition, Vector3.zero });
                    }

                    // Optionally update the UI
                    if (apogeeText != null && perigeeText != null)
                    {

                        float apogeeAltitude = (apogeePosition.magnitude - 637.1f) * 10f; // Convert to kilometers
                        float perigeeAltitude = (perigeePosition.magnitude - 637.1f) * 10f; // Convert to kilometers

                        UpdateApogeePerigeeUI(apogeeAltitude, perigeeAltitude);
                    }

                    apogeePerigeeUpdateTime = Time.time + updateIntervalApogeePerigee;
                }

                if (cameraMovement != null && cameraMovement.targetBody == trackedBody)
                {
                    if (originLineRenderer != null && trackedBody != null && showOriginLines)
                    {
                        originLineRenderer.enabled = true;
                        originLineRenderer.positionCount = 2;
                        originLineRenderer.SetPositions(new Vector3[] { trackedBody.transform.position, Vector3.zero });
                    }
                }

                if (mainCamera != null && trackedBody != null && showPredictionLines)
                {
                    float distanceToCamera = Vector3.Distance(mainCamera.transform.position, trackedBody.transform.position);
                    predictionLineRenderer.enabled = distanceToCamera > lineDisableDistance;
                }

                // positionChangeThreshold = 1000f;
                orbitIsDirty = false;

            }

            if (isThrusting)
            {
                yield return new WaitForSeconds(.5f);
            }
            yield return new WaitForSeconds(.1f);
        }
    }

    /**
    * Updates the UI elements for apogee and perigee.
    * @param apogee - Farthest orbit path distance from planet
    * @param timeScale - Closest orbit path distance to planet
    **/
    private void UpdateApogeePerigeeUI(float apogee, float perigee)
    {
        if (apogeeText != null)
        {
            apogeeText.text = $"Apogee: {apogee:F2} km";
        }

        if (perigeeText != null)
        {
            if (perigee < 0)
            {
                perigee = 0;
            }
            perigeeText.text = $"Perigee: {perigee:F2} km";
        }
    }

    /**
    * Adjusts the trajectory prediction settings based on time scale.
    * @param timeScale - The current time slider value for simulation speed.
    **/
    public void AdjustPredictionSettings(float timeScale)
    {
        // if (timeScale <= 1f)
        // {
        //     predictionSteps = 1000;
        //     predictionDeltaTime = .5f;
        // }
        // else if (timeScale <= 10f)
        // {
        //     predictionSteps = 2000;
        //     predictionDeltaTime = 1f;
        // }
        // else if (timeScale <= 50f)
        // {
        //     predictionSteps = 3000;
        //     predictionDeltaTime = 2f;
        // }
        // else if (timeScale <= 100f)
        // {
        //     predictionSteps = 3000;
        //     predictionDeltaTime = 5f;
        // }

        float distance = transform.position.magnitude; // distance from (0,0,0)
        // float speed = velocity.magnitude;
        float speed = 300f;


        // Try a "baseDeltaTime" and then adapt it:
        float baseDeltaTime = 0.5f;
        float minDeltaTime = 0.5f;
        float maxDeltaTime = 10f;

        // Example: bigger deltaTime at big distance, smaller at high speed
        float adjustedDelta = baseDeltaTime * (1 + distance / 1000f) / (1 + speed / 10f);
        adjustedDelta = Mathf.Clamp(adjustedDelta, minDeltaTime, maxDeltaTime);

        // Assign your new adaptive deltaTime
        predictionDeltaTime = adjustedDelta;

        // Keep or tweak the step count as needed:
        predictionSteps = 5000;
    }

    /**
    * Sets the enabled state of specific LineRenderers associated with this NBody.
    * @param showPrediction Whether to show/hide the prediction lines (predictionRenderer, activeRenderer, backgroundRenderer).
    * @param showOrigin Whether to show/hide the origin line.
    **/
    public void SetLineVisibility(bool showPrediction, bool showOrigin, bool showApogeePerigee)
    {
        showPredictionLines = showPrediction;
        showOriginLines = showOrigin;
        showApogeePerigeeLines = showApogeePerigee;

        if (!showPrediction && predictionLineRenderer != null)
        {
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

        if (!showApogeePerigee && apogeeLineRenderer != null && perigeeLineRenderer != null)
        {
            apogeeLineRenderer.positionCount = 0;
            perigeeLineRenderer.positionCount = 0;
        }

        if (predictionLineRenderer != null) predictionLineRenderer.enabled = showPrediction;
        if (originLineRenderer != null) originLineRenderer.enabled = showOrigin;
        if (apogeeLineRenderer != null) apogeeLineRenderer.enabled = showApogeePerigee;
        if (perigeeLineRenderer != null) perigeeLineRenderer.enabled = showApogeePerigee;
    }

    /** 
    * Retrieves a material from the pool or creates a new one if it doesn't exist 
    * @param hexColor - The hexColor associated with a material from dictionary.
    **/
    private Material GetOrCreateMaterial(string hexColor)
    {
        if (materialPool.ContainsKey(hexColor))
        {
            return materialPool[hexColor];
        }
        else
        {
            Material newMaterial = new Material(Shader.Find("Sprites/Default"));
            if (ColorUtility.TryParseHtmlString(hexColor, out Color colorValue))
            {
                newMaterial.color = colorValue;
            }

            materialPool[hexColor] = newMaterial;
            return newMaterial;
        }
    }
}
