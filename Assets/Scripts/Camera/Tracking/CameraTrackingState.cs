using UnityEngine;

/// <summary>
/// Owns camera target selection and restore history so CameraController can focus on transitions.
/// </summary>
internal sealed class CameraTrackingState
{
    public CameraMode Mode { get; private set; } = CameraMode.Track;
    public NBody CurrentBody { get; private set; }
    public Transform CurrentPlaceholder { get; private set; }

    public NBody LastTrackedBeforeEarth { get; private set; }
    public NBody LastTrackedBeforePlaceholder { get; private set; }
    public NBody LastTrackedBeforeFree { get; private set; }

    public bool PreserveAngleNextTrack { get; set; }
    public bool PlaceholderEarthViewActive { get; private set; }

    public bool IsFree => Mode == CameraMode.Free && !PlaceholderEarthViewActive;
    public bool IsEarthView => Mode == CameraMode.Earth || PlaceholderEarthViewActive;
    public bool IsTrackingPlaceholder => CurrentPlaceholder != null && CurrentBody == null && !PlaceholderEarthViewActive;
    public bool HasPreviousEarthTarget => LastTrackedBeforeEarth != null;

    public void SetMode(CameraMode mode) => Mode = mode;

    public void Track(CameraTarget target)
    {
        if (target.IsBody)
        {
            TrackBody(target.Body);
            return;
        }

        if (target.IsPlaceholder)
        {
            TrackPlaceholder(target.Placeholder);
            return;
        }

        if (target.IsEarth)
            EnterEarth();
    }

    public void TrackBody(NBody body)
    {
        CurrentBody = body;
        CurrentPlaceholder = null;

        if (body != LastTrackedBeforeEarth)
            LastTrackedBeforeEarth = null;
    }

    public void TrackPlaceholder(Transform placeholder)
    {
        if (CurrentBody != null)
            LastTrackedBeforePlaceholder = CurrentBody;

        CurrentBody = null;
        CurrentPlaceholder = placeholder;
    }

    public void PreviewPlaceholder(Transform placeholder)
    {
        CurrentBody = null;
        CurrentPlaceholder = placeholder;
    }

    public void ClearCurrentTarget()
    {
        CurrentBody = null;
        CurrentPlaceholder = null;
    }

    public void BreakToFree()
    {
        if (CurrentBody != null)
            LastTrackedBeforeFree = CurrentBody;

        CurrentBody = null;
        CurrentPlaceholder = null;
        PreserveAngleNextTrack = true;
    }

    public void EnterEarth()
    {
        CurrentPlaceholder = null;

        if (CurrentBody != null)
            LastTrackedBeforeEarth = CurrentBody;
    }

    public NBody ConsumeLastTrackedBeforePlaceholder()
    {
        NBody body = LastTrackedBeforePlaceholder;
        LastTrackedBeforePlaceholder = null;
        return body;
    }

    public NBody ConsumeLastTrackedBeforeFree()
    {
        NBody body = LastTrackedBeforeFree;
        LastTrackedBeforeFree = null;
        return body;
    }

    public void ClearLastTrackedBeforeFree() => LastTrackedBeforeFree = null;

    public NBody ConsumeLastTrackedBeforeEarth()
    {
        NBody body = LastTrackedBeforeEarth;
        LastTrackedBeforeEarth = null;
        return body;
    }

    public void ClearLastTrackedBeforeEarth() => LastTrackedBeforeEarth = null;

    public void BeginPlaceholderEarthView() => PlaceholderEarthViewActive = true;
    public void EndPlaceholderEarthView() => PlaceholderEarthViewActive = false;
}
