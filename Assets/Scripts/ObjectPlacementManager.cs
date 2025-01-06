using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

/**
 * ObjectPlacementManager manages the placement of celestial bodies in the scene.
 * This script handles user input for specifying the radius, name, and position of planets,
 * as well as transitioning between placement and tracking modes.
 */
public class ObjectPlacementManager : MonoBehaviour
{
    [Header("References")]
    public Camera mainCamera;
    public GameObject spherePrefab; // Placeholder GameObject without NBody script.
    public GameObject nbodyPrefab;  // Real NBody prefab (with physics).
    public GravityManager gravityManager;
    public TMP_InputField velocityInput; // Expects input in the format "x,y,z".
    public TMP_InputField radiusInput;
    public TMP_InputField objectNameInputField;
    public TextMeshProUGUI feedbackText;
    public CameraMovement cameraMovement;
    public VelocityDragManager velocityDragManager;

    [Header("Placement State")]
    private GameObject lastPlacedGameObject; // Reference to the last placed placeholder GameObject.
    private GameObject realNBodyObject;      // Reference to the instantiated NBody GameObject.
    private NBody lastPlacedNBody;           // Reference to the NBody component.
    private bool isInPlacementMode = false;
    private int satelliteCount = 0;

    [Header("Camera Components")]
    public Transform cameraPivotTransform;
    public Transform cameraTransform;  // Camera as a child of the pivot.
    private Vector3 defaultLocalPosition;

    /**
     * Initializes the feedback text with instructions.
     */
    private void Start()
    {
        feedbackText.text = "Set radius, then 'Place Planet'. After placement, enter velocity and 'Set Velocity'.";
    }

    /**
     * Starts the placement process for a new celestial body.
     */
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
            feedbackText.text = "Please enter a radius in the format x,y,z.";
            return;
        }

        if (!TryParseVector3(radiusText, out Vector3 parsedRadius))
        {
            feedbackText.text = "Invalid radius format. Use x,y,z with no spaces.";
            return;
        }

        parsedRadius = new Vector3(
            Mathf.Clamp(parsedRadius.x, 1f, 100f),
            Mathf.Clamp(parsedRadius.y, 1f, 100f),
            Mathf.Clamp(parsedRadius.z, 1f, 100f)
        );

        lastPlacedGameObject = Instantiate(spherePrefab);
        lastPlacedGameObject.transform.localScale = new Vector3(parsedRadius.x * 0.2f, parsedRadius.y * 0.2f, parsedRadius.z * 0.2f);
        lastPlacedGameObject.transform.position = mainCamera.transform.position + mainCamera.transform.forward * 10f;

        satelliteCount++;
        string customName = !string.IsNullOrWhiteSpace(objectNameInputField?.text) ? objectNameInputField.text : $"Satellite {satelliteCount}";
        lastPlacedGameObject.name = customName;
        lastPlacedGameObject.tag = "Planet";

        Renderer renderer = lastPlacedGameObject.GetComponent<Renderer>();
        if (renderer != null)
        {
            Color c = renderer.material.color;
            c.a = 0.5f; // Semi-transparent for indication.
            renderer.material.color = c;
        }

        lastPlacedNBody = null;

        if (velocityDragManager != null)
        {
            velocityDragManager.ResetDragManager();
            velocityDragManager.planet = lastPlacedGameObject;
            feedbackText.text = "Planet placed. You can now left-click and drag in the scene to set velocity!";
        }
        else
        {
            feedbackText.text = "Planet placed. (No VelocityDragManager assigned)";
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
        }

        ClearAndUnfocusInputField(radiusInput);
        ClearAndUnfocusInputField(objectNameInputField);

        feedbackText.text = "Planet placed without gravity.\nEnter velocity (x,y,z) and click 'Set Velocity' to start movement.";
    }

    /**
     * Cancels the current placement and removes the placeholder object.
     */
    public void CancelPlacement()
    {
        if (lastPlacedGameObject != null)
        {
            Destroy(lastPlacedGameObject);
            lastPlacedGameObject = null;
        }

        feedbackText.text = "Placement canceled. Returned to tracking mode.";
        ExitFreeCam();
    }

    /**
     * Tries to parse a string as a Vector3.
     * @param input The input string in "x,y,z" format.
     * @param result The resulting Vector3.
     * @return True if parsing succeeds; false otherwise.
     */
    private bool TryParseVector3(string input, out Vector3 result)
    {
        result = Vector3.zero;
        string[] parts = input.Split(',');
        if (parts.Length != 3) return false;

        return float.TryParse(parts[0], out result.x) &&
               float.TryParse(parts[1], out result.y) &&
               float.TryParse(parts[2], out result.z);
    }

    /**
     * Clears and unfocuses a TMP_InputField.
     * @param inputField The input field to clear.
     */
    private void ClearAndUnfocusInputField(TMP_InputField inputField)
    {
        if (inputField != null)
        {
            inputField.text = "";
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    /**
     * Deselects the current UI element to prevent accidental input.
     */
    public void DeselectUI()
    {
        EventSystem.current.SetSelectedGameObject(null);
    }

    /**
     * Enables FreeCam mode for object placement.
     */
    public void BreakToFreeCam()
    {
        Debug.Log("Switching to FreeCam...");
        isInPlacementMode = true;
    }

    /**
     * Disables FreeCam mode and returns to tracking.
     */
    public void ExitFreeCam()
    {
        Debug.Log("Exiting FreeCam...");
        isInPlacementMode = false;
    }

    /**
     * Resets the camera position to its default relative to the pivot.
     */
    private void ResetCameraPosition()
    {
        if (cameraTransform != null)
        {
            Debug.Log($"Resetting Camera to default local position: {defaultLocalPosition}");
            cameraTransform.localPosition = defaultLocalPosition;
            cameraTransform.localRotation = Quaternion.identity;
        }
        else
        {
            Debug.LogError("cameraTransform is null. Ensure it is assigned in the Inspector!");
        }
    }

    /**
     * Resets the pivot rotation to its default state.
     */
    private void ResetPivotRotation()
    {
        if (cameraPivotTransform != null)
        {
            Debug.Log("Resetting CameraPivot rotation to identity (pointing at the planet).");
            cameraPivotTransform.rotation = Quaternion.identity;
        }
        else
        {
            Debug.LogError("cameraPivotTransform is null. Ensure it is assigned in the Inspector!");
        }
    }

    /**
     * Resets the reference to the last placed GameObject.
     */
    public void ResetLastPlacedGameObject()
    {
        lastPlacedGameObject = null;
    }
}