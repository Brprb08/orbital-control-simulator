using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages the visibility of specific line renderers (prediction, origin, and apogee/perigee lines)
/// for all NBody instances in the scene. Works with ToggleButton scripts to control UI-based visibility toggles.
/// </summary>
public class LineVisibilityManager : MonoBehaviour
{
    public static LineVisibilityManager Instance { get; private set; }
    private NBody trackedBody;
    public TrajectoryRenderer centralTrajectoryRenderer;

    /// <summary>
    /// Enum for types of lines that can be toggled on/off.
    /// </summary>
    public enum LineType
    {
        Prediction,
        Origin,
        ApogeePerigee
    }

    private Dictionary<LineType, bool> lineVisibilityStates = new Dictionary<LineType, bool>()
    {
        { LineType.Prediction, true }, // Default to visible
        { LineType.Origin, true },
        { LineType.ApogeePerigee, true }
    };

    private List<NBody> nBodyInstances = new List<NBody>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    /// <summary>
    /// Registers an NBody instance with the manager.
    /// Call this method from NBody.Awake().
    /// </summary>
    /// <param name="body">The NBody instance to register.</param>
    public void RegisterNBody(NBody body)
    {
        if (!nBodyInstances.Contains(body))
        {
            nBodyInstances.Add(body);
            ApplyVisibilityToBody(body);
        }
    }

    /// <summary>
    /// Deregisters an NBody instance from the manager.
    /// Call this method from NBody.OnDestroy().
    /// </summary>
    /// <param name="body">The NBody instance to deregister.</param>
    public void DeregisterNBody(NBody body)
    {
        if (nBodyInstances.Contains(body))
        {
            nBodyInstances.Remove(body);
        }
    }

    /// <summary>
    /// Sets the visibility of a specific line type across all registered NBody instances.
    /// Called by ToggleButton scripts when a button is pressed or released.
    /// </summary>
    /// <param name="lineType">The type of line to toggle.</param>
    /// <param name="isVisible">True to show the line; false to hide it.</param>
    public void SetLineVisibility(LineType lineType, bool isVisible)
    {
        if (lineVisibilityStates.ContainsKey(lineType))
        {
            lineVisibilityStates[lineType] = isVisible;

            centralTrajectoryRenderer = FindFirstObjectByType<TrajectoryRenderer>();
            if (centralTrajectoryRenderer != null)
            {
                bool currentPredictionState = lineVisibilityStates[LineType.Prediction];
                bool currentOriginState = lineVisibilityStates[LineType.Origin];
                bool currentApogeePerigeeState = lineVisibilityStates[LineType.ApogeePerigee];
                centralTrajectoryRenderer.SetLineVisibility(currentPredictionState, currentOriginState, currentApogeePerigeeState);
            }
            else
            {
                Debug.LogWarning("Central TrajectoryRenderer not found!");
            }

            Debug.Log($"LineVisibilityManager: {lineType} Lines are now {(isVisible ? "Enabled" : "Disabled")}");
        }
        else
        {
            Debug.LogError($"LineVisibilityManager: Attempted to toggle unknown LineType '{lineType}'.");
        }
    }

    /// <summary>
    /// Applies the current visibility states to a specific NBody instance.
    /// Used when a new NBody registers.
    /// </summary>
    /// <param name="body">The NBody instance to apply visibility to.</param>
    private void ApplyVisibilityToBody(NBody body)
    {
        TrajectoryRenderer trajectoryRenderer = body.GetComponentInChildren<TrajectoryRenderer>();
        if (trajectoryRenderer != null)
        {
            trajectoryRenderer.SetLineVisibility(
                    showPrediction: lineVisibilityStates[LineType.Prediction],
                    showOrigin: lineVisibilityStates[LineType.Origin],
                    showApogeePerigee: lineVisibilityStates[LineType.ApogeePerigee]
                );
        }
    }

    /// <summary>
    /// Returns the current visibility state of the specified line type.
    /// Used by ToggleButton scripts to preset toggle state.
    /// </summary>
    /// <param name="lineType">The line type to check.</param>
    /// <returns>True if the line is initially visible, false otherwise.</returns>
    public bool GetInitialLineState(LineType lineType)
    {
        if (lineVisibilityStates.ContainsKey(lineType))
        {
            return lineVisibilityStates[lineType];
        }
        return true;  // Default to visible if not found
    }

    /// <summary>
    /// Sets the NBody to be tracked for line rendering purposes.
    /// </summary>
    /// <param name="body">The NBody to track.</param>
    public void SetTrackedBody(NBody body)
    {
        trackedBody = body;
    }
}