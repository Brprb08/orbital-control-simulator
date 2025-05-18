using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Handles user interaction for applying velocity to a selected planet.
/// Allows the user to click and drag to set the velocity vector and apply it to the planet.
/// </summary>
public class VelocityDragManager : MonoBehaviour
{
    [Header("References")]
    public Camera mainCamera;
    public LineRenderer dragLineRenderer;
    public TMP_InputField velocityDisplayText;
    public Slider velocitySpeedSlider;
    public Button setVelocityButton;
    public GravityManager gravityManager;
    public TrajectoryRenderer trajectoryRenderer;
    private ObjectPlacementManager objectPlacementManager;
    private UIManager uIManager;
    private GameObject dragSphereObject;

    [Header("UI Elements")]
    public Slider speedSlider;

    [Header("Planet to Apply Velocity To")]
    public GameObject planet;
    public float sphereRadiusMultiplier = 10f;

    [Header("Mass Handling")]
    public float placeholderMass;

    [Header("Dragging Elements")]
    private bool isDragging = false;
    private bool isVelocitySet = false;
    private Vector3 dragStartPos;
    private Vector3 currentVelocity;
    private SphereCollider dragSphereCollider;
    private Vector3 dragDirection = Vector3.zero;
    private float sliderSpeed = 0f;
    private float lastLineUpdateTime = 0f;
    private float lineUpdateInterval = 0.05f;

    /// <summary>
    /// Initializes the drag manager and sets up required components and UI bindings.
    /// </summary>
    private void Start()
    {
        if (dragLineRenderer != null)
        {
            dragLineRenderer.positionCount = 0;
        }

        if (speedSlider != null)
        {
            speedSlider.onValueChanged.AddListener(OnSpeedSliderChanged);
        }

        if (velocityDisplayText != null)
        {
            velocityDisplayText.onValueChanged.AddListener(OnVelocityInputChanged);
            velocityDisplayText.interactable = false;
        }

        if (velocitySpeedSlider != null)
        {
            velocitySpeedSlider.interactable = false;
        }

        if (setVelocityButton != null)
        {
            setVelocityButton.interactable = false;
        }

        if (dragSphereObject == null)
        {
            dragSphereObject = new GameObject("DragSphereTemp");
            dragSphereCollider = dragSphereObject.AddComponent<SphereCollider>();
            dragSphereCollider.isTrigger = true;
            dragSphereObject.layer = LayerMask.NameToLayer("DragSphere");
        }

        objectPlacementManager = ObjectPlacementManager.Instance;
        uIManager = UIManager.Instance;

        StartCoroutine(FindTrajectoryRendererWithDelay());
    }

    /// <summary>
    /// Handles mouse input and updates the velocity drag logic.
    /// </summary>
    private void Update()
    {
        if (isVelocitySet)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }
            StartDrag();
        }

        if (isDragging && Input.GetMouseButton(0))
        {
            UpdateDrag();
        }

        if (Input.GetMouseButtonUp(0) && isDragging)
        {
            EndDrag();
        }
    }

    /// <summary>
    /// Begins the drag process to define a velocity direction.
    /// </summary>
    private void StartDrag()
    {
        if (planet == null || mainCamera == null) return;
        isDragging = true;
        dragStartPos = planet.transform.position;

        dragSphereObject.transform.position = planet.transform.position;
        dragSphereObject.transform.rotation = Quaternion.identity;
        dragSphereObject.transform.localScale = Vector3.one;

        float planetScale = planet.transform.localScale.x;
        float sphereRadius = Mathf.Max(1f, planetScale * sphereRadiusMultiplier);
        dragSphereCollider.radius = sphereRadius;

        if (dragLineRenderer != null)
        {
            dragLineRenderer.positionCount = 2;
            dragLineRenderer.SetPosition(0, dragStartPos);
            dragLineRenderer.SetPosition(1, dragStartPos);
            dragLineRenderer.widthMultiplier = 0.25f;
        }
        velocityDisplayText.interactable = true;
        velocitySpeedSlider.interactable = true;
        setVelocityButton.interactable = true;
        Canvas.ForceUpdateCanvases();  // Force UI to update

        dragDirection = Vector3.zero;
    }

    /// <summary>
    /// Continuously updates the drag line and direction while the user is dragging.
    /// </summary>
    private void UpdateDrag()
    {
        if (!isDragging) return;

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

    /// <summary>
    /// Delays trajectory renderer search to ensure scene objects are ready.
    /// </summary>
    private IEnumerator FindTrajectoryRendererWithDelay()
    {
        yield return new WaitForSeconds(0.1f);
        trajectoryRenderer = Object.FindFirstObjectByType<TrajectoryRenderer>();

        if (trajectoryRenderer == null)
        {
            Debug.LogError("TrajectoryRenderer not found after delay!");
        }
    }

    /// <summary>
    /// Calculates the intersection point on the far side of a sphere given a ray.
    /// </summary>
    /// <param name="ray">Ray that points to a location inside the drag sphere.</param>
    /// <param name="sphereCenter">Center of the sphere in world space.</param>
    /// <param name="radius">Radius of the sphere.</param>
    /// <returns>Intersection point on the sphere's surface.</returns>
    private Vector3 GetFarSideIntersection(Ray ray, Vector3 sphereCenter, float radius)
    {
        Vector3 d = ray.direction.normalized;
        Vector3 oc = ray.origin - sphereCenter;

        float b = 2f * Vector3.Dot(oc, d);
        float c = oc.sqrMagnitude - (radius * radius);
        float discriminant = b * b - 4f * c;

        if (discriminant < 0f)
        {
            return sphereCenter + (d * radius);
        }

        float sqrtDisc = Mathf.Sqrt(discriminant);
        float t1 = (-b - sqrtDisc) / 2f;
        float t2 = (-b + sqrtDisc) / 2f;

        float chosenT = (t2 >= 0f) ? t2 : t1;
        if (chosenT < 0f)
        {
            return sphereCenter + (d * radius);
        }

        return ray.origin + d * chosenT;
    }

    /// <summary>
    /// Ends the drag process. Velocity will be applied later via confirmation.
    /// </summary>
    private void EndDrag()
    {
        isDragging = false;
    }

    /// <summary>
    /// Updates the velocity vector and line based on slider speed.
    /// </summary>
    /// <param name="value">Current value of the speed slider.</param>
    public void OnSpeedSliderChanged(float value)
    {
        sliderSpeed = value;
        currentVelocity = dragDirection * sliderSpeed;

        float x = currentVelocity.x;
        float y = currentVelocity.y;
        float z = currentVelocity.z;
        if (velocityDisplayText != null && x != 0 && y != 0 && z != 0)
        {
            velocityDisplayText.onValueChanged.RemoveListener(OnVelocityInputChanged);
            velocityDisplayText.text = $"{currentVelocity.x:F2}, {currentVelocity.y:F2}, {currentVelocity.z:F2}";
            velocityDisplayText.onValueChanged.AddListener(OnVelocityInputChanged);
        }
    }

    /// <summary>
    /// Gets the 3D mouse position projected onto the surface of a unit sphere.
    /// </summary>
    /// <returns>World space position on the sphere, or Vector3.zero if invalid.</returns>
    private Vector3 GetMouseWorldPositionOnSphere()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        Vector3 d = ray.direction.normalized;
        Vector3 sphereCenter = planet.transform.position;
        float radius = 1f;

        Vector3 oc = ray.origin - sphereCenter;
        bool isInsideSphere = (oc.sqrMagnitude < radius * radius);

        float b = 2f * Vector3.Dot(oc, d);
        float c = oc.sqrMagnitude - (radius * radius);
        float discriminant = b * b - 4f * c;

        if (discriminant < 0f)
        {
            return Vector3.zero;
        }

        float sqrtDisc = Mathf.Sqrt(discriminant);
        float t1 = (-b - sqrtDisc) / 2f;
        float t2 = (-b + sqrtDisc) / 2f;

        float tMin = Mathf.Min(t1, t2);
        float tMax = Mathf.Max(t1, t2);

        if (tMax < 0f)
        {
            return Vector3.zero;
        }

        float chosenT = isInsideSphere ? tMax : (tMin < 0f ? tMax : tMin);

        if (chosenT < 0f)
        {
            return Vector3.zero;
        }

        return ray.origin + d * chosenT;
    }

    /// <summary>
    /// Applies the current velocity to the selected planet when confirmed.
    /// </summary>
    public void callApplyVelocity()
    {
        ApplyVelocityToPlanet(currentVelocity);
        EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>
    /// Applies a specified velocity to the selected planet and finalizes its placement.
    /// </summary>
    /// <param name="velocityToApply">The velocity vector to apply to the planet.</param>
    public void ApplyVelocityToPlanet(Vector3 velocityToApply)
    {
        if (planet == null) return;

        NBody planetNBody = planet.GetComponent<NBody>();
        if (planetNBody == null)
        {
            planetNBody = planet.AddComponent<NBody>();
            if (planetNBody == null)
            {
                Debug.LogError($"Failed to add NBody to {planet.name}!");
                return;
            }

            planetNBody.mass = placeholderMass > 0f ? placeholderMass : 400000f;
            // planetNBody.radius = planet.transform.localScale.x;
            // DONT HARDCODE THIS EVENTUALLY
            planetNBody.radius = .002f;
            planetNBody.cameraDistanceRadius = 1f;
        }

        planetNBody.velocity = velocityToApply;
        gravityManager.RegisterBody(planetNBody);

        trajectoryRenderer.SetTrackedBody(planetNBody);
        trajectoryRenderer.orbitIsDirty = true;

        CameraController cameraController = gravityManager.GetComponent<CameraController>();
        if (cameraController != null)
        {
            cameraController.RefreshBodiesList();
            int newIndex = cameraController.Bodies.IndexOf(planetNBody);
            if (newIndex >= 0)
            {
                cameraController.currentIndex = newIndex;
                cameraController.SwitchToRealNBody(planetNBody);
            }
        }

        //Debug.Log($"Applied velocity {velocityToApply} to {planet.name} via drag.");
        planet = null;
        isVelocitySet = true;

        if (dragLineRenderer != null)
        {
            dragLineRenderer.positionCount = 0;
        }

        objectPlacementManager.ResetLastPlacedGameObject();
        uIManager.OnTrackCamPressed();

        if (velocityDisplayText != null)
        {
            velocityDisplayText.text = "";
            velocityDisplayText.interactable = false;
            velocitySpeedSlider.interactable = false;
            velocitySpeedSlider.value = 0f;
            setVelocityButton.interactable = false;

        }
    }

    /// <summary>
    /// Called when the velocity input field is manually edited.
    /// </summary>
    /// <param name="inputText">The input string in "x,y,z" format.</param>
    private void OnVelocityInputChanged(string inputText)
    {
        if (inputText == "")
        {
            return;
        }

        Vector3 newVelocity;
        if (ParsingUtils.Instance.TryParseVector3(inputText, out newVelocity))
        {
            currentVelocity = newVelocity;
            UpdateLineRenderer();
        }
        else
        {
            Debug.LogWarning("Invalid velocity format. Please use 'x,y,z'.");
        }
    }

    /// <summary>
    /// Updates the velocity drag line to match the current velocity vector.
    /// </summary>
    private void UpdateLineRenderer()
    {
        if (dragLineRenderer != null && planet != null)
        {
            Vector3 startPos = planet.transform.position;
            float radius = planet.transform.localScale.x * sphereRadiusMultiplier;
            Vector3 velocityDirection = currentVelocity.normalized;
            Ray velocityRay = new Ray(startPos, velocityDirection);
            Vector3 intersectionPoint = GetFarSideIntersection(velocityRay, startPos, radius);

            if (intersectionPoint != Vector3.zero)
            {
                dragLineRenderer.positionCount = 2;
                dragLineRenderer.SetPosition(0, startPos);
                dragLineRenderer.SetPosition(1, intersectionPoint);
                velocityDisplayText.interactable = true;
            }
            else
            {
                dragLineRenderer.positionCount = 0;
                velocityDisplayText.interactable = false;
            }
        }
    }

    /// <summary>
    /// Resets the drag manager to prepare for a new velocity input session.
    /// </summary>
    public void ResetDragManager()
    {
        isVelocitySet = false;
    }
}
