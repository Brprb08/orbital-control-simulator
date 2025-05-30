using UnityEngine;

/// <summary>
/// Drag onto a single “Bootstrap” GameObject.  Assign *all* your systems
/// (GravityManager, CameraController, UIManager, …) in the inspector,
/// then at runtime this wires the SimContext and calls Initialize() on each.
/// </summary>
public class SimulationBootstrap : MonoBehaviour
{
    [Header("Core Systems")]
    public GravityManager gravityManager;
    public CameraController cameraController;
    public CameraMovement cameraMovement;
    public BodyDropdownManager bodyDropdownManager;
    public ManeuverNodeManager maneuverNodeManager;
    public ThrustController thrustController;
    public TimeController timeController;
    public UIManager uIManager;
    public LineVisibilityManager lineVisibilityManager;
    public TrajectoryComputeController trajectoryComputeController;
    public TrajectoryRenderer trajectoryRenderer;
    public ObjectPlacementManager objectPlacementManager;
    public VelocityDragManager velocityDragManager;

    private SimContext ctx;

    void Awake()
    {
        // 1) build context
        ctx = new SimContext
        {
            LineVisibilityManager = lineVisibilityManager,
            GravityManager = gravityManager,
            CameraController = cameraController,
            CameraMovement = cameraMovement,
            BodyDropdownManager = bodyDropdownManager,
            ManeuverNodeManager = maneuverNodeManager,
            ThrustController = thrustController,
            TimeController = timeController,
            UIManager = uIManager,
            TrajectoryComputeController = trajectoryComputeController,
            TrajectoryRenderer = trajectoryRenderer,
            ObjectPlacementManager = objectPlacementManager,
            VelocityDragManager = velocityDragManager
        };

        // 2) initialize in dependency order
        lineVisibilityManager.Initialize(ctx);
        cameraMovement.Initialize(ctx);
        trajectoryRenderer.Initialize(ctx);
        gravityManager.Initialize(ctx);
        cameraController.Initialize(ctx);
        bodyDropdownManager.Initialize(ctx);
        maneuverNodeManager.Initialize(ctx);
        thrustController.Initialize(ctx);
        timeController.Initialize(ctx);
        uIManager.Initialize(ctx);
        trajectoryComputeController.Initialize(ctx);

        objectPlacementManager.Initialize(ctx);
        velocityDragManager.Initialize(ctx);

        foreach (var nbody in FindObjectsByType<NBody>(FindObjectsSortMode.None))
            nbody.Initialize(ctx);

        trajectoryRenderer.SetTrackedBody(cameraController.Bodies[0]);
    }
}
