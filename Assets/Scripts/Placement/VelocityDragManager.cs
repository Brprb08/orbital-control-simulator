using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections;

/// <summary>
/// Lets the user set an initial velocity for a placed body by click-and-dragging from the body,
/// with live and deferred trajectory previews and synchronized UI updates.
/// </summary>
public class VelocityDragManager : MonoBehaviour
{
    [Header("References - Components")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] public LineRenderer dragLineRenderer;
    [SerializeField] public TrajectoryRenderer trajectoryRenderer;
    [SerializeField] private TutorialController tutorialController;

    // Set at runtime
    private ICameraTracker cameraTracker;
    private BodyService bodyService;
    private UIManager uIManager;
    private ObjectPlacementManager objectPlacementManager;

    [Header("References - UI")]
    [SerializeField] private TMP_InputField velocityDisplayText;
    [SerializeField] private Slider speedSlider;
    [SerializeField] private Button setVelocityButton;
    [SerializeField] private TextMeshProUGUI feedbackText;

    [Header("Planet to Apply Velocity To")]
    [SerializeField] public GameObject planet;
    [SerializeField] private float sphereRadiusMultiplier = 10f;

    [Header("Mass Handling")]
    [SerializeField] public float placeholderMass;

    private bool isDragging;
    private bool isVelocitySet;
    private Vector3 currentVelocity;
    private Vector3 dragDirection = Vector3.zero;
    private float sliderSpeed;
    private float lastLineUpdateTime;
    [SerializeField] private float lineUpdateInterval = 0.05f;

    private const float MaxVelocityMagnitude = 5.0f; // TODO: confirm units vs message

    private GameObject dragSphereObject;
    private SphereCollider dragSphereCollider;

    [SerializeField] private float longPreviewDelay = 0.6f;
    [SerializeField] private int longPreviewSteps = 3000;
    [SerializeField] private float longPreviewDt = 60f;
    private Coroutine longPreviewCo;
    private int previewGeneration;

    private SimContext ctx;

    /// <summary>
    /// Indicates whether a velocity has been applied to the current body.
    /// </summary>
    public bool HasAppliedVelocity => isVelocitySet;

    /// <summary>
    /// Injects dependencies, wires UI events, and prepares temporary drag helpers.
    /// Call once after creating the manager.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        uIManager = ctx.UIManager;
        trajectoryRenderer = ctx.TrajectoryRenderer;
        objectPlacementManager = ctx.ObjectPlacementManager;
        cameraTracker = ctx.CameraTracker;
        tutorialController = ctx.TutorialController;
        bodyService = ctx.BodyService;

        if (dragLineRenderer) dragLineRenderer.positionCount = 0;

        if (speedSlider != null)
        {
            speedSlider.onValueChanged.AddListener(OnSpeedSliderChanged);
            speedSlider.interactable = false;
        }

        if (velocityDisplayText != null)
        {
            velocityDisplayText.onValueChanged.AddListener(OnVelocityInputChanged);
            velocityDisplayText.interactable = false;
        }

        if (setVelocityButton != null) setVelocityButton.interactable = false;

        // Drag sphere for ray/surface math
        dragSphereObject = new GameObject("DragSphereTemp");
        dragSphereCollider = dragSphereObject.AddComponent<SphereCollider>();
        dragSphereCollider.isTrigger = true;
        dragSphereObject.layer = LayerMask.NameToLayer("DragSphere");
        dragSphereObject.SetActive(false);
    }

    /// <summary>
    /// Handles mouse input for drag gestures and dispatches drag lifecycle.
    /// </summary>
    private void Update()
    {
        if (isVelocitySet) return;

        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current.IsPointerOverGameObject()) return;
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
    /// Begins a drag from the target body, initializes helpers, and enables UI.
    /// </summary>
    private void StartDrag()
    {
        if (planet == null || mainCamera == null) return;

        speedSlider.interactable = false;

        CancelLongPreviewDebounce();

        isDragging = true;
        if (tutorialController.inTutorialMode)
        {
            tutorialController.hasClickAndDrag = true;
        }

        dragSphereObject.transform.SetPositionAndRotation(planet.transform.position, Quaternion.identity);
        dragSphereObject.transform.localScale = Vector3.one;
        dragSphereCollider.radius = Mathf.Max(1f, planet.transform.localScale.x * sphereRadiusMultiplier);
        dragSphereObject.SetActive(true);

        if (dragLineRenderer != null)
        {
            dragLineRenderer.positionCount = 2;
            dragLineRenderer.SetPosition(0, planet.transform.position);
            dragLineRenderer.SetPosition(1, planet.transform.position);
            dragLineRenderer.widthMultiplier = 0.25f;
        }

        SetUIInteractable(true);
        dragDirection = Vector3.zero;
    }

    /// <summary>
    /// Updates the drag vector from mouse position, refreshes the preview line, and triggers previews.
    /// </summary>
    private void UpdateDrag()
    {
        if (Time.time - lastLineUpdateTime < lineUpdateInterval) return;
        lastLineUpdateTime = Time.time;

        Vector3 sphereCenter = planet.transform.position;
        float radius = planet.transform.localScale.x * sphereRadiusMultiplier;
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        Vector3 intersection = GetFarSideIntersection(ray, sphereCenter, radius);

        if (dragLineRenderer == null) return;

        dragLineRenderer.SetPosition(1, intersection);
        dragDirection = (intersection - sphereCenter).normalized;
        currentVelocity = dragDirection * sliderSpeed;

        if (trajectoryRenderer != null)
        {
            float massForPreview = (placeholderMass > 0f) ? placeholderMass : 400000f;
            trajectoryRenderer.QuickPreviewFromState(planet.transform.position, currentVelocity, massForPreview);
        }

        ScheduleLongPreviewForGhost();
    }

    /// <summary>
    /// Ends the drag interaction and schedules a longer preview.
    /// </summary>
    private void EndDrag()
    {
        isDragging = false;
        dragSphereObject.SetActive(false);
        ScheduleLongPreviewForGhost();
    }

    /// <summary>
    /// Updates the speed from the slider, syncs text display, and refreshes previews.
    /// </summary>
    public void OnSpeedSliderChanged(float value)
    {
        sliderSpeed = value;
        currentVelocity = dragDirection * sliderSpeed;

        if (tutorialController.inTutorialMode)
            tutorialController.hasAddVelocity = true;

        if (velocityDisplayText != null && currentVelocity != Vector3.zero)
        {
            velocityDisplayText.onValueChanged.RemoveListener(OnVelocityInputChanged);
            velocityDisplayText.text = FormatVelocityForUI(currentVelocity);
            velocityDisplayText.onValueChanged.AddListener(OnVelocityInputChanged);
        }

        if (trajectoryRenderer != null && planet != null)
        {
            float massForPreview = (placeholderMass > 0f) ? placeholderMass : 400000f;
            trajectoryRenderer.QuickPreviewFromState(planet.transform.position, currentVelocity, massForPreview);
        }
        ScheduleLongPreviewForGhost();
    }

    /// <summary>
    /// Formats a velocity vector for display in the UI.
    /// </summary>
    private string FormatVelocityForUI(Vector3 v)
    {
        return $"{(v.x * 10f):F2}, {(v.z * 10f):F2}, {(v.y * 10f):F2}";
    }

    /// <summary>
    /// Parses velocity text input and updates the preview when valid.
    /// </summary>
    private void OnVelocityInputChanged(string inputText)
    {
        if (string.IsNullOrWhiteSpace(inputText)) return;

        if (ParsingUtils.TryParseVector3(inputText, out var newVelocity))
        {
            currentVelocity = newVelocity;
            setVelocityButton.interactable = true;
            UpdateLineRenderer();
            ScheduleLongPreviewForGhost();
        }
        else
        {
            Debug.LogWarning("Invalid velocity format. Expected 'x,y,z'.");
        }
    }

    /// <summary>
    /// Applies the current velocity from UI, clears preview, and deselects UI focus.
    /// </summary>
    public void callApplyVelocity()
    {
        trajectoryRenderer?.ClearPreview();
        ApplyVelocityToPlanet(currentVelocity);
        EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>
    /// Applies a velocity to the target body, creating an NBody if needed, registers it,
    /// requests a full trajectory update, and resets related UI/state.
    /// </summary>
    public void ApplyVelocityToPlanet(Vector3 velocityToApply)
    {
        if (planet == null) return;

        if (Mathf.Abs(velocityToApply.x) > MaxVelocityMagnitude ||
            Mathf.Abs(velocityToApply.y) > MaxVelocityMagnitude ||
            Mathf.Abs(velocityToApply.z) > MaxVelocityMagnitude)
        {
            var msg = $"Max velocity component is {MaxVelocityMagnitude} (check units)";
            Debug.LogWarning($"Velocity too high ({velocityToApply.magnitude:F2}). {msg}");
            if (feedbackText != null) feedbackText.text = msg;
            return;
        }

        var nbody = planet.GetComponent<NBody>();
        if (nbody == null)
        {
            nbody = planet.AddComponent<NBody>();
            nbody.mass = (placeholderMass > 0f) ? placeholderMass : 400000f;
            nbody.trueMass = (placeholderMass > 0f) ? (double)placeholderMass : 400000d;
            nbody.radius = .002f;
            nbody.cameraDistanceRadius = 1f;
            nbody.Initialize(ctx);
        }

        if (tutorialController.inTutorialMode)
            tutorialController.hasSetVelocity = true;

        nbody.velocity = velocityToApply;
        bodyService.Register(nbody);

        CancelLongPreviewDebounce();
        (cameraTracker ?? ctx.CameraTracker)?.TrackBody(nbody);
        trajectoryRenderer.RequestFullOrbitPass();
        planet = null;
        isVelocitySet = true;

        if (dragLineRenderer != null) dragLineRenderer.positionCount = 0;

        objectPlacementManager.ResetLastPlacedGameObject();
        uIManager?.OnTrackCamPressed();

        if (velocityDisplayText != null) { velocityDisplayText.text = ""; velocityDisplayText.interactable = false; }
        if (speedSlider != null) { speedSlider.interactable = false; speedSlider.value = 0f; }
        if (setVelocityButton != null) setVelocityButton.interactable = false;
    }

    /// <summary>
    /// Computes the ray–sphere far-side intersection point; falls back to the ray direction at radius if no hit.
    /// </summary>
    private Vector3 GetFarSideIntersection(Ray ray, Vector3 sphereCenter, float radius)
    {
        Vector3 d = ray.direction.normalized;
        Vector3 oc = ray.origin - sphereCenter;

        float b = 2f * Vector3.Dot(oc, d);
        float c = oc.sqrMagnitude - (radius * radius);
        float disc = b * b - 4f * c;

        if (disc < 0f) return sphereCenter + (d * radius);

        float sqrtDisc = Mathf.Sqrt(disc);
        float t1 = (-b - sqrtDisc) / 2f;
        float t2 = (-b + sqrtDisc) / 2f;

        float chosenT = (t2 >= 0f) ? t2 : t1;
        if (chosenT < 0f) return sphereCenter + (d * radius);

        return ray.origin + d * chosenT;
    }

    /// <summary>
    /// Recomputes the preview line from the body outward along the current velocity direction.
    /// </summary>
    private void UpdateLineRenderer()
    {
        if (dragLineRenderer == null || planet == null) return;

        Vector3 startPos = planet.transform.position;
        float radius = planet.transform.localScale.x * sphereRadiusMultiplier;
        Vector3 dir = currentVelocity.sqrMagnitude > 0f ? currentVelocity.normalized : Vector3.forward;

        Ray r = new Ray(startPos, dir);
        Vector3 intersection = GetFarSideIntersection(r, startPos, radius);

        if (intersection != Vector3.zero)
        {
            dragLineRenderer.positionCount = 2;
            dragLineRenderer.SetPosition(0, startPos);
            dragLineRenderer.SetPosition(1, intersection);
            if (velocityDisplayText != null) velocityDisplayText.interactable = true;
        }
        else
        {
            dragLineRenderer.positionCount = 0;
            if (velocityDisplayText != null) velocityDisplayText.interactable = false;
        }
    }

    /// <summary>
    /// Enables or disables the velocity editing UI controls.
    /// </summary>
    private void SetUIInteractable(bool enable)
    {
        if (velocityDisplayText != null) velocityDisplayText.interactable = enable;
        if (speedSlider != null) speedSlider.interactable = enable;
        if (setVelocityButton != null) setVelocityButton.interactable = enable;
    }

    /// <summary>
    /// Debounces and schedules a longer, more detailed trajectory preview for the current ghost body.
    /// </summary>
    private void ScheduleLongPreviewForGhost()
    {
        if (planet == null || trajectoryRenderer == null) return;

        if (longPreviewCo != null) StopCoroutine(longPreviewCo);
        int thisGen = ++previewGeneration;
        longPreviewCo = StartCoroutine(LongPreviewAfterIdle_Ghost(thisGen));
    }

    /// <summary>
    /// Waits for idle time, then runs a longer single-shot preview if still applicable.
    /// </summary>
    private IEnumerator LongPreviewAfterIdle_Ghost(int gen)
    {
        float t = 0f;
        while (t < longPreviewDelay)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (gen != previewGeneration || planet == null) yield break;

        float massForPreview = (placeholderMass > 0f) ? placeholderMass : 400000f;
        trajectoryRenderer.QuickPreviewOnceLong(
            planet.transform.position,
            currentVelocity,
            massForPreview,
            longPreviewSteps,
            longPreviewDt
        );
        longPreviewCo = null;
    }

    /// <summary>
    /// Cancels any pending long preview and invalidates queued generations.
    /// </summary>
    private void CancelLongPreviewDebounce()
    {
        if (longPreviewCo != null) StopCoroutine(longPreviewCo);
        longPreviewCo = null;
        previewGeneration++;
    }

    /// <summary>
    /// Clears previews, UI states, and temporary drag artifacts without changing simulation bodies.
    /// </summary>
    public void ClearManualArtifacts()
    {
        CancelLongPreviewDebounce();

        isDragging = false;
        isVelocitySet = false;

        if (dragLineRenderer != null) dragLineRenderer.positionCount = 0;

        if (velocityDisplayText != null) { velocityDisplayText.text = ""; velocityDisplayText.interactable = false; }
        if (speedSlider != null) { speedSlider.interactable = false; speedSlider.value = 0f; }
        if (setVelocityButton != null) setVelocityButton.interactable = false;

        if (dragSphereObject != null) dragSphereObject.SetActive(false);

        planet = null;

        trajectoryRenderer?.ClearPreview();
        trajectoryRenderer?.ClearPreManeuverLine();
    }

    /// <summary>
    /// Resets internal flags and re-enables UI editing after a previous apply.
    /// </summary>
    public void ResetDragManager()
    {
        isVelocitySet = false;
        if (velocityDisplayText != null) velocityDisplayText.interactable = true;
        trajectoryRenderer?.ClearPreview();
    }
}
