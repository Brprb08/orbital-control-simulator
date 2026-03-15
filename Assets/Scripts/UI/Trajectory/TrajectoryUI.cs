using System;
using TMPro;

public class TrajectoryUI
{
    private readonly UIReferences refs;

    public event Action ClearPreManeuverClicked;

    public TrajectoryUI(UIReferences refs)
    {
        this.refs = refs;
    }

    public void Initialize()
    {
        if (refs.removePreManeuverLineButton != null)
        {
            refs.removePreManeuverLineButton.onClick.RemoveListener(OnClearPreManeuverPressed);
            refs.removePreManeuverLineButton.onClick.AddListener(OnClearPreManeuverPressed);
            refs.removePreManeuverLineButton.gameObject.SetActive(false);
        }

        SetApogeePerigeePanelVisible(false);
        UpdateDeltaV(0f);
    }

    public void Dispose()
    {
        if (refs.removePreManeuverLineButton != null)
            refs.removePreManeuverLineButton.onClick.RemoveListener(OnClearPreManeuverPressed);
    }

    public void SetApogeePerigeePanelVisible(bool visible)
    {
        if (refs.apogeePerigeePanel != null)
            refs.apogeePerigeePanel.SetActive(visible);
    }

    public void SetRemovePreManeuverButtonVisible(bool visible)
    {
        if (refs.removePreManeuverLineButton != null)
            refs.removePreManeuverLineButton.gameObject.SetActive(visible);
    }

    public void UpdateDeltaV(float deltaV)
    {
        if (refs.deltaVText == null) return;

        if (deltaV != 0f)
            SetText(refs.deltaVText, "DeltaV", deltaV * 1000f, "m/s", "F3");
        else
            refs.deltaVText.text = "";
    }

    public void UpdateOrbitUI(
        float apogee,
        float perigee,
        float semiMajorAxis,
        float eccentricity,
        float orbitalPeriod,
        float inclination,
        float raan,
        float meanAnomaly,
        float timeToPerigee,
        float timeToApogee)
    {
        SetText(refs.apogeeText, "Apogee", apogee);
        SetText(refs.perigeeText, "Perigee", perigee);
        SetText(refs.semiMajorAxisText, "Semi-Major Axis", semiMajorAxis * 10f);
        SetText(refs.eccentricityText, "Eccentricity", eccentricity, "", "F3");
        SetText(refs.orbitalPeriodText, "Orbital Period", orbitalPeriod, "s");
        SetText(refs.inclinationText, "Inclination", inclination, "°", "F1");
        SetText(refs.raanText, "RAAN", raan, "°", "F1");
        SetText(refs.meanAnomalyText, "Mean Anomaly", meanAnomaly, "rad", "F2");

        var (valPeri, unitPeri) = TimeFormatUtils.GetBestTimeUnit(timeToPerigee);
        var (valApo, unitApo) = TimeFormatUtils.GetBestTimeUnit(timeToApogee);

        SetText(refs.timeToPerigeeText, "Time to Perigee", valPeri, unitPeri, "F2");
        SetText(refs.timeToApogeeText, "Time to Apogee", valApo, unitApo, "F2");
    }

    private void OnClearPreManeuverPressed()
    {
        ClearPreManeuverClicked?.Invoke();
    }

    private void SetText(TextMeshProUGUI textElement, string label, float value, string unit = "km", string format = "F0")
    {
        if (textElement != null)
            textElement.text = value >= 0 ? $"{label}: {value.ToString(format)} {unit}".Trim() : string.Empty;
    }
}