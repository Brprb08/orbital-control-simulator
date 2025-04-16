using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/**
* ObjectPlacementManager manages the placement of celestial bodies in the scene.
* This script handles user input for specifying the radius, name, and position of planets,
* as well as transitioning between placement and tracking modes.
**/
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

    /**
    * Starts the placement process for a new celestial body.
    **/
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
            Mathf.Clamp(parsedRadius.x, 1f, 100f),
            Mathf.Clamp(parsedRadius.y, 1f, 100f),
            Mathf.Clamp(parsedRadius.z, 1f, 100f)
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

    /**
    * Cancels the current placement and removes the placeholder object.
    **/
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

    /**
    * Clears and unfocuses a TMP_InputField.
    * @param inputField - The input field to clear.
    **/
    private void ClearAndUnfocusInputField(TMP_InputField inputField)
    {
        if (inputField != null)
        {
            inputField.text = "";
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    /**
    * Enables FreeCam mode for object placement.
    * Called from 'Free Cam' button
    **/
    public void BreakToFreeCam()
    {
        Debug.Log("Switching to FreeCam...");
        isInPlacementMode = true;
    }

    /**
    * Disables FreeCam mode and returns to tracking.
    **/
    public void ExitFreeCam()
    {
        Debug.Log("Exiting FreeCam...");

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

    /**
    * Resets the reference to the last placed GameObject.
    **/
    public void ResetLastPlacedGameObject()
    {
        feedbackText.text = "";
        lastPlacedGameObject = null;
    }
}