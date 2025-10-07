using UnityEngine;
using System;

public interface ICameraTracker
{
    CameraMode Mode { get; }
    event Action<CameraMode> OnModeChanged;

    // events
    event Action<NBody> OnTrackedBodyChanged;
    event Action<Transform> OnTrackedPlaceholderChanged;

    // state
    bool IsFree { get; }
    bool IsEarthView { get; }
    NBody CurrentBody { get; }
    Transform CurrentPlaceholder { get; }

    bool IsTrackingPlaceholder { get; }

    // commands
    void TrackBody(NBody body);
    void TrackPlaceholder(Transform placeholder);
    void SwitchToEarthCam();
    void BreakToFreeCam();
    void ReturnToTracking();
    void RefreshBodiesList();
    void BeginUiSuppress();
    void EndUiSuppress();

    void PreviewPlaceholderInFree(Transform placeholder);
    void EndPreviewPlaceholder();
}
