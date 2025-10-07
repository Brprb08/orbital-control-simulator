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
    // public static TrajectoryRenderer Instance { get; private set; }

    [Header("Trajectory Prediction Settings")]
    public int predictionSteps = 5000;
    public float predictionDeltaTime = 5f;
    public bool orbitIsDirty = true;
    private bool isThrusting = false;

    [Header("References - UI & Scripts")]
    public TextMeshProUGUI apogeeText;
    public TextMeshProUGUI perigeeText;
    public ThrustController thrustController;
    public CameraMovement cameraMovement;
    public GravityManager gravityManager;
    private UIManager uIManager;

    [Header("References - Camera & Body")]
    private Camera mainCamera;
    public NBody trackedBody;

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
    public ProceduralLineRenderer preManeuverLine;

    [Header("Line Colors")]
    public string predictionLineColor = "#2978FF"; // Blue
    public string originLineColor = "#FFFFFF";     // White
    public string apogeeLineColor = "#C0392B";     // Red
    public string perigeeLineColor = "#009B4D";    // Green
    private float lineDisableDistance = 20f;

    [Header("Prediction State")]
    private bool isComputingPrediction = false;
    private bool savedOriginalOrbit = false;

    [Header("Prediction Timing")]
    float nextTime = 0f;
    float interval = 0.5f;

    [Header("Prediction Output")]
    public List<Vector3> latestPrediction = new List<Vector3>();
    public float latestPredictionDeltaTime;
    public float latestPredictionStartTime;

    [Header("Preview (while dragging)")]
    public ProceduralLineRenderer previewLine;
    private Coroutine previewRoutine;
    private Vector3 previewPos, previewVel;
    private float previewMass;
    private bool previewDirty;


    private SimContext ctx;

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.gravityManager = ctx.GravityManager;
        this.cameraMovement = ctx.CameraMovement;
        this.thrustController = ctx.ThrustController;
        this.uIManager = ctx.UIManager;

        mainCamera = Camera.main;
        showPredictionLines = true;
        showOriginLines = true;
        showApogeePerigeeLines = true;

        predictionProceduralLine = CreateProceduralLineRenderer("Prediction1Line", predictionLineColor);
        originProceduralLine = CreateProceduralLineRenderer("OriginLine", originLineColor);
        apogeeProceduralLine = CreateProceduralLineRenderer("ApogeeLine", apogeeLineColor);
        perigeeProceduralLine = CreateProceduralLineRenderer("PerigeeLine", perigeeLineColor);
        preManeuverLine = CreateProceduralLineRenderer("PreManeuverLine", "#CCCCCC");
        previewLine = CreateProceduralLineRenderer("PreviewLine", "#FFD166"); // soft yellow

        if (gravityManager == null) Debug.LogError("[TrajectoryRenderer] missing GravityManager");
        if (cameraMovement == null) Debug.LogError("[TrajectoryRenderer] missing CameraMovement");
        if (thrustController == null) Debug.LogError("[TrajectoryRenderer] missing ThrustController");
        if (uIManager == null) Debug.LogError("[TrajectoryRenderer] missing UIManager");
    }

    /// <summary>
    /// Updates internal state, including thrust status, each frame.
    /// </summary>
    void Update()
    {
        if (trackedBody == null)
        {
            // ensure lines are not lingering
            if (predictionProceduralLine != null) predictionProceduralLine.Clear();
            if (originProceduralLine != null) originProceduralLine.Clear();
            if (apogeeProceduralLine != null) apogeeProceduralLine.Clear();
            if (perigeeProceduralLine != null) perigeeProceduralLine.Clear();
            if (preManeuverLine != null) preManeuverLine.Clear();

            // hide the apogee/perigee panel if you want it gone in FreeCam
            if (uIManager != null) uIManager.ShowApogeePerigeePanel(false);
            return;
        }

        if (thrustController != null)
        {
            isThrusting = thrustController.IsThrusting;

            if (isThrusting && !savedOriginalOrbit)
            {
                SaveCurrentTrajectoryAsOriginal();
                savedOriginalOrbit = true;
            }
            else if (!isThrusting)
            {
                savedOriginalOrbit = false;
            }
        }

        if (trackedBody.cumulativeDeltaVUsed != 0f)
        {
            ShowDeltaV();
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
        // 1) Stop any previous prediction loop right away
        if (predictionCoroutine != null)
        {
            StopCoroutine(predictionCoroutine);
            predictionCoroutine = null;
        }

        // 2) IMMEDIATE visual clear so no lingering geometry can show
        if (predictionProceduralLine != null) predictionProceduralLine.Clear();
        if (originProceduralLine != null) originProceduralLine.Clear();
        if (apogeeProceduralLine != null) apogeeProceduralLine.Clear();
        if (perigeeProceduralLine != null) perigeeProceduralLine.Clear();
        if (preManeuverLine != null) preManeuverLine.Clear();

        // 3) Assign new tracked body
        trackedBody = body;

        // 4) If going to FreeCam/placeholder etc., keep UI hidden and bail
        if (trackedBody == null)
        {
            if (uIManager != null) uIManager.ShowApogeePerigeePanel(false);
            orbitIsDirty = false;
            isComputingPrediction = false;
            return;
        }

        // 5) We have a real target: show UI and recompute next frame
        if (uIManager != null) uIManager.ShowApogeePerigeePanel(true);

        orbitIsDirty = true;            // force a fresh prediction
        isComputingPrediction = false;  // reset guard just in case
        predictionCoroutine = StartCoroutine(RecomputeTrajectory());
    }


    public void ClearAllLinesAndUI()
    {
        SetTrackedBody(null); // the logic above already clears visuals + UI and stops coroutines
    }

    private void SaveCurrentTrajectoryAsOriginal()
    {
        trackedBody.CalculatePredictedTrajectoryGPU_Async(predictionSteps, predictionDeltaTime, (resultList) =>
        {
            var clipped = ClipTrajectory(resultList.ToArray());
            preManeuverLine.UpdateLine(clipped);
        });
    }

    public void ClearPreManeuverLine()
    {
        preManeuverLine.Clear();
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
                preManeuverLine.Clear();
                yield return new WaitForSeconds(0.1f);
                continue;
            }

            var orbitalParams = OrbitalCalculations.CalculateOrbitalParameters(
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
                    100000
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
                latestPrediction = new List<Vector3>(resultList);

                float totalSimTime = predictionSteps * predictionDeltaTime;
                float actualDeltaTime = totalSimTime / latestPrediction.Count;

                latestPredictionDeltaTime = actualDeltaTime;
                latestPredictionStartTime = gravityManager.simulationTime;

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

                    uIManager.UpdateOrbitUI(apogeeAltitude, perigeeAltitude, orbitalParams.semiMajorAxis, orbitalParams.eccentricity,
                        orbitalParams.orbitalPeriod, orbitalParams.inclination, orbitalParams.RAAN);
                }
            }
        }
    }

    private void ShowDeltaV()
    {
        uIManager.UpdateDeltaV(trackedBody.cumulativeDeltaVUsed);
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
                preManeuverLine.SetVisibility(false);
            }
            else
            {
                predictionProceduralLine.SetVisibility(true);
                originProceduralLine.SetVisibility(true);
                apogeeProceduralLine.SetVisibility(true);
                perigeeProceduralLine.SetVisibility(true);
                preManeuverLine.SetVisibility(true);
            }
        }
    }

    /// <summary>
    /// Request a cheap, throttled preview from an arbitrary state (used while dragging).
    /// </summary>
    public void QuickPreviewFromState(Vector3 startPos, Vector3 startVel, float bodyMass)
    {
        // stash latest and mark dirty; a single coroutine will service updates
        previewPos = startPos;
        previewVel = startVel;
        previewMass = Mathf.Max(1f, bodyMass);
        previewDirty = true;

        if (previewRoutine == null)
            previewRoutine = StartCoroutine(QuickPreviewLoop());
    }

    /// <summary>Clear the transient preview line.</summary>
    public void ClearPreview()
    {
        previewDirty = false;
        if (previewLine != null) previewLine.Clear();
        if (previewRoutine != null)
        {
            StopCoroutine(previewRoutine);
            previewRoutine = null;
        }
    }

    private IEnumerator QuickPreviewLoop()
    {
        // Throttle to ~10 Hz so we don't spam the GPU while the slider changes every frame.
        const float tick = 0.1f;

        while (true)
        {
            if (!previewDirty)
            {
                yield return new WaitForSeconds(tick);
                continue;
            }
            previewDirty = false;

            // Build "other bodies" arrays (influence field). Keep it tiny for speed.
            var others = ctx.BodyService.Bodies;
            var posList = BuildOtherPositions();
            var massList = BuildOtherMasses();

            if (posList.Count == 0 || massList.Count == 0)
            {
                // Nothing to simulate against; clear preview and wait for next tick
                if (previewLine != null) previewLine.Clear();
                yield return new WaitForSeconds(tick);
                continue;
            }

            for (int i = 0; i < others.Count; i++)
            {
                var b = others[i];
                // optional: skip the preview body itself if it already has an NBody
                posList.Add(b.transform.position);
                massList.Add((float)b.mass);
            }

            // Cheap settings — fast and smooth enough for a preview
            int steps = 1500;          // not huge
            float dt = 2f;             // small-ish timestep
            ctx.TrajectoryComputeController.CalculateTrajectoryGPU_Async(
                previewPos,
                previewVel,
                previewMass,
                posList.ToArray(),
                massList.ToArray(),
                dt,
                steps,
                (points) =>
                {
                    if (points == null || points.Length < 2)
                    {
                        previewLine.Clear();
                        return;
                    }
                    // Clip to impacts like your main path does (optional)
                    var clipped = ClipTrajectory(points);
                    previewLine.UpdateLine(clipped);
                });

            yield return new WaitForSeconds(tick);
        }
    }

    private List<Vector3> BuildOtherPositions()
    {
        var svc = ctx.BodyService;
        var list = new List<Vector3>();
        if (svc != null && svc.Bodies != null)
        {
            for (int i = 0; i < svc.Bodies.Count; i++)
            {
                var b = svc.Bodies[i];
                if (b != null) list.Add(b.transform.position);
            }
        }
        // Fallback: at least include Earth so count > 0
        if (list.Count == 0 && svc != null && svc.CentralBody != null)
            list.Add(svc.CentralBody.transform.position);

        return list;
    }

    private List<float> BuildOtherMasses()
    {
        var svc = ctx.BodyService;
        var list = new List<float>();
        if (svc != null && svc.Bodies != null)
        {
            for (int i = 0; i < svc.Bodies.Count; i++)
            {
                var b = svc.Bodies[i];
                if (b != null) list.Add((float)b.mass);
            }
        }
        // Fallback: central body mass
        if (list.Count == 0 && svc != null && svc.CentralBody != null)
            list.Add((float)svc.CentralBody.mass);

        return list;
    }

    /// <summary>
    /// Do a single higher-quality preview pass (used on drag end).
    /// Temporarily pauses the throttled loop so the long preview isn't overwritten.
    /// </summary>
    public void QuickPreviewOnceLong(Vector3 startPos, Vector3 startVel, float bodyMass, int steps = 8000, float dt = 2f)
    {
        // Pause the throttled loop so it doesn't overwrite this longer pass
        if (previewRoutine != null)
        {
            StopCoroutine(previewRoutine);
            previewRoutine = null;
        }

        var posList = BuildOtherPositions();
        var massList = BuildOtherMasses();

        ctx.TrajectoryComputeController.CalculateTrajectoryGPU_Async(
            startPos,
            startVel,
            Mathf.Max(1f, bodyMass),
            posList.ToArray(),
            massList.ToArray(),
            dt,
            steps,
            (points) =>
            {
                if (points == null || points.Length < 2)
                {
                    previewLine.Clear();
                    return;
                }
                var clipped = ClipTrajectory(points);
                previewLine.UpdateLine(clipped);

                // After we’ve drawn the long pass, resume the light, throttled loop
                if (previewRoutine == null)
                    previewRoutine = StartCoroutine(QuickPreviewLoop());
            });
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

