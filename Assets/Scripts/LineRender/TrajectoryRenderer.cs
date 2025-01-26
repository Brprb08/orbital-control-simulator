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
    public bool orbitIsDirty = true;
    private bool isThrusting = false;
    private bool update = false;

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
    [SerializeField]
    public CameraMovement cameraMovement;
    private Camera mainCamera;
    private NBody trackedBody;
    public ProceduralLineRenderer proceduralLine;
    private UIManager uIManager;

    [Header("Line Display Flags")]
    private bool showPredictionLines;
    private bool showOriginLines;
    private bool showApogeePerigeeLines;

    [Header("Coroutine")]
    private Coroutine predictionCoroutine;
    private float updateIntervalApogeePerigee = 10f;
    private float apogeePerigeeUpdateTime = 1f;

    public float apogeeDistance = 0f;
    public float perigeeDistance = 0f;
    public bool justSwitchedTrack = false;

    // private Dictionary<int, List<Vector3>> orbitChunks = new Dictionary<int, List<Vector3>>();
    // private List<Vector3> accumulatedPositions = new List<Vector3>();
    // private const int chunkSize = 100;

    [Header("Optimizations")]
    public bool useLOD = true;
    public float lodDistanceThreshold = 5000f;
    public float maxRecomputeInterval = 5f;

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
        Debug.LogError("*)(*)(*)(*)(*)*()(*)");
        showPredictionLines = true;
        showOriginLines = true;
        showApogeePerigeeLines = true;

        cameraMovement = CameraMovement.Instance;
        thrustController = ThrustController.Instance;
        uIManager = UIManager.Instance;
    }

    void Start()
    {
        if (proceduralLine == null)
        {
            proceduralLine = FindFirstObjectByType<ProceduralLineRenderer>();
        }
    }

    void Update()
    {
        if (thrustController != null)
        {
            isThrusting = thrustController.IsThrusting;
        }
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
    * Recomputes the prediction, origin, and apogee/perigee line renders using the GPU
    **/
    public IEnumerator RecomputeTrajectory()
    {
        Vector3 lastPosition = trackedBody.transform.position;
        while (true)
        {
            if (trackedBody == null)
                yield return new WaitForSeconds(0.1f);

            if (cameraMovement == null || cameraMovement.targetBody != trackedBody)
            {
                predictionLineRenderer.enabled = false;
                originLineRenderer.enabled = false;
                apogeeLineRenderer.enabled = false;
                perigeeLineRenderer.enabled = false;
                yield return new WaitForSeconds(0.1f);
                continue;
            }

            float eccentricity, semiMajorAxis, orbitalPeriod = 0f;

            trackedBody.ComputeOrbitalElements(out semiMajorAxis, out eccentricity, trackedBody.centralBodyMass);
            bool isElliptical = eccentricity < 1f;
            Debug.LogError("Show Prediction prior: " + showPredictionLines + " --- Orbit is Dirty: " + orbitIsDirty);
            if (showPredictionLines && (update || isThrusting || orbitIsDirty || (isElliptical && (predictionSteps == 5000 || predictionSteps == 3000) && !isThrusting)))
            {
                Debug.LogError("INSIDE PREDICTION LINE RENDER");
                if (isElliptical)
                {
                    float gravitationalParameter = PhysicsConstants.G * trackedBody.centralBodyMass;
                    orbitalPeriod = 2f * Mathf.PI * Mathf.Sqrt(Mathf.Pow(semiMajorAxis, 3) / gravitationalParameter);

                    // Adjust prediction steps to cover the full orbital loop
                    predictionSteps = Mathf.Clamp(
                        Mathf.CeilToInt(orbitalPeriod / predictionDeltaTime),
                        1,
                        30000
                    );
                }
                else
                {
                    // For hyperbolic orbits, use a fixed number of steps
                    predictionSteps = 5000;
                }

                if (isThrusting)
                {
                    predictionSteps = 3000;
                }

                predictionLineRenderer.positionCount = 0;

                trackedBody.CalculatePredictedTrajectoryGPU_Async(predictionSteps, predictionDeltaTime, (resultList) =>
                {
                    proceduralLine.UpdateLine(resultList.ToArray());
                });

                orbitIsDirty = false;
                if (update) update = false;
            }

            if (showApogeePerigeeLines && Time.time >= apogeePerigeeUpdateTime)
            {
                trackedBody.GetOrbitalApogeePerigee(trackedBody.centralBodyMass, out Vector3 apogeePosition, out Vector3 perigeePosition);

                if (apogeeLineRenderer != null && perigeeLineRenderer != null)
                {
                    apogeeLineRenderer.enabled = true;
                    apogeeLineRenderer.positionCount = 2;
                    apogeeLineRenderer.SetPositions(new Vector3[] { apogeePosition, Vector3.zero });

                    perigeeLineRenderer.enabled = true;
                    perigeeLineRenderer.positionCount = 2;
                    perigeeLineRenderer.SetPositions(new Vector3[] { perigeePosition, Vector3.zero });

                    if (apogeeText != null && perigeeText != null)
                    {
                        float apogeeAltitude = (apogeePosition.magnitude - 637.1f) * 10f; // Convert to kilometers
                        float perigeeAltitude = (perigeePosition.magnitude - 637.1f) * 10f; // Convert to kilometers

                        UpdateApogeePerigeeUI(apogeeAltitude, perigeeAltitude);
                    }
                }
                apogeePerigeeUpdateTime = Time.time + updateIntervalApogeePerigee;
            }

            // if (showPredictionLines)
            // {
            //     float distanceToCamera = Vector3.Distance(mainCamera.transform.position, trackedBody.transform.position);
            //     bool show = distanceToCamera > lineDisableDistance;
            //     showPredictionLines = show;
            //     showOriginLines = show;
            //     showApogeePerigeeLines = show;
            // }

            if (originLineRenderer != null && showOriginLines)
            {
                originLineRenderer.enabled = true;
                originLineRenderer.positionCount = 2;
                originLineRenderer.SetPositions(new Vector3[] { trackedBody.transform.position, Vector3.zero });
            }

            if (isThrusting)
            {
                if (Time.timeScale >= 50)
                {
                    yield return new WaitForSeconds(3f);
                }
                yield return new WaitForSeconds(1f);
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
        float distance = transform.position.magnitude;
        float speed = 300f;
        float baseDeltaTime = 0.5f;
        float minDeltaTime = 0.5f;
        float maxDeltaTime = 3f;

        float adjustedDelta = baseDeltaTime * (1 + distance / 1000f) / (1 + speed / 10f);
        adjustedDelta = Mathf.Clamp(adjustedDelta, minDeltaTime, maxDeltaTime);

        predictionDeltaTime = adjustedDelta;
        predictionSteps = 5000;
    }

    /**
    * Sets the enabled state of specific LineRenderers associated with this NBody.
    * @param showPrediction Whether to show/hide the prediction lines (predictionRenderer, activeRenderer, backgroundRenderer).
    * @param showOrigin Whether to show/hide the origin line.
    * @param showApogeePerigee Whether to show/hide the apogee/perigee lines and panel.
    **/
    public void SetLineVisibility(bool showPrediction, bool showOrigin, bool showApogeePerigee)
    {
        Debug.LogError("######## SetLineVisibility:TR:SHOW PRED: " + showPrediction + " #######");
        showPredictionLines = showPrediction;
        showOriginLines = showOrigin;
        showApogeePerigeeLines = showApogeePerigee;

        if (!showPrediction && proceduralLine != null)
        {
            proceduralLine.UpdateLine(new Vector3[0]);
        }

        if (!showOrigin && originLineRenderer != null)
        {
            originLineRenderer.positionCount = 0;
        }

        if (apogeeLineRenderer != null && perigeeLineRenderer != null)
        {
            if (!showApogeePerigee)
            {
                apogeeLineRenderer.positionCount = 0;
                perigeeLineRenderer.positionCount = 0;
            }

            if (uIManager != null)
            {
                uIManager.ShowApogeePerigeePanel(showApogeePerigeeLines);
            }
        }

        Debug.LogError(showPredictionLines);
        // Re-run RecomputeTrajectory to show lines when reset
        if (showPredictionLines)
        {
            Debug.LogError("CHANGING ORBIT-IS-DIRTY TO TRUE");
            orbitIsDirty = true;
        }
        else
        {
            Debug.LogError("CHANGING ORBIT-IS-DIRTY TO FALSE");
            orbitIsDirty = false;
        }
        // if (originLineRenderer != null) originLineRenderer.enabled = showOrigin;
        // if (apogeeLineRenderer != null) apogeeLineRenderer.enabled = showApogeePerigee;
        // if (perigeeLineRenderer != null) perigeeLineRenderer.enabled = showApogeePerigee;
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
