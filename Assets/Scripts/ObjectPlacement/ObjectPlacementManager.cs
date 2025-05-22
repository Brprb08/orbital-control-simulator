using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Manages the placement of celestial bodies in the scene.
/// Handles user input for specifying radius, name, and mass of new bodies,
/// and transitions between placement and tracking modes.
/// </summary>
public class ObjectPlacementManager : MonoBehaviour
{
    public static ObjectPlacementManager Instance { get; private set; }

    [Header("References")]
    public Camera mainCamera;
    public GameObject spherePrefab; // Placeholder GameObject without NBody script
    public GravityManager gravityManager;
    public TMP_InputField radiusInput;
    public TMP_InputField objectNameInputField;
    public TMP_InputField massInput;
    public TextMeshProUGUI feedbackText;
    public VelocityDragManager velocityDragManager;
    public TMP_InputField nameInputField;
    public TMP_InputField massInputField;
    public TMP_InputField radiusInputField;
    public Button placeObjectButton;
    public TMP_InputField positionInput;

    [Header("TLE Placement")]
    public TMP_InputField tleNameInputField;
    public TMP_InputField tleMassInputField;
    public TMP_InputField tleLine1InputField;
    public TMP_InputField tleLine2InputField;
    public Button placeTLEObjectButton;

    [Header("Ghost Preview")]
    public GameObject ghostPreviewPrefab;
    private GameObject ghostInstance;

    [Header("Placement State")]
    public GameObject lastPlacedGameObject; // Reference to the last placed placeholder GameObject
    private bool isInPlacementMode = false;
    private int satelliteCount = 0;
    private bool objectIsPlaced = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        positionInput.onValueChanged.AddListener(OnPositionInputChanged);

        if (ghostPreviewPrefab != null)
        {
            ghostInstance = Instantiate(ghostPreviewPrefab);
            ghostInstance.SetActive(false); // Start hidden
        }
    }

    /// <summary>
    /// Starts the placement process for a new celestial body.
    /// Parses radius and mass input, instantiates a placeholder object,
    /// and initializes velocity drag UI.
    /// </summary>
    public void StartPlacement()
    {
        if (lastPlacedGameObject != null)
        {
            feedbackText.text = "You must set the velocity of the current planet before placing another.";
            return;
        }

        if (!isInPlacementMode)
        {
            feedbackText.text = "You must be in FreeCam mode to place planets.";
            return;
        }

        string radiusText = radiusInput.text;
        if (string.IsNullOrWhiteSpace(radiusText))
        {
            feedbackText.text = "Please enter a radius in the format x,y,z. Numbers only.";
            return;
        }

        if (!ParsingUtils.TryParseVector3(radiusText, out Vector3 parsedRadius))
        {
            feedbackText.text = "Invalid radius. Use numeric x,y,z.";
            return;
        }

        string massText = massInput.text;
        if (string.IsNullOrWhiteSpace(massText))
        {
            feedbackText.text = "Please enter a numeric mass between 5 and 1,000,000 kg.";
            return;
        }

        if (!ParsingUtils.TryParseMass(massText, out float mass))
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

        objectIsPlaced = true;

        lastPlacedGameObject = Instantiate(spherePrefab);
        lastPlacedGameObject.transform.localScale = new Vector3(parsedRadius.x * 1f, parsedRadius.y * 1f, parsedRadius.z * 1f);

        if (ParsingUtils.TryParseVector3(positionInput.text, out Vector3 parsedPosition))
        {
            lastPlacedGameObject.transform.position = parsedPosition;
        }
        else
        {
            lastPlacedGameObject.transform.position = mainCamera.transform.position + mainCamera.transform.forward * 10f;
        }

        if (ghostInstance != null)
        {
            ghostInstance.SetActive(false);
        }

        satelliteCount++;
        string customName = !string.IsNullOrWhiteSpace(objectNameInputField?.text) ? objectNameInputField.text : $"Satellite {satelliteCount}";
        lastPlacedGameObject.name = customName;
        lastPlacedGameObject.tag = "Planet";

        if (velocityDragManager != null)
        {
            velocityDragManager.ResetDragManager();
            velocityDragManager.planet = lastPlacedGameObject;
            velocityDragManager.placeholderMass = placeholderMass;
        }

        SetupCameraTracking(lastPlacedGameObject);
        // CameraController camController = gravityManager.GetComponent<CameraController>();
        // if (camController != null)
        // {
        //     camController.RefreshBodiesList();
        //     camController.SetTargetPlaceholder(lastPlacedGameObject.transform);
        //     if (camController.IsFreeCamMode)
        //     {
        //         camController.ReturnToTracking();
        //     }
        //     camController.SetInEarthView(false);
        // }

        ClearAndUnfocusInputField(radiusInput);
        ClearAndUnfocusInputField(positionInput);
        ClearAndUnfocusInputField(objectNameInputField);
        ClearAndUnfocusInputField(massInput);

        if (nameInputField != null && massInputField != null && radiusInputField != null)
        {
            nameInputField.interactable = false;

            positionInput.interactable = false;

            massInputField.interactable = false;

            radiusInputField.interactable = false;

            placeObjectButton.interactable = false;
        }

        feedbackText.text =
    "Setting Satellite Velocity:\n\n" +
"• Click the satellite to activate the direction line.\n" +
"• Drag the line to set the desired direction.\n" +
"• Use the velocity input field to adjust speed.\n" +
"• The line updates to reflect direction and speed.\n" +
"• Click \"Set Velocity\" to apply the changes.";
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

        satelliteCount++;
        lastPlacedGameObject = Instantiate(spherePrefab);
        lastPlacedGameObject.name = name;
        lastPlacedGameObject.tag = "Planet";
        lastPlacedGameObject.transform.position = position;
        lastPlacedGameObject.transform.localScale = Vector3.one * 1f;

        objectIsPlaced = true;

        // Apply velocity directly
        if (velocityDragManager != null)
        {
            velocityDragManager.planet = lastPlacedGameObject;
            velocityDragManager.placeholderMass = mass;
        }

        SetupCameraTracking(lastPlacedGameObject);
        // CameraController camController = gravityManager.GetComponent<CameraController>();
        // if (camController != null)
        // {
        //     camController.RefreshBodiesList();
        //     camController.SetTargetPlaceholder(lastPlacedGameObject.transform);
        //     if (camController.IsFreeCamMode)
        //     {
        //         camController.ReturnToTracking();
        //     }
        //     camController.SetInEarthView(false);
        // }

        // Apply velocity directly
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

    /// <summary>
    /// Cancels the current placement process and removes the placeholder object.
    /// Also resets velocity UI and tracking camera.
    /// </summary>
    public void CancelPlacement()
    {
        if (lastPlacedGameObject != null)
        {
            Destroy(lastPlacedGameObject);
            lastPlacedGameObject = null;
        }

        feedbackText.text = "";

        velocityDragManager.dragLineRenderer.positionCount = 0;

        CameraController.Instance.UpdateTrajectoryRender(CameraController.Instance.currentIndex);
        CameraController.Instance.isTrackingPlaceholder = false;
        CameraController.Instance.ReturnToTracking();
    }

    private void SetupCameraTracking(GameObject target)
    {
        CameraController camController = CameraController.Instance;
        if (camController != null)
        {
            camController.RefreshBodiesList();
            camController.SetTargetPlaceholder(target.transform);
            if (camController.IsFreeCamMode)
            {
                camController.ReturnToTracking();
            }
            camController.SetInEarthView(false);
        }
    }

    /// <summary>
    /// Handles changes to the position input field during object placement.
    /// Moves the camera so that it faces the desired target position and shows a ghost preview at that position.
    /// </summary>
    /// <param name="input">The string input from the user, expected in "x,y,z" format.</param>
    private void OnPositionInputChanged(string input)
    {
        if (mainCamera == null)
            return;

        if (ParsingUtils.TryParseVector3(input, out Vector3 targetPosition))
        {
            ghostInstance.SetActive(true);
            ghostInstance.transform.position = targetPosition;

            float placementDistance = 10f;

            Vector3 directionToOrigin = (Vector3.zero - targetPosition).normalized;

            // Move camera so the object will be placed at targetPosition
            Vector3 cameraPosition = targetPosition - directionToOrigin * placementDistance;

            Quaternion rotation = Quaternion.LookRotation(directionToOrigin, Vector3.up);

            mainCamera.transform.SetPositionAndRotation(cameraPosition, rotation);
        }
        else
        {
            ghostInstance.SetActive(false); // Hide if input is invalid
        }
    }

    /// <summary>
    /// Clears and unfocuses the specified TMP input field.
    /// </summary>
    /// <param name="inputField">The input field to clear and unfocus.</param>
    private void ClearAndUnfocusInputField(TMP_InputField inputField)
    {
        if (inputField != null)
        {
            inputField.text = "";
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    /// <summary>
    /// Enables placement mode (FreeCam) to allow new objects to be added.
    /// Called by the Free Cam button.
    /// </summary>
    public void BreakToFreeCam()
    {
        //Debug.Log("Switching to FreeCam...");
        isInPlacementMode = true;
    }

    /// <summary>
    /// Disables FreeCam mode and reverts back to tracking mode.
    /// Cancels placement if an object was being placed.
    /// </summary>
    public void ExitFreeCam()
    {
        //Debug.Log("Exiting FreeCam...");

        if (objectIsPlaced)
        {
            objectIsPlaced = false;
            CancelPlacement();
        }
        else
        {
            CameraController.Instance.ReturnToTracking();
        }
        isInPlacementMode = false;
    }

    /// <summary>
    /// Resets the reference to the last placed placeholder GameObject.
    /// Clears feedback text.
    /// </summary>
    public void ResetLastPlacedGameObject()
    {
        feedbackText.text = "";
        lastPlacedGameObject = null;
    }
}