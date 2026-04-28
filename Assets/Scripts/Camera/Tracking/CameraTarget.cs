using UnityEngine;

/// <summary>
/// Describes the concrete thing a camera transition should focus.
/// </summary>
internal readonly struct CameraTarget
{
    public enum TargetKind
    {
        None,
        Body,
        Placeholder,
        Earth
    }

    private CameraTarget(TargetKind kind, NBody body, Transform placeholder)
    {
        Kind = kind;
        Body = body;
        Placeholder = placeholder;
    }

    public TargetKind Kind { get; }
    public NBody Body { get; }
    public Transform Placeholder { get; }

    public bool IsBody => Kind == TargetKind.Body && Body != null;
    public bool IsPlaceholder => Kind == TargetKind.Placeholder && Placeholder != null;
    public bool IsEarth => Kind == TargetKind.Earth && Body != null;
    public bool IsNone => Kind == TargetKind.None;

    public CameraMode Mode => IsEarth ? CameraMode.Earth : CameraMode.Track;

    public static CameraTarget None() => new(TargetKind.None, null, null);
    public static CameraTarget BodyTarget(NBody body) => new(TargetKind.Body, body, null);
    public static CameraTarget PlaceholderTarget(Transform placeholder) => new(TargetKind.Placeholder, null, placeholder);
    public static CameraTarget EarthTarget(NBody earth) => new(TargetKind.Earth, earth, null);
}
