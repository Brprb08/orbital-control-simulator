using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

public class ObjectPlacementManager : MonoBehaviour
{
    public Camera mainCamera;
    public GameObject spherePrefab;
    public GravityManager gravityManager;
    public TMP_InputField velocityInput;
    public TMP_InputField radiusInput;
    public TextMeshProUGUI feedbackText;
    public CameraMovement cameraMovement;

    private GameObject previewObject;
    private Vector3 initialVelocity = Vector3.zero;
    private bool isInPlacementMode = false;

    private void Start()
    {
        feedbackText.text = "Click 'Start Placement' to place an object.";
    }

    private void Update()
    {
        if (isInPlacementMode && previewObject != null)
        {
            MovePreviewObject();

            if (Input.GetMouseButtonDown(0)) // Left-click to place object
            {
                PlaceObject();
            }
        }
    }

    public void StartPlacement()
    {
        if (previewObject != null) return;

        float radius;
        if (!float.TryParse(radiusInput.text, out radius) || radius <= 0)
        {
            feedbackText.text = "Invalid radius. Please enter a positive number.";
            return;
        }

        previewObject = Instantiate(spherePrefab);
        float validRadius = Mathf.Max(1f, radius);
        previewObject.transform.localScale = Vector3.one * validRadius * 2f * 0.1f;
        previewObject.GetComponent<Renderer>().material.color = new Color(1f, 1f, 1f, 0.5f);

        feedbackText.text = "Move your mouse to position the object. Left-click to place, or right-click to cancel.";

        // Enter FreeCam and disable FreeCam rotation
        BreakToFreeCam();
    }

    public void CancelPlacement()
    {
        if (previewObject != null)
        {
            Destroy(previewObject);
            previewObject = null;
        }

        feedbackText.text = "Placement canceled. Returned to tracking mode.";

        ExitFreeCam();
    }

    public void SetInitialVelocity()
    {
        float velocity;
        if (!float.TryParse(velocityInput.text, out velocity) || velocity <= 0)
        {
            feedbackText.text = "Invalid velocity. Please enter a positive number.";
            return;
        }

        initialVelocity = new Vector3(velocity / 10000f, 0, 0);
        feedbackText.text = $"Velocity set to {velocity} m/s.";
    }

    private void MovePreviewObject()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            previewObject.transform.position = hit.point;
        }
    }

    private void PlaceObject()
    {
        if (previewObject == null) return;

        GameObject newObject = Instantiate(spherePrefab);
        newObject.transform.position = previewObject.transform.position;
        newObject.transform.localScale = previewObject.transform.localScale;

        NBody nBody = newObject.AddComponent<NBody>();
        float radius = newObject.transform.localScale.x * 10f;
        nBody.radius = radius;
        nBody.mass = Mathf.Pow(radius, 3) * 1e12f;
        nBody.velocity = initialVelocity;

        gravityManager.RegisterBody(nBody);

        Destroy(previewObject);
        previewObject = null;
        isInPlacementMode = false;

        feedbackText.text = "Object placed!";

        // Exit FreeCam and re-enable FreeCam rotation
        ExitFreeCam();
    }

    public void DeselectUI()
    {
        EventSystem.current.SetSelectedGameObject(null); // Deselect any UI element
    }

    public void BreakToFreeCam()
    {
        Debug.Log("Entering FreeCam mode.");
        isInPlacementMode = true;

        DeselectUI();

        if (cameraMovement != null)
        {
            cameraMovement.enabled = false; // Disable planet tracking
        }

        FreeCamera freeCamera = mainCamera.GetComponent<FreeCamera>();
        if (freeCamera != null)
        {
            freeCamera.TogglePlacementMode(true);
        }
    }

    public void ExitFreeCam()
    {
        Debug.Log("Exiting FreeCam mode after placement.");
        isInPlacementMode = false;

        FreeCamera freeCamera = mainCamera.GetComponent<FreeCamera>();
        if (freeCamera != null)
        {
            freeCamera.TogglePlacementMode(false); // Re-enable FreeCam rotation
        }

        if (cameraMovement != null)
        {
            cameraMovement.enabled = true; // Re-enable planet tracking
        }
    }


}