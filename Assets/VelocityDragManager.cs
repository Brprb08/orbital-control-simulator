using UnityEngine;
using TMPro;            // If you're using TextMeshPro

public class VelocityDragManager : MonoBehaviour
{
    [Header("References")]
    public Camera mainCamera;             // Assign your main/free camera
    public LineRenderer dragLineRenderer; // A LineRenderer to visualize the velocity line
    public TextMeshProUGUI velocityDisplay; // TextMeshPro or Text to show velocity

    [Header("Planet to Apply Velocity To")]
    public GameObject planet;       // Assign the planet you want to set velocity on

    [Header("Settings")]
    public float velocityScale = .0001f;     // Multiplier for the length of the drag
    public LayerMask dragPlaneLayer;     // LayerMask for your plane or geometry used for raycast

    private bool isDragging = false;     // True while the user is dragging the mouse
    private bool isVelocitySet = false;
    private Vector3 dragStartPos;        // World position where the drag began
    private Vector3 currentVelocity;     // Computed velocity from the drag line
    private int maxSpeed = 2;
    private Plane dragPlane;

    public GravityManager gravityManager;

    private void Start()
    {
        if (dragLineRenderer != null)
        {
            // Hide the line at the start
            dragLineRenderer.positionCount = 0;
        }
    }

    private void Update()
    {
        if (isVelocitySet)
        {
            return; // Prevent further dragging once the velocity is set
        }

        // 1) Begin drag on left mouse button down
        if (Input.GetMouseButtonDown(0))
        {
            StartDrag();
        }

        // 2) Continue dragging while left mouse is held
        if (isDragging && Input.GetMouseButton(0))
        {
            UpdateDrag();
        }

        // 3) End drag on left mouse button release
        if (Input.GetMouseButtonUp(0) && isDragging)
        {
            EndDrag();
        }
    }

    /// <summary>
    /// Called when the user first presses LMB.
    /// </summary>
    private void StartDrag()
    {
        if (planet == null || mainCamera == null) return;

        isDragging = true;
        dragStartPos = planet.transform.position;

        Vector3 planeNormal = (planet.transform.position - mainCamera.transform.position).normalized;
        dragPlane = new Plane(planeNormal, planet.transform.position);

        if (dragLineRenderer != null)
        {
            dragLineRenderer.positionCount = 2;
            dragLineRenderer.SetPosition(0, dragStartPos);
            dragLineRenderer.SetPosition(1, dragStartPos);
        }
    }

    /// <summary>
    /// Called each frame while user is holding LMB.
    /// </summary>
    private void UpdateDrag()
    {
        Vector3 mouseWorldPos = GetMouseWorldPositionOnPlane();
        if (mouseWorldPos != Vector3.zero)
        {
            if (dragLineRenderer != null)
            {
                dragLineRenderer.SetPosition(1, mouseWorldPos);
            }

            Vector3 direction = (mouseWorldPos - dragStartPos);

            // 1) Multiply by velocityScale
            Vector3 rawVelocity = direction * velocityScale;

            // 2) Clamp its magnitude to a safe range
            float maxSpeed = 1f; // Tweak to taste
            currentVelocity = Vector3.ClampMagnitude(rawVelocity, maxSpeed);

            // UI feedback
            if (velocityDisplay != null)
            {
                velocityDisplay.text = $"Velocity: ({currentVelocity.x:F2}, {currentVelocity.y:F2}, {currentVelocity.z:F2})";
            }
        }
    }

    /// <summary>
    /// Called when the user releases LMB.
    /// </summary>
    private void EndDrag()
    {
        isDragging = false;

        Debug.Log(currentVelocity);
        // Apply the computed velocity to the planet
        ApplyVelocityToPlanet(currentVelocity);

        // Hide or reset the line
        if (dragLineRenderer != null)
        {
            dragLineRenderer.positionCount = 0;
        }
    }

    /// <summary>
    /// Raycast against the plane (or geometry) to get the mouse position in 3D.
    /// </summary>
    private Vector3 GetMouseWorldPositionOnPlane()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        float enter;
        // If our dynamic dragPlane is valid and the ray intersects it...
        if (dragPlane.Raycast(ray, out enter))
        {
            return ray.GetPoint(enter);
        }

        // If there's somehow no intersection (plane parallel?), return zero
        return Vector3.zero;
    }

    /// <summary>
    /// Example method to apply velocity to your planet's NBody component.
    /// </summary>
    private void ApplyVelocityToPlanet(Vector3 velocityToApply)
    {
        if (planet == null) return;

        // Add NBody component to the existing placeholder
        NBody planetNBody = planet.GetComponent<NBody>();
        if (planetNBody == null)
        {
            Debug.Log($"Adding NBody to placeholder {planet.name}");
            planetNBody = planet.AddComponent<NBody>();
            if (planetNBody == null)
            {
                Debug.LogError($"Failed to add NBody to {planet.name}!");
                return;
            }

            planetNBody.mass = 400000f;
            planetNBody.radius = planet.transform.localScale.x * 10f;
        }

        planetNBody.velocity = velocityToApply;
        gravityManager.RegisterBody(planetNBody);

        CameraController cameraController = gravityManager.GetComponent<CameraController>();
        if (cameraController != null)
        {
            cameraController.RefreshBodiesList();

            // Set the camera to track the newly placed planet
            int newIndex = cameraController.Bodies.IndexOf(planetNBody);
            if (newIndex >= 0)
            {
                cameraController.currentIndex = newIndex;  // Track the newly placed body
                cameraController.SwitchToRealNBody(planetNBody);  // Set camera to track it
            }

            // ★ FIX: After setting the velocity, go back to FreeCam 
            // cameraController.BreakToFreeCam();
        }

        Debug.Log($"Applied velocity {velocityToApply} to {planet.name} via drag.");
        planet = null;
        isVelocitySet = true;

        ObjectPlacementManager objectPlacementManager = gravityManager.GetComponent<ObjectPlacementManager>();
        if (objectPlacementManager != null)
        {
            objectPlacementManager.ResetLastPlacedGameObject();
        }

        UIManager uIManager = gravityManager.GetComponent<UIManager>();
        if (uIManager != null)
        {
            uIManager.OnTrackCamPressed();
        }
    }

    /// <summary>
    /// Reset the drag manager for a new planet.
    /// </summary>
    public void ResetDragManager()
    {
        isVelocitySet = false;  // Allow dragging for the new planet
    }
}