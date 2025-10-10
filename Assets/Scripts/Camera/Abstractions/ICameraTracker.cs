using UnityEngine;
using System;

/// <summary>
/// Defines camera tracking behavior, including mode switching and target management.
/// Handles transitions between free, Earth, and body tracking modes, and provides
/// events for when the tracked object or mode changes.
/// </summary>
public interface ICameraTracker
{
    CameraMode Mode { get; }
    event Action<CameraMode> OnModeChanged;

    event Action<NBody> OnTrackedBodyChanged;
    event Action<Transform> OnTrackedPlaceholderChanged;

    // state
    bool IsFree { get; }
    bool IsEarthView { get; }
    NBody CurrentBody { get; }
    Transform CurrentPlaceholder { get; }

    bool IsTrackingPlaceholder { get; }

    // Calls to CameraController
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
