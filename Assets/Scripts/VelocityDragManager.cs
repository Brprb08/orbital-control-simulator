using UnityEngine;
using TMPro;            // If you're using TextMeshPro
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class VelocityDragManager : MonoBehaviour
{
    [Header("References")]
    public Camera mainCamera;             // Assign your main/free camera
    public LineRenderer dragLineRenderer; // A LineRenderer to visualize the velocity line
    public TMP_InputField velocityDisplayText;

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
    public LayerMask dragSphereLayerMask;
    private GameObject dragSphereObject;
    private SphereCollider dragSphereCollider;
    public float sphereRadiusMultiplier = 10f;
    public Slider speedSlider;
    private Vector3 dragDirection = Vector3.zero;
    private float sliderSpeed = 0f;
    private float lastLineUpdateTime = 0f;
    private float lineUpdateInterval = 0.05f;

    private void Start()
    {
        if (dragLineRenderer != null)
        {
            // Hide the line at the start
            dragLineRenderer.positionCount = 0;
        }

        if (speedSlider != null)
        {
            // This ensures "OnSpeedSliderChanged" is called whenever user moves the slider
            speedSlider.onValueChanged.AddListener(OnSpeedSliderChanged);
        }

        if (velocityDisplayText != null)
        {
            // Add a listener to detect when the user types into the input field
            velocityDisplayText.onValueChanged.AddListener(OnVelocityInputChanged);
            velocityDisplayText.interactable = false;
        }

        if (dragSphereObject == null)
        {
            dragSphereObject = new GameObject("DragSphereTemp");
            dragSphereCollider = dragSphereObject.AddComponent<SphereCollider>();
            dragSphereCollider.isTrigger = true;
            dragSphereObject.layer = LayerMask.NameToLayer("DragSphere");
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
            if (EventSystem.current.IsPointerOverGameObject())
            {
                // This means the user clicked a UI button, slider, etc.
                // => do NOT start the drag
                return;
            }
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

        // -- (2) Position the sphere at the planet's center in world space --
        dragSphereObject.transform.position = planet.transform.position;
        // Reset local rotation/scale if you want a clean base
        dragSphereObject.transform.rotation = Quaternion.identity;
        dragSphereObject.transform.localScale = Vector3.one; // If not parented, this is enough

        // -- (3) Set the collider's radius to be larger than the planet --
        float planetScale = planet.transform.localScale.x;  // or maybe the average of x,y,z
        float sphereRadius = Mathf.Max(1f, planetScale * sphereRadiusMultiplier);
        dragSphereCollider.radius = 200f;  // the physics radius    

        if (dragLineRenderer != null)
        {
            dragLineRenderer.positionCount = 2;
            dragLineRenderer.SetPosition(0, dragStartPos);
            dragLineRenderer.SetPosition(1, dragStartPos);
            dragLineRenderer.widthMultiplier = .25f;

            Material lineMaterial = new Material(Shader.Find("Unlit/Color"));
            lineMaterial.color = Color.red;
            dragLineRenderer.material = lineMaterial;

            velocityDisplayText.interactable = true;
        }

        dragDirection = Vector3.zero;
    }

    /// <summary>
    /// Called each frame while user is holding LMB.
    /// </summary>
    private void UpdateDrag()
    {
        if (!isDragging) return;

        // Only update every `lineUpdateInterval` seconds
        if (Time.time - lastLineUpdateTime < lineUpdateInterval)
        {
            return;
        }
        lastLineUpdateTime = Time.time;

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        Vector3 sphereCenter = planet.transform.position;
        float radius = planet.transform.localScale.x * sphereRadiusMultiplier;
        Vector3 intersectionPoint = GetFarSideIntersection(ray, sphereCenter, radius);

        if (intersectionPoint != Vector3.zero && dragLineRenderer != null)
        {
            dragLineRenderer.SetPosition(1, intersectionPoint);
            dragDirection = (intersectionPoint - sphereCenter).normalized;
            currentVelocity = dragDirection * sliderSpeed;
        }
    }

    // (A) The helper method for "far side" intersection
    private Vector3 GetFarSideIntersection(Ray ray, Vector3 sphereCenter, float radius)
    {
        Vector3 d = ray.direction.normalized;
        Vector3 oc = ray.origin - sphereCenter;

        float b = 2f * Vector3.Dot(oc, d);
        float c = oc.sqrMagnitude - (radius * radius);
        float discriminant = b * b - 4f * c;

        if (discriminant < 0f)
        {
            // If no intersection, return point on far side of sphere along ray direction
            return sphereCenter + (d * radius);
        }

        float sqrtDisc = Mathf.Sqrt(discriminant);
        float t1 = (-b - sqrtDisc) / 2f;
        float t2 = (-b + sqrtDisc) / 2f;

        float chosenT = (t2 >= 0f) ? t2 : t1;
        if (chosenT < 0f)
        {
            // If still no valid hit, default to farthest visible point along ray
            return sphereCenter + (d * radius);
        }

        return ray.origin + d * chosenT;
    }

    /// <summary>
    /// Called when the user releases LMB.
    /// </summary>
    private void EndDrag()
    {
        isDragging = false;

        Debug.Log(currentVelocity);
        // Apply the computed velocity to the planet
        // ApplyVelocityToPlanet(currentVelocity);

        // Hide or reset the line
        // if (dragLineRenderer != null)
        // {
        //     dragLineRenderer.positionCount = 0;
        // }
    }

    public void OnSpeedSliderChanged(float value)
    {
        // value in [0..1] or whatever you set in Inspector
        sliderSpeed = value;

        // Update "currentVelocity" = direction * slider
        currentVelocity = dragDirection * sliderSpeed;

        // Update text
        if (velocityDisplayText != null)
        {
            velocityDisplayText.onValueChanged.RemoveListener(OnVelocityInputChanged);

            velocityDisplayText.text = $"{currentVelocity.x:F2}, {currentVelocity.y:F2}, {currentVelocity.z:F2}";

            velocityDisplayText.onValueChanged.AddListener(OnVelocityInputChanged);
        }

        // Optionally update line length (if you want the line to reflect speed)
        // if (dragLineRenderer != null && dragLineRenderer.positionCount >= 2)
        // {
        //     Vector3 endPos = planet.transform.position + (dragDirection * sliderSpeed / velocityScale);
        //     dragLineRenderer.SetPosition(1, endPos);
        // }
    }

    /// <summary>
    /// Raycast against the plane (or geometry) to get the mouse position in 3D.
    /// </summary>
    private Vector3 GetMouseWorldPositionOnSphere()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        Vector3 d = ray.direction.normalized; // ensure direction is normalized
        Vector3 sphereCenter = planet.transform.position;
        float radius = 1f; // changed from dragSphereRadius

        Vector3 oc = ray.origin - sphereCenter;
        bool isInsideSphere = (oc.sqrMagnitude < radius * radius);

        // Quadratic terms for sphere intersection
        // Equation: (oc + t*d).sqrMagnitude = radius^2
        // => (oc•oc) + 2*t*(oc•d) + t^2*(d•d) - radius^2 = 0
        // d•d = 1 since d is normalized
        float b = 2f * Vector3.Dot(oc, d);
        float c = oc.sqrMagnitude - (radius * radius);
        float discriminant = b * b - 4f * c;

        // No real intersection
        if (discriminant < 0f)
        {
            return Vector3.zero;
        }

        // Two possible solutions
        float sqrtDisc = Mathf.Sqrt(discriminant);
        float t1 = (-b - sqrtDisc) / 2f;
        float t2 = (-b + sqrtDisc) / 2f;

        // Gather positive solutions only
        float tMin = Mathf.Min(t1, t2);
        float tMax = Mathf.Max(t1, t2);

        // If both are behind camera, no valid intersection
        if (tMax < 0f)
        {
            return Vector3.zero;
        }

        float chosenT;
        if (isInsideSphere)
        {
            // We are inside the sphere, so the smaller 't' is behind us,
            // and the larger 't' is in front of us.
            chosenT = tMax;
        }
        else
        {
            // We are outside the sphere, so the smaller positive t is the front intersection.
            if (tMin < 0f)
            {
                // tMin is behind us, so we must take tMax
                chosenT = tMax;
            }
            else
            {
                // tMin is in front, so that's the intersection we want
                chosenT = tMin;
            }
        }

        // If chosenT is still negative for some reason, no valid intersection
        if (chosenT < 0f)
        {
            return Vector3.zero;
        }

        // Finally compute intersection point
        Vector3 intersectionPoint = ray.origin + d * chosenT;
        return intersectionPoint;
    }

    public void callApplyVelocity()
    {
        ApplyVelocityToPlanet(currentVelocity);
    }

    /// <summary>
    /// Example method to apply velocity to your planet's NBody component.
    /// </summary>
    public void ApplyVelocityToPlanet(Vector3 velocityToApply)
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
        planetNBody = null;
        isVelocitySet = true;

        if (dragLineRenderer != null)
        {
            dragLineRenderer.positionCount = 0;  // Hides the line
                                                 // Or dragLineRenderer.enabled = false; // if you prefer just disabling it
        }

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

        if (velocityDisplayText != null)
        {
            velocityDisplayText.text = "";
            velocityDisplayText.interactable = false;
        }
    }

    private void OnVelocityInputChanged(string inputText)
    {
        // Try to parse the input as a Vector3 (format: "x,y,z")
        Vector3 newVelocity;
        if (TryParseVector3(inputText, out newVelocity))
        {
            currentVelocity = newVelocity; // Update the current velocity
            UpdateLineRenderer();          // Update the line renderer to match new velocity
        }
        else
        {
            Debug.LogWarning("Invalid velocity format. Please use 'x,y,z'.");
        }
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

    private void UpdateLineRenderer()
    {
        if (dragLineRenderer != null && planet != null)
        {
            Vector3 startPos = planet.transform.position;  // Start at planet's center
            float radius = planet.transform.localScale.x * sphereRadiusMultiplier;  // Sphere collider's radius

            // Calculate the direction of the velocity as a normalized vector
            Vector3 velocityDirection = currentVelocity.normalized;

            // Create a ray from the planet's center in the direction of the velocity
            Ray velocityRay = new Ray(startPos, velocityDirection);

            // Get the intersection point with the sphere collider
            Vector3 intersectionPoint = GetFarSideIntersection(velocityRay, startPos, radius);

            if (intersectionPoint != Vector3.zero)
            {
                dragLineRenderer.positionCount = 2;
                dragLineRenderer.SetPosition(0, startPos);        // Start point
                dragLineRenderer.SetPosition(1, intersectionPoint);  // End point at the sphere surface

                velocityDisplayText.interactable = true;
            }
            else
            {
                dragLineRenderer.positionCount = 0;
                velocityDisplayText.interactable = false; // Disable if no valid velocity
            }
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