using UnityEngine;

/// <summary>
/// Defines all requirement types that can appear in a tutorial step,
/// representing specific player actions or conditions that must be met
/// to progress through the tutorial sequence.
/// </summary>
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
    EnterPosition,
    EnterMass,
    EnterRadius,
    PlaceSatellite,
    AddVelocity,
    SetVelocity,
    ChangedTimeScale,
    ApplyThrust,
    ClickSetupForNode,
    PlaceManeuverNode
}

/// <summary>
/// Describes a single tutorial requirement, including its type and display label
/// for checklist items in the tutorial UI.
/// </summary>
[System.Serializable]
public struct RequirementDef
{
    public RequirementType type;
    [Tooltip("What the checkbox label shows for this requirement.")]
    public string label;
}

/// <summary>
/// Represents one step in the tutorial sequence, containing instructional text,
/// a list of completion requirements, optional interstitial transition content,
/// and auto-advance settings for smooth tutorial flow.
/// </summary>
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
