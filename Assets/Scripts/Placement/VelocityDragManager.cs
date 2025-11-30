using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections;

/// <summary>
/// Handles click-and-drag velocity input for a body, keeps the UI in sync,
/// and drives short/long trajectory previews through the TrajectoryRenderer.
/// </summary>
public class VelocityDragManager : MonoBehaviour
{
    [Header("References - Components")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] public TrajectoryRenderer trajectoryRenderer;
    [SerializeField] private TutorialController tutorialController;

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

    private const float MaxVelocityMagnitude = 5.0f;

    private GameObject dragSphereObject;
    private SphereCollider dragSphereCollider;

    [Header("Preview Settings")]
    [Tooltip("Delay (seconds) after input stops before running the long preview.")]
    [SerializeField] private float longPreviewDelay = 0.2f;
    [SerializeField] private int longPreviewSteps = 2000;
    [SerializeField] private float longPreviewDt = 60f;
    private Coroutine longPreviewCo;

    private SimContext ctx;

    [Header("Runtime Arrow")]
    [SerializeField] private RuntimeArrow dragArrow;
    [SerializeField] private float arrowLength = 7f;
    [SerializeField] private float arrowHeadLen = 0.8f;
    [SerializeField] private float arrowThickness = 0.5f;
    [SerializeField] private float arrowHeadRad = 0.15f;
    [SerializeField] private Color arrowColor = new Color(0.3f, 1f, 1f, 1f);

    public bool HasAppliedVelocity => isVelocitySet;

    [Header("Performance Tuning")]
    [Tooltip("Minimum time (seconds) between quick preview recomputes while dragging.")]
    [SerializeField] private float minPreviewInterval = 0.05f; // 20 Hz
    [Tooltip("Angular change (degrees) required to trigger a new quick preview.")]
    [SerializeField] private float directionAngleThresholdDeg = 0.5f;
    [Tooltip("Speed magnitude delta required to trigger a new quick preview.")]
    [SerializeField] private float speedThreshold = 0.01f;

    private float lastPreviewTime;
    private Vector3 lastPreviewVel;
    private Vector3 lastPreviewDir;

    private Vector3 lastArrowStart, lastArrowEnd;

    /// <summary>
    /// Binds references from the sim context, wires up UI and input handlers,
    /// and sets up the temporary drag sphere and runtime arrow.
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

        dragSphereObject = new GameObject("DragSphereTemp");
        dragSphereCollider = dragSphereObject.AddComponent<SphereCollider>();
        dragSphereCollider.isTrigger = true;
        dragSphereObject.layer = LayerMask.NameToLayer("DragSphere");
        dragSphereObject.SetActive(false);

        EnsureDragArrow();
        dragArrow.Hide();

        // initialize caches
        lastArrowStart = lastArrowEnd = new Vector3(float.NaN, float.NaN, float.NaN);

        // Use NaN as a sentinel so the first change always triggers
        lastPreviewDir = new Vector3(float.NaN, float.NaN, float.NaN);
        lastPreviewVel = new Vector3(float.NaN, float.NaN, float.NaN);
        lastPreviewTime = -999f;
    }

    /// <summary>
    /// Makes sure there is a RuntimeArrow instance and applies basic styling.
    /// </summary>
    private void EnsureDragArrow()
    {
        if (dragArrow == null)
        {
            var go = new GameObject("DragArrow");
            dragArrow = go.AddComponent<RuntimeArrow>();
        }
        dragArrow.SetColor(arrowColor);
        dragArrow.Hide();
    }

    /// <summary>
    /// Handles mouse input for starting/continuing/ending the drag gesture and
    /// keeps the initial "hint" arrow visible when no velocity is set.
    /// </summary>
    private void Update()
    {
        if (!isVelocitySet && planet != null && !dragArrow.gameObject.activeSelf)
        {
            Vector3 start = planet.transform.position;

            var center = ctx.BodyService.CentralBody.transform.position;
            Vector3 dir = (center - start).normalized;

            Vector3 end = start + dir * arrowLength;
            dragDirection = dir;
            ShowArrowCached(start, end);
        }

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
    /// Begins a drag session: enables the drag sphere, shows a default arrow,
    /// and kicks off an initial preview.
    /// </summary>
    private void StartDrag()
    {
        if (planet == null || mainCamera == null) return;

        CancelLongPreviewDebounce();
        isDragging = true;

        if (tutorialController.inTutorialMode)
            tutorialController.hasClickAndDrag = true;

        dragSphereObject.transform.SetPositionAndRotation(planet.transform.position, Quaternion.identity);
        dragSphereObject.transform.localScale = Vector3.one;
        dragSphereCollider.radius = Mathf.Max(1f, planet.transform.localScale.x * sphereRadiusMultiplier);
        dragSphereObject.SetActive(true);

        ShowArrowCached(planet.transform.position, planet.transform.position + Vector3.forward * arrowLength);
        SetUIInteractable(true);
        dragDirection = Vector3.forward;

        // force a preview immediately at drag start
        lastPreviewTime = -999f;
        TryQuickPreview();
    }

    /// <summary>
    /// Updates drag direction based on mouse position on a virtual sphere
    /// and pushes that into the arrow and quick preview.
    /// </summary>
    private void UpdateDrag()
    {
        Vector3 sphereCenter = planet.transform.position;
        float radius = planet.transform.localScale.x * sphereRadiusMultiplier;
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        Vector3 intersection = GetFarSideIntersection(ray, sphereCenter, radius);

        dragDirection = (intersection - sphereCenter).normalized;
        currentVelocity = dragDirection * sliderSpeed;

        Vector3 arrowEnd = sphereCenter + dragDirection * arrowLength;
        ShowArrowCached(sphereCenter, arrowEnd);

        // quick preview (no long preview scheduling while dragging)
        TryQuickPreview();
    }

    /// <summary>
    /// Ends the drag gesture but keeps the ghost arrow around
    /// and schedules a long preview after a short idle.
    /// </summary>
    private void EndDrag()
    {
        isDragging = false;
        dragSphereObject.SetActive(false);
        // keep arrow visible to indicate direction
        ScheduleLongPreviewForGhost();
    }

    /// <summary>
    /// Called when the speed slider changes. Updates current velocity,
    /// UI display, arrow, and triggers a new preview.
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

        // quick preview and cached arrow update
        TryQuickPreview();
        UpdateArrowFromCurrent();

        if (!isDragging)
        {
            CancelLongPreviewDebounce();
            ScheduleLongPreviewForGhost();
        }
    }

    /// <summary>
    /// Formats velocity for the text field. Note: values are scaled into km/s-ish.
    /// </summary>
    private string FormatVelocityForUI(Vector3 v)
    {
        return $"{(v.x * 10f):F2}, {(v.z * 10f):F2}, {(v.y * 10f):F2}";
    }

    /// <summary>
    /// Parses manual velocity input and refreshes the ghost arrow and preview.
    /// </summary>
    private void OnVelocityInputChanged(string inputText)
    {
        if (string.IsNullOrWhiteSpace(inputText)) return;

        if (ParsingUtils.TryParseVector3(inputText, out var newVelocity))
        {
            currentVelocity = newVelocity;
            setVelocityButton.interactable = true;
            UpdateArrowFromCurrent();
            // preview immediately when user types a valid vector
            TryQuickPreview();
        }

        if (!isDragging)
        {
            CancelLongPreviewDebounce();
            ScheduleLongPreviewForGhost();
        }
    }

    /// <summary>
    /// Button callback: applies the currently staged velocity to the planet.
    /// </summary>
    public void callApplyVelocity()
    {
        trajectoryRenderer?.ClearPreview();
        ApplyVelocityToPlanet(currentVelocity);
        EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>
    /// Makes sure the planet has an NBody (and attitude) and then
    /// actually applies the velocity, registers it, and hands it off to tracking.
    /// </summary>
    public void ApplyVelocityToPlanet(Vector3 velocityToApply)
    {
        if (planet == null) return;

        var nbody = planet.GetComponent<NBody>();
        if (nbody == null)
        {
            nbody = planet.AddComponent<NBody>();
            nbody.mass = (placeholderMass > 0f) ? placeholderMass : 400000f;
            nbody.trueMass = (placeholderMass > 0f) ? (double)placeholderMass : 400000d;
            nbody.radius = 0.0002f;
            nbody.cameraDistanceRadius = 1f;
            nbody.isCentralBody = false;
            nbody.Initialize(ctx);
        }

        // Ensure the craft has an attitude component
        var attitude = planet.GetComponent<AttitudeController>();
        if (attitude == null)
        {
            attitude = planet.AddComponent<AttitudeController>();
            attitude.mode = AttitudeController.PointingMode.Velocity; // Prograde pointing
            attitude.snapAttitude = false;                            // smooth slew
            attitude.maxSlewRateDegPerSec = 60f;
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

        dragArrow.Hide();
        objectPlacementManager.ResetLastPlacedGameObject();
        uIManager?.OnTrackCamPressed();

        if (velocityDisplayText != null) { velocityDisplayText.text = ""; velocityDisplayText.interactable = false; }
        if (speedSlider != null) { speedSlider.interactable = false; speedSlider.value = 0f; }
        if (setVelocityButton != null) setVelocityButton.interactable = false;
    }

    /// <summary>
    /// Returns the intersection on the far side of a sphere along the given ray.
    /// Used to project mouse drags onto a virtual sphere around the planet.
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
    /// Rebuilds the arrow from the current direction and planet position.
    /// </summary>
    private void UpdateArrowFromCurrent()
    {
        if (planet == null || dragArrow == null) return;

        Vector3 startPos = planet.transform.position;
        Vector3 dir = (dragDirection.sqrMagnitude > 1e-6f) ? dragDirection : Vector3.forward;
        Vector3 end = startPos + dir * arrowLength;
        ShowArrowCached(startPos, end);
    }

    /// <summary>
    /// Enables or disables the velocity UI controls as a group.
    /// </summary>
    private void SetUIInteractable(bool enable)
    {
        if (velocityDisplayText != null) velocityDisplayText.interactable = enable;
        if (speedSlider != null) speedSlider.interactable = enable;
        if (setVelocityButton != null) setVelocityButton.interactable = enable;
    }

    /// <summary>
    /// Returns true if the drag direction or speed has changed enough
    /// to justify another quick preview.
    /// </summary>
    private bool ChangedEnough()
    {
        if (dragDirection == Vector3.zero) return false;

        bool firstDir = float.IsNaN(lastPreviewDir.x);
        bool firstVel = float.IsNaN(lastPreviewVel.x);
        if (firstDir || firstVel) return true;

        bool dirChanged = Vector3.Angle(lastPreviewDir, dragDirection) > directionAngleThresholdDeg;
        bool spdChanged = Mathf.Abs(currentVelocity.magnitude - lastPreviewVel.magnitude) > speedThreshold;

        return dirChanged || spdChanged;
    }

    /// <summary>
    /// Schedules a short, cheap preview trajectory, throttled by time and input deltas.
    /// </summary>
    private void TryQuickPreview()
    {
        if (trajectoryRenderer == null || planet == null) return;
        var svc = ctx?.BodyService;
        if (svc == null || svc.CentralBody == null) return;

        if ((Time.unscaledTime - lastPreviewTime) < minPreviewInterval) return;
        if (!ChangedEnough()) return;

        float massForPreview = (placeholderMass > 0f) ? placeholderMass : 400000f;
        trajectoryRenderer.QuickPreviewFromState(planet.transform.position, currentVelocity, massForPreview);

        lastPreviewTime = Time.unscaledTime;
        lastPreviewDir = dragDirection;
        lastPreviewVel = currentVelocity;
    }

    /// <summary>
    /// Shows the arrow only when its endpoints have actually changed.
    /// </summary>
    private void ShowArrowCached(Vector3 start, Vector3 end)
    {
        if ((start - lastArrowStart).sqrMagnitude < 1e-6f &&
            (end - lastArrowEnd).sqrMagnitude < 1e-6f) return;

        dragArrow.Show(start, end, arrowThickness, arrowHeadLen, arrowHeadRad);
        lastArrowStart = start;
        lastArrowEnd = end;
    }

    /// <summary>
    /// Starts a timer that will trigger a longer, cheaper trajectory preview
    /// after the user stops interacting.
    /// </summary>
    private void ScheduleLongPreviewForGhost()
    {
        if (planet == null || trajectoryRenderer == null) return;
        if (longPreviewCo != null) StopCoroutine(longPreviewCo);
        longPreviewCo = StartCoroutine(LongPreviewAfterIdle());
    }

    /// <summary>
    /// Waits for a small idle window, then kicks off a longer preview
    /// using the current velocity as a ghost orbit.
    /// </summary>
    private IEnumerator LongPreviewAfterIdle()
    {
        float t = 0f;
        while (t < longPreviewDelay)
        {
            // If interacting again, cancel this long preview
            if (isDragging) yield break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (planet == null) yield break;

        float massForPreview = (placeholderMass > 0f) ? placeholderMass : 400000f;
        trajectoryRenderer.QuickPreviewOnceLong(
            planet.transform.position,
            currentVelocity,
            massForPreview,
            longPreviewSteps,
            longPreviewDt,
            singleOrbit: false);
        longPreviewCo = null;
    }

    /// <summary>
    /// Cancels any pending long-preview debounce coroutine.
    /// </summary>
    private void CancelLongPreviewDebounce()
    {
        if (longPreviewCo != null) StopCoroutine(longPreviewCo);
        longPreviewCo = null;
    }

    /// <summary>
    /// Clears drag-related state, hides visuals, and throws away any ghost previews.
    /// </summary>
    public void ClearManualArtifacts()
    {
        CancelLongPreviewDebounce();
        isDragging = false;
        isVelocitySet = false;
        dragArrow.Hide();

        if (velocityDisplayText != null) { velocityDisplayText.text = ""; velocityDisplayText.interactable = false; }
        if (speedSlider != null) { speedSlider.interactable = false; speedSlider.value = 0f; }
        if (setVelocityButton != null) setVelocityButton.interactable = false;

        if (dragSphereObject != null) dragSphereObject.SetActive(false);
        planet = null;

        trajectoryRenderer?.ClearPreview();
        trajectoryRenderer?.ClearPreManeuverOrbit();
    }

    /// <summary>
    /// Resets high-level state so the drag flow can run again for a new body.
    /// </summary>
    public void ResetDragManager()
    {
        isVelocitySet = false;
        if (velocityDisplayText != null) velocityDisplayText.interactable = true;
        trajectoryRenderer?.ClearPreview();
    }
}
