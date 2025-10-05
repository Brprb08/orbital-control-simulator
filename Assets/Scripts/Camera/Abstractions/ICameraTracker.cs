using UnityEngine;
using System;

public interface ICameraTracker
{
    // events
    event Action<NBody> OnTrackedBodyChanged;
    event Action<Transform> OnTrackedPlaceholderChanged;
    event Action<bool> OnFreeModeChanged;
    event Action<bool> OnEarthViewChanged;

    // state
    bool IsFree { get; }
    bool IsEarthView { get; }
    NBody CurrentBody { get; }
    Transform CurrentPlaceholder { get; }

    // commands
    void TrackBody(NBody body);
    void TrackPlaceholder(Transform placeholder);
    void SwitchToEarthCam();
    void BreakToFreeCam();
    void ReturnToTracking();
    void RefreshBodiesList();
    void BeginUiSuppress();
    void EndUiSuppress();
}
