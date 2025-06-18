public class SimContext
{
    public GravityManager GravityManager { get; set; }
    public CameraController CameraController { get; set; }
    public CameraMovement CameraMovement { get; set; }
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
}