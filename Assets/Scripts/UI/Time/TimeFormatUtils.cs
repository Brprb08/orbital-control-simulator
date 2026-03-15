using UnityEngine;

public static class TimeFormatUtils
{
    /// <summary>
    /// Chooses the "best" unit (seconds, minutes, hours) for a duration,
    /// and returns the converted value plus a unit label.
    /// </summary>
    public static (float value, string unit) GetBestTimeUnit(float seconds)
    {
        if (!float.IsFinite(seconds))
            return (0f, "seconds");

        float sign = Mathf.Sign(seconds);
        float abs = Mathf.Abs(seconds);

        if (abs < 60f)
            return (seconds, "sec");
        if (abs < 3600f)
            return (sign * (abs / 60f), "min");

        return (sign * (abs / 3600f), "hr");
    }

}
