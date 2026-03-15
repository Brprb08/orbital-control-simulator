using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PlacementFieldsUI
{
    private readonly UIRoot uiRoot;
    private readonly TutorialController tutorialController;
    private readonly Camera mainCamera;

    // Manual
    private readonly TMP_InputField objectNameInputField;
    private readonly TMP_InputField massInput;
    private readonly TMP_InputField radiusInput;
    private readonly TMP_InputField positionInput;
    private readonly Button placeObjectButton;
    private readonly TextMeshProUGUI feedbackText;

    // Kepler
    private readonly TMP_InputField kepNameInputField;
    private readonly TMP_InputField kepMassInputField;
    private readonly TMP_InputField kepADegOrMetersInputField;
    private readonly TMP_InputField kepEccInputField;
    private readonly TMP_InputField kepIncDegInputField;
    private readonly TMP_InputField kepRAANDegInputField;
    private readonly TMP_InputField kepArgPDegInputField;
    private readonly TMP_InputField kepTrueAnomDegInputField;
    private readonly Button placeKeplerObjectButton;

    // TLE
    private readonly TMP_InputField tleNameInputField;
    private readonly TMP_InputField tleMassInputField;
    private readonly TMP_InputField tleLine1InputField;
    private readonly TMP_InputField tleLine2InputField;
    private readonly Button placeTleObjectButton;

    public TMP_InputField ObjectNameInputField => objectNameInputField;
    public TMP_InputField MassInput => massInput;
    public TMP_InputField RadiusInput => radiusInput;
    public TMP_InputField PositionInput => positionInput;

    public TMP_InputField KepNameInputField => kepNameInputField;
    public TMP_InputField KepMassInputField => kepMassInputField;
    public TMP_InputField KepADegOrMetersInputField => kepADegOrMetersInputField;
    public TMP_InputField KepEccInputField => kepEccInputField;
    public TMP_InputField KepIncDegInputField => kepIncDegInputField;
    public TMP_InputField KepRAANDegInputField => kepRAANDegInputField;
    public TMP_InputField KepArgPDegInputField => kepArgPDegInputField;
    public TMP_InputField KepTrueAnomDegInputField => kepTrueAnomDegInputField;

    public TMP_InputField TleNameInputField => tleNameInputField;
    public TMP_InputField TleMassInputField => tleMassInputField;
    public TMP_InputField TleLine1InputField => tleLine1InputField;
    public TMP_InputField TleLine2InputField => tleLine2InputField;

    public PlacementFieldsUI(
        UIRoot uiRoot,
        TutorialController tutorialController,
        Camera mainCamera,
        TMP_InputField objectNameInputField,
        TMP_InputField massInput,
        TMP_InputField radiusInput,
        TMP_InputField positionInput,
        Button placeObjectButton,
        TextMeshProUGUI feedbackText,
        TMP_InputField kepNameInputField,
        TMP_InputField kepMassInputField,
        TMP_InputField kepADegOrMetersInputField,
        TMP_InputField kepEccInputField,
        TMP_InputField kepIncDegInputField,
        TMP_InputField kepRAANDegInputField,
        TMP_InputField kepArgPDegInputField,
        TMP_InputField kepTrueAnomDegInputField,
        Button placeKeplerObjectButton,
        TMP_InputField tleNameInputField,
        TMP_InputField tleMassInputField,
        TMP_InputField tleLine1InputField,
        TMP_InputField tleLine2InputField,
        Button placeTleObjectButton)
    {
        this.uiRoot = uiRoot;
        this.tutorialController = tutorialController;
        this.mainCamera = mainCamera;

        this.objectNameInputField = objectNameInputField;
        this.massInput = massInput;
        this.radiusInput = radiusInput;
        this.positionInput = positionInput;
        this.placeObjectButton = placeObjectButton;
        this.feedbackText = feedbackText;

        this.kepNameInputField = kepNameInputField;
        this.kepMassInputField = kepMassInputField;
        this.kepADegOrMetersInputField = kepADegOrMetersInputField;
        this.kepEccInputField = kepEccInputField;
        this.kepIncDegInputField = kepIncDegInputField;
        this.kepRAANDegInputField = kepRAANDegInputField;
        this.kepArgPDegInputField = kepArgPDegInputField;
        this.kepTrueAnomDegInputField = kepTrueAnomDegInputField;
        this.placeKeplerObjectButton = placeKeplerObjectButton;

        this.tleNameInputField = tleNameInputField;
        this.tleMassInputField = tleMassInputField;
        this.tleLine1InputField = tleLine1InputField;
        this.tleLine2InputField = tleLine2InputField;
        this.placeTleObjectButton = placeTleObjectButton;
    }

    public void BindTutorialHooks()
    {
        if (tutorialController == null || !tutorialController.inTutorialMode)
            return;

        if (massInput != null)
            massInput.onValueChanged.AddListener(OnMassInputChanged);

        if (radiusInput != null)
            radiusInput.onValueChanged.AddListener(OnRadiusInputChanged);
    }

    public void UnbindTutorialHooks()
    {
        if (massInput != null)
            massInput.onValueChanged.RemoveListener(OnMassInputChanged);

        if (radiusInput != null)
            radiusInput.onValueChanged.RemoveListener(OnRadiusInputChanged);
    }

    public void LockManualInputs(bool locked)
    {
        if (objectNameInputField != null) objectNameInputField.interactable = !locked;
        if (positionInput != null) positionInput.interactable = !locked;
        if (massInput != null) massInput.interactable = !locked;
        if (radiusInput != null) radiusInput.interactable = !locked;
        if (placeObjectButton != null) placeObjectButton.interactable = !locked;

        uiRoot?.SetPlacementButtonsLocked(locked);
        SetTrackCamButtonInteractable(false);
    }

    public void SetTrackCamButtonInteractable(bool state)
    {
        uiRoot?.SetTrackCamButtonInteractable(state);
    }

    public void SetFeedback(string msg)
    {
        if (feedbackText != null)
            feedbackText.text = msg ?? string.Empty;
    }

    public void ClearAllFields()
    {
        ClearAndUnfocusInputField(radiusInput);
        ClearAndUnfocusInputField(positionInput);
        ClearAndUnfocusInputField(objectNameInputField);
        ClearAndUnfocusInputField(massInput);

        ClearAndUnfocusInputField(tleNameInputField);
        ClearAndUnfocusInputField(tleMassInputField);
        ClearAndUnfocusInputField(tleLine1InputField);
        ClearAndUnfocusInputField(tleLine2InputField);

        ClearAndUnfocusInputField(kepNameInputField);
        ClearAndUnfocusInputField(kepMassInputField);
        ClearAndUnfocusInputField(kepADegOrMetersInputField);
        ClearAndUnfocusInputField(kepEccInputField);
        ClearAndUnfocusInputField(kepIncDegInputField);
        ClearAndUnfocusInputField(kepRAANDegInputField);
        ClearAndUnfocusInputField(kepArgPDegInputField);
        ClearAndUnfocusInputField(kepTrueAnomDegInputField);
    }

    private void ClearAndUnfocusInputField(TMP_InputField inputField)
    {
        if (inputField == null) return;

        inputField.text = string.Empty;

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
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
            SetFeedback("Invalid Mass: Should be between 500-1,000,000. Units are in kg by default.");
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

        if (ParsingUtils.TryParseVector3(input, out _))
        {
            if (tutorialController != null)
                tutorialController.hasRadiusBeenEnteredForSatellite = true;

            SetFeedback(string.Empty);
        }
        else
        {
            SetFeedback("Invalid Radius: Format is x,y,z. Example 1,2,1");
        }
    }
}