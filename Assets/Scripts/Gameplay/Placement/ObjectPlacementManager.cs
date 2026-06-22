using System;
using UnityEngine;

/// <summary>
/// Builds and spawns satellites from three placement paths:
/// 1) Manual position/mass/radius
/// 2) Keplerian elements
/// 3) TLE
/// UI field plumbing lives in PlacementFieldsUI, while placement mode/panel
/// visibility lives in PlacementUIController. PendingSatellitePlacement names
/// the manual-placement state where a placeholder exists but needs velocity.
/// </summary>
public class ObjectPlacementManager : MonoBehaviour
{
    [Header("References - Core")]
    [SerializeField] private Camera _mainCamera;
    [SerializeField] private SatelliteSpawner _satelliteSpawner;
    [SerializeField] private VelocityDragManager _velocityDragManager;
    [SerializeField] private TutorialController _tutorialController;

    private ICameraTracker _cameraTracker;
    private UIRoot _uiRoot;
    private SimContext _ctx;
    private PlacementFieldsUI _fields;

    [Header("Units & Central Body")]
    [Tooltip("Meters per 1 sim unit. If world units are kilometers, set this to 1000.")]
    [SerializeField] private double _metersPerUnit = 10000.0;

    [Tooltip("Standard gravitational parameter μ = GM of the central body, in m^3/s^2 (Earth by default).")]
    [SerializeField] private double _mu = 3.986004418e14;

    [Tooltip("Earth radius in meters (used for simple safety checks).")]
    [SerializeField] private double _earthRadiusMeters = 6378137.0;

    [Header("Ghost Preview")]
    [SerializeField] private GameObject _ghostPreviewPrefab;
    private GameObject _ghostInstance;
    private bool _ghostObjectPlaced;
    private bool _clearingPosition;

    [Header("Placement State")]
    [SerializeField] private PendingSatellitePlacement _pendingPlacement = new();

    private const int MaxSatelliteNameLength = 15;

    private static readonly PlacementValidators.RangeF MassRange = new(500f, 1000000f);
    private static readonly PlacementValidators.RangeF RadiusClamp = new(
        SatelliteSizing.MinPhysicalRadiusMeters,
        SatelliteSizing.MaxPhysicalRadiusMeters
    );
    private static readonly PlacementValidators.DistanceBoundsF PosBounds = new(638f, 5000f);

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
    /// hooks it up to the velocity-drag manager, and locks the inputs.
    /// </summary>
    public void StartPlacement()
    {
        if (!CanStartPlacement(out var gateErr))
        {
            SetFeedback(gateErr);
            return;
        }

        if (!TryBuildManualPlaceholderData(
                out string name,
                out Vector3 position,
                out Vector3 radius,
                out float mass,
                out string error))
        {
            SetFeedback(error);
            return;
        }

        HideGhost();

        GameObject placeholder = _satelliteSpawner.CreatePlaceholder(
            name,
            position,
            radius,
            mass,
            _velocityDragManager
        );

        PendingPlacement.Set(placeholder);
        PreviewSilently(placeholder != null ? placeholder.transform : null);

        LockManualPlacementInputs(true);
        ClearAllFields();

        if (_tutorialController != null)
            _tutorialController.hasSatelliteBeenPlaced = true;

        SetFeedback(
            "Setting Satellite Velocity:\n\n" +
            "• Click the satellite and drag.\n" +
            "• Set the desired direction.\n" +
            "• Use input field to adjust speed."
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

        if (!TryBuildKeplerSpawnData(
                out string name,
                out double mass,
                out Vector3 position,
                out Vector3 velocity,
                out string error))
        {
            SetFeedback(error);
            return;
        }

        _satelliteSpawner.SpawnSatellite(name, position, (float)mass, velocity, trackAfterSpawn: true);

        ClearAllFields();
        SetFeedback($"Placed '{name}' from Keplerian elements.");

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

        if (!TryBuildTleSpawnData(
                out string name,
                out double mass,
                out Vector3 position,
                out Vector3 velocity,
                out DateTime whenUtc,
                out DateTime epochUtc,
                out string error))
        {
            SetFeedback(error);
            return;
        }

        if (_velocityDragManager?.trajectoryRenderer?.preManeuverLine != null)
            _velocityDragManager.trajectoryRenderer.preManeuverLine.Clear();

        _satelliteSpawner.SpawnSatellite(name, position, (float)mass, velocity, trackAfterSpawn: true);

        ClearAllFields();
        SetFeedback(
            $"Placed '{name}' from TLE at {whenUtc:yyyy-MM-dd HH:mm:ss}Z " +
            $"(epoch {epochUtc:yyyy-MM-dd HH:mm:ss}Z)."
        );

        PendingPlacement.Clear();
        UpdateTrackCamButtonState(false);
    }

    /// <summary>
    /// Clears all input fields for manual, Kepler, and TLE placement.
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

            if (distanceFromEarth < PosBounds.Min || distanceFromEarth > PosBounds.Max)
            {
                _ghostObjectPlaced = false;
                if (_ghostInstance != null) _ghostInstance.SetActive(false);

                SetFeedback($"Position magnitude must be between {PosBounds.Min} and {PosBounds.Max}. Example: 641,0,0");
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

    /// <summary>
    /// Validates and builds data for a manual placeholder placement.
    /// </summary>
    private bool TryBuildManualPlaceholderData(
        out string name,
        out Vector3 position,
        out Vector3 radius,
        out float mass,
        out string error)
    {
        name = default;
        position = default;
        radius = default;
        mass = default;
        error = null;

        if (!PlacementValidators.TryGetName(
                _fields.ObjectNameInputField,
                "Satellite",
                _satelliteSpawner.SatelliteCount,
                MaxSatelliteNameLength,
                out name,
                out error))
        {
            return false;
        }

        if (!PlacementValidators.TryGetPositionOrDefault(
                _fields.PositionInput,
                _mainCamera.transform,
                10f,
                PosBounds,
                out position,
                out error))
        {
            return false;
        }

        if (!PlacementValidators.TryGetRadius(_fields.RadiusInput, RadiusClamp, out radius, out error))
        {
            return false;
        }

        if (!PlacementValidators.TryGetMass(_fields.MassInput, MassRange, out mass, out error))
        {
            return false;
        }

        if (radius == Vector3.zero)
        {
            radius = Vector3.one;
        }

        return true;
    }

    /// <summary>
    /// Validates Kepler inputs and produces Unity-space spawn position/velocity.
    /// </summary>
    private bool TryBuildKeplerSpawnData(
        out string name,
        out double mass,
        out Vector3 position,
        out Vector3 velocity,
        out string error)
    {
        name = default;
        mass = default;
        position = default;
        velocity = default;
        error = null;

        if (!PlacementValidators.TryGetName(
                _fields.KepNameInputField,
                "Kepler Sat",
                _satelliteSpawner.SatelliteCount + 1,
                MaxSatelliteNameLength,
                out name,
                out error))
        {
            return false;
        }

        if (!PlacementValidators.TryGetMass(_fields.KepMassInputField, MassRange, out var massF, out error))
        {
            return false;
        }

        mass = massF;

        if (!PlacementValidators.TryGetDouble(_fields.KepADegOrMetersInputField, out double aMeters))
        {
            error = "Invalid semi-major axis 'a'.";
            return false;
        }

        if (!PlacementValidators.TryGetDouble(_fields.KepEccInputField, out double e) || e < 0.0 || e >= 1.0)
        {
            error = "Invalid eccentricity 'e'. Use 0 ≤ e < 1.";
            return false;
        }

        if (!PlacementValidators.TryGetDouble(_fields.KepIncDegInputField, out double iDeg) ||
            !PlacementValidators.TryGetDouble(_fields.KepRAANDegInputField, out double raanDeg) ||
            !PlacementValidators.TryGetDouble(_fields.KepArgPDegInputField, out double argpDeg) ||
            !PlacementValidators.TryGetDouble(_fields.KepTrueAnomDegInputField, out double trueAnomDeg))
        {
            error = "Invalid angle(s): i / RAAN / ω / ν.";
            return false;
        }

        try
        {
            var (rEci, vEci) = KeplerUtils.FromElements(
                aMeters,
                e,
                iDeg,
                raanDeg,
                argpDeg,
                trueAnomDeg,
                _mu
            );

            double rp = aMeters * (1.0 - e);
            if (rp <= _earthRadiusMeters * 1.001)
            {
                double altKm = (rp - _earthRadiusMeters) / 1000.0;
                error = $"Orbit intersects Earth (perigee alt {altKm:F1} km). Increase 'a' or reduce 'e'.";
                return false;
            }

            position = FrameUtils.EciToUnity(rEci, _metersPerUnit);
            velocity = FrameUtils.VelEciToUnity(vEci, _metersPerUnit);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Kepler placement failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Validates TLE input, propagates to now, and produces Unity-space spawn data.
    /// </summary>
    private bool TryBuildTleSpawnData(
        out string name,
        out double mass,
        out Vector3 spawnPos,
        out Vector3 spawnVel,
        out DateTime whenUtc,
        out DateTime epochUtc,
        out string error)
    {
        name = default;
        mass = default;
        spawnPos = default;
        spawnVel = default;
        whenUtc = DateTime.UtcNow;
        epochUtc = default;
        error = null;

        if (!PlacementValidators.TryGetMass(_fields.TleMassInputField, MassRange, out var massF, out error))
        {
            return false;
        }

        mass = massF;

        name = !string.IsNullOrWhiteSpace(_fields.TleNameInputField?.text)
            ? _fields.TleNameInputField.text.Trim()
            : $"TLE Satellite {_satelliteSpawner.NextSatelliteIndex}";

        if (!TLEParser.TryPropagate(
                _fields.TleLine1InputField.text,
                _fields.TleLine2InputField.text,
                whenUtc,
                out Vector3d rEci_m,
                out Vector3d vEci_mps,
                out epochUtc))
        {
            error = "Invalid TLE input or propagation failed.";
            return false;
        }

        if (rEci_m.magnitude <= _earthRadiusMeters * 1.001)
        {
            error = "Computed position intersects Earth. Check TLE/time.";
            return false;
        }

        spawnPos = FrameUtils.EciToUnity(rEci_m, _metersPerUnit);
        spawnVel = FrameUtils.VelEciToUnity(vEci_mps, _metersPerUnit);

        return true;
    }
}
