public class SimContext
{
    public BodyService BodyService { get; set; }
    public GravityManager GravityManager { get; set; }
    public CameraController CameraController { get; set; }
    public CameraMovement CameraMovement { get; set; }
    // public CameraMode CameraMode { get; set; }
    public FreeCamera FreeCamera { get; set; }
    public BodyDropdownManager BodyDropdownManager { get; set; }
    public ManeuverNodeManager ManeuverNodeManager { get; set; }
    public ThrustController ThrustController { get; set; }
    public TimeController TimeController { get; set; }
    public UIManager UIManager { get; set; }
    public LineVisibilityManager LineVisibilityManager { get; set; }
    public TrajectoryComputeController TrajectoryComputeController { get; set; }
    public TrajectoryRenderer TrajectoryRenderer { get; set; }
    public ObjectPlacementManager ObjectPlacementManager { get; set; }
    public VelocityDragManager VelocityDragManager { get; set; }
    public RocketThrustAudio RocketThrustAudio { get; set; }
    public TutorialController TutorialController { get; set; }
    public ICameraTracker CameraTracker => CameraController;
    // public IBodyRegistry BodyRegistry => GravityManager;
    public CameraButtonProxy CameraButtonProxy { get; set; }

}