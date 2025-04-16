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
public class TrajectoryRenderer : MonoBehaviour
{
    public static TrajectoryRenderer Instance { get; private set; }

    [Header("Trajectory Prediction Settings")]
    public int predictionSteps = 5000;
    public float predictionDeltaTime = 5f;
    public bool orbitIsDirty = true;
    private bool isThrusting = false;

    [Header("References")]
    public TextMeshProUGUI apogeeText;
    public TextMeshProUGUI perigeeText;
    public ThrustController thrustController;
    [SerializeField]
    public CameraMovement cameraMovement;
    private Camera mainCamera;
    private NBody trackedBody;

    private UIManager uIManager;

    [Header("Line Display Flags")]
    private bool showPredictionLines;
    private bool showOriginLines;
    private bool showApogeePerigeeLines;
    private bool apogeePerigeeLinesDirty = true;

    [Header("Coroutine")]
    private Coroutine predictionCoroutine;

    [Header("Procedural Lines")]
    public ProceduralLineRenderer predictionProceduralLine;
    public ProceduralLineRenderer originProceduralLine;
    public ProceduralLineRenderer apogeeProceduralLine;
    public ProceduralLineRenderer perigeeProceduralLine;

    [Header("Line Colors")]
    public string predictionLineColor = "#2978FF"; // Blue
    public string originLineColor = "#FFFFFF";     // White
    public string apogeeLineColor = "#C0392B";     // Red
    public string perigeeLineColor = "#009B4D";    // Green
    private float lineDisableDistance = 20f;

    private bool isComputingPrediction = false;

    /**
    * Initializes line renderers and sets up materials
    **/
    void Awake()
    {

        mainCamera = Camera.main;
        showPredictionLines = true;
        showOriginLines = true;
        showApogeePerigeeLines = true;
        predictionProceduralLine = CreateProceduralLineRenderer("Prediction1Line", predictionLineColor);
        originProceduralLine = CreateProceduralLineRenderer("OriginLine", originLineColor);
        apogeeProceduralLine = CreateProceduralLineRenderer("ApogeeLine", apogeeLineColor);
        perigeeProceduralLine = CreateProceduralLineRenderer("PerigeeLine", perigeeLineColor);

        cameraMovement = CameraMovement.Instance;
        thrustController = ThrustController.Instance;
        uIManager = UIManager.Instance;
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

    private ProceduralLineRenderer CreateProceduralLineRenderer(string name, string hexColor)
    {
        GameObject lineObject = new GameObject(name);

        ProceduralLineRenderer lineRenderer = lineObject.AddComponent<ProceduralLineRenderer>();

        lineRenderer.SetLineColor(hexColor);

        lineRenderer.SetLineWidth(0.1f);

        return lineRenderer;
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
                predictionProceduralLine.Clear();
                originProceduralLine.Clear();
                apogeeProceduralLine.Clear();
                perigeeProceduralLine.Clear();
                yield return new WaitForSeconds(0.1f);
                continue;
            }

            var orbitalParams = OrbitalCalculations.Instance.CalculateOrbitalParameters(
                trackedBody.centralBodyMass,
                Vector3.zero,
                trackedBody.transform,
                trackedBody.velocity
            );

            if (!orbitalParams.isValid)
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            bool isElliptical = orbitalParams.eccentricity < 1f;

            // If we should show prediction lines and..
            //     - isThrusting = true
            //     - orbitIsDirty = true
            //     - If not thrusting, orbit is elliptical, and prediction steps are still low
            if (showPredictionLines && (isThrusting || orbitIsDirty || (isElliptical && (predictionSteps == 5000 || predictionSteps == 3000) && !isThrusting)))
            {
                if (!isComputingPrediction)
                {
                    isComputingPrediction = true;
                    if (isElliptical)
                    {
                        float gravitationalParameter = PhysicsConstants.G * trackedBody.centralBodyMass;
                        orbitalParams.orbitalPeriod = 2f * Mathf.PI * Mathf.Sqrt(Mathf.Pow(orbitalParams.semiMajorAxis, 3) / gravitationalParameter);

                        // Adjust prediction steps to cover the full orbital loop
                        predictionSteps = Mathf.Clamp(
                            Mathf.CeilToInt(orbitalParams.orbitalPeriod / predictionDeltaTime),
                            1,
                            70000
                        );
                    }
                    else
                    {
                        // For hyperbolic orbits use a fixed number of steps
                        predictionSteps = 5000;
                    }

                    if (isThrusting)
                    {
                        predictionSteps = 3000;
                    }

                    trackedBody.CalculatePredictedTrajectoryGPU_Async(predictionSteps, predictionDeltaTime, (resultList) =>
                    {
                        var fullTrajectory = resultList.ToArray();

                        var clippedPoints = ClipTrajectory(fullTrajectory);

                        predictionProceduralLine.UpdateLine(clippedPoints);
                    });

                    orbitIsDirty = false;
                    isComputingPrediction = false;
                }
            }

            if (showApogeePerigeeLines)
            {
                if (apogeeProceduralLine != null && perigeeProceduralLine != null)
                {
                    if (!orbitalParams.isCircular)
                    {
                        apogeeProceduralLine.UpdateLine(new Vector3[] { orbitalParams.apogeePosition, Vector3.zero });
                        perigeeProceduralLine.UpdateLine(new Vector3[] { orbitalParams.perigeePosition, Vector3.zero });
                    }

                    if (apogeeText != null && perigeeText != null)
                    {
                        float apogeeAltitude = (orbitalParams.apogeePosition.magnitude - 637.8f) * 10f; // Convert to kilometers
                        float perigeeAltitude = (orbitalParams.perigeePosition.magnitude - 637.8f) * 10f; // Convert to kilometers

                        UIManager.Instance.UpdateOrbitUI(apogeeAltitude, perigeeAltitude, orbitalParams.semiMajorAxis, orbitalParams.eccentricity, orbitalParams.orbitalPeriod);
                    }
                }
            }

            if (showPredictionLines)
            {
                float distanceToCamera = Vector3.Distance(mainCamera.transform.position, trackedBody.transform.position);
                bool show = distanceToCamera > lineDisableDistance;
                if (!show)
                {
                    predictionProceduralLine.SetVisibility(false);
                    originProceduralLine.SetVisibility(false);
                    apogeeProceduralLine.SetVisibility(false);
                    perigeeProceduralLine.SetVisibility(false);
                }
                else
                {
                    predictionProceduralLine.SetVisibility(true);
                    originProceduralLine.SetVisibility(true);
                    apogeeProceduralLine.SetVisibility(true);
                    perigeeProceduralLine.SetVisibility(true);
                }
            }

            if (originProceduralLine != null && showOriginLines)
            {
                originProceduralLine.UpdateLine(new Vector3[] { trackedBody.transform.position, Vector3.zero });
            }

            if (isThrusting)
            {
                // For high timescales, slightly reduce update speed
                if (Time.timeScale >= 50)
                {
                    yield return new WaitForSeconds(3f);
                }
                yield return new WaitForSeconds(1f);
            }
            yield return new WaitForSeconds(.1f);
        }

    }

    private Vector3[] ClipTrajectory(Vector3[] points)
    {
        if (points == null || points.Length < 2)
            return points;

        List<Vector3> clippedPoints = new List<Vector3>();

        // Always include the first point
        clippedPoints.Add(points[0]);

        for (int i = 1; i < points.Length; i++)
        {
            Vector3 start = points[i - 1];
            Vector3 end = points[i];
            Vector3 dir = end - start;
            float dist = dir.magnitude;

            if (Physics.Raycast(start, dir.normalized, out RaycastHit hit, dist))
            {
                if (hit.collider.CompareTag("CentralBody"))
                {
                    // Add the intersection point and then stop
                    clippedPoints.Add(hit.point);
                    break;
                }
            }

            // If no collision, just add the next point
            clippedPoints.Add(end);
        }

        return clippedPoints.ToArray();
    }

    /**
    * Sets the enabled state of specific LineRenderers associated with this NBody.
    * @param showPrediction Whether to show/hide the prediction lines (predictionRenderer, activeRenderer, backgroundRenderer).
    * @param showOrigin Whether to show/hide the origin line.
    * @param showApogeePerigee Whether to show/hide the apogee/perigee lines and panel.
    **/
    public void SetLineVisibility(bool showPrediction, bool showOrigin, bool showApogeePerigee)
    {
        showPredictionLines = showPrediction;
        showOriginLines = showOrigin;
        showApogeePerigeeLines = showApogeePerigee;

        if (!showPrediction && predictionProceduralLine != null)
        {
            predictionProceduralLine.Clear();
        }

        if (!showOrigin && originProceduralLine != null)
        {
            originProceduralLine.Clear();
        }

        if (apogeeProceduralLine != null && perigeeProceduralLine != null)
        {
            if (!showApogeePerigee)
            {
                apogeeProceduralLine.Clear();
                perigeeProceduralLine.Clear();
            }

            if (uIManager != null)
            {
                uIManager.ShowApogeePerigeePanel(showApogeePerigeeLines);
            }
        }

        if (showApogeePerigeeLines)
        {
            apogeePerigeeLinesDirty = true;
        }

        // Re-run RecomputeTrajectory to show lines when reset
        if (showPredictionLines)
        {
            orbitIsDirty = true;
        }
        else
        {
            orbitIsDirty = false;
        }
    }
}

