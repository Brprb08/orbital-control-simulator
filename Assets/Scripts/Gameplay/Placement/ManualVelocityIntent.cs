using UnityEngine;

public enum ManualOrbitBaseDirection
{
    Prograde,
    Retrograde
}

public enum ManualOrbitSpeedIntentSelection
{
    None,
    Circularize,
    RaiseApogee,
    LowerPerigee
}

/// <summary>
/// Pure state and rules for manual-placement orbit intent controls.
/// </summary>
public sealed class ManualVelocityIntent
{
    public ManualOrbitBaseDirection BaseDirection { get; private set; } = ManualOrbitBaseDirection.Prograde;
    public ManualOrbitSpeedIntentSelection SpeedSelection { get; private set; } = ManualOrbitSpeedIntentSelection.None;
    public float BaseSpeedScale { get; private set; }
    public float SpeedTrimScale { get; private set; } = 1f;
    public float SpeedScale { get; private set; }
    public float RadialShapeAmount { get; private set; }
    public float TiltDegrees { get; private set; }

    public void Reset(float defaultSpeedScale)
    {
        BaseDirection = ManualOrbitBaseDirection.Prograde;
        RadialShapeAmount = 0f;
        TiltDegrees = 0f;
        SetBaseSpeedScale(defaultSpeedScale);
        SpeedSelection = ManualOrbitSpeedIntentSelection.None;
    }

    public void StageCircular(float speedScale)
    {
        RadialShapeAmount = 0f;
        SetBaseSpeedScale(speedScale);
        SpeedSelection = ManualOrbitSpeedIntentSelection.Circularize;
    }

    public void StageRetrogradeCircular(float speedScale)
    {
        BaseDirection = ManualOrbitBaseDirection.Retrograde;
        RadialShapeAmount = 0f;
        TiltDegrees = 0f;
        SetBaseSpeedScale(speedScale);
        SpeedSelection = ManualOrbitSpeedIntentSelection.Circularize;
    }

    public void StageRaiseApogee(float speedScale)
    {
        SetBaseSpeedScale(speedScale);
        SpeedSelection = ManualOrbitSpeedIntentSelection.RaiseApogee;
    }

    public void StageLowerPerigee(float speedScale)
    {
        SetBaseSpeedScale(speedScale);
        SpeedSelection = ManualOrbitSpeedIntentSelection.LowerPerigee;
    }

    public void SelectPrograde()
    {
        BaseDirection = ManualOrbitBaseDirection.Prograde;
    }

    public void SelectRetrograde()
    {
        BaseDirection = ManualOrbitBaseDirection.Retrograde;
    }

    public void SelectSpeedIntent(ManualOrbitSpeedIntentSelection selection)
    {
        SpeedSelection = selection;
    }

    public void SetVelocityScale(float scale)
    {
        SetBaseSpeedScale(scale);
        SpeedSelection = ManualOrbitSpeedIntentSelection.None;
    }

    public void SetTrimScale(float trimScale)
    {
        SpeedTrimScale = Mathf.Max(0.01f, trimScale);
        RefreshSpeedScale();
    }

    public void StepRadial(float direction, float clickStep, float maxAmount)
    {
        float stepDirection = Mathf.Sign(direction);
        float next = RadialShapeAmount + stepDirection * Mathf.Max(0.01f, clickStep);
        float max = Mathf.Max(0f, maxAmount);
        RadialShapeAmount = Mathf.Clamp(next, -max, max);
    }

    public void ClearRadial()
    {
        RadialShapeAmount = 0f;
    }

    public void StepTilt(float direction, float clickDegrees, float maxDegrees)
    {
        float stepDirection = Mathf.Sign(direction);
        float next = TiltDegrees + stepDirection * Mathf.Max(0.1f, clickDegrees);
        float max = Mathf.Clamp(maxDegrees, 0f, 90f);
        TiltDegrees = Mathf.Clamp(next, -max, max);
    }

    public void ClearTilt()
    {
        TiltDegrees = 0f;
    }

    public void ClearShapeModifiers(float speedScale)
    {
        BaseDirection = ManualOrbitBaseDirection.Prograde;
        RadialShapeAmount = 0f;
        TiltDegrees = 0f;
        SetBaseSpeedScale(speedScale);
        SpeedSelection = ManualOrbitSpeedIntentSelection.Circularize;
    }

    public string BuildSummary()
    {
        string baseText = BaseDirection == ManualOrbitBaseDirection.Retrograde ? "retrograde" : "prograde";
        string radialText = FormatSignedShape("radial", RadialShapeAmount, "out", "in");
        string tiltText = FormatSignedDegrees("tilt", TiltDegrees, "-", "+");
        string trimText = Mathf.Abs(SpeedTrimScale - 1f) <= 0.001f
            ? "center trim"
            : $"{SpeedTrimScale:0.##}x trim";

        return $"{baseText}, {radialText}, {tiltText}, {SpeedScale:0.##}x speed ({trimText})";
    }

    private void SetBaseSpeedScale(float scale)
    {
        BaseSpeedScale = Mathf.Max(0.01f, scale);
        SpeedTrimScale = 1f;
        RefreshSpeedScale();
    }

    private void RefreshSpeedScale()
    {
        SpeedScale = Mathf.Max(0.01f, BaseSpeedScale * SpeedTrimScale);
    }

    private static string FormatSignedShape(string label, float amount, string positiveName, string negativeName)
    {
        if (Mathf.Abs(amount) <= 0.0001f)
            return $"no {label}";

        string direction = amount > 0f ? positiveName : negativeName;
        return $"{label} {direction} {Mathf.Abs(amount):P0}";
    }

    private static string FormatSignedDegrees(string label, float degrees, string positiveName, string negativeName)
    {
        if (Mathf.Abs(degrees) <= 0.0001f)
            return $"no {label}";

        string direction = degrees > 0f ? positiveName : negativeName;
        return $"{label} {direction} {Mathf.Abs(degrees):0.#} deg";
    }
}
