using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

public class ObjectPlacementManager : MonoBehaviour
{
    public Camera mainCamera;
    public GameObject spherePrefab; // No NBody script attached to this prefab
    public GravityManager gravityManager;
    public TMP_InputField velocityInput; // Expect a format like "x,y,z"
    public TMP_InputField radiusInput;
    public TMP_InputField objectNameInputField;
    public TextMeshProUGUI feedbackText;
    public CameraMovement cameraMovement;

    private GameObject lastPlacedGameObject = null; // Reference to the raw GameObject
    private NBody lastPlacedNBody = null; // Reference to the final NBody component
    private bool isInPlacementMode = false;
    public Transform cameraPivotTransform;
    public Transform cameraTransform; // Main Camera as a child of CameraPivot
    private Vector3 defaultLocalPosition;
    private int satelliteCount = 0;

    private void Start()
    {
        feedbackText.text = "Set radius, then 'Place Planet'. After placement, enter velocity and 'Set Velocity'.";
    }

    public void StartPlacement()
    {
        // Only allow placement if in FreeCam mode
        if (!isInPlacementMode)
        {
            feedbackText.text = "You must be in FreeCam mode to place planets.";
            return;
        }

        float radius;
        if (!float.TryParse(radiusInput.text, out radius) || radius <= 0)
        {
            feedbackText.text = "Invalid radius. Please enter a positive number.";
            return;
        }

        // Create the actual planet (no NBody component at this point)
        lastPlacedGameObject = Instantiate(spherePrefab);
        float validRadius = Mathf.Max(1f, radius);
        lastPlacedGameObject.transform.localScale = Vector3.one * validRadius * 2f * 0.1f;
        lastPlacedGameObject.transform.position = mainCamera.transform.position + mainCamera.transform.forward * 10f;

        satelliteCount++; // Increment the counter
        string customName = objectNameInputField != null ? objectNameInputField.text : "";
        if (string.IsNullOrWhiteSpace(customName))
        {
            customName = $"Satellite {satelliteCount}";
        }
        lastPlacedGameObject.name = customName;

        // Make it semi-transparent (optional)
        Renderer r = lastPlacedGameObject.GetComponent<Renderer>();
        if (r != null)
        {
            Color c = r.material.color;
            c.a = 0.5f; // Semi-transparent
            r.material.color = c;
        }

        ClearAndUnfocusInputField(radiusInput);
        ClearAndUnfocusInputField(objectNameInputField);

        feedbackText.text = "Planet placed without gravity.\nEnter velocity (x,y,z) and click 'Set Velocity' to start movement.";
    }

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

    public void SetInitialVelocity()
    {
        if (lastPlacedGameObject == null)
        {
            feedbackText.text = "No planet available to set velocity. Place one first.";
            return;
        }

        string velocityText = velocityInput.text;
        if (string.IsNullOrWhiteSpace(velocityText))
        {
            feedbackText.text = "Please enter a velocity in the format x,y,z";
            return;
        }

        Vector3 parsedVelocity;
        if (!TryParseVector3(velocityText, out parsedVelocity))
        {
            feedbackText.text = "Invalid velocity format. Use x,y,z with no spaces.";
            return;
        }

        // Add the NBody component to the object at runtime
        lastPlacedNBody = lastPlacedGameObject.AddComponent<NBody>();

        // Configure NBody properties
        float radius = lastPlacedGameObject.transform.localScale.x * 10f;
        lastPlacedNBody.radius = radius;
        lastPlacedNBody.mass = 400000f; // Density-based calculation for mass
        lastPlacedNBody.velocity = parsedVelocity;
        NBody nBody = lastPlacedGameObject.GetComponent<NBody>();
        if (nBody != null)
        {
            nBody.predictionSteps = 1000;
        }

        // Register NBody with GravityManager
        gravityManager.RegisterBody(lastPlacedNBody);

        CameraController cameraController = gravityManager.GetComponent<CameraController>();
        Debug.Log($"CameraController: {cameraController}");
        if (cameraController != null)
        {
            cameraController.RefreshBodiesList();
        }
        lastPlacedGameObject = null;

        ClearAndUnfocusInputField(velocityInput);

        feedbackText.text = $"Velocity set to {parsedVelocity}. The planet will now move under gravity!";
    }

    private bool TryParseVector3(string input, out Vector3 result)
    {
        result = Vector3.zero;
        string[] parts = input.Split(',');
        if (parts.Length != 3) return false;

        float x, y, z;
        if (!float.TryParse(parts[0], out x)) return false;
        if (!float.TryParse(parts[1], out y)) return false;
        if (!float.TryParse(parts[2], out z)) return false;

        result = new Vector3(x, y, z);
        return true;
    }

    private void ClearAndUnfocusInputField(TMP_InputField inputField)
    {
        inputField.text = ""; // Clear the text
        EventSystem.current.SetSelectedGameObject(null); // Unfocus the field
    }

    public void DeselectUI()
    {
        EventSystem.current.SetSelectedGameObject(null);
    }

    public void BreakToFreeCam()
    {
        Debug.Log("Switching to FreeCam...");
        isInPlacementMode = true;
    }

    public void ExitFreeCam()
    {
        Debug.Log("Exiting FreeCam...");
        isInPlacementMode = false;
    }

    private void ResetCameraPosition()
    {
        if (cameraTransform != null)
        {
            // Reset Main Camera to default local position relative to the pivot
            Debug.Log($"Resetting Camera to default local position: {defaultLocalPosition}");
            cameraTransform.localPosition = defaultLocalPosition;

            // Reset Main Camera local rotation
            cameraTransform.localRotation = Quaternion.identity;
        }
        else
        {
            Debug.LogError("cameraTransform is null. Ensure it is assigned in the Inspector!");
        }
    }

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
}