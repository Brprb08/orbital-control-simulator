// TutorialTypes.cs
using UnityEngine;

public enum RequirementType
{
    None,
    RotateViewRMB,
    ZoomScroll,
    SwitchSatelliteTrack,
    SwitchToEarthCam,
    SwitchToFreeCam,
    PressW,
    PressA,
    PressS,
    PressD,
    RotateViewRMBFree,
    EnterMass,
    EnterRadius,
    PlaceSatellite,
    ClickSatelliteAndDrag,
    AddVelocity,
    SetVelocity,
    ChangedTimeScale,
    ApplyThrust,
    ClickSetupForNode,
    PlaceManeuverNode
}

[System.Serializable]
public struct RequirementDef
{
    public RequirementType type;
    [Tooltip("What the checkbox label shows for this requirement.")]
    public string label;
}

[System.Serializable]
public struct TutorialStep
{
    [TextArea(3, 12)] public string body;
    public RequirementDef[] requirements;

    [TextArea(2, 8)] public string interstitialBody;
    public bool showInterstitialAfterComplete;
    public bool autoAdvanceFromInterstitial;
    public float autoAdvanceDelay;
}
