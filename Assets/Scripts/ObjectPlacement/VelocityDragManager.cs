using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections;

public class VelocityDragManager : MonoBehaviour
{
    [Header("References - Components")]
    public Camera mainCamera;
    public LineRenderer dragLineRenderer;
    public GravityManager gravityManager;
    public TrajectoryRenderer trajectoryRenderer;
    public CameraController cameraController;
    private ICameraTracker cameraTracker;
    public TutorialController tutorialController;
    private BodyService bodyService;

    [Header("References - UI")]
    public TMP_InputField velocityDisplayText;
    public Slider speedSlider;           // preferred
    public Button setVelocityButton;
    public TextMeshProUGUI feedbackText;

    [Header("References - Scripts")]
    private ObjectPlacementManager objectPlacementManager;
    private UIManager uIManager;

    [Header("Planet to Apply Velocity To")]
    public GameObject planet;
    public float sphereRadiusMultiplier = 10f;

    [Header("Mass Handling")]
    public float placeholderMass;

    private bool isDragging;
    private bool isVelocitySet;
    private Vector3 currentVelocity;
    private Vector3 dragDirection = Vector3.zero;
    private float sliderSpeed;
    private float lastLineUpdateTime;
    [SerializeField] private float lineUpdateInterval = 0.05f;

    private const float MaxVelocityMagnitude = 5.0f;

    private GameObject dragSphereObject;
    private SphereCollider dragSphereCollider;

    [SerializeField] private float longPreviewDelay = 0.6f;
    [SerializeField] private int longPreviewSteps = 3000;
    [SerializeField] private float longPreviewDt = 60f;

    private Coroutine longPreviewCo;
    private int previewGeneration;

    private SimContext ctx;

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

        // Use either slider; drive both through the same handler if both are assigned
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
        // Prepare drag sphere
        dragSphereObject.transform.SetPositionAndRotation(planet.transform.position, Quaternion.identity);
        dragSphereObject.transform.localScale = Vector3.one;
        dragSphereCollider.radius = Mathf.Max(1f, planet.transform.localScale.x * sphereRadiusMultiplier);
        dragSphereObject.SetActive(true);

        // Init line
        if (dragLineRenderer != null)
        {
            dragLineRenderer.positionCount = 2;
            dragLineRenderer.SetPosition(0, planet.transform.position);
            dragLineRenderer.SetPosition(1, planet.transform.position);
            dragLineRenderer.widthMultiplier = 0.25f;
        }

        // Enable UI
        SetUIInteractable(true);
        dragDirection = Vector3.zero;
    }

    private void UpdateDrag()
    {
        if (Time.time - lastLineUpdateTime < lineUpdateInterval) return;
        lastLineUpdateTime = Time.time;

        Vector3 sphereCenter = planet.transform.position;
        float radius = planet.transform.localScale.x * sphereRadiusMultiplier;
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        Vector3 intersection = GetFarSideIntersection(ray, sphereCenter, radius);

        if (intersection == Vector3.zero || dragLineRenderer == null) return;

        dragLineRenderer.SetPosition(1, intersection);
        dragDirection = (intersection - sphereCenter).normalized;
        currentVelocity = dragDirection * sliderSpeed;

        // live short preview
        if (trajectoryRenderer != null)
        {
            float massForPreview = (placeholderMass > 0f) ? placeholderMass : 400000f;
            trajectoryRenderer.QuickPreviewFromState(planet.transform.position, currentVelocity, massForPreview);
        }

        // queue longer pass
        ScheduleLongPreviewForGhost();
    }

    private void EndDrag()
    {
        isDragging = false;
        dragSphereObject.SetActive(false);
        ScheduleLongPreviewForGhost();
    }

    public void OnSpeedSliderChanged(float value)
    {
        sliderSpeed = value;
        currentVelocity = dragDirection * sliderSpeed;

        if (tutorialController.inTutorialMode)
        {
            tutorialController.hasAddVelocity = true;
        }
        tutorialController.hasAddVelocity = true;

        if (velocityDisplayText != null && currentVelocity != Vector3.zero)
        {
            velocityDisplayText.onValueChanged.RemoveListener(OnVelocityInputChanged);
            // display in “real” coordinates (x,z,y) * 10
            velocityDisplayText.text = $"{(currentVelocity.x * 10f):F2}, {(currentVelocity.z * 10f):F2}, {(currentVelocity.y * 10f):F2}";
            velocityDisplayText.onValueChanged.AddListener(OnVelocityInputChanged);
        }

        // live update + debounce long pass
        if (trajectoryRenderer != null && planet != null)
        {
            float massForPreview = (placeholderMass > 0f) ? placeholderMass : 400000f;
            trajectoryRenderer.QuickPreviewFromState(planet.transform.position, currentVelocity, massForPreview);
        }
        ScheduleLongPreviewForGhost();
    }

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

    public void callApplyVelocity()
    {
        trajectoryRenderer?.ClearPreview();
        ApplyVelocityToPlanet(currentVelocity);
        EventSystem.current.SetSelectedGameObject(null);
    }

    public void ApplyVelocityToPlanet(Vector3 velocityToApply)
    {
        if (planet == null) return;

        // simple clamp
        if (Mathf.Abs(velocityToApply.x) > MaxVelocityMagnitude ||
            Mathf.Abs(velocityToApply.y) > MaxVelocityMagnitude ||
            Mathf.Abs(velocityToApply.z) > MaxVelocityMagnitude)
        {
            var msg = $"Max velocity is {MaxVelocityMagnitude} (20 km/s)";
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
        {
            tutorialController.hasSetVelocity = true;
        }

        nbody.velocity = velocityToApply;
        bodyService.Register(nbody);

        (cameraTracker ?? ctx.CameraTracker)?.TrackBody(nbody);

        planet = null;
        isVelocitySet = true;

        if (dragLineRenderer != null) dragLineRenderer.positionCount = 0;

        objectPlacementManager.ResetLastPlacedGameObject();
        uIManager.OnTrackCamPressed();

        // reset UI
        if (velocityDisplayText != null)
        {
            velocityDisplayText.text = "";
            velocityDisplayText.interactable = false;
        }
        if (speedSlider != null) { speedSlider.interactable = false; speedSlider.value = 0f; }
        if (setVelocityButton != null) setVelocityButton.interactable = false;
    }

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

    private void SetUIInteractable(bool enable)
    {
        if (velocityDisplayText != null) velocityDisplayText.interactable = enable;
        if (speedSlider != null) speedSlider.interactable = enable;
        if (setVelocityButton != null) setVelocityButton.interactable = enable;
    }

    private void ScheduleLongPreviewForGhost()
    {
        if (planet == null || trajectoryRenderer == null) return;

        if (longPreviewCo != null) StopCoroutine(longPreviewCo);
        int thisGen = ++previewGeneration;
        longPreviewCo = StartCoroutine(LongPreviewAfterIdle_Ghost(thisGen));
    }

    private IEnumerator LongPreviewAfterIdle_Ghost(int gen)
    {
        yield return new WaitForSeconds(longPreviewDelay);
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

    private void CancelLongPreviewDebounce()
    {
        if (longPreviewCo != null) StopCoroutine(longPreviewCo);
        longPreviewCo = null;
        previewGeneration++; // invalidate pending runs
    }

    public void ClearManualArtifacts()
    {
        CancelLongPreviewDebounce();

        isDragging = false;
        isVelocitySet = false;

        if (dragLineRenderer != null) dragLineRenderer.positionCount = 0;

        if (velocityDisplayText != null) { velocityDisplayText.text = ""; velocityDisplayText.interactable = false; }
        if (speedSlider != null) { speedSlider.interactable = false; speedSlider.value = 0f; }
        if (setVelocityButton != null) setVelocityButton.interactable = false;

        // ensure drag sphere/collider are disabled
        if (dragSphereObject != null) dragSphereObject.SetActive(false);

        planet = null;

        trajectoryRenderer?.ClearPreview();
        trajectoryRenderer?.ClearPreManeuverLine();
    }



    public void ResetDragManager()
    {
        isVelocitySet = false;
        if (velocityDisplayText != null) velocityDisplayText.interactable = true;
        trajectoryRenderer?.ClearPreview();
    }
}
