using UnityEngine;
using UnityEngine.EventSystems;

public class UIRoot : MonoBehaviour
{
    [SerializeField] private UIReferences refs;
    [SerializeField] private NBodyVectorOverlayController vectorOverlayController;

    private SimContext ctx;
    private ICameraTracker cameraTracker;
    private ObjectPlacementManager objectPlacementManager;
    private TutorialController tutorialController;

    private TimeUI timeUI;
    private TrajectoryUI trajectoryUI;
    private InstructionsUIController instructionsUI;
    private VectorOverlayUIController vectorUI;
    private TutorialUIController tutorialUI;
    private PlacementUIController placementUI;
    private FlightUIController flightUI;
    private CameraModeUIController cameraModeUI;

    public TimeUI TimeUI => timeUI;
    public TrajectoryUI TrajectoryUI => trajectoryUI;
    public InstructionsUIController InstructionsUI => instructionsUI;
    public VectorOverlayUIController VectorUI => vectorUI;
    public TutorialUIController TutorialUI => tutorialUI;
    public PlacementUIController PlacementUI => placementUI;
    public FlightUIController FlightUI => flightUI;
    public CameraModeUIController CameraModeUI => cameraModeUI;

    private bool initialized;

    public void Initialize(SimContext ctx)
    {
        if (initialized) return;
        initialized = true;

        this.ctx = ctx;
        cameraTracker = ctx.CameraTracker;
        objectPlacementManager = ctx.ObjectPlacementManager;
        tutorialController = ctx.TutorialController;

        timeUI = new TimeUI(refs);

        trajectoryUI = new TrajectoryUI(refs);
        trajectoryUI.Initialize();

        instructionsUI = new InstructionsUIController(refs, initialVisible: false);
        instructionsUI.Initialize();

        vectorUI = new VectorOverlayUIController(refs, vectorOverlayController);
        vectorUI.Initialize();

        tutorialUI = new TutorialUIController(refs, tutorialController);

        placementUI = new PlacementUIController(refs, objectPlacementManager);
        placementUI.Initialize();

        flightUI = new FlightUIController(refs);
        flightUI.Initialize();

        cameraModeUI = new CameraModeUIController(refs);

        BindButtons();
        BindCameraEvents();
        RefreshAll();
    }

    private void OnDestroy()
    {
        UnbindCameraEvents();
        UnbindButtons();

        timeUI?.Dispose();
        trajectoryUI?.Dispose();
    }

    private void BindButtons()
    {
        if (refs.freeCamButton != null)
            refs.freeCamButton.onClick.AddListener(OnFreeCamPressed);

        if (refs.trackCamButton != null)
            refs.trackCamButton.onClick.AddListener(OnTrackCamPressed);

        if (refs.instructionsButton != null)
            refs.instructionsButton.onClick.AddListener(OnInstructionsPressed);

        if (refs.placementModeButton != null)
            refs.placementModeButton.onClick.AddListener(OnPlacementModePressed);

        if (refs.burnControlButton != null)
            refs.burnControlButton.onClick.AddListener(OnBurnModePressed);

        if (refs.vectorToggleButton != null)
            refs.vectorToggleButton.onClick.AddListener(OnVectorTogglePressed);

        if (refs.skipButton != null)
            refs.skipButton.onClick.AddListener(OnSkipTutorialPressed);
    }

    private void UnbindButtons()
    {
        if (refs.freeCamButton != null)
            refs.freeCamButton.onClick.RemoveListener(OnFreeCamPressed);

        if (refs.trackCamButton != null)
            refs.trackCamButton.onClick.RemoveListener(OnTrackCamPressed);

        if (refs.instructionsButton != null)
            refs.instructionsButton.onClick.RemoveListener(OnInstructionsPressed);

        if (refs.placementModeButton != null)
            refs.placementModeButton.onClick.RemoveListener(OnPlacementModePressed);

        if (refs.burnControlButton != null)
            refs.burnControlButton.onClick.RemoveListener(OnBurnModePressed);

        if (refs.vectorToggleButton != null)
            refs.vectorToggleButton.onClick.RemoveListener(OnVectorTogglePressed);

        if (refs.skipButton != null)
            refs.skipButton.onClick.RemoveListener(OnSkipTutorialPressed);
    }

    private void BindCameraEvents()
    {
        if (cameraTracker == null) return;

        cameraTracker.OnModeChanged += HandleModeChanged;
        cameraTracker.OnTrackedBodyChanged += HandleTrackedBodyChanged;
        cameraTracker.OnTrackedPlaceholderChanged += HandleTrackedPlaceholderChanged;
    }

    private void UnbindCameraEvents()
    {
        if (cameraTracker == null) return;

        cameraTracker.OnModeChanged -= HandleModeChanged;
        cameraTracker.OnTrackedBodyChanged -= HandleTrackedBodyChanged;
        cameraTracker.OnTrackedPlaceholderChanged -= HandleTrackedPlaceholderChanged;
    }

    private void HandleModeChanged(CameraMode mode)
    {
        RefreshAll();
    }

    private void HandleTrackedBodyChanged(NBody _)
    {
        cameraModeUI?.HandleTrackedBodyChanged(cameraTracker);
        RefreshAll();
    }

    private void HandleTrackedPlaceholderChanged(Transform _)
    {
        RefreshAll();
    }

    private void RefreshAll()
    {
        if (cameraTracker == null) return;

        CameraMode mode = cameraTracker.Mode;

        cameraModeUI?.Apply(mode);
        placementUI?.Apply(mode);
        flightUI?.Apply(mode);
        instructionsUI?.Apply(mode);
        vectorUI?.Apply(mode);
    }

    private void OnFreeCamPressed()
    {
        cameraTracker?.BreakToFreeCam();
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void OnTrackCamPressed()
    {
        cameraTracker?.ReturnToTracking();
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void OnInstructionsPressed()
    {
        instructionsUI?.Toggle();
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void OnPlacementModePressed()
    {
        placementUI?.CyclePlacementMode();

        if (cameraTracker != null && cameraTracker.Mode == CameraMode.Free)
            objectPlacementManager?.ClearAllFields();

        RefreshAll();

        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void OnBurnModePressed()
    {
        flightUI?.ToggleBurnMode();
        RefreshAll();
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void OnVectorTogglePressed()
    {
        vectorUI?.Toggle();
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void OnSkipTutorialPressed()
    {
        tutorialUI?.SkipTutorial();
        EventSystem.current?.SetSelectedGameObject(null);
    }

    // PLACEMENT
    public void SetPlacementButtonsLocked(bool locked)
    {
        if (refs.placementModeButton != null)
            refs.placementModeButton.interactable = !locked;

        if (refs.randomSatelliteButton != null)
            refs.randomSatelliteButton.interactable = !locked;
    }

    public void SetTrackCamButtonInteractable(bool interactable)
    {
        if (refs.trackCamButton != null)
            refs.trackCamButton.interactable = interactable;
    }

    public void EnterTrackingMode()
    {
        cameraTracker?.ReturnToTracking();
        EventSystem.current?.SetSelectedGameObject(null);
    }

    // TIME CONTROLS
    public void SetGameplayUiVisibleForPause(bool show)
    {
        if (!show)
        {
            SetActive(refs.thrustButtons, false);
            SetActive(refs.maneuverNodePanel, false);
            SetActive(refs.burnControlsPanel, false);
            SetActive(refs.attitudeControlPanel, false);
            SetActive(refs.toggleOptionsPanel, false);
            SetActive(refs.dropdown, false);

            SetActive(refs.objectPlacementPanel, false);
            SetActive(refs.placeTLEPanel, false);
            SetActive(refs.placeKeplerPanel, false);
            SetActive(refs.placementSelectPanel, false);
            SetActive(refs.randomPlacementPanel, false);

            SetActive(refs.cameraControls, false);
            SetActive(refs.confirmRemoveSatPanel, false);

            return;
        }

        RefreshAll();
    }

    private void SetActive(GameObject go, bool show)
    {
        if (go != null)
            go.SetActive(show);
    }
}