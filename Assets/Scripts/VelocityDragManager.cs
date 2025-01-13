using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;

/**
* VelocityDragManager handles the user interaction for applying velocity to a selected planet.
* This script allows the user to click and drag to set the velocity vector and apply it to the planet.
**/
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

    [Header("Planet to Apply Velocity To")]
    public GameObject planet;

    public float sphereRadiusMultiplier = 10f;

    [Header("UI Components")]
    public Slider speedSlider;

    [Header("Mass Handling")]
    public float placeholderMass;


    private bool isDragging = false;
    private bool isVelocitySet = false;
    private Vector3 dragStartPos;
    private Vector3 currentVelocity;
    private GameObject dragSphereObject;
    private SphereCollider dragSphereCollider;
    private Vector3 dragDirection = Vector3.zero;
    private float sliderSpeed = 0f;
    private float lastLineUpdateTime = 0f;
    private float lineUpdateInterval = 0.05f;

    [Header("UI Elements")]
    public TextMeshProUGUI apogeeText;
    public TextMeshProUGUI perigeeText;

    /**
    * Initializes the drag manager and sets up components.
    **/
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

        StartCoroutine(FindTrajectoryRendererWithDelay());
    }

    /**
    * Updates the drag process based on user input.
    **/
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

    /**
    * Begins the drag process.
    **/
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

            Material lineMaterial = new Material(Shader.Find("Unlit/Color"));
            lineMaterial.color = Color.red;
            dragLineRenderer.material = lineMaterial;

            velocityDisplayText.interactable = true;
            velocitySpeedSlider.interactable = true;
            setVelocityButton.interactable = true;
        }

        dragDirection = Vector3.zero;
    }

    /**
    * Updates the drag line and velocity during the drag.
    **/
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

    /**
    * Small wait to make sure TrajectoryRenderer is found
    **/
    private IEnumerator FindTrajectoryRendererWithDelay()
    {
        yield return new WaitForSeconds(0.1f);
        trajectoryRenderer = Object.FindFirstObjectByType<TrajectoryRenderer>();

        if (trajectoryRenderer == null)
        {
            Debug.LogError("TrajectoryRenderer not found after delay!");
        }
    }

    /**
    * Gets the intersection point with the far side of the sphere.
    * @param ray - Ray that points to a locaiton inside of dragSphere
    * @param sphereCenter - 3D location of the center of dragSphere
    * @param radius - Radius of the dragSphere
    **/
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

    /**
    * Ends the drag process and applies the velocity.
    **/
    private void EndDrag()
    {
        isDragging = false;
        Debug.Log(currentVelocity);
    }

    /**
    * Updates the slider speed and corresponding velocity.
    * @param value - Current TimeScale value of the sim
    **/
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

    /**
    * Gets the mouse position in 3D space on the sphere surface.
    **/
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

    /**
    * Applies the computed velocity to the planet's NBody component.
    **/
    public void callApplyVelocity()
    {
        ApplyVelocityToPlanet(currentVelocity);
    }

    /**
    * Applies the specified velocity to the planet.
    * @param velocityToApply - Velocity the new object will be set to
    **/
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
            planetNBody.radius = planet.transform.localScale.x * 10f;
        }

        planetNBody.velocity = velocityToApply;
        gravityManager.RegisterBody(planetNBody);
        if (GetComponentInChildren<TrajectoryRenderer>() == null)
        {
            GameObject trajectoryObj = new GameObject($"{gameObject.name}_TrajectoryRenderer");
            trajectoryObj.transform.parent = this.transform;
            trajectoryRenderer = trajectoryObj.AddComponent<TrajectoryRenderer>();
            trajectoryRenderer.apogeeText = this.apogeeText;
            trajectoryRenderer.perigeeText = this.perigeeText;
            trajectoryRenderer.predictionSteps = 1000;
            trajectoryRenderer.predictionDeltaTime = 5f;
            trajectoryRenderer.lineWidth = 3f;
            trajectoryRenderer.lineColor = Color.blue;
            trajectoryRenderer.lineDisableDistance = 50f;

            // Assign this NBody to TrajectoryRenderer
            trajectoryRenderer.SetTrackedBody(planetNBody);
        }


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

        Debug.Log($"Applied velocity {velocityToApply} to {planet.name} via drag.");
        planet = null;
        isVelocitySet = true;

        if (dragLineRenderer != null)
        {
            dragLineRenderer.positionCount = 0;
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
            velocitySpeedSlider.interactable = false;
            velocitySpeedSlider.value = 0f;
            setVelocityButton.interactable = false;

        }
    }

    /**
    * Handles changes in the velocity input field.
    * @param inputText - Velocity input with manual (x,y,z) setting
     */
    private void OnVelocityInputChanged(string inputText)
    {
        Vector3 newVelocity;
        if (TryParseVector3(inputText, out newVelocity))
        {
            currentVelocity = newVelocity;
            UpdateLineRenderer();
        }
        else
        {
            Debug.LogWarning("Invalid velocity format. Please use 'x,y,z'.");
        }
    }

    /**
    * Parses a Vector3 from a string input.
    * @param input - String input from input field 
    * @param result - Vector 3 output from (x,y,z) format
    **/
    private bool TryParseVector3(string input, out Vector3 result)
    {
        result = Vector3.zero;
        string[] parts = input.Split(',');
        if (parts.Length != 3) return false;

        float x, y, z;
        if (!float.TryParse(parts[0], out x) || !float.TryParse(parts[1], out y) || !float.TryParse(parts[2], out z)) return false;

        result = new Vector3(x, y, z);
        return true;
    }

    /**
    * Updates the line renderer to match the current velocity.
    **/
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

    /**
    * Resets the drag manager for a new planet.
    **/
    public void ResetDragManager()
    {
        isVelocitySet = false;
    }
}
