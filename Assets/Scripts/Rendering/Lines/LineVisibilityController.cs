using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Centralizes visibility control for prediction, origin, and apogee/perigee lines.
/// Updates the shared TrajectoryRenderer and applies current states to newly registered bodies.
/// </summary>
public class LineVisibilityController : MonoBehaviour
{
    [Header("References - UI")]
    private NBody trackedBody;
    public TrajectoryRenderer centralTrajectoryRenderer;

    /// <summary>
    /// Line categories that can be toggled.
    /// </summary>
    public enum LineType
    {
        Prediction,
        Origin,
        ApogeePerigee
    }

    private Dictionary<LineType, bool> lineVisibilityStates = new Dictionary<LineType, bool>()
    {
        { LineType.Prediction, true },
        { LineType.Origin, true },
        { LineType.ApogeePerigee, true }
    };

    private List<NBody> nBodyInstances = new List<NBody>();

    private SimContext ctx;

    /// <summary>
    /// Injects the simulation context and binds the central trajectory renderer.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.centralTrajectoryRenderer = ctx.TrajectoryRenderer;
    }

    /// <summary>
    /// Registers an NBody so its lines follow the current visibility settings.
    /// </summary>
    public void RegisterNBody(NBody body)
    {
        if (!nBodyInstances.Contains(body))
        {
            nBodyInstances.Add(body);
            ApplyVisibilityToBody(body);
        }
    }

    /// <summary>
    /// Removes a previously registered NBody.
    /// </summary>
    public void DeregisterNBody(NBody body)
    {
        if (nBodyInstances.Contains(body))
        {
            nBodyInstances.Remove(body);
        }
    }

    /// <summary>
    /// Sets visibility for a given line type and propagates it to the central renderer.
    /// </summary>
    /// <param name="lineType">The line category to toggle.</param>
    /// <param name="isVisible">Whether the line should be visible.</param>
    public void SetLineVisibility(LineType lineType, bool isVisible)
    {
        if (lineVisibilityStates.ContainsKey(lineType))
        {
            lineVisibilityStates[lineType] = isVisible;

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

            Debug.Log($"LineVisibilityController: {lineType} Lines are now {(isVisible ? "Enabled" : "Disabled")}");
        }
        else
        {
            Debug.LogError($"LineVisibilityController: Attempted to toggle unknown LineType '{lineType}'.");
        }
    }

    /// <summary>
    /// Applies current visibility settings to a specific body’s trajectory renderer.
    /// </summary>
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
    /// Returns the current default visibility for a given line type (used to initialize UI toggles).
    /// </summary>
    public bool GetInitialLineState(LineType lineType)
    {
        if (lineVisibilityStates.ContainsKey(lineType))
        {
            return lineVisibilityStates[lineType];
        }
        return true;
    }

    /// <summary>
    /// Updates which body is considered “tracked” for any systems that need this reference.
    /// </summary>
    public void SetTrackedBody(NBody body)
    {
        trackedBody = body;
    }
}
