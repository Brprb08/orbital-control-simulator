using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using TMPro;

public class ManeuverNodeManager : MonoBehaviour
{
    [Header("Maneuver Nodes")]
    public List<ManeuverNode> nodes = new();
    public List<Vector3> cachedTrajectory;

    [Header("Trajectory Rendering")]
    public ProceduralLineRenderer maneuverTrajectoryLine;
    public TrajectoryRenderer trajectoryRenderer;
    public TimeController timeController;

    [Header("References - UI")]
    public Slider maneuverTimeSlider;
    public TMP_Dropdown burnDropdown;
    public Button setupButton;
    public Slider adjustNodeSlider;
    public Button placeNodeButton;

    [Header("UI Controls")]
    public bool isSliderActive = false;

    [SerializeField] Material green;
    [SerializeField] Material red;

    public GravityManager gravityManager;
    private SimContext ctx;

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.gravityManager = ctx.GravityManager;
        this.trajectoryRenderer = ctx.TrajectoryRenderer;
        this.timeController = ctx.TimeController;

        burnDropdown.ClearOptions();
        List<string> burnOptions = new List<string>
        {
            "Prograde",
            "Retrograde",
            "Radial In",
            "Radial Out",
            "Normal",
            "Anti-Normal"
        };

        List<TMP_Dropdown.OptionData> optionData = burnOptions
            .Select(dir => new TMP_Dropdown.OptionData(dir))
            .ToList();

        burnDropdown.AddOptions(optionData);

        adjustNodeSlider.interactable = false;
        placeNodeButton.interactable = false;
    }

    /// <summary>
    /// Adds a new preview maneuver node at a fixed time offset along the current predicted trajectory.
    /// Calculates the interpolated position and initializes slider controls for user adjustment.
    /// </summary>
    public void OnAddManeuverNode()
    {
        // TrajectoryRenderer trajectoryRenderer = FindFirstObjectByType<TrajectoryRenderer>();
        timeController.SetTimeScale(1f);
        timeController.timeSlider.value = Time.timeScale;
        var body = trajectoryRenderer.trackedBody;
        if (body == null) return;

        if (trajectoryRenderer.latestPrediction == null || trajectoryRenderer.latestPrediction.Count == 0)
        {
            Debug.LogError("No trajectory prediction available!");
            return;
        }

        float initialOffsetTime = 20f;
        float burnTime = gravityManager.simulationTime + initialOffsetTime;
        float deltaT = trajectoryRenderer.latestPredictionDeltaTime;

        float timeFromPredictionStart = burnTime - trajectoryRenderer.latestPredictionStartTime;
        if (timeFromPredictionStart < 0)
        {
            Debug.LogError("Burn time is before prediction start time. This is invalid.");
            return;
        }

        float floatIndex = timeFromPredictionStart / deltaT;
        int index = Mathf.FloorToInt(floatIndex);
        float t = floatIndex - index;

        index = Mathf.Clamp(index, 0, trajectoryRenderer.latestPrediction.Count - 2);
        Vector3 a = trajectoryRenderer.latestPrediction[index];
        Vector3 b = trajectoryRenderer.latestPrediction[index + 1];
        Vector3 burnPos = Vector3.Lerp(a, b, t);
        Vector3 deltaV = body.velocity.normalized * 1f;
        float burnDuration = 5f;

        // Set as a preview node, not committed yet
        CreatePreviewNode(burnPos, burnTime, deltaV, burnDuration);

        // Setup the slider so the user can move the node around
        SetupSlider(trajectoryRenderer.latestPrediction, burnTime, deltaT);

        setupButton.interactable = false;
        adjustNodeSlider.interactable = true;
        placeNodeButton.interactable = true;
    }


    /// <summary>
    /// Finalizes the current preview node, marking it as ready for execution.
    /// If the burn is in the past, it is wrapped forward by one or more orbital periods.
    /// </summary>
    public void FinalizeManeuver()
    {
        if (nodes.Count == 0) return;

        var node = nodes[0];
        node.isFinalized = true;
        node.marker.name = "ManeuverNode";
        node.marker.GetComponent<Renderer>().material = new Material(red);
        node.marker.GetComponent<Renderer>().material.SetColor("_BaseColor", red.GetColor("_BaseColor"));

        isSliderActive = false;

        // Wrap burn time if it's in the past
        float simTime = gravityManager.simulationTime;
        var trackedBody = trajectoryRenderer.trackedBody;
        var centralBody = gravityManager.CentralBody;

        // Use your orbital parameter calculation
        OrbitalParameters orbit = OrbitalCalculations.CalculateOrbitalParameters(
            centralBody.mass,
            centralBody.transform.position,
            trackedBody.transform,
            trackedBody.velocity
        );

        if (!orbit.isCircular)
        {
            Debug.LogWarning("Invalid orbit — burn time will not be wrapped.");
            return;
        }

        // Wrap the burn time forward in increments of orbital period
        while (node.burnTime < simTime)
        {
            node.burnTime += orbit.orbitalPeriod;
        }

        Debug.Log($"Finalized maneuver burn time: {node.burnTime:F2} (Simulation time: {simTime:F2})");
    }

    // /// <summary>
    // /// Adds a finalized maneuver node to the tracked body with specified parameters.
    // /// </summary>
    // /// <param name="position">World-space position of the node marker.</param>
    // /// <param name="burnTime">The simulation time at which the burn begins.</param>
    // /// <param name="deltaV">The intended change in velocity for the burn.</param>
    // /// <param name="duration">The duration of the burn in simulation seconds.</param>

    // public void AddNode(Vector3 position, float burnTime, Vector3 deltaV, float duration)
    // {
    //     // TrajectoryRenderer trajectoryRenderer = FindFirstObjectByType<TrajectoryRenderer>();
    //     var trackedBody = trajectoryRenderer.trackedBody;
    //     var node = new ManeuverNode
    //     {
    //         position = position,
    //         burnTime = burnTime,
    //         deltaV = deltaV,
    //         marker = GameObject.CreatePrimitive(PrimitiveType.Sphere),
    //         targetBody = trackedBody,
    //         duration = duration,
    //         burnType = GetBurnChoice()
    //     };

    //     node.marker.transform.position = position;
    //     node.marker.transform.localScale = Vector3.one * 5f;
    //     node.marker.name = "ManeuverNode";
    //     node.marker.GetComponent<Renderer>().material.color = Color.cyan;

    //     nodes.Add(node);
    //     UpdateManeuverPrediction();
    // }

    /// <summary>
    /// Removes the given maneuver node and its associated marker from the system.
    /// </summary>
    /// <param name="node">The maneuver node to remove.</param>
    public void RemoveNode(ManeuverNode node)
    {
        if (node.marker != null) Destroy(node.marker);
        nodes.Remove(node);
        UpdateManeuverPrediction();
    }

    /// <summary>
    /// Clears all maneuver nodes and destroys their markers from the scene.
    /// </summary>
    public void ClearAllNodes()
    {
        foreach (var node in nodes)
            if (node.marker != null)
                Destroy(node.marker);
        nodes.Clear();
        // maneuverTrajectoryLine.Clear();
    }

    /// <summary>
    /// Recalculates the post-burn trajectory based on the node's delta-V and burn timing.
    /// Requires a valid pre-burn trajectory prediction.
    /// </summary>
    /// <param name="preBurnList">The list of trajectory points before the burn.</param>
    public void UpdateManeuverPrediction(List<Vector3> preBurnList = null)
    {
        if (nodes.Count == 0 || trajectoryRenderer == null)
            return;

        var trackedBody = trajectoryRenderer.trackedBody;
        if (trackedBody == null || preBurnList == null) return;

        var node = nodes[0];
        // int burnStep = Mathf.FloorToInt(node.burnTime / trackedBody.predictionDeltaTime);Time.fixedDeltaTime
        int burnStep = Mathf.FloorToInt(node.burnTime / Time.fixedDeltaTime);

        if (burnStep >= preBurnList.Count)
            return;

        cachedTrajectory = preBurnList;
        var burnPos = preBurnList[burnStep];
        // var preVel = EstimateVelocity(preBurnList, burnStep, trackedBody.predictionDeltaTime);
        var preVel = EstimateVelocity(preBurnList, burnStep, Time.fixedDeltaTime);

        var newVel = preVel + node.deltaV;

        // trackedBody.CalculatePredictedTrajectoryGPU_Async(
        //     trajectoryRenderer.predictionSteps,
        //     Time.fixedDeltaTime,
        //     (postBurnList) =>
        //     {
        //         var fullPath = preBurnList.Take(burnStep).ToList();
        //         fullPath.AddRange(postBurnList);
        //         maneuverTrajectoryLine.UpdateLine(fullPath.ToArray());
        //     },
        //     overrideStartPosition: burnPos,
        //     overrideStartVelocity: newVel
        // );
    }

    /// <summary>
    /// Creates a temporary (preview) maneuver node at the specified location and time.
    /// Used before finalizing the node with user confirmation.
    /// </summary>
    /// <param name="position">World-space position of the preview node.</param>
    /// <param name="burnTime">The proposed burn start time.</param>
    /// <param name="deltaV">Proposed change in velocity.</param>
    /// <param name="duration">Duration of the burn.</param>
    public void CreatePreviewNode(Vector3 position, float burnTime, Vector3 deltaV, float duration)
    {
        ClearAllNodes(); // Only one node at a time, clear any previous ones

        var trackedBody = trajectoryRenderer.trackedBody;
        var node = new ManeuverNode
        {
            position = position,
            burnTime = burnTime,
            deltaV = deltaV,
            marker = GameObject.CreatePrimitive(PrimitiveType.Sphere),
            targetBody = trackedBody,
            duration = duration,
            isFinalized = false,
            burnType = GetBurnChoice()
        };

        node.marker.transform.position = position;
        node.marker.transform.localScale = Vector3.one * 5f;
        node.marker.name = "ManeuverNodePreview";
        node.marker.GetComponent<Renderer>().material = new Material(green);
        node.marker.GetComponent<Renderer>().material.SetColor("_BaseColor", green.GetColor("_BaseColor"));

        nodes.Add(node);
        cachedTrajectory = trajectoryRenderer.latestPrediction;
        UpdateManeuverPrediction(cachedTrajectory);
    }

    /// <summary>
    /// Estimates the velocity of the body at a given step in the trajectory using central difference.
    /// </summary>
    /// <param name="trajectory">The full list of trajectory positions.</param>
    /// <param name="step">The index at which to estimate velocity.</param>
    /// <param name="dt">The simulation time step between trajectory points.</param>
    /// <returns>A velocity vector approximation at the specified step.</returns>
    public Vector3 EstimateVelocity(List<Vector3> trajectory, int step, float dt)
    {
        if (step <= 0 || step >= trajectory.Count - 1) return Vector3.zero;
        return (trajectory[step + 1] - trajectory[step - 1]) / (2f * dt);
    }

    /// <summary>
    /// Initializes the time slider UI component to allow users to move the maneuver node along the trajectory.
    /// </summary>
    /// <param name="trajectory">The full trajectory list used for slider reference.</param>
    /// <param name="burnTime">The default time at which the burn is scheduled.</param>
    /// <param name="predictionDeltaTime">Time delta between trajectory points.</param>
    public void SetupSlider(List<Vector3> trajectory, float burnTime, float predictionDeltaTime)
    {
        if (trajectory == null || trajectory.Count == 0 || maneuverTimeSlider == null)
            return;

        isSliderActive = true;
        maneuverTimeSlider.wholeNumbers = false;

        // Allow the slider to cover the full trajectory range, with fractions
        maneuverTimeSlider.minValue = 0f;
        maneuverTimeSlider.maxValue = trajectory.Count - 1;

        float floatIndex = (burnTime - trajectoryRenderer.latestPredictionStartTime) / predictionDeltaTime;
        maneuverTimeSlider.value = Mathf.Clamp(floatIndex, 0f, maneuverTimeSlider.maxValue);

        maneuverTimeSlider.onValueChanged.RemoveAllListeners();
        maneuverTimeSlider.onValueChanged.AddListener(OnSliderChanged);
    }

    /// <summary>
    /// Called when the user adjusts the maneuver time slider. Updates the node's position and timing accordingly.
    /// </summary>
    /// <param name="value">The slider's normalized value representing a point along the trajectory.</param>
    public void OnSliderChanged(float value)
    {
        if (!isSliderActive || nodes.Count == 0 || cachedTrajectory == null || cachedTrajectory.Count < 2)
            return;

        var node = nodes[0];

        float floatIndex = value;
        int index = Mathf.FloorToInt(floatIndex);
        float t = floatIndex - index;

        index = Mathf.Clamp(index, 0, cachedTrajectory.Count - 2);

        Vector3 a = cachedTrajectory[index];
        Vector3 b = cachedTrajectory[index + 1];
        Vector3 interpolatedPos = Vector3.Lerp(a, b, t);

        float newBurnTime = trajectoryRenderer.latestPredictionStartTime +
                            floatIndex * trajectoryRenderer.latestPredictionDeltaTime;

        node.burnTime = newBurnTime;
        node.position = interpolatedPos;
        node.marker.transform.position = interpolatedPos;

        UpdateManeuverPrediction(cachedTrajectory);
    }

    /// <summary>
    /// Returns the maneuver burn direction based on the current dropdown selection and body orientation.
    /// </summary>
    /// <param name="targetBody">The body whose orientation and velocity determine reference axes.</param>
    /// <returns>A unit vector representing the burn direction.</returns>
    public Vector3 GetBurnDirectionFromDropdown(NBody targetBody)
    {
        if (trajectoryRenderer == null || targetBody == null)
            return targetBody.velocity.normalized; // fallback to prograde

        string selection = burnDropdown.options[burnDropdown.value].text;
        Vector3 velocity = targetBody.velocity.normalized;
        Vector3 up = targetBody.transform.position.normalized;
        Vector3 right = Vector3.Cross(up, velocity).normalized;

        return selection switch
        {
            "Prograde" => velocity,
            "Retrograde" => -velocity,
            "Radial In" => -up,
            "Radial Out" => up,
            "Normal" => right,
            "Anti-Normal" => -right,
            _ => velocity
        };
    }

    /// <summary>
    /// Gets the string label of the currently selected burn direction from the dropdown.
    /// </summary>
    /// <returns>The name of the selected maneuver type (e.g., "Prograde").</returns>
    public string GetBurnChoice()
    {
        return burnDropdown.options[burnDropdown.value].text;
    }
}
