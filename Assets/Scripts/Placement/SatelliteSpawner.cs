using UnityEngine;

public class SatelliteSpawner : MonoBehaviour
{
    [Header("Prefabs & Core")]
    [SerializeField] private GameObject spherePrefab;
    [SerializeField] private GameObject ghostSatPrefab;
    [SerializeField] private TrajectoryRenderer trajectoryRenderer;

    private SimContext ctx;
    private ICameraTracker cameraTracker;
    // private UIManager uiManager;

    private int satelliteCount;

    public int SatelliteCount => satelliteCount;
    public int NextSatelliteIndex => satelliteCount + 1;

    /// <summary>
    /// Must be called by a higher-level manager (e.g. ObjectPlacementManager) after context is ready.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.cameraTracker = ctx.CameraTracker;
        // this.uiManager = ctx.UIManager;
        this.trajectoryRenderer = ctx.TrajectoryRenderer;
    }

    /// <summary>
    /// Spawn a fully initialized NBody satellite.
    /// </summary>
    public NBody SpawnSatellite(string name, Vector3 position, float mass, Vector3 initialVelocity, bool trackAfterSpawn, bool isGhost = false)
    {
        satelliteCount++;

        var prefabToUse = isGhost ? ghostSatPrefab : spherePrefab;
        var go = Instantiate(prefabToUse);
        go.name = name;
        go.tag = "Satellite";
        go.transform.position = position;
        go.transform.localScale = new Vector3(2f, 2f, 2f);

        var nbody = go.GetComponent<NBody>();
        if (nbody == null)
            nbody = go.AddComponent<NBody>();

        nbody.mass = mass;
        nbody.trueMass = mass;
        nbody.radius = SatelliteSizing.ResolvePhysicalRadiusSimUnits(go.transform.localScale);
        nbody.cameraDistanceRadius = 1f;
        nbody.isCentralBody = false;
        nbody.Initialize(ctx);

        if (isGhost)
        {
            nbody.state = new NBody.OrbitalState(
                new Unity.Mathematics.double3(position.x, position.y, position.z),
                new Unity.Mathematics.double3(initialVelocity.x, initialVelocity.y, initialVelocity.z),
                0f,
                nbody.trueMass,
                nbody.radius,
                nbody.dragCoefficient,
                Vector3.zero
            );
        }

        nbody.velocity = initialVelocity;

        var attitude = go.GetComponent<AttitudeController>();
        if (attitude == null)
        {
            attitude = go.AddComponent<AttitudeController>();
            attitude.mode = AttitudeController.PointingMode.Velocity;
            attitude.snapAttitude = false;
            attitude.maxSlewRateDegPerSec = 60f;
        }

        ctx.BodyService.Register(nbody);
        cameraTracker?.RefreshBodiesList();
        trajectoryRenderer?.RequestFullOrbitPass();

        if (trackAfterSpawn)
        {
            TrackBody(nbody);
        }

        return nbody;
    }

    /// <summary>
    /// Creates a placeholder GameObject for manual placement and configures the velocity drag manager.
    /// </summary>
    public GameObject CreatePlaceholder(
        string name,
        Vector3 position,
        Vector3 scale,
        float mass,
        VelocityDragManager velocityDragManager)
    {
        satelliteCount++;

        var go = Instantiate(spherePrefab);
        go.name = name;
        go.tag = "Satellite";
        go.transform.position = position;
        go.transform.localScale = scale;

        cameraTracker?.RefreshBodiesList();

        if (velocityDragManager != null)
        {
            Debug.Log("[Spawner] Wiring drag manager with planet + mass");
            velocityDragManager.ConfigurePendingPlacement(go, mass);
        }

        return go;
    }

    /// <summary>
    /// Helper to track a given body via camera/UI.
    /// </summary>
    // public void TrackBody(NBody body)
    // {
    //     if (body == null) return;

    //     (cameraTracker ?? ctx?.CameraTracker)?.TrackBody(body);
    //     uiManager?.OnTrackCamPressed();
    // }

    public void TrackBody(NBody body)
    {
        if (body == null) return;

        var tracker = cameraTracker ?? ctx?.CameraTracker;
        tracker?.TrackBody(body);
        tracker?.ReturnToTracking();
    }
}
