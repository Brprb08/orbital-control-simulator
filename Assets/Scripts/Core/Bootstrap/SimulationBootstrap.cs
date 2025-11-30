using UnityEngine;

/// <summary>
/// Attach to a single "Bootstrap" GameObject. Assign all systems in the Inspector.
/// At runtime, builds a SimContext and calls Initialize() on each system in dependency order.
/// </summary>
public class SimulationBootstrap : MonoBehaviour
{
    [Header("Core Systems")]
    public BodyRuntimeCoordinator bodyRuntimeCoordinator;
    public CameraController cameraController;
    public CameraMovement cameraMovement;
    public FreeCamera freeCamera;
    public BodyDropdownManager bodyDropdownManager;
    public ManeuverNodeManager maneuverNodeManager;
    public ThrustController thrustController;
    public TimeController timeController;
    public UIManager uIManager;
    public LineVisibilityController lineVisibilityController;
    public TrajectoryComputeController trajectoryComputeController;
    public TrajectoryRenderer trajectoryRenderer;
    public ObjectPlacementManager objectPlacementManager;
    public VelocityDragManager velocityDragManager;
    public RocketThrustAudio rocketThrustAudio;
    public TutorialController tutorialController;
    public CameraButtonProxy cameraButtonProxy;
    public RandomSatelliteSpawner randomSatelliteSpawner;
    public SatelliteSpawner satelliteSpawner;
    public BodyService bodyService;
    public AttitudeUIBinder attitudeUIBinder;
    private SimContext ctx;

    /// <summary>
    /// Creates the shared context and initializes all registered systems.
    /// </summary>
    void Awake()
    {
        ctx = new SimContext
        {
            LineVisibilityController = lineVisibilityController,
            BodyRuntimeCoordinator = bodyRuntimeCoordinator,
            CameraController = cameraController,
            CameraMovement = cameraMovement,
            FreeCamera = freeCamera,
            BodyDropdownManager = bodyDropdownManager,
            ManeuverNodeManager = maneuverNodeManager,
            ThrustController = thrustController,
            TimeController = timeController,
            UIManager = uIManager,
            TrajectoryComputeController = trajectoryComputeController,
            TrajectoryRenderer = trajectoryRenderer,
            ObjectPlacementManager = objectPlacementManager,
            VelocityDragManager = velocityDragManager,
            RocketThrustAudio = rocketThrustAudio,
            TutorialController = tutorialController,
            CameraButtonProxy = cameraButtonProxy,
            RandomSatelliteSpawner = randomSatelliteSpawner,
            BodyService = bodyService,
            AttitudeUIBinder = attitudeUIBinder,
        };

        // Initialize in dependency order
        lineVisibilityController.Initialize(ctx);
        bodyService.Initialize(ctx);
        bodyRuntimeCoordinator.Initialize(ctx);
        cameraMovement.Initialize(ctx);
        cameraButtonProxy.Initialize(ctx);
        freeCamera.Initialize(ctx);
        cameraController.Initialize(ctx);
        trajectoryRenderer.Initialize(ctx);
        bodyDropdownManager.Initialize(ctx);
        maneuverNodeManager.Initialize(ctx);
        thrustController.Initialize(ctx);
        timeController.Initialize(ctx);
        uIManager.Initialize(ctx);
        trajectoryComputeController.Initialize(ctx);
        objectPlacementManager.Initialize(ctx);
        velocityDragManager.Initialize(ctx);
        randomSatelliteSpawner.Initialize(ctx);
        satelliteSpawner.Initialize(ctx);
        rocketThrustAudio.Initialize(ctx);
        tutorialController.Initialize(ctx);
        attitudeUIBinder.Initialize(ctx);
    }
}
