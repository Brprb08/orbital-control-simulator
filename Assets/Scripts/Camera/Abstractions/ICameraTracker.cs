using System;
using UnityEngine;

/// <summary>
/// Defines camera tracking behavior, including mode switching and target management.
/// Handles transitions between free, Earth, and body tracking modes, and provides
/// events for when the tracked object or mode changes.
/// </summary>
public interface ICameraTracker
{
    // Mode + state
    CameraMode Mode { get; }
    bool IsFree { get; }
    bool IsEarthView { get; }
    bool IsTrackingPlaceholder { get; }

    NBody CurrentBody { get; }
    Transform CurrentPlaceholder { get; }

    // Events
    event Action<CameraMode> OnModeChanged;
    event Action<NBody> OnTrackedBodyChanged;
    event Action<Transform> OnTrackedPlaceholderChanged;

    // Core camera control
    void TrackBody(NBody body);
    void TrackPlaceholder(Transform placeholder);
    void SwitchToEarthCam();
    void BreakToFreeCam();
    void ReturnToTracking();

    // Satellite list maintenance
    void RefreshBodiesList();

    // UI-signal suppression (for placement flows)
    void BeginUiSuppress();
    void EndUiSuppress();

    // Placement preview helpers
    void PreviewPlaceholderInFree(Transform placeholder);
    void EndPreviewPlaceholder();
}
