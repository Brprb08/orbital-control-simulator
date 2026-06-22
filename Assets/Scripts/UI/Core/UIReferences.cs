using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Inspector wiring bucket for shared UI objects. Controllers read from this
/// single place so gameplay classes do not each serialize duplicate UI fields.
/// </summary>
public class UIReferences : MonoBehaviour
{
    [Header("Buttons")]
    public Button freeCamButton;
    public Button trackCamButton;
    public Button instructionsButton;
    public Button placeObjectButton;
    public Button placeKeplerObjectButton;
    public Button placeTleObjectButton;
    public Button placementModeButton;
    public Button randomSatelliteButton;
    public Button burnControlButton;
    public Button removePreManeuverLineButton;
    public Button vectorToggleButton;
    public Button earthView;
    public Button removeSatellite;
    public Button skipButton;
    public Button pauseButton;

    [Header("Panels")]
    public GameObject objectPlacementPanel;
    public GameObject objectInfoPanel;
    public GameObject thrustButtons;
    public GameObject maneuverNodePanel;
    public GameObject burnControlsPanel;
    public GameObject apogeePerigeePanel;
    public GameObject timeControlsPanel;
    public GameObject instructionsPanel;
    public GameObject toggleOptionsPanel;
    public GameObject dropdown;
    public GameObject placeTLEPanel;
    public GameObject placementSelectPanel;
    public GameObject randomPlacementPanel;
    public GameObject cameraControls;
    public GameObject placeKeplerPanel;
    public GameObject confirmRemoveSatPanel;
    public GameObject attitudeControlPanel;
    public GameObject tutorialPanel;

    [Header("Input Fields")]
    public TMP_InputField nameInputField;
    public TMP_InputField positionInputField;
    public TMP_InputField massInputField;
    public TMP_InputField radiusInputField;
    public TMP_InputField velocityInputField;
    public TMP_InputField kepNameInputField;
    public TMP_InputField kepMassInputField;
    public TMP_InputField kepADegOrMetersInputField;
    public TMP_InputField kepEccInputField;
    public TMP_InputField kepIncDegInputField;
    public TMP_InputField kepRAANDegInputField;
    public TMP_InputField kepArgPDegInputField;
    public TMP_InputField kepTrueAnomDegInputField;
    public TMP_InputField tleNameInputField;
    public TMP_InputField tleMassInputField;
    public TMP_InputField tleLine1InputField;
    public TMP_InputField tleLine2InputField;

    [Header("Dropdowns")]
    public TMP_Dropdown trackedSatellites;

    [Header("Text")]
    public TMP_Text earthCamButtonText;
    public TextMeshProUGUI instructionText;
    public TextMeshProUGUI feedbackText;
    public TextMeshProUGUI apogeeText;
    public TextMeshProUGUI perigeeText;
    public TextMeshProUGUI semiMajorAxisText;
    public TextMeshProUGUI eccentricityText;
    public TextMeshProUGUI orbitalPeriodText;
    public TextMeshProUGUI inclinationText;
    public TextMeshProUGUI raanText;
    public TextMeshProUGUI meanAnomalyText;
    public TextMeshProUGUI deltaVText;
    public TextMeshProUGUI timeToPerigeeText;
    public TextMeshProUGUI timeToApogeeText;
    public TextMeshProUGUI vectorToggleButtonText;
    public TextMeshProUGUI timeScaleText;
    public TextMeshProUGUI pauseButtonText;

    [Header("Sliders")]
    public Slider timeSlider;
}
