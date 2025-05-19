using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Mathematics;

/// <summary>
/// Handles the rendering of trajectory prediction lines for celestial bodies.
/// This includes prediction, origin, and apogee/perigee lines, as well as updating the UI.
/// </summary>
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

    float nextTime = 0f;
    float interval = .5f;

    /// <summary>
    /// Initializes trajectory line renderers and singleton references.
    /// </summary>
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

    /// <summary>
    /// Updates internal state, including thrust status, each frame.
    /// </summary>
    void Update()
    {
        if (thrustController != null)
        {
            isThrusting = thrustController.IsThrusting;
        }
    }

    /// <summary>
    /// Stops the trajectory prediction coroutine when this object is destroyed.
    /// </summary>
    void OnDestroy()
    {
        if (predictionCoroutine != null)
        {
            StopCoroutine(predictionCoroutine);
        }
    }

    /// <summary>
    /// Creates a new procedural line renderer GameObject with the specified color.
    /// </summary>
    /// <param name="name">The name of the new line GameObject.</param>
    /// <param name="hexColor">Hex color string (e.g., "#FF0000").</param>
    /// <returns>The created ProceduralLineRenderer.</returns>
    private ProceduralLineRenderer CreateProceduralLineRenderer(string name, string hexColor)
    {
        GameObject lineObject = new GameObject(name);

        ProceduralLineRenderer lineRenderer = lineObject.AddComponent<ProceduralLineRenderer>();

        lineRenderer.SetLineColor(hexColor);

        lineRenderer.SetLineWidth(0.1f);

        return lineRenderer;
    }

    /// <summary>
    /// Assigns the NBody to be tracked for trajectory rendering.
    /// </summary>
    /// <param name="body">The NBody to track.</param>
    public void SetTrackedBody(NBody body)
    {
        trackedBody = body;

        if (trackedBody != null)
        {
            predictionCoroutine = StartCoroutine(RecomputeTrajectory());
        }
    }

    /// <summary>
    /// Continuously recomputes and updates trajectory prediction lines using orbital calculations.
    /// </summary>
    /// <returns>Coroutine enumerator.</returns>
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
                trackedBody.state.centralBodyMass,
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

            // TEMP CHECK TO STOP RENDERS FOR MOON
            bool moonFlag = false;
            if (trackedBody.name == "Moon")
            {
                moonFlag = true;
            }

            // If we should show prediction lines and..
            //     - isThrusting = true
            //     - orbitIsDirty = true
            //     - If not thrusting, orbit is elliptical, and prediction steps are still low
            // TEMP MOON FLAG TO PREVENT LINE RENDERS FOR MOON
            if (showPredictionLines && (isThrusting || orbitIsDirty || (isElliptical && (predictionSteps == 5000 || predictionSteps == 3000) && !isThrusting)) && !moonFlag)
            {
                ComputePredictionLine(orbitalParams, isElliptical);
            }

            if (Time.time >= nextTime)
            {
                ShowApogeePerigeeLines(orbitalParams);
                nextTime = Time.time + interval;
            }

            ToggleLines();

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

    /// <summary>
    /// Computes the trajectory prediction line, including adjusting for orbital shape and thrust.
    /// </summary>
    /// <param name="orbitalParams">Calculated orbital parameters.</param>
    /// <param name="isElliptical">Whether the orbit is elliptical.</param>
    private void ComputePredictionLine(OrbitalParameters orbitalParams, bool isElliptical)
    {
        if (trackedBody.name == "Moon")
        {
            Debug.Log("[PREDICTION]: Skipping prediction line for Moon.");
            predictionProceduralLine.Clear();
            orbitIsDirty = false;
            isComputingPrediction = false;
            return;
        }

        if (!isComputingPrediction)
        {
            isComputingPrediction = true;
            if (isElliptical)
            {
                float gravitationalParameter = PhysicsConstants.G * trackedBody.state.centralBodyMass;
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

    /// <summary>
    /// Clips a trajectory based on raycasting collisions with tagged objects.
    /// </summary>
    /// <param name="points">Full trajectory points array.</param>
    /// <returns>Clipped points array.</returns>
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

    /// <summary>
    /// Draws apogee and perigee lines and updates the UI with related orbital stats.
    /// </summary>
    /// <param name="orbitalParams">Orbital parameters used for rendering and display.</param>
    private void ShowApogeePerigeeLines(OrbitalParameters orbitalParams)
    {
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

                    UIManager.Instance.UpdateOrbitUI(apogeeAltitude, perigeeAltitude, orbitalParams.semiMajorAxis, orbitalParams.eccentricity,
                        orbitalParams.orbitalPeriod, orbitalParams.inclination, orbitalParams.RAAN);
                }
            }
        }
    }

    /// <summary>
    /// Toggles line visibility based on camera distance.
    /// </summary>
    private void ToggleLines()
    {
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
    }

    /// <summary>
    /// Sets the visibility of prediction, origin, and apogee/perigee lines.
    /// </summary>
    /// <param name="showPrediction">Whether to show prediction lines.</param>
    /// <param name="showOrigin">Whether to show origin lines.</param>
    /// <param name="showApogeePerigee">Whether to show apogee/perigee lines.</param>
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

