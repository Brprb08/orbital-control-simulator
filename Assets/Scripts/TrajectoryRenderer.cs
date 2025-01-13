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
    public int predictionSteps = 1000;
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

    [Header("UI Updates")]
    private float previousApogeeDistance = float.MaxValue;
    private float previousPerigeeDistance = float.MaxValue;

    private bool showPredictionLines;
    private bool showOriginLines;
    private bool showApogeePerigeeLines;

    [Header("Coroutine")]
    private Coroutine predictionCoroutine;

    private float updateInterval = 2f;
    private float nextUpdateTime = 1f;
    public float apogeeDistance = 0f;
    public float perigeeDistance = 0f;
    public bool justSwitchedTrack = false;

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
        ConfigureLineRenderer(perigeeLineRenderer, 3f, "#00FF00");

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

        // if (predictionCoroutine != null)
        // {
        //     PauseCoroutine(predictionCoroutine);
        // }

        if (trackedBody != null)
        {
            predictionCoroutine = StartCoroutine(UpdatePredictedTrajectoryCoroutine());
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
    private IEnumerator UpdatePredictedTrajectoryCoroutine()
    {
        // previousApogeeDistance = 0f;
        // previousPerigeeDistance = 0f;
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


                AdjustPredictionSettings(Time.timeScale);

                if (positions != null && positions.Count > 0)
                {
                    predictionLineRenderer.positionCount = positions.Count;
                    predictionLineRenderer.SetPositions(positions.ToArray());

                }
                else
                {
                    predictionLineRenderer.enabled = false;
                }

                if (showApogeePerigeeLines)
                {
                    Vector3 apogeePoint, perigeePoint;
                    trackedBody.GetApogeePerigee(positions, out apogeePoint, out perigeePoint, out apogeeDistance, out perigeeDistance);

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

                    if (apogeeText != null && perigeeText != null && showApogeePerigeeLines)
                    {
                        if (Mathf.Abs(apogeeDistance - previousApogeeDistance) > 3f || Mathf.Abs(perigeeDistance - previousPerigeeDistance) > 3f)
                        {
                            UpdateApogeePerigeeUI(apogeeDistance, perigeeDistance);
                            previousApogeeDistance = apogeeDistance;
                            previousPerigeeDistance = perigeeDistance;
                        }
                    }
                }

                nextUpdateTime = Time.time + updateInterval;
            }

            if (cameraMovement != null && cameraMovement.targetBody == trackedBody)
            {
                if (originLineRenderer != null && trackedBody != null && showOriginLines)
                {
                    // Ensure the positions are relative to the NBody's position
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

            yield return new WaitForSeconds(0.1f);
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
            predictionSteps = 3000;
            predictionDeltaTime = 8f;
        }
        else if (timeScale <= 100f)
        {
            predictionSteps = 3000;
            predictionDeltaTime = 10f;
        }
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
