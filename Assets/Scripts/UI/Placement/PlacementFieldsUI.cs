using TMPro;
using UnityEngine;

/// <summary>
/// Owns placement form fields: clearing input text, locking field interactivity,
/// feedback text, and tutorial field-entry hooks. It deliberately does not decide
/// which placement mode/panel is visible and does not spawn satellites.
/// </summary>
public class PlacementFieldsUI
{
    private readonly UIReferences refs;
    private readonly UIRoot uiRoot;
    private readonly TutorialController tutorialController;
    private readonly Camera mainCamera;

    public TMP_InputField ObjectNameInputField => refs.nameInputField;
    public TMP_InputField MassInput => refs.massInputField;
    public TMP_InputField FuelMassInput => refs.fuelMassInputField;
    public TMP_InputField RadiusInput => refs.radiusInputField;
    public TMP_InputField PositionInput => refs.positionInputField;

    public TMP_InputField KepNameInputField => refs.kepNameInputField;
    public TMP_InputField KepMassInputField => refs.kepMassInputField;
    public TMP_InputField KepFuelMassInputField => refs.kepFuelMassInputField;
    public TMP_InputField KepADegOrMetersInputField => refs.kepADegOrMetersInputField;
    public TMP_InputField KepEccInputField => refs.kepEccInputField;
    public TMP_InputField KepIncDegInputField => refs.kepIncDegInputField;
    public TMP_InputField KepRAANDegInputField => refs.kepRAANDegInputField;
    public TMP_InputField KepArgPDegInputField => refs.kepArgPDegInputField;
    public TMP_InputField KepTrueAnomDegInputField => refs.kepTrueAnomDegInputField;

    public TMP_InputField TleNameInputField => refs.tleNameInputField;
    public TMP_InputField TleMassInputField => refs.tleMassInputField;
    public TMP_InputField TleFuelMassInputField => refs.tleFuelMassInputField;
    public TMP_InputField TleLine1InputField => refs.tleLine1InputField;
    public TMP_InputField TleLine2InputField => refs.tleLine2InputField;

    public TMP_InputField ConstellationNamePrefixInputField => refs.constellationNamePrefixInputField;
    public TMP_InputField ConstellationMassInputField => refs.constellationMassInputField;
    public TMP_InputField ConstellationFuelMassInputField => refs.constellationFuelMassInputField;
    public TMP_InputField ConstellationSemiMajorAxisInputField => refs.constellationSemiMajorAxisInputField;
    public TMP_InputField ConstellationEccInputField => refs.constellationEccInputField;
    public TMP_InputField ConstellationIncDegInputField => refs.constellationIncDegInputField;
    public TMP_InputField ConstellationArgPDegInputField => refs.constellationArgPDegInputField;
    public TMP_InputField ConstellationRAANStartDegInputField => refs.constellationRAANStartDegInputField;
    public TMP_InputField ConstellationRAANSpreadDegInputField => refs.constellationRAANSpreadDegInputField;
    public TMP_InputField ConstellationPlanesInputField => refs.constellationPlanesInputField;
    public TMP_InputField ConstellationSatellitesPerPlaneInputField => refs.constellationSatellitesPerPlaneInputField;
    public TMP_InputField ConstellationWalkerPhaseInputField => refs.constellationWalkerPhaseInputField;
    public TMP_InputField ConstellationTrueAnomalyOffsetDegInputField => refs.constellationTrueAnomalyOffsetDegInputField;

    public PlacementFieldsUI(UIReferences refs, UIRoot uiRoot, TutorialController tutorialController, Camera mainCamera)
    {
        this.refs = refs;
        this.uiRoot = uiRoot;
        this.tutorialController = tutorialController;
        this.mainCamera = mainCamera;
    }

    public void BindTutorialHooks()
    {
        if (tutorialController == null || !tutorialController.inTutorialMode)
            return;

        if (MassInput != null)
            MassInput.onValueChanged.AddListener(OnMassInputChanged);

        if (RadiusInput != null)
            RadiusInput.onValueChanged.AddListener(OnRadiusInputChanged);
    }

    public void UnbindTutorialHooks()
    {
        if (MassInput != null)
            MassInput.onValueChanged.RemoveListener(OnMassInputChanged);

        if (RadiusInput != null)
            RadiusInput.onValueChanged.RemoveListener(OnRadiusInputChanged);
    }

    public void LockManualInputs(bool locked)
    {
        UIHelpers.SetInteractable(
            !locked,
            ObjectNameInputField,
            PositionInput,
            MassInput,
            FuelMassInput,
            RadiusInput,
            refs.placeObjectButton
        );

        uiRoot?.SetPlacementButtonsLocked(locked);
        SetTrackCamButtonInteractable(false);
    }

    public void SetTrackCamButtonInteractable(bool state)
    {
        uiRoot?.SetTrackCamButtonInteractable(state);
    }

    public void SetFeedback(string msg)
    {
        UIHelpers.SetText(refs.feedbackText, msg);
    }

    public void ClearAllFields()
    {
        UIHelpers.ClearInputs(
            true,
            RadiusInput,
            PositionInput,
            ObjectNameInputField,
            MassInput,
            FuelMassInput,
            TleNameInputField,
            TleMassInputField,
            TleFuelMassInputField,
            TleLine1InputField,
            TleLine2InputField,
            KepNameInputField,
            KepMassInputField,
            KepFuelMassInputField,
            KepADegOrMetersInputField,
            KepEccInputField,
            KepIncDegInputField,
            KepRAANDegInputField,
            KepArgPDegInputField,
            KepTrueAnomDegInputField,
            ConstellationNamePrefixInputField,
            ConstellationMassInputField,
            ConstellationFuelMassInputField,
            ConstellationSemiMajorAxisInputField,
            ConstellationEccInputField,
            ConstellationIncDegInputField,
            ConstellationArgPDegInputField,
            ConstellationRAANStartDegInputField,
            ConstellationRAANSpreadDegInputField,
            ConstellationPlanesInputField,
            ConstellationSatellitesPerPlaneInputField,
            ConstellationWalkerPhaseInputField,
            ConstellationTrueAnomalyOffsetDegInputField
        );
    }

    private void OnMassInputChanged(string input)
    {
        if (mainCamera == null) return;

        if (string.IsNullOrWhiteSpace(input))
        {
            SetFeedback(string.Empty);
            return;
        }

        if (ParsingUtils.TryParseMass(input, out _))
        {
            if (tutorialController != null)
                tutorialController.hasMassBeenEnteredForSatellite = true;

            SetFeedback(string.Empty);
        }
        else
        {
            SetFeedback($"Invalid Mass: Should be between {SimulationLimits.MinSatelliteMassKg:N0}-{SimulationLimits.MaxSatelliteMassKg:N0}. Units are in kg by default.");
        }
    }

    private void OnRadiusInputChanged(string input)
    {
        if (mainCamera == null) return;

        if (string.IsNullOrWhiteSpace(input))
        {
            SetFeedback(string.Empty);
            return;
        }

        if (PlacementValidators.TryGetRadius(input, PlacementSpawnBuilder.RadiusClamp, out _, out string error))
        {
            if (tutorialController != null)
                tutorialController.hasRadiusBeenEnteredForSatellite = true;

            SetFeedback(string.Empty);
        }
        else
        {
            SetFeedback(error);
        }
    }
}
