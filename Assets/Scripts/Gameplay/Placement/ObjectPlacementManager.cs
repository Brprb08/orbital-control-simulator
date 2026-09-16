using System;
using UnityEngine;

/// <summary>
/// Builds and spawns satellites from four placement paths:
/// 1) Manual position/mass/radius
/// 2) Keplerian elements
/// 3) TLE
/// 4) Constellation layouts
/// UI field plumbing lives in PlacementFieldsUI, while placement mode/panel
/// visibility lives in PlacementUIController. PendingSatellitePlacement names
/// the manual-placement state where a placeholder exists but needs velocity.
/// </summary>
public class ObjectPlacementManager : MonoBehaviour
{
    [Header("References - Core")]
    [SerializeField] private Camera _mainCamera;
    [SerializeField] private SatelliteSpawner _satelliteSpawner;
    [SerializeField] private PendingVelocityPlacementController _pendingVelocityPlacementController;
    [SerializeField] private TutorialController _tutorialController;

    private ICameraTracker _cameraTracker;
    private UIRoot _uiRoot;
    private SimContext _ctx;
    private PlacementFieldsUI _fields;
    private PlacementSpawnBuilder _spawnBuilder;

    [Header("Units & Central Body")]
    [Tooltip("Placement input scale. The simulation and native physics use 10,000 meters per unit; this field does not rescale physics.")]
    [SerializeField] private double _metersPerUnit = SimulationUnits.MetersPerUnit;

    [Tooltip("Standard gravitational parameter μ = GM of the central body, in m^3/s^2 (Earth by default).")]
    [SerializeField] private double _mu = PhysicsConstants.EarthMuMeters;

    [Tooltip("Earth radius in meters (used for simple safety checks).")]
    [SerializeField] private double _earthRadiusMeters = PhysicsConstants.EarthRadiusMeters;

    [Header("Constellations")]
    [SerializeField, Min(1)] private int _maxConstellationSatellites = ConstellationGenerator.DefaultMaxSatellites;

    [Header("Ghost Preview")]
    [SerializeField] private GameObject _ghostPreviewPrefab;
    [SerializeField, Min(0f)] private float _manualPlacementEarthCamDistance = 4000f;
    private GameObject _ghostInstance;
    private bool _ghostObjectPlaced;
    private bool _clearingPosition;

    [Header("Placement State")]
    [SerializeField] private PendingSatellitePlacement _pendingPlacement = new();

    // Public for RandomSatelliteSpawner
    public double Mu => _mu;
    public double EarthRadiusMeters => _earthRadiusMeters;
    public double MetersPerUnit => _metersPerUnit;

    private PendingSatellitePlacement PendingPlacement
    {
        get
        {
            if (_pendingPlacement == null)
                _pendingPlacement = new PendingSatellitePlacement();

            return _pendingPlacement;
        }
    }

    /// <summary>
    /// Injects the simulation context and wires dependencies + UI listeners.
    /// Also creates and hides the ghost preview if a prefab is provided.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        _ctx = ctx;
        _cameraTracker = ctx.CameraTracker;
        _uiRoot = ctx.UIRoot;
        _tutorialController = ctx.TutorialController ?? _tutorialController;
        _fields = new PlacementFieldsUI(_uiRoot.References, _uiRoot, _tutorialController, _mainCamera);
        _spawnBuilder = new PlacementSpawnBuilder(
            _fields,
            _mainCamera,
            _satelliteSpawner,
            _metersPerUnit,
            _mu,
            _earthRadiusMeters
        );
        _fields.BindTutorialHooks();

        if (_fields.PositionInput != null)
            _fields.PositionInput.onValueChanged.AddListener(OnPositionInputChanged);

        if (_ghostPreviewPrefab != null)
        {
            _ghostInstance = Instantiate(_ghostPreviewPrefab);
            HideGhost();
        }
    }

    private void OnDestroy()
    {
        _fields?.UnbindTutorialHooks();

        if (_fields?.PositionInput != null)
            _fields.PositionInput.onValueChanged.RemoveListener(OnPositionInputChanged);
    }

    /// <summary>
    /// Manual placement flow: validates manual fields, spawns a placeholder,
    /// hooks it up to the velocity staging manager, and locks the inputs.
    /// </summary>
    public void StartPlacement()
    {
        if (!CanStartPlacement(out var gateErr))
        {
            SetFeedback(gateErr);
            return;
        }

        if (!_spawnBuilder.TryBuildManualPlaceholder(out var placement, out string error))
        {
            SetFeedback(error);
            return;
        }

        HideGhost();

        GameObject placeholder = _satelliteSpawner.CreatePlaceholder(
            placement.Name,
            placement.Position,
            placement.RadiusMeters,
            placement.Mass,
            _pendingVelocityPlacementController,
            placement.FuelMassKg
        );

        PendingPlacement.Set(placeholder);
        PreviewSilently(placeholder != null ? placeholder.transform : null);
        SwitchManualPlacementToEarthCam();

        LockManualPlacementInputs(true);
        ClearAllFields();

        if (_tutorialController != null)
            _tutorialController.hasSatelliteBeenPlaced = true;

        SetFeedback(
            "Setting Satellite Velocity:\n\n" +
            "• Choose prograde/retrograde and orbit shaping.\n" +
            "• Adjust speed scale or enter an exact vector.\n" +
            "• Click Set Velocity when the preview looks right."
        );

        UIHelpers.ClearSelection();
    }

    /// <summary>
    /// Keplerian placement flow: validates elements, converts to ECI position/velocity,
    /// checks for Earth intersection, then spawns a tracked satellite.
    /// </summary>
    public void PlaceObjectFromKepler()
    {
        if (!CanStartPlacement(out var gateErr))
        {
            SetFeedback(gateErr);
            return;
        }

        if (!_spawnBuilder.TryBuildKeplerSpawn(out var spawn, out string error))
        {
            SetFeedback(error);
            return;
        }

        _satelliteSpawner.SpawnSatellite(
            spawn.Name,
            spawn.Position,
            (float)spawn.Mass,
            spawn.Velocity,
            trackAfterSpawn: true,
            fuelMassKg: spawn.FuelMassKg
        );

        ClearAllFields();
        SetFeedback($"Placed '{spawn.Name}' from Keplerian elements.");

        PendingPlacement.Clear();
        UpdateTrackCamButtonState(false);
    }

    /// <summary>
    /// TLE placement flow: parses and propagates the TLE to "now", converts to Unity,
    /// and spawns a tracked satellite if the position is valid.
    /// </summary>
    public void PlaceObjectFromTLE()
    {
        if (!CanStartPlacement(out var gateErr))
        {
            SetFeedback(gateErr);
            return;
        }

        if (!_spawnBuilder.TryBuildTleSpawn(out var tle, out string error))
        {
            SetFeedback(error);
            return;
        }

        if (_pendingVelocityPlacementController?.trajectoryRenderer?.preManeuverLine != null)
            _pendingVelocityPlacementController.trajectoryRenderer.preManeuverLine.Clear();

        _satelliteSpawner.SpawnSatellite(
            tle.Spawn.Name,
            tle.Spawn.Position,
            (float)tle.Spawn.Mass,
            tle.Spawn.Velocity,
            trackAfterSpawn: true,
            fuelMassKg: tle.Spawn.FuelMassKg
        );

        ClearAllFields();
        SetFeedback(
            $"Placed '{tle.Spawn.Name}' from TLE at {tle.WhenUtc:yyyy-MM-dd HH:mm:ss}Z " +
            $"(epoch {tle.EpochUtc:yyyy-MM-dd HH:mm:ss}Z)."
        );

        PendingPlacement.Clear();
        UpdateTrackCamButtonState(false);
    }

    /// <summary>
    /// Constellation placement flow: validates Walker-style layout inputs,
    /// generates individual Keplerian states, and spawns each member as a
    /// normal satellite with constellation metadata attached.
    /// </summary>
    public void PlaceConstellation()
    {
        if (!CanStartPlacement(out var gateErr))
        {
            SetFeedback(gateErr);
            return;
        }

        if (!_spawnBuilder.TryBuildConstellationSpawn(
                _maxConstellationSatellites,
                out var constellation,
                out string error))
        {
            SetFeedback(error);
            return;
        }

        string constellationId = Guid.NewGuid().ToString("N");
        NBody firstBody = null;
        var spawnedMembers = new System.Collections.Generic.List<NBody>(constellation.Members.Count);

        _satelliteSpawner.BeginBulkSpawn();
        try
        {
            foreach (var member in constellation.Members)
            {
                NBody body = _satelliteSpawner.SpawnSatellite(
                    member.Spawn.Name,
                    member.Spawn.Position,
                    (float)member.Spawn.Mass,
                    member.Spawn.Velocity,
                    trackAfterSpawn: false,
                    fuelMassKg: member.Spawn.FuelMassKg
                );

                var metadata = body.GetComponent<ConstellationMember>();
                if (metadata == null)
                    metadata = body.gameObject.AddComponent<ConstellationMember>();

                metadata.Configure(
                    constellationId,
                    constellation.Definition.NamePrefix,
                    member.PlaneIndex,
                    member.SlotIndex,
                    member.MemberIndex
                );

                if (firstBody == null)
                    firstBody = body;

                spawnedMembers.Add(body);
            }
        }
        finally
        {
            _satelliteSpawner.EndBulkSpawn();
        }

        _ctx?.ConstellationRegistry?.RegisterConstellation(
            constellationId,
            constellation.Definition,
            spawnedMembers
        );

        if (firstBody != null)
            _satelliteSpawner.TrackBody(firstBody);

        ClearAllFields();
        SetFeedback(
            $"Created constellation '{constellation.Definition.NamePrefix}' with " +
            $"{constellation.Definition.TotalSatellites} satellites across " +
            $"{constellation.Definition.Planes} planes."
        );

        PendingPlacement.Clear();
        UpdateTrackCamButtonState(false);
    }

    /// <summary>
    /// Clears all input fields for manual, Kepler, TLE, and constellation placement.
    /// </summary>
    public void ClearAllFields()
    {
        _clearingPosition = true;
        _fields?.ClearAllFields();
        _clearingPosition = false;
    }

    /// <summary>
    /// Cancels the current placement, if any, and returns camera to its previous tracking target.
    /// </summary>
    public void CancelPlacement()
    {
        PendingPlacement.DestroyAndClear();

        SetFeedback(string.Empty);
        _cameraTracker?.ReturnToTracking();
    }

    /// <summary>
    /// Clears any "pending placement" state so another manual placement can start.
    /// </summary>
    public void ClearPendingPlacement()
    {
        SetFeedback(string.Empty);
        PendingPlacement.Clear();
    }

    /// <summary>
    /// Handles manual position changes, drives ghost preview visibility,
    /// and enforces a simple radial distance constraint.
    /// </summary>
    private void OnPositionInputChanged(string input)
    {
        if (_mainCamera == null) return;

        if (_ghostObjectPlaced && !_clearingPosition && _fields?.PositionInput != null && _fields.PositionInput.isFocused)
        {
            _cameraTracker?.BreakToFreeCam();
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            HideGhost();
            SetFeedback(string.Empty);
            return;
        }

        if (ParsingUtils.TryParseVector3(input, out Vector3 targetPosition))
        {
            float distanceFromEarth = Vector3.Distance(Vector3.zero, targetPosition);

            if (distanceFromEarth < PlacementSpawnBuilder.PositionBounds.Min ||
                distanceFromEarth > PlacementSpawnBuilder.PositionBounds.Max)
            {
                _ghostObjectPlaced = false;
                if (_ghostInstance != null) _ghostInstance.SetActive(false);

                SetFeedback(
                    $"Position magnitude must be between {PlacementSpawnBuilder.PositionBounds.Min} " +
                    $"and {PlacementSpawnBuilder.PositionBounds.Max}. Example: 641,0,0"
                );
                return;
            }

            if (_tutorialController != null)
                _tutorialController.hasPositionBeenEnteredForSatellite = true;

            ShowGhostAt(targetPosition);
            SetFeedback(string.Empty);
        }
        else
        {
            HideGhost();
            SetFeedback("Invalid Position: Format is x,y,z. Example 1000,200,30");
        }
    }

    /// <summary>
    /// Runs a "silent" camera preview of a transform in free cam mode.
    /// </summary>
    private void PreviewSilently(Transform t)
    {
        if (_cameraTracker == null || t == null) return;

        _cameraTracker.BeginUiSuppress();
        _cameraTracker.PreviewPlaceholderInFree(t);
        _cameraTracker.EndUiSuppress();
    }

    private void SwitchManualPlacementToEarthCam()
    {
        if (_ctx?.CameraButtonProxy != null)
        {
            _ctx.CameraButtonProxy.EarthCam(_manualPlacementEarthCamDistance);
            return;
        }

        if (_ctx?.CameraController != null)
            _ctx.CameraController.SwitchToEarthCam(_manualPlacementEarthCamDistance);
        else
            _cameraTracker?.SwitchToEarthCam();

        _ctx?.UIRoot?.RefreshAllUi();
    }

    private void LockManualPlacementInputs(bool locked)
    {
        _fields?.LockManualInputs(locked);
    }

    private void UpdateTrackCamButtonState(bool state)
    {
        _fields?.SetTrackCamButtonInteractable(state);
    }

    private void SetFeedback(string msg)
    {
        _fields?.SetFeedback(msg);
    }

    /// <summary>
    /// Shows or moves the ghost preview to a world-space position and previews it in the camera.
    /// </summary>
    private void ShowGhostAt(Vector3 pos)
    {
        if (!_ghostInstance) return;

        _ghostInstance.SetActive(true);
        _ghostInstance.transform.position = pos;
        PreviewSilently(_ghostInstance.transform);
        _ghostObjectPlaced = true;
    }

    /// <summary>
    /// Hides the ghost preview, if present.
    /// </summary>
    private void HideGhost()
    {
        if (_ghostInstance) _ghostInstance.SetActive(false);
        _ghostObjectPlaced = false;
    }

    /// <summary>
    /// Checks whether a new placement can start (no pending velocity-set,
    /// and camera in Free mode).
    /// </summary>
    private bool CanStartPlacement(out string error)
    {
        if (PendingPlacement.HasSatellite)
        {
            error = $"Finish setting velocity for '{PendingPlacement.SatelliteName}' first.";
            return false;
        }

        if (_cameraTracker == null)
        {
            error = "CameraTracker not set.";
            return false;
        }

        if (_cameraTracker.Mode != CameraMode.Free)
        {
            error = $"Switch to FreeCam (current: {_cameraTracker.Mode}).";
            return false;
        }

        error = null;
        return true;
    }
}
