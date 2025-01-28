using UnityEngine;
using System.Collections.Generic;

/**
* Manages the visibility of specific line renderers (e.g., prediction, origin, and apogee/perigee lines)
* for all NBody instances in the scene. Works with `ToggleButton` scripts to control UI-based
* visibility toggles.
**/
public class LineVisibilityManager : MonoBehaviour
{
    public static LineVisibilityManager Instance { get; private set; }
    private NBody trackedBody;

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

    /**
    * Registers an NBody instance with the manager.
    * Call this method from NBody.Awake()
    * @param body - The NBody instance to register
    **/
    public void RegisterNBody(NBody body)
    {
        if (!nBodyInstances.Contains(body))
        {
            nBodyInstances.Add(body);
            ApplyVisibilityToBody(body);
        }
    }

    /**
    * Deregisters an NBody instance from the manager.
    * Call this method from NBody.OnDestroy()
    * @param body - The NBody instance to deregister
    **/
    public void DeregisterNBody(NBody body)
    {
        if (nBodyInstances.Contains(body))
        {
            nBodyInstances.Remove(body);
        }
    }

    /**
    * Sets the visibility of a specific LineType across all registered NBody instances.
    * Called by ToggleButton scripts when a button is pressed or released.
    * @param lineType - The type of line to toggle
    * @param isVisible - True to show the lines, False to hide
    **/
    public void SetLineVisibility(LineType lineType, bool isVisible)
    {
        if (lineVisibilityStates.ContainsKey(lineType))
        {
            lineVisibilityStates[lineType] = isVisible;

            foreach (NBody body in nBodyInstances)
            {
                TrajectoryRenderer trajectoryRenderer = body.GetComponentInChildren<TrajectoryRenderer>();
                if (trajectoryRenderer != null)
                {
                    bool currentPredictionState = lineVisibilityStates[LineType.Prediction];
                    bool currentOriginState = lineVisibilityStates[LineType.Origin];
                    bool currentApogeePerigeeState = lineVisibilityStates[LineType.ApogeePerigee];
                    trajectoryRenderer.SetLineVisibility(currentPredictionState, currentOriginState, currentApogeePerigeeState);
                }
                else
                {
                    Debug.LogWarning($"No TrajectoryRenderer found for {body.name}");
                }

            }

            Debug.Log($"LineVisibilityManager: {lineType} Lines are now {(isVisible ? "Enabled" : "Disabled")}");
        }
        else
        {
            Debug.LogError($"LineVisibilityManager: Attempted to toggle unknown LineType '{lineType}'.");
        }
    }

    /**
    * Applies the current visibility states to a specific NBody instance.
    * Used when a new NBody registers.
    * @param body - The NBody instance to apply visibility to
    **/
    private void ApplyVisibilityToBody(NBody body)
    {
        TrajectoryRenderer trajectoryRenderer = body.GetComponentInChildren<TrajectoryRenderer>();
        if (trajectoryRenderer != null)
        {
            Debug.LogError("APPLY VISIBILITY SETTING LINE VISIBILITY");
            trajectoryRenderer.SetLineVisibility(
                    showPrediction: lineVisibilityStates[LineType.Prediction],
                    showOrigin: lineVisibilityStates[LineType.Origin],
                    showApogeePerigee: lineVisibilityStates[LineType.ApogeePerigee]
                );
        }
    }

    /**
    * Used by Togglebutton for presetting the button states
    **/
    public bool GetInitialLineState(LineType lineType)
    {
        if (lineVisibilityStates.ContainsKey(lineType))
        {
            return lineVisibilityStates[lineType];
        }
        return true;  // Default to visible if not found
    }

    /**
    * Assigns the NBody to be tracked by this TrajectoryRenderer.
    * @param body - Nbody the line renders switch to.
    **/
    public void SetTrackedBody(NBody body)
    {
        Debug.LogError(body);
        trackedBody = body;
    }
}