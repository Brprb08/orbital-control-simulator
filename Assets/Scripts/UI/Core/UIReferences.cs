using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIReferences : MonoBehaviour
{
    [Header("Buttons")]
    public Button freeCamButton;
    public Button trackCamButton;
    public Button instructionsButton;
    public Button placeObjectButton;
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