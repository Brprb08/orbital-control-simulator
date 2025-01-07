using UnityEngine;
using System.Collections.Generic;

/**
 * LineVisibilityManager handles the toggling of specific LineRenderers across all NBody instances.
 * It works in conjunction with ToggleButton scripts attached to UI Buttons.
 */
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

    // Dictionary to track the current visibility state of each LineType
    private Dictionary<LineType, bool> lineVisibilityStates = new Dictionary<LineType, bool>()
    {
        { LineType.Prediction, true }, // Default to visible
        { LineType.Origin, true }      // Default to visible
    };

    // List to keep track of all registered NBody instances
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

    /// <summary>
    /// Registers an NBody instance with the manager.
    /// Call this method from NBody.Awake()
    /// </summary>
    /// <param name="body">The NBody instance to register.</param>
    public void RegisterNBody(NBody body)
    {
        if (!nBodyInstances.Contains(body))
        {
            nBodyInstances.Add(body);
            // Apply current visibility states to the newly registered NBody
            ApplyVisibilityToBody(body);
        }
    }

    /// <summary>
    /// Deregisters an NBody instance from the manager.
    /// Call this method from NBody.OnDestroy()
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
    /// Sets the visibility of a specific LineType across all registered NBody instances.
    /// Called by ToggleButton scripts when a button is pressed or released.
    /// </summary>
    /// <param name="lineType">The type of line to toggle.</param>
    /// <param name="isVisible">True to show the lines, False to hide.</param>
    public void SetLineVisibility(LineType lineType, bool isVisible)
    {
        if (lineVisibilityStates.ContainsKey(lineType))
        {
            // Update the visibility state in the dictionary
            lineVisibilityStates[lineType] = isVisible;

            // Apply the updated visibility state to all registered NBody instances
            foreach (NBody body in nBodyInstances)
            {
                // Retrieve current states
                bool currentPredictionState = lineVisibilityStates[LineType.Prediction];
                bool currentOriginState = lineVisibilityStates[LineType.Origin];

                // Apply both states to ensure independent control
                body.SetLineVisibility(currentPredictionState, currentOriginState);
            }

            // Optional: Log the change for debugging purposes
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
        body.SetLineVisibility(
            showPrediction: lineVisibilityStates[LineType.Prediction],
            showOrigin: lineVisibilityStates[LineType.Origin]
        );
    }

    public bool GetInitialLineState(LineType lineType)
    {
        if (lineVisibilityStates.ContainsKey(lineType))
        {
            return lineVisibilityStates[lineType];  // Return initial visibility state
        }
        return true;  // Default to visible if not found
    }
}