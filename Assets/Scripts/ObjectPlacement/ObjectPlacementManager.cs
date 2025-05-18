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

        if (!ParsingUtils.Instance.TryParseVector3(radiusText, out Vector3 parsedRadius))
        {
            feedbackText.text = "Invalid radius. Use numeric x,y,z.";
            return;
        }

        string massText = massInput.text;
        if (string.IsNullOrWhiteSpace(massText))
        {
            feedbackText.text = "Please enter a numeric mass between 5 and 5.0e23 kg.";
            return;
        }

        if (!ParsingUtils.Instance.TryParseMass(massText, out float mass))
        {
            feedbackText.text = "Invalid mass. Enter a number between 500 and 5.0e23.";
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
        lastPlacedGameObject.transform.position = mainCamera.transform.position + mainCamera.transform.forward * 10f;

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

        CameraController camController = gravityManager.GetComponent<CameraController>();
        if (camController != null)
        {
            camController.RefreshBodiesList();
            camController.SetTargetPlaceholder(lastPlacedGameObject.transform);
            if (camController.IsFreeCamMode)
            {
                camController.ReturnToTracking();
            }
            camController.SetInEarthView(false);
        }

        ClearAndUnfocusInputField(radiusInput);
        ClearAndUnfocusInputField(objectNameInputField);
        ClearAndUnfocusInputField(massInput);

        if (nameInputField != null && massInputField != null && radiusInputField != null)
        {
            nameInputField.interactable = false;

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