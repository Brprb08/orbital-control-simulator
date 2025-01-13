using UnityEngine;
using System.Collections.Generic;

/**
* Manages the visibility of specific line renderers (e.g., prediction and origin lines)
* for all NBody instances in the scene. Works with `ToggleButton` scripts to control UI-based
* visibility toggles.
**/
public class LineVisibilityManager : MonoBehaviour
{
    // Singleton instance for easy access
    public static LineVisibilityManager Instance { get; private set; }

    // Enum representing different types of lines that can be toggled
    public enum LineType
    {
        Prediction, // Controls predictionRenderer, activeRenderer, backgroundRenderer
        Origin      // Controls originLineRenderer
    }

    private Dictionary<LineType, bool> lineVisibilityStates = new Dictionary<LineType, bool>()
    {
        { LineType.Prediction, true }, // Default to visible
        { LineType.Origin, true }      // Default to visible
    };

    private List<NBody> nBodyInstances = new List<NBody>();

    void Awake()
    {
        // Implement Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            // Optionally, make this persistent across scenes
            // DontDestroyOnLoad(gameObject);
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
            // Apply current visibility states to the newly registered NBody
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
            // Update the visibility state in the dictionary
            lineVisibilityStates[lineType] = isVisible;

            // Apply the updated visibility state to all registered NBody instances
            foreach (NBody body in nBodyInstances)
            {
                // Get the TrajectoryRenderer attached to this specific NBody
                TrajectoryRenderer trajectoryRenderer = body.GetComponentInChildren<TrajectoryRenderer>();
                if (trajectoryRenderer != null)
                {
                    bool currentPredictionState = lineVisibilityStates[LineType.Prediction];
                    bool currentOriginState = lineVisibilityStates[LineType.Origin];

                    trajectoryRenderer.SetLineVisibility(currentPredictionState, currentOriginState);
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
            trajectoryRenderer.SetLineVisibility(
                    showPrediction: lineVisibilityStates[LineType.Prediction],
                    showOrigin: lineVisibilityStates[LineType.Origin]
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
            return lineVisibilityStates[lineType];  // Return initial visibility state
        }
        return true;  // Default to visible if not found
    }
}