using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ObjectPlacementManager : MonoBehaviour
{
    [Header("References - Core")]
    public Camera mainCamera;
    public GameObject spherePrefab; // placeholder without NBody
    public TrajectoryRenderer trajectoryRenderer;
    public CameraController cameraController;
    private ICameraTracker cameraTracker;
    public CameraMovement cameraMovement;
    public VelocityDragManager velocityDragManager;
    public TutorialController tutorialController;
    private GravityManager gravityManager;
    private UIManager uIManager;
    private NBody lastManualNBody;

    [Header("References - UI")]
    public TMP_InputField objectNameInputField;
    public TMP_InputField nameInputField;
    public TMP_InputField massInput;
    public TMP_InputField massInputField;
    public TMP_InputField radiusInput;
    // public TMP_InputField radiusInputField;
    public TMP_InputField positionInput;
    public TextMeshProUGUI feedbackText;
    public Button placeObjectButton;

    [Header("TLE Placement")]
    public TMP_InputField tleNameInputField;
    public TMP_InputField tleMassInputField;
    public TMP_InputField tleLine1InputField;
    public TMP_InputField tleLine2InputField;
    public Button placeTLEObjectButton;

    [Header("Ghost Preview")]
    public GameObject ghostPreviewPrefab;
    private GameObject ghostInstance;
    private bool ghostObjectPlaced = false;
    private bool clearingPosition = false;

    [Header("Placement State")]
    public GameObject lastPlacedGameObject; // last placeholder
    private int satelliteCount = 0;

    private const int MaxSatelliteNameLength = 15;

    private SimContext ctx;

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        trajectoryRenderer = ctx.TrajectoryRenderer;
        cameraController = ctx.CameraController;
        cameraMovement = ctx.CameraMovement;
        tutorialController = ctx.TutorialController;
        cameraTracker = ctx.CameraTracker; // make sure this is set
        uIManager = ctx.UIManager;
        gravityManager = ctx.GravityManager;
        if (tutorialController.inTutorialMode)
        {
            massInput.onValueChanged.AddListener(OnMassInputChanged);
            radiusInput.onValueChanged.AddListener(OnRadiusInputChanged);
        }
        positionInput.onValueChanged.AddListener(OnPositionInputChanged);

        if (ghostPreviewPrefab != null)
        {
            ghostInstance = Instantiate(ghostPreviewPrefab);
            ghostInstance.SetActive(false);
        }
    }

    public void StartPlacement()
    {
        if (lastPlacedGameObject != null)
        {
            feedbackText.text = "You must set the velocity of the current planet before placing another.";
            return;
        }

        if (!cameraTracker.IsFree)
        {
            feedbackText.text = "You must be in FreeCam mode to place planets.";
            return;
        }

        string customName = objectNameInputField?.text;
        if (!string.IsNullOrWhiteSpace(customName) && customName.Length > MaxSatelliteNameLength)
        {
            feedbackText.text = $"Satellite name too long. Max {MaxSatelliteNameLength} characters.";
            return;
        }
        if (string.IsNullOrWhiteSpace(customName)) customName = $"Satellite {satelliteCount}";

        Vector3 parsedPosition;
        if (string.IsNullOrWhiteSpace(positionInput.text))
        {
            parsedPosition = mainCamera.transform.position + mainCamera.transform.forward * 10f;
        }
        else
        {
            if (!ParsingUtils.TryParseVector3(positionInput.text, out parsedPosition))
            {
                feedbackText.text = "Invalid position input. Please use numeric x,y,z format.";
                return;
            }

            float distanceFromEarth = Vector3.Distance(Vector3.zero, parsedPosition);
            float minDistance = 638f;
            float maxDistance = 5000f;
            if (distanceFromEarth < minDistance || distanceFromEarth > maxDistance)
            {
                feedbackText.text = $"Invalid position: must be between {minDistance * 10f:N0} km and {maxDistance * 10f:N0} km from Earth's center.";
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(radiusInput.text))
        {
            feedbackText.text = "Please enter a radius in the format x,y,z. Numbers only.";
            return;
        }
        if (!ParsingUtils.TryParseVector3(radiusInput.text, out Vector3 parsedRadius))
        {
            feedbackText.text = "Invalid radius. Use numeric x,y,z.";
            return;
        }

        if (string.IsNullOrWhiteSpace(massInput.text))
        {
            feedbackText.text = "Please enter a numeric mass between 500 and 1,000,000 kg.";
            return;
        }
        if (!ParsingUtils.TryParseMass(massInput.text, out float mass))
        {
            feedbackText.text = "Invalid mass. Enter a number between 500 and 1,000,000.";
            return;
        }

        float placeholderMass = mass;
        parsedRadius = new Vector3(
            Mathf.Clamp(parsedRadius.x, .5f, 1f),
            Mathf.Clamp(parsedRadius.y, .5f, 1f),
            Mathf.Clamp(parsedRadius.z, .5f, 1f)
        );

        lastPlacedGameObject = Instantiate(spherePrefab);
        lastPlacedGameObject.transform.localScale = new Vector3(parsedRadius.x, parsedRadius.y, parsedRadius.z);
        lastPlacedGameObject.transform.position = parsedPosition;

        if (ghostInstance != null) ghostInstance.SetActive(false);

        satelliteCount++;
        lastPlacedGameObject.name = customName;
        lastPlacedGameObject.tag = "Planet";

        if (velocityDragManager != null)
        {
            velocityDragManager.ResetDragManager();
            velocityDragManager.planet = lastPlacedGameObject;
            velocityDragManager.placeholderMass = placeholderMass;
        }

        TrackSilently(lastPlacedGameObject.transform);
        uIManager.trackCamButton.interactable = false;
        uIManager.placementModeButton.interactable = false;

        ClearAndUnfocusInputField(radiusInput);
        ClearAndUnfocusInputField(positionInput);
        ClearAndUnfocusInputField(objectNameInputField);
        ClearAndUnfocusInputField(massInput);

        if (nameInputField != null && massInputField != null && radiusInput != null)
        {
            nameInputField.interactable = false;
            positionInput.interactable = false;
            massInputField.interactable = false;
            radiusInput.interactable = false;
            placeObjectButton.interactable = false;
        }

        tutorialController.hasSatelliteBeenPlaced = true;

        feedbackText.text =
            "Setting Satellite Velocity:\n\n" +
            "• Click the satellite and drag.\n" +
            "• Set the desired direction.\n" +
            "• Use input field to adjust speed.";
        EventSystem.current.SetSelectedGameObject(null);
    }

    public void PlaceObjectFromTLE()
    {
        if (!TLEParser.TryParseTLE(tleLine1InputField.text, tleLine2InputField.text, out Vector3 position, out Vector3 velocity))
        {
            feedbackText.text = "Invalid TLE input. Check formatting.";
            return;
        }

        string name = !string.IsNullOrWhiteSpace(tleNameInputField.text) ? tleNameInputField.text : $"TLE Satellite {satelliteCount + 1}";
        if (!ParsingUtils.TryParseMass(tleMassInputField.text, out float mass))
        {
            feedbackText.text = "Invalid mass. Enter a number between 500 and 1,000,000.";
            return;
        }

        // Clear any lines that might have been left from manual placement mode
        velocityDragManager.trajectoryRenderer.preManeuverLine.Clear();

        satelliteCount++;
        lastPlacedGameObject = Instantiate(spherePrefab);
        lastPlacedGameObject.name = name;
        lastPlacedGameObject.tag = "Satellite";
        lastPlacedGameObject.transform.position = position;
        lastPlacedGameObject.transform.localScale = Vector3.one;

        if (velocityDragManager != null)
        {
            velocityDragManager.planet = lastPlacedGameObject;
            velocityDragManager.placeholderMass = mass;
        }

        cameraTracker.RefreshBodiesList();

        if (velocityDragManager != null)
        {
            velocityDragManager.planet = lastPlacedGameObject;
            velocityDragManager.placeholderMass = mass;
            velocityDragManager.ApplyVelocityToPlanet(velocity);
        }

        ClearAndUnfocusInputField(tleNameInputField);
        ClearAndUnfocusInputField(tleMassInputField);
        ClearAndUnfocusInputField(tleLine1InputField);
        ClearAndUnfocusInputField(tleLine2InputField);
    }

    public void ClearFields()
    {
        if (ghostInstance != null)
        {
            if (ghostInstance != null) ghostInstance.SetActive(false);
            nameInputField.text = "";
            positionInput.text = "";
            massInput.text = "";
            radiusInput.text = "";
            cameraTracker.BreakToFreeCam();
        }
    }

    public void CancelPlacement()
    {
        if (lastPlacedGameObject != null)
        {
            Destroy(lastPlacedGameObject);
            lastPlacedGameObject = null;
        }

        feedbackText.text = "";

        if (velocityDragManager != null && velocityDragManager.dragLineRenderer != null)
            velocityDragManager.dragLineRenderer.positionCount = 0;

        if (cameraTracker != null) cameraTracker.ReturnToTracking();
    }

    private void TrackSilently(Transform transform)
    {
        if (cameraTracker == null || transform == null) return;
        cameraTracker.BeginUiSuppress();
        cameraTracker.TrackPlaceholder(transform);
        cameraTracker.EndUiSuppress();
    }

    private void OnMassInputChanged(string input)
    {
        if (mainCamera == null) return;

        if (string.IsNullOrWhiteSpace(input))
        {
            feedbackText.text = "";
            return;
        }

        if (ParsingUtils.TryParseMass(input, out _))
        {
            tutorialController.hasMassBeenEnteredForSatellite = true;
            feedbackText.text = "";
        }
        else
        {
            feedbackText.text = "Invalid. Mass should be 500-1,000,000 kg.";
        }
    }

    private void OnRadiusInputChanged(string input)
    {
        if (mainCamera == null) return;

        if (string.IsNullOrWhiteSpace(input))
        {
            feedbackText.text = "";
            return;
        }

        if (ParsingUtils.TryParseVector3(input, out _))
        {
            tutorialController.hasRadiusBeenEnteredForSatellite = true;
            feedbackText.text = "";
        }
        else
        {
            feedbackText.text = "Invalid format. Use numeric x,y,z values.";
        }
    }

    private void OnPositionInputChanged(string input)
    {
        if (mainCamera == null) return;

        if (ghostObjectPlaced && !clearingPosition && positionInput != null && positionInput.isFocused)
        {
            cameraTracker.BreakToFreeCam();
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            if (ghostInstance != null) ghostInstance.SetActive(false);
            feedbackText.text = "";
            ghostObjectPlaced = false;
            return;
        }

        if (ParsingUtils.TryParseVector3(input, out Vector3 targetPosition))
        {
            float distanceFromEarth = Vector3.Distance(Vector3.zero, targetPosition);
            float minDistance = 638f;
            float maxDistance = 5000f;

            if (distanceFromEarth < minDistance || distanceFromEarth > maxDistance)
            {
                ghostObjectPlaced = false;
                if (ghostInstance != null) ghostInstance.SetActive(false);
                feedbackText.text = $"Distance must be between {minDistance * 10f:N0} km and {maxDistance * 10f:N0} km from Earth.";
                return;
            }

            tutorialController.hasPositionBeenEnteredForSatellite = true;

            if (ghostInstance != null)
            {
                ghostInstance.SetActive(true);
                ghostInstance.transform.position = targetPosition;
            }
            TrackSilently(ghostInstance.transform);
            // Change this ghost object placed after TrackSilently call to make sure it doesnt conflict with ReturnToTracking
            ghostObjectPlaced = true;
            feedbackText.text = "";
        }
        else
        {
            ghostObjectPlaced = false;
            if (ghostInstance != null) ghostInstance.SetActive(false);
            feedbackText.text = "Invalid format. Use numeric x,y,z values.";
        }
    }

    public void ClearManualPlacementCompletely()
    {
        // 1) clear drag & preview artifacts
        velocityDragManager?.ClearManualArtifacts();
        trajectoryRenderer?.ClearPreview();
        trajectoryRenderer?.ClearPreManeuverLine();

        // 2) kill any placeholder sphere (pre-velocity object)
        if (lastPlacedGameObject != null)
        {
            Destroy(lastPlacedGameObject);
            lastPlacedGameObject = null;
        }

        // 4) ghost
        if (ghostInstance != null) ghostInstance.SetActive(false);
    }


    private void ClearAndUnfocusInputField(TMP_InputField inputField)
    {
        if (inputField == null) return;
        clearingPosition = true;
        inputField.text = "";
        EventSystem.current.SetSelectedGameObject(null);
        clearingPosition = false;
    }

    public void ResetLastPlacedGameObject()
    {
        feedbackText.text = "";
        lastPlacedGameObject = null;
    }
}
