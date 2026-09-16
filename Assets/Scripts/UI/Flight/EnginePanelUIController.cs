using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>
/// Presents the selected satellite's propulsion settings. NBody validates and owns
/// configuration; this helper owns panel state, input binding, and readouts only.
/// Created by FlightUIController; no component needs attaching to the Canvas.
/// </summary>
public sealed class EnginePanelUIController : IDisposable
{
    // Illustrative simulator presets, not specifications for particular real engines.
    private static readonly (string Name, float Thrust, float Isp)[] Presets =
    {
        ("Low thrust (1 N)", 1f, 3000f),
        ("Standard (10 kN)", 10000f, 300f),
        ("High thrust (50 kN)", 50000f, 300f)
    };
    private static readonly CultureInfo Numbers = CultureInfo.InvariantCulture;
    private readonly UIReferences refs;
    private readonly ICameraTracker tracker;
    private readonly ManeuverNodeManager maneuvers;
    private readonly List<Action> unbind = new();
    private NBody body;
    private bool open;
    private bool gameplayVisible = true;
    private bool refreshing;
    private float nextReadoutTime;
    private string validationMessage = "";

    public EnginePanelUIController(UIReferences refs, ICameraTracker tracker, ManeuverNodeManager maneuvers = null)
    {
        this.refs = refs;
        this.tracker = tracker;
        this.maneuvers = maneuvers;
    }

    public void Initialize()
    {
        if (refs.enginePanelButton != null)
            refs.enginePanelButton.onClick.AddListener(Toggle);
        if (refs.enginePresetDropdown != null)
        {
            var options = new List<string> { "Custom" };
            foreach (var preset in Presets) options.Add(preset.Name);
            refs.enginePresetDropdown.ClearOptions();
            refs.enginePresetDropdown.AddOptions(options);
            refs.enginePresetDropdown.onValueChanged.AddListener(SelectPreset);
        }
        BindInput(refs.engineThrustInputField, ApplyThrust);
        BindInput(refs.engineIspInputField, ApplyIsp);
        BindInput(refs.engineMassInputField, ApplyMass);
        BindInput(refs.engineFuelMassInputField, ApplyFuel);
        if (refs.engineUnlimitedFuelToggle != null)
            refs.engineUnlimitedFuelToggle.onValueChanged.AddListener(SetUnlimitedFuel);
        RefreshSelection();
    }

    public void Dispose()
    {
        if (refs.engineUnlimitedFuelToggle != null)
            refs.engineUnlimitedFuelToggle.onValueChanged.RemoveListener(SetUnlimitedFuel);
        if (body != null) body.FlightConfigurationChanged -= OnConfigurationChanged;
        if (refs.enginePanelButton != null) refs.enginePanelButton.onClick.RemoveListener(Toggle);
        if (refs.enginePresetDropdown != null) refs.enginePresetDropdown.onValueChanged.RemoveListener(SelectPreset);
        foreach (var remove in unbind) remove();
        unbind.Clear();
    }

    private NBody SelectedSatellite()
    {
        NBody selected = CameraVisibilityPolicy.SelectedBody(tracker);
        return selected != null && !selected.isCentralBody && !selected.isReferenceOrbit ? selected : null;
    }

    public void RefreshSelection()
    {
        NBody selected = SelectedSatellite();
        if (selected != body)
        {
            // Cancel unfinished edits/dropdown choices before binding a different ship.
            refreshing = true;
            if (refs.enginePresetDropdown != null) refs.enginePresetDropdown.Hide();
            refs.engineThrustInputField?.DeactivateInputField();
            refs.engineIspInputField?.DeactivateInputField();
            refs.engineMassInputField?.DeactivateInputField();
            refs.engineFuelMassInputField?.DeactivateInputField();
            if (body != null) body.FlightConfigurationChanged -= OnConfigurationChanged;
            body = selected;
            if (body != null) body.FlightConfigurationChanged += OnConfigurationChanged;
            validationMessage = "";
            RefreshFields(force: true);
            refreshing = false;
        }
        if (body == null) open = false;
        ApplyVisibility();
        RefreshReadouts();
    }

    public void SetGameplayVisible(bool visible)
    {
        gameplayVisible = visible;
        if (!visible) open = false;
        ApplyVisibility();
    }

    public void Tick()
    {
        if (SelectedSatellite() != body || (open && body == null)) RefreshSelection();
        if (!open || !gameplayVisible || Time.unscaledTime < nextReadoutTime) return;
        nextReadoutTime = Time.unscaledTime + 0.1f;
        RefreshReadouts();
    }

    private void Toggle()
    {
        RefreshSelection();
        if (body == null || !gameplayVisible || refs.enginePanel == null) return;
        open = !open;
        validationMessage = "";
        ApplyVisibility();
        RefreshFields(force: true);
        RefreshReadouts();
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void ApplyVisibility()
    {
        bool available = gameplayVisible && body != null;
        UIHelpers.SetActive(refs.enginePanelButton != null ? refs.enginePanelButton.gameObject : null, available);
        UIHelpers.SetActive(refs.enginePanel, available && open);
        if (refs.enginePanelButton != null)
        {
            refs.enginePanelButton.interactable = refs.enginePanel != null;
            TMP_Text label = refs.enginePanelButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = available && open && refs.enginePanel != null
                    ? "Close Engine Info"
                    : "Open Engine Info";
        }
    }

    private bool CanEdit() => !refreshing && open && gameplayVisible && body != null &&
        body == SelectedSatellite() && body.CanEditFlightConfiguration;

    private void BindInput(TMP_InputField field, Func<double, bool> apply)
    {
        if (field == null) return;
        NBody editingBody = null;
        UnityAction<string> select = _ => editingBody = body;
        UnityAction<string> finish = text =>
        {
            if (refreshing) return;
            if (editingBody != body || !CanEdit())
            {
                RefreshFields(force: true);
                return;
            }
            bool valid = double.TryParse(text, NumberStyles.Float, Numbers, out double value) &&
                double.IsFinite(value) && apply(value);
            validationMessage = valid ? "" : "Invalid value. Thrust and fuel must be nonnegative; Isp and dry mass must be positive. All values and total mass must fit the supported numeric range.";
            RefreshFields(force: true);
            RefreshReadouts();
        };
        field.onSelect.AddListener(select);
        field.onEndEdit.AddListener(finish);
        unbind.Add(() =>
        {
            if (field == null) return;
            field.onSelect.RemoveListener(select);
            field.onEndEdit.RemoveListener(finish);
        });
    }

    private bool ApplyThrust(double value) => value <= float.MaxValue &&
        body.TryConfigurePropulsion((float)value, body.SpecificImpulseSeconds, body.ThrustScale);
    private bool ApplyIsp(double value) => value <= float.MaxValue &&
        body.TryConfigurePropulsion(body.EngineThrustNewtons, (float)value, body.ThrustScale);
    private bool ApplyMass(double value) => value > 1e-6 && value <= float.MaxValue && body.TrySetMassKilograms(value);
    private bool ApplyFuel(double value) => body.TrySetFuelMassKilograms(value);

    private void SetUnlimitedFuel(bool unlimited)
    {
        if (CanEdit())
        {
            body.TrySetUnlimitedPropellant(unlimited);
            validationMessage = "";
        }
        RefreshFields(force: false);
        RefreshReadouts();
    }

    private void SelectPreset(int index)
    {
        if (!CanEdit() || index <= 0 || index > Presets.Length)
        {
            RefreshFields(force: true);
            return;
        }
        var preset = Presets[index - 1];
        body.TryConfigurePropulsion(preset.Thrust, preset.Isp, body.ThrustScale);
        validationMessage = "";
        RefreshFields(force: true);
        RefreshReadouts();
    }

    private void OnConfigurationChanged(NBody _) => RefreshFields(force: false);

    private void RefreshFields(bool force)
    {
        if (body == null) return;
        if (refs.engineUnlimitedFuelToggle != null)
            refs.engineUnlimitedFuelToggle.SetIsOnWithoutNotify(body.UnlimitedPropellant);
        SetInput(refs.engineThrustInputField, body.EngineThrustNewtons.ToString("G9", Numbers), force);
        SetInput(refs.engineIspInputField, body.SpecificImpulseSeconds.ToString("G9", Numbers), force);
        SetInput(refs.engineMassInputField, body.DryMassKilograms.ToString("G17", Numbers), force);
        SetInput(refs.engineFuelMassInputField, body.FuelMassKilograms.ToString("G17", Numbers), force);
        if (refs.enginePresetDropdown != null)
        {
            int match = 0;
            for (int i = 0; i < Presets.Length; i++)
                if (body.EngineThrustNewtons == Presets[i].Thrust && body.SpecificImpulseSeconds == Presets[i].Isp)
                    match = i + 1;
            refs.enginePresetDropdown.SetValueWithoutNotify(match);
        }
    }

    private static void SetInput(TMP_InputField field, string value, bool force)
    {
        if (field != null && (force || !field.isFocused)) field.SetTextWithoutNotify(value);
    }

    private void RefreshReadouts()
    {
        if (body == null) return;
        bool editable = body.CanEditFlightConfiguration;
        UIHelpers.SetInteractable(refs.enginePresetDropdown, editable);
        UIHelpers.SetInteractable(refs.engineThrustInputField, editable);
        UIHelpers.SetInteractable(refs.engineIspInputField, editable);
        UIHelpers.SetInteractable(refs.engineMassInputField, editable);
        UIHelpers.SetInteractable(refs.engineFuelMassInputField, editable);
        UIHelpers.SetInteractable(refs.engineUnlimitedFuelToggle, editable);
        // Physics consumption intentionally emits no configuration-edit event.
        SetInput(refs.engineFuelMassInputField, body.FuelMassKilograms.ToString("G9", Numbers), force: false);
        if (refs.engineTargetText != null) refs.engineTargetText.text = body.name;
        string available = body.UnlimitedPropellant ? "Unlimited" :
            body.AvailableDeltaVMetersPerSecond.ToString("F3", Numbers) + " m/s (ideal)";
        if (refs.engineReadoutText != null)
        {
            refs.engineReadoutText.text = string.Format(Numbers,
                "Dry mass: {0:G7} kg\nFuel remaining: {1:G7} kg\nTotal mass: {2:G7} kg\nEffective thrust: {3:G7} N ({4:G4}x)\nAcceleration at this mass: {5:G6} m/s\u00b2\nDelivered \u0394v: {6:F3} m/s\nFuel mode: {7}\nAvailable \u0394v: {8}",
                body.DryMassKilograms, body.FuelMassKilograms, body.TotalMassKilograms,
                body.EffectiveThrustNewtons, body.ThrustScale,
                body.FullThrustAccelerationMetersPerSecondSquared, body.DeliveredDeltaVMetersPerSecond,
                body.UnlimitedPropellant ? "Unlimited" : "Finite", available);
            ManeuverNode preview = maneuvers != null ? maneuvers.CurrentNode : null;
            if (preview != null && preview.targetBody == body)
                refs.engineReadoutText.text += string.Format(Numbers,
                    "\nPreview \u0394v: {0:F3} m/s\nPreview fuel use: {1:G7} kg",
                    preview.predictedPropulsiveDeltaVMetersPerSecond, preview.predictedFuelUsedKg);
        }
        if (refs.engineStatusText != null)
        {
            ManeuverNode node = maneuvers != null ? maneuvers.CurrentNode : null;
            string fuelWarning = !body.UnlimitedPropellant && body.FuelMassKilograms <= 0.0
                ? "Fuel empty: thrust unavailable. "
                : node != null && node.targetBody == body && node.insufficientPropellant
                    ? "Insufficient fuel for the full maneuver; preview includes early cutoff. " : "";
            refs.engineStatusText.text = fuelWarning + (!editable
                ? "Stop thrust and clear any finalized maneuver node to edit."
                : validationMessage.Length > 0 ? validationMessage
                : body.UnlimitedPropellant
                    ? "Unlimited: fuel contributes mass but is not consumed."
                    : "Finite: burns consume fuel and reduce total mass. Available \u0394v is an ideal propulsion budget.");
        }
    }
}
