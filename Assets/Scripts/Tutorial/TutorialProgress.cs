using System;
using System.Collections.Generic;

/// <summary>
/// Tracks completion state for all tutorial requirements.
/// Provides lookup, mutation, and reset utilities for step evaluation.
/// </summary>
public class TutorialProgress
{
    private readonly Dictionary<RequirementType, bool> _completed = new();

    public TutorialProgress()
    {
        foreach (RequirementType type in Enum.GetValues(typeof(RequirementType)))
        {
            if (type != RequirementType.None)
                _completed[type] = false;
        }
    }

    /// <summary>
    /// Returns whether a given requirement has been completed.
    /// </summary>
    public bool IsComplete(RequirementType type)
    {
        if (type == RequirementType.None) return true;
        return _completed.TryGetValue(type, out var v) && v;
    }

    /// <summary>
    /// Marks a specific requirement as complete or incomplete.
    /// </summary>
    public void SetComplete(RequirementType type, bool value = true)
    {
        if (type == RequirementType.None) return;
        if (_completed.ContainsKey(type))
            _completed[type] = value;
    }

    /// <summary>
    /// Clears all progress flags, setting all requirements to incomplete.
    /// </summary>
    public void ResetAll()
    {
        var keys = new List<RequirementType>(_completed.Keys);
        foreach (var k in keys)
            _completed[k] = false;
    }
}
