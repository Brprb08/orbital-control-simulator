using UnityEngine;
using UnityEngine.Rendering;

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
    private int bulkSpawnDepth;
    private bool pendingBodyListRefresh;
    private bool pendingTrajectoryRefresh;

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
    public NBody SpawnSatellite(string name, Vector3 position, float mass, Vector3 initialVelocity, bool trackAfterSpawn,
        bool isGhost = false, double fuelMassKg = SimulationLimits.DefaultSatelliteFuelMassKg)
    {
        if (!NBody.IsValidMassComposition(mass, fuelMassKg))
            throw new System.ArgumentException("Invalid satellite dry/fuel mass.");
        satelliteCount++;

        var prefabToUse = isGhost ? ghostSatPrefab : spherePrefab;
        var go = Instantiate(prefabToUse);
        go.name = name;
        go.tag = "Satellite";
        go.transform.position = position;
        Vector3 physicalRadiusMeters = Vector3.one * SatelliteSizing.DefaultPhysicalRadiusMeters;
        ConfigureDynamicSatelliteRendering(go, isGhost);

        var nbody = InitializeSatellite(go, mass, physicalRadiusMeters, initialVelocity, fuelMassKg: fuelMassKg);

        ctx.BodyService.Register(nbody);
        RequestPostSpawnRefresh();

        if (trackAfterSpawn)
        {
            TrackBody(nbody);
        }

        return nbody;
    }

    /// <summary>
    /// Prepares the physical state for either a new spawn or a manual placeholder.
    /// Registration owns context initialization and attitude components; callers own tracking.
    /// Manual placeholders retain prefab camera/body flags; entered dry/fuel masses always win.
    /// </summary>
    internal static NBody InitializeSatellite(
        GameObject satellite, float mass, Vector3 radiusMeters, Vector3 initialVelocity,
        bool preserveExistingBodyProperties = false, double fuelMassKg = SimulationLimits.DefaultSatelliteFuelMassKg)
    {
        var body = satellite.GetComponent<NBody>();
        bool isNew = body == null;
        if (isNew) body = satellite.AddComponent<NBody>();

        if (isNew || !preserveExistingBodyProperties)
        {
            body.cameraDistanceRadius = SatelliteSizing.CameraDistanceRadius;
            body.isCentralBody = false;
        }

        // Placement mass inputs are authoritative, including manual launch from a prefab.
        if (!body.TrySetMassesKilograms(mass, fuelMassKg))
            throw new System.ArgumentException("Invalid or locked satellite dry/fuel mass.");

        satellite.transform.localScale = SatelliteSizing.ResolveVisualScale(radiusMeters);
        body.radius = SatelliteSizing.ResolvePhysicalRadiusSimUnits(radiusMeters);
        body.velocity = initialVelocity;
        body.state = new NBody.OrbitalState(
            satellite.transform.position.ToDouble3(), initialVelocity.ToDouble3(),
            0f, body.TotalMassKilograms, body.radius, body.dragCoefficient, Vector3.zero);
        return body;
    }

    private static void ConfigureDynamicSatelliteRendering(GameObject satellite, bool isGhost)
    {
        if (satellite == null || isGhost)
            return;

        // Constellation members are tiny dynamic objects. Their shadows, probes,
        // reflections, and PhysX colliders provide no useful gameplay signal but
        // scale linearly with every spawned satellite.
        foreach (Renderer renderer in satellite.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        foreach (Collider collider in satellite.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
    }

    public void BeginBulkSpawn()
    {
        if (bulkSpawnDepth == 0)
            ctx?.BodyService?.BeginBulkRegistration();

        bulkSpawnDepth++;
    }

    public void EndBulkSpawn()
    {
        if (bulkSpawnDepth <= 0)
            return;

        bulkSpawnDepth--;
        if (bulkSpawnDepth > 0)
            return;

        ctx?.BodyService?.EndBulkRegistration();
        FlushPostSpawnRefreshes();
    }

    /// <summary>
    /// Creates a placeholder GameObject for manual placement and configures velocity staging.
    /// </summary>
    public GameObject CreatePlaceholder(
        string name,
        Vector3 position,
        Vector3 radiusMeters,
        float mass,
        PendingVelocityPlacementController pendingVelocityPlacementController,
        double fuelMassKg = SimulationLimits.DefaultSatelliteFuelMassKg)
    {
        satelliteCount++;

        var go = Instantiate(spherePrefab);
        go.name = name;
        go.tag = "Satellite";
        go.transform.position = position;
        go.transform.localScale = SatelliteSizing.ResolveVisualScale(radiusMeters);

        cameraTracker?.RefreshBodiesList();

        if (pendingVelocityPlacementController != null)
        {
            Debug.Log("[Spawner] Wiring velocity staging with planet + mass");
            pendingVelocityPlacementController.ConfigurePendingPlacement(go, mass, radiusMeters, fuelMassKg);
        }

        return go;
    }

    private void RequestPostSpawnRefresh()
    {
        if (bulkSpawnDepth > 0)
        {
            pendingBodyListRefresh = true;
            pendingTrajectoryRefresh = true;
            return;
        }

        cameraTracker?.RefreshBodiesList();
        trajectoryRenderer?.RequestFullOrbitPass();
    }

    private void FlushPostSpawnRefreshes()
    {
        if (pendingBodyListRefresh)
            cameraTracker?.RefreshBodiesList();

        if (pendingTrajectoryRefresh)
            trajectoryRenderer?.RequestFullOrbitPass();

        pendingBodyListRefresh = false;
        pendingTrajectoryRefresh = false;
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
