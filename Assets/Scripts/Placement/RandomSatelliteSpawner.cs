using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Spawns satellites on randomized orbits using Kepler elements,
/// driven from simple UI buttons. Honors camera mode and shares
/// central-body parameters via ObjectPlacementManager.
/// </summary>
public class RandomSatelliteSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ObjectPlacementManager placementManager;
    [SerializeField] private SatelliteSpawner satelliteSpawner;
    [SerializeField] private Button randomSatButton;
    [SerializeField] private Button burstButton;

    [Header("Counts")]
    [SerializeField] private int clickCount = 1;
    [SerializeField] private int shiftClickCount = 10;
    [SerializeField] private int ctrlClickCount = 100;
    [SerializeField] private int burstCount = 500;

    [Header("Random Orbit Ranges")]
    [SerializeField] private Vector2 eccentricityRange = new Vector2(0.0f, 0.70f);
    [SerializeField] private Vector2 perigeeAltKmRange = new Vector2(700f, 2000f);
    [SerializeField] private Vector2 apogeeAltKmRange = new Vector2(700f, 35000f);

    [SerializeField] private float minAltDifferenceKm = 50f;
    [SerializeField] private Vector2 massRangeKg = new Vector2(500f, 50_000f);
    [SerializeField] private int maxRetries = 8; // attempts per satellite

    private ICameraTracker cameraTracker;
    private SimContext ctx;

    /// <summary>
    /// Injects the sim context and caches core references.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        cameraTracker = ctx.CameraTracker;
        placementManager = ctx.ObjectPlacementManager;
    }

    /// <summary>
    /// Hooks button listeners when the spawner becomes active.
    /// </summary>
    private void OnEnable()
    {
        if (randomSatButton != null)
            randomSatButton.onClick.AddListener(OnRandomSatButtonClicked);

        if (burstButton != null)
            burstButton.onClick.AddListener(OnBurstButtonClicked);
    }

    /// <summary>
    /// Unhooks button listeners when the spawner is disabled.
    /// </summary>
    private void OnDisable()
    {
        if (randomSatButton != null)
            randomSatButton.onClick.RemoveListener(OnRandomSatButtonClicked);

        if (burstButton != null)
            burstButton.onClick.RemoveListener(OnBurstButtonClicked);
    }

    /// <summary>
    /// Handles the "Random Sat" button: picks a spawn count based on
    /// modifier keys and spawns a batch of random satellites.
    /// </summary>
    private void OnRandomSatButtonClicked()
    {
        if (!CanStartPlacement()) return;

        int count = GetRandomSpawnCountFromModifiers();
        SpawnRandomSatellites(count);
    }

    /// <summary>
    /// Handles the "Burst" button: spawns a fixed, larger batch.
    /// </summary>
    private void OnBurstButtonClicked()
    {
        if (!CanStartPlacement()) return;

        int count = Mathf.Max(1, burstCount);
        SpawnRandomSatellites(count);
    }

    /// <summary>
    /// Checks whether random spawning is allowed (camera mode etc.).
    /// </summary>
    private bool CanStartPlacement()
    {
        if (cameraTracker == null)
        {
            Debug.LogWarning("RandomSatelliteSpawner: CameraTracker not set.");
            return false;
        }

        if (cameraTracker.Mode != CameraMode.Free)
        {
            Debug.LogWarning($"RandomSatelliteSpawner: Switch to FreeCam (current: {cameraTracker.Mode}).");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Maps key modifiers to a spawn count (click, Shift+click, Ctrl/Cmd+click).
    /// </summary>
    private int GetRandomSpawnCountFromModifiers()
    {
        int count = clickCount;

        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            count = shiftClickCount;

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        bool ctrlOrCmd = Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
#else
        bool ctrlOrCmd = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#endif
        if (ctrlOrCmd)
            count = ctrlClickCount;

        return count;
    }

    /// <summary>
    /// Spawns a batch of random satellites, tracking the last one if any were placed.
    /// </summary>
    private void SpawnRandomSatellites(int count)
    {
        int placed = 0;
        NBody last = null;

        for (int i = 0; i < count; i++)
        {
            if (TryPlaceOneRandomSatellite(out var created))
            {
                placed++;
                last = created;
            }
        }

        if (last != null)
        {
            satelliteSpawner.TrackBody(last);
        }

        Debug.Log($"RandomSatelliteSpawner: Spawned {placed}/{count} random satellite(s).");
    }

    /// <summary>
    /// Attempts to generate and place a single random orbit within the configured ranges.
    /// Returns true on success and outputs the created NBody.
    /// </summary>
    private bool TryPlaceOneRandomSatellite(out NBody created)
    {
        created = null;

        double mu = placementManager.Mu;
        double earthRadiusMeters = placementManager.EarthRadiusMeters;
        double metersPerUnit = placementManager.MetersPerUnit;

        for (int r = 0; r < maxRetries; r++)
        {
            float mass = UnityEngine.Random.Range(massRangeKg.x, massRangeKg.y);

            float perigeeAltKm = UnityEngine.Random.Range(perigeeAltKmRange.x, perigeeAltKmRange.y);
            float minApogeeAllowedKm = Mathf.Max(perigeeAltKm + minAltDifferenceKm, apogeeAltKmRange.x);
            float maxApogeeAllowedKm = apogeeAltKmRange.y;

            if (minApogeeAllowedKm >= maxApogeeAllowedKm)
                continue; // no valid apogee for this perigee, retry

            float apogeeAltKm = UnityEngine.Random.Range(minApogeeAllowedKm, maxApogeeAllowedKm);

            double rp = earthRadiusMeters + perigeeAltKm * 1000.0;
            double ra = earthRadiusMeters + apogeeAltKm * 1000.0;

            double a = 0.5 * (rp + ra);
            double e = (ra - rp) / (ra + rp);

            e = Mathf.Clamp((float)e, eccentricityRange.x, eccentricityRange.y);

            float incDeg = UnityEngine.Random.Range(0f, 180f);
            float raanDeg = UnityEngine.Random.Range(0f, 360f);
            float argpDeg = UnityEngine.Random.Range(0f, 360f);
            float truDeg = UnityEngine.Random.Range(0f, 360f);

            try
            {
                var (rEci, vEci) = KeplerUtils.FromElements(a, e, incDeg, raanDeg, argpDeg, truDeg, mu);

                if (rEci.magnitude <= earthRadiusMeters * 1.001)
                    continue;

                var pos = FrameUtils.EciToUnity(rEci, metersPerUnit);
                var vel = FrameUtils.VelEciToUnity(vEci, metersPerUnit);

                string name = $"Rand Sat {satelliteSpawner.NextSatelliteIndex}";
                if (name.Length > 15)
                    name = name.Substring(0, 15);

                created = satelliteSpawner.SpawnSatellite(name, pos, mass, vel, trackAfterSpawn: false);
                return true;
            }
            catch
            {
                // retry with new random draw
            }
        }

        return false;
    }
}
