using System;
using TMPro;
using UnityEngine;

public sealed class ManualOrbitReadout
{
    private const string UnavailableText = "--";

    private readonly References refs;
    private (double mass, Vector3 center, Vector3 position, Vector3 velocity)? lastInputs;
    private OrbitalParameters orbit;

    public ManualOrbitReadout(References refs)
    {
        this.refs = refs ?? new References();
    }

    public void SetVisible(bool visible)
    {
        if (refs.Panel != null && refs.Panel.activeSelf != visible)
            refs.Panel.SetActive(visible);
    }

    public string Refresh(GameObject pendingBody, Vector3 currentVelocity, NBody centralBody, float kilometersPerUnit)
    {
        if (pendingBody == null || centralBody == null || !(centralBody.TotalMassKilograms > 0.0) ||
            currentVelocity.sqrMagnitude <= 1e-12f)
        {
            Clear();
            return null;
        }

        var inputs = (mass: centralBody.TotalMassKilograms, center: centralBody.transform.position,
            position: pendingBody.transform.position, velocity: currentVelocity);
        if (!lastInputs.HasValue || !lastInputs.Value.Equals(inputs))
        {
            orbit = OrbitalCalculations.CalculateOrbitalParameters(
                inputs.mass, inputs.center.ToDouble3(), inputs.position.ToDouble3(), inputs.velocity.ToDouble3());
            lastInputs = inputs;
        }

        if (!orbit.isValid)
        {
            Clear();
            return null;
        }

        float centralRadius = (float)centralBody.radius;
        float perigeeKm = (orbit.perigeeRadius - centralRadius) * kilometersPerUnit;
        float? apogeeKm = orbit.apogeeRadius >= 0f
            ? (orbit.apogeeRadius - centralRadius) * kilometersPerUnit
            : null;
        float? semiMajorKm = orbit.semiMajorAxis > 0f ? orbit.semiMajorAxis * kilometersPerUnit : null;
        float? periodSeconds = orbit.orbitalPeriod > 0f ? orbit.orbitalPeriod : null;
        float? timeToApogeeSeconds = orbit.timeToApogee > 0f ? orbit.timeToApogee : null;

        SetOrbitText(refs.PerigeeText, "Perigee", perigeeKm, "km", "F1");
        SetOrbitText(refs.ApogeeText, "Apogee", apogeeKm, "km", "F1", nullText: "Apogee: Escape");
        SetOrbitText(refs.InclinationText, "Inclination", orbit.inclination, "deg", "F1");
        SetOrbitText(refs.EccentricityText, "Ecc", orbit.eccentricity, string.Empty, "F4");
        SetOrbitText(refs.SemiMajorAxisText, "Semi-major", semiMajorKm, "km", "F1", nullText: "Semi-major: Escape");
        SetOrbitText(refs.OrbitalPeriodText, "Period", periodSeconds, "s", "F1", nullText: "Period: Escape");
        SetOrbitText(refs.RaanText, "RAAN", orbit.RAAN, "deg", "F1");
        SetOrbitText(refs.TrueAnomalyText, "True anomaly", orbit.trueAnomaly, "deg", "F1");
        SetOrbitText(refs.TimeToPerigeeText, "T to perigee", orbit.timeToPerigee, "s", "F1");
        SetOrbitText(refs.TimeToApogeeText, "T to apogee", timeToApogeeSeconds, "s", "F1", nullText: "T to apogee: Escape");
        return currentVelocity.sqrMagnitude > SimulationLimits.MinPlacementSpeedSquared
            ? BuildLaunchPreviewText(orbit, perigeeKm, apogeeKm)
            : null;
    }

    private static string BuildLaunchPreviewText(OrbitalParameters orbit, float perigeeKm, float? apogeeKm)
    {
        if (orbit.apogeeRadius < 0f)
        {
            return perigeeKm <= 0f
                ? "Launch preview: escape path intersects the planet."
                : $"Launch preview: escape trajectory, perigee {perigeeKm:F1} km.";
        }

        if (perigeeKm <= 0f)
            return $"Launch preview: impact likely, perigee {perigeeKm:F1} km.";

        if (orbit.eccentricity < 0.05f)
            return $"Launch preview: stable near-circular orbit, perigee {perigeeKm:F1} km.";

        return $"Launch preview: stable elliptical orbit, perigee {perigeeKm:F1} km, apogee {apogeeKm:F1} km.";
    }

    public void Clear()
    {
        lastInputs = null;
        orbit = default;
        SetTextDirect(refs.ApogeeText, $"Apogee: {UnavailableText}");
        SetTextDirect(refs.PerigeeText, $"Perigee: {UnavailableText}");
        SetTextDirect(refs.InclinationText, $"Inclination: {UnavailableText}");
        SetTextDirect(refs.EccentricityText, $"Ecc: {UnavailableText}");
        SetTextDirect(refs.SemiMajorAxisText, $"Semi-major: {UnavailableText}");
        SetTextDirect(refs.OrbitalPeriodText, $"Period: {UnavailableText}");
        SetTextDirect(refs.RaanText, $"RAAN: {UnavailableText}");
        SetTextDirect(refs.TrueAnomalyText, $"True anomaly: {UnavailableText}");
        SetTextDirect(refs.TimeToPerigeeText, $"T to perigee: {UnavailableText}");
        SetTextDirect(refs.TimeToApogeeText, $"T to apogee: {UnavailableText}");
    }

    private static void SetOrbitText(
        TextMeshProUGUI text,
        string label,
        float? value,
        string unit,
        string format,
        string nullText = null)
    {
        if (text == null)
            return;

        if (!value.HasValue || !float.IsFinite(value.Value))
        {
            text.text = nullText ?? $"{label}: {UnavailableText}";
            return;
        }

        string suffix = string.IsNullOrWhiteSpace(unit) ? string.Empty : $" {unit}";
        text.text = $"{label}: {value.Value.ToString(format)}{suffix}";
    }

    private static void SetTextDirect(TextMeshProUGUI text, string value)
    {
        if (text != null)
            text.text = value;
    }

    [Serializable]
    public sealed class References
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private TextMeshProUGUI apogeeText;
        [SerializeField] private TextMeshProUGUI perigeeText;
        [SerializeField] private TextMeshProUGUI inclinationText;
        [SerializeField] private TextMeshProUGUI eccentricityText;
        [SerializeField] private TextMeshProUGUI semiMajorAxisText;
        [SerializeField] private TextMeshProUGUI orbitalPeriodText;
        [SerializeField] private TextMeshProUGUI raanText;
        [SerializeField] private TextMeshProUGUI trueAnomalyText;
        [SerializeField] private TextMeshProUGUI timeToPerigeeText;
        [SerializeField] private TextMeshProUGUI timeToApogeeText;

        public GameObject Panel => panel;
        public TextMeshProUGUI ApogeeText => apogeeText;
        public TextMeshProUGUI PerigeeText => perigeeText;
        public TextMeshProUGUI InclinationText => inclinationText;
        public TextMeshProUGUI EccentricityText => eccentricityText;
        public TextMeshProUGUI SemiMajorAxisText => semiMajorAxisText;
        public TextMeshProUGUI OrbitalPeriodText => orbitalPeriodText;
        public TextMeshProUGUI RaanText => raanText;
        public TextMeshProUGUI TrueAnomalyText => trueAnomalyText;
        public TextMeshProUGUI TimeToPerigeeText => timeToPerigeeText;
        public TextMeshProUGUI TimeToApogeeText => timeToApogeeText;
    }
}
