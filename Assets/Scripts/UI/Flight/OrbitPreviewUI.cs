using UnityEngine;
using TMPro;

public class OrbitPreviewUI : MonoBehaviour
{
    [Header("Preview Orbit Readout (Optional)")]
    public TMP_Text previewPeriapsisLabel;
    public TMP_Text previewApoapsisLabel;
    public TMP_Text previewPeriodLabel;
    public TMP_Text previewInclinationLabel;
    public TMP_Text previewEccentricityLabel;
    public TMP_Text previewTPlusLabel;

    public void Show(OrbitalParameters p, NBody central)
    {
        Show(p, central, float.NaN);
    }

    public void Show(OrbitalParameters p, NBody central, float timeToNodeSeconds)
    {
        if (!p.isValid || central == null)
        {
            ShowInvalid();
            return;
        }

        Vector3 center = central.transform.position;

        float rp = (p.perigeePosition - center).magnitude;
        float ra = (p.apogeePosition - center).magnitude;

        float periAlt = (rp - central.radius) * 10f;
        float apoAlt = (ra - central.radius) * 10f;

        if (!float.IsFinite(periAlt)) periAlt = 0f;
        if (!float.IsFinite(apoAlt)) apoAlt = 0f;

        if (previewPeriapsisLabel != null)
            previewPeriapsisLabel.text = $"Periapsis: {periAlt:0} km";

        if (previewApoapsisLabel != null)
        {
            previewApoapsisLabel.text = p.orbitalPeriod > 0f
                ? $"Apoapsis: {apoAlt:0} km"
                : "Apoapsis: --";
        }

        if (previewPeriodLabel != null)
        {
            previewPeriodLabel.text = p.orbitalPeriod > 0f
                ? $"Period: {p.orbitalPeriod:0}s"
                : "Period: escape";
        }

        if (previewInclinationLabel != null)
            previewInclinationLabel.text = $"Incl: {p.inclination:0.0} deg";

        if (previewEccentricityLabel != null)
            previewEccentricityLabel.text = $"e: {p.eccentricity:0.000}";

        UpdateTPlus(timeToNodeSeconds);
    }

    public void UpdateTPlus(float timeToNodeSeconds)
    {
        if (previewTPlusLabel != null)
            previewTPlusLabel.text = FormatTPlus(timeToNodeSeconds);
    }

    public void ShowInvalid()
    {
        if (previewPeriapsisLabel != null)
            previewPeriapsisLabel.text = "Periapsis: --";
        if (previewApoapsisLabel != null)
            previewApoapsisLabel.text = "Apoapsis: --";
        if (previewPeriodLabel != null)
            previewPeriodLabel.text = "Period: --";
        if (previewInclinationLabel != null)
            previewInclinationLabel.text = "Incl: --";
        if (previewEccentricityLabel != null)
            previewEccentricityLabel.text = "e: --";
        if (previewTPlusLabel != null)
            previewTPlusLabel.text = "T+: --";
    }

    private static string FormatTPlus(float timeToNodeSeconds)
    {
        if (!float.IsFinite(timeToNodeSeconds))
            return "T+: --";

        string prefix = timeToNodeSeconds >= 0f ? "T+" : "T-";
        var (value, unit) = TimeFormatUtils.GetBestTimeUnit(Mathf.Abs(timeToNodeSeconds));
        string format = unit == "sec" ? "0.0" : "0.00";
        return $"{prefix}: {value.ToString(format)} {unit}";
    }
}
