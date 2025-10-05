// TutorialProgress.cs
using System.Collections.Generic;

public class TutorialProgress
{
    private readonly Dictionary<RequirementType, bool> _completed = new()
    {
        { RequirementType.RotateViewRMB, false },
        { RequirementType.ZoomScroll, false },
        { RequirementType.SwitchSatelliteTrack, false },
        { RequirementType.SwitchToEarthCam, false },
        { RequirementType.SwitchToFreeCam, false },
        { RequirementType.PressW, false },
        { RequirementType.PressA, false },
        { RequirementType.PressS, false },
        { RequirementType.PressD, false },
        { RequirementType.RotateViewRMBFree, false },
        { RequirementType.EnterMass, false },
        { RequirementType.EnterRadius, false },
        { RequirementType.PlaceSatellite, false },
        { RequirementType.ClickSatelliteAndDrag, false },
        { RequirementType.AddVelocity, false },
        { RequirementType.SetVelocity, false },
        { RequirementType.ChangedTimeScale, false },
        { RequirementType.ApplyThrust, false },
        { RequirementType.ClickSetupForNode, false },
        { RequirementType.PlaceManeuverNode, false },
    };

    public bool IsComplete(RequirementType type)
    {
        if (type == RequirementType.None) return true;
        return _completed.TryGetValue(type, out var v) && v;
    }

    public void SetComplete(RequirementType type, bool value = true)
    {
        if (type == RequirementType.None) return;
        if (_completed.ContainsKey(type)) _completed[type] = value;
    }

    public void ResetAll()
    {
        var keys = new List<RequirementType>(_completed.Keys);
        foreach (var k in keys) _completed[k] = false;
    }
}
