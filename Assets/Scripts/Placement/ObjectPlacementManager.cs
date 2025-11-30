using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System;

/// <summary>
/// Handles satellite placement from three paths:
/// 1) Manual position/mass/radius
/// 2) Keplerian elements
/// 3) TLE
/// Also manages the “ghost” preview, simple validation, and interaction with
/// camera tracking and the velocity-drag flow.
/// </summary>
public class ObjectPlacementManager : MonoBehaviour
{
    [Header("References - Core")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private SatelliteSpawner satelliteSpawner;
    [SerializeField] private VelocityDragManager velocityDragManager;
    [SerializeField] private TutorialController tutorialController;

    private ICameraTracker cameraTracker;
    private UIManager uIManager;
    private SimContext ctx;

    [Header("References - UI (Manual)")]
    [SerializeField] private TMP_InputField objectNameInputField;
    [SerializeField] private TMP_InputField massInput;
    [SerializeField] private TMP_InputField radiusInput;
    [SerializeField] private TMP_InputField positionInput;
    [SerializeField] private Button placeObjectButton;
    [SerializeField] private TextMeshProUGUI feedbackText;

    [Header("References - UI (Kepler)")]
    [SerializeField] private TMP_InputField kepNameInputField;
    [SerializeField] private TMP_InputField kepMassInputField;
    [SerializeField] private TMP_InputField kepADegOrMetersInputField;   // a (meters)
    [SerializeField] private TMP_InputField kepEccInputField;            // e
    [SerializeField] private TMP_InputField kepIncDegInputField;         // i (deg)
    [SerializeField] private TMP_InputField kepRAANDegInputField;        // Ω (deg)
    [SerializeField] private TMP_InputField kepArgPDegInputField;        // ω (deg)
    [SerializeField] private TMP_InputField kepTrueAnomDegInputField;    // ν (deg)
    [SerializeField] private Button placeKeplerObjectButton;

    [Header("References - UI (TLE)")]
    [SerializeField] private TMP_InputField tleNameInputField;
    [SerializeField] private TMP_InputField tleMassInputField;
    [SerializeField] private TMP_InputField tleLine1InputField;
    [SerializeField] private TMP_InputField tleLine2InputField;
    [SerializeField] private Button placeTLEObjectButton;

    [Header("Units & Central Body")]
    [Tooltip("Meters per 1 sim unit. If world units are kilometers, set this to 1000.")]
    [SerializeField] private double metersPerUnit = 10000.0;

    [Tooltip("Standard gravitational parameter μ = GM of the central body, in m^3/s^2 (Earth by default).")]
    [SerializeField] private double mu = 3.986004418e14;

    [Tooltip("Earth radius in meters (used for simple safety checks).")]
    [SerializeField] private double earthRadiusMeters = 6378137.0;

    [Header("Ghost Preview")]
    [SerializeField] private GameObject ghostPreviewPrefab;
    private GameObject ghostInstance;
    private bool ghostObjectPlaced;
    private bool clearingPosition;

    [Header("Placement State")]
    [SerializeField] private GameObject lastPlacedGameObject;   // manual-placement blocker

    private const int MaxSatelliteNameLength = 15;

    private static readonly PlacementValidators.RangeF MassRange = new(500f, 1000000f);
    private static readonly PlacementValidators.RangeF RadiusClamp = new(0.5f, 1.0f);
    private static readonly PlacementValidators.DistanceBoundsF PosBounds = new(638f, 5000f);

    // Public for RandomSatelliteSpawner
    public double Mu => mu;
    public double EarthRadiusMeters => earthRadiusMeters;
    public double MetersPerUnit => metersPerUnit;

    /// <summary>
    /// Injects the simulation context and wires dependencies + UI listeners.
    /// Also creates and hides the ghost preview if a prefab is provided.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        cameraTracker = ctx.CameraTracker;
        uIManager = ctx.UIManager;

        if (tutorialController.inTutorialMode)
        {
            massInput.onValueChanged.AddListener(OnMassInputChanged);
            radiusInput.onValueChanged.AddListener(OnRadiusInputChanged);
        }

        positionInput.onValueChanged.AddListener(OnPositionInputChanged);

        if (ghostPreviewPrefab != null)
        {
            ghostInstance = Instantiate(ghostPreviewPrefab);
            HideGhost();
        }
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

        if (!PlacementValidators.TryGetName(objectNameInputField, "Satellite", satelliteSpawner.SatelliteCount, MaxSatelliteNameLength, out var name, out var err))
        {
            SetFeedback(err);
            return;
        }

        if (!PlacementValidators.TryGetPositionOrDefault(positionInput, mainCamera.transform, 10f, PosBounds, out var pos, out err))
        {
            SetFeedback(err);
            return;
        }

        if (!PlacementValidators.TryGetRadius(radiusInput, RadiusClamp, out var radius, out err))
        {
            SetFeedback(err);
            return;
        }

        if (!PlacementValidators.TryGetMass(massInput, MassRange, out var mass, out err))
        {
            SetFeedback(err);
            return;
        }

        HideGhost();

        // radius currently unused visually; using default scale
        Vector3 radiusDefault = Vector3.one;

        lastPlacedGameObject = satelliteSpawner.CreatePlaceholder(name, pos, radiusDefault, mass, velocityDragManager);
        PreviewSilently(lastPlacedGameObject.transform);

        LockManualPlacementInputs(true);
        ClearAllFields();

        tutorialController.hasSatelliteBeenPlaced = true;
        SetFeedback(
            "Setting Satellite Velocity:\n\n" +
            "• Click the satellite and drag.\n" +
            "• Set the desired direction.\n" +
            "• Use input field to adjust speed."
        );

        EventSystem.current.SetSelectedGameObject(null);
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

        if (!PlacementValidators.TryGetName(kepNameInputField, "Kepler Sat", satelliteSpawner.SatelliteCount + 1, MaxSatelliteNameLength, out var name, out var err))
        {
            SetFeedback(err);
            return;
        }

        if (!PlacementValidators.TryGetMass(kepMassInputField, MassRange, out var mass, out err))
        {
            SetFeedback(err);
            return;
        }

        if (!PlacementValidators.TryGetDouble(kepADegOrMetersInputField, out double aMeters))
        {
            SetFeedback("Invalid semi-major axis 'a'.");
            return;
        }

        if (!PlacementValidators.TryGetDouble(kepEccInputField, out double e) || e < 0.0 || e >= 1.0)
        {
            SetFeedback("Invalid eccentricity 'e'. Use 0 ≤ e < 1.");
            return;
        }

        if (!PlacementValidators.TryGetDouble(kepIncDegInputField, out double iDeg) ||
            !PlacementValidators.TryGetDouble(kepRAANDegInputField, out double raanDeg) ||
            !PlacementValidators.TryGetDouble(kepArgPDegInputField, out double argpDeg) ||
            !PlacementValidators.TryGetDouble(kepTrueAnomDegInputField, out double trueAnomDeg))
        {
            SetFeedback("Invalid angle(s): i / RAAN / ω / ν.");
            return;
        }

        try
        {
            var (rEci, vEci) = KeplerUtils.FromElements(aMeters, e, iDeg, raanDeg, argpDeg, trueAnomDeg, mu);

            double rp = aMeters * (1.0 - e);
            if (rp <= earthRadiusMeters * 1.001)
            {
                double altKm = (rp - earthRadiusMeters) / 1000.0;
                SetFeedback($"Orbit intersects Earth (perigee alt {altKm:F1} km). Increase 'a' or reduce 'e'.");
                return;
            }

            var pos = FrameUtils.EciToUnity(rEci, metersPerUnit);
            var vel = FrameUtils.VelEciToUnity(vEci, metersPerUnit);

            satelliteSpawner.SpawnSatellite(name, pos, mass, vel, trackAfterSpawn: true);

            ClearAllFields();
            SetFeedback($"Placed '{name}' from Keplerian elements.");

            lastPlacedGameObject = null;
            UpdateTrackCamButtonState(false);
        }
        catch (Exception ex)
        {
            SetFeedback($"Kepler placement failed: {ex.Message}");
        }
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

        if (!PlacementValidators.TryGetMass(tleMassInputField, MassRange, out var mass, out var err))
        {
            SetFeedback(err);
            return;
        }

        string name = !string.IsNullOrWhiteSpace(tleNameInputField?.text)
            ? tleNameInputField.text.Trim()
            : $"TLE Satellite {satelliteSpawner.NextSatelliteIndex}";

        DateTime whenUtc = DateTime.UtcNow;

        if (!TLEParser.TryPropagate(
                tleLine1InputField.text,
                tleLine2InputField.text,
                whenUtc,
                out Vector3d rEci_m,
                out Vector3d vEci_mps,
                out DateTime epochUtc))
        {
            SetFeedback("Invalid TLE input or propagation failed.");
            return;
        }

        if (rEci_m.magnitude <= earthRadiusMeters * 1.001)
        {
            SetFeedback("Computed position intersects Earth. Check TLE/time.");
            return;
        }

        var spawnPos = FrameUtils.EciToUnity(rEci_m, metersPerUnit);
        var spawnVel = FrameUtils.VelEciToUnity(vEci_mps, metersPerUnit);

        velocityDragManager?.trajectoryRenderer?.preManeuverLine?.Clear();

        satelliteSpawner.SpawnSatellite(name, spawnPos, mass, spawnVel, trackAfterSpawn: true);

        ClearAllFields();
        SetFeedback(
            $"Placed '{name}' from TLE at {whenUtc:yyyy-MM-dd HH:mm:ss}Z " +
            $"(epoch {epochUtc:yyyy-MM-dd HH:mm:ss}Z)."
        );

        lastPlacedGameObject = null;
        UpdateTrackCamButtonState(false);
    }

    /// <summary>
    /// Checks whether a new placement can start (no pending velocity-set,
    /// and camera in Free mode).
    /// </summary>
    private bool CanStartPlacement(out string error)
    {
        if (lastPlacedGameObject != null)
        {
            error = $"Finish setting velocity for '{lastPlacedGameObject.name}' first.";
            return false;
        }

        if (cameraTracker == null)
        {
            error = "CameraTracker not set.";
            return false;
        }

        if (cameraTracker.Mode != CameraMode.Free)
        {
            error = $"Switch to FreeCam (current: {cameraTracker.Mode}).";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Locks or unlocks manual placement inputs and related UI controls.
    /// </summary>
    private void LockManualPlacementInputs(bool locked)
    {
        if (objectNameInputField != null) objectNameInputField.interactable = !locked;
        if (positionInput != null) positionInput.interactable = !locked;
        if (massInput != null) massInput.interactable = !locked;
        if (radiusInput != null) radiusInput.interactable = !locked;
        if (placeObjectButton != null) placeObjectButton.interactable = !locked;
        if (uIManager?.placementModeButton != null) uIManager.placementModeButton.interactable = !locked;
        if (uIManager?.randomSatelliteButton != null) uIManager.randomSatelliteButton.interactable = !locked;

        UpdateTrackCamButtonState(false);
    }

    /// <summary>
    /// Enables or disables the track camera button.
    /// </summary>
    private void UpdateTrackCamButtonState(bool state)
    {
        if (uIManager?.trackCamButton == null) return;
        uIManager.trackCamButton.interactable = state;
    }

    /// <summary>
    /// Sets feedback text into the UI panel.
    /// </summary>
    private void SetFeedback(string msg)
    {
        if (feedbackText != null)
            feedbackText.text = msg ?? string.Empty;
    }

    /// <summary>
    /// Clears an input field and removes focus.
    /// </summary>
    private void ClearAndUnfocusInputField(TMP_InputField inputField)
    {
        if (inputField == null) return;

        clearingPosition = true;
        inputField.text = string.Empty;
        EventSystem.current.SetSelectedGameObject(null);
        clearingPosition = false;
    }

    /// <summary>
    /// Clears all input fields for manual, Kepler, and TLE placement.
    /// </summary>
    public void ClearAllFields()
    {
        ClearAndUnfocusInputField(radiusInput);
        ClearAndUnfocusInputField(positionInput);
        ClearAndUnfocusInputField(objectNameInputField);
        ClearAndUnfocusInputField(massInput);

        ClearAndUnfocusInputField(tleNameInputField);
        ClearAndUnfocusInputField(tleMassInputField);
        ClearAndUnfocusInputField(tleLine1InputField);
        ClearAndUnfocusInputField(tleLine2InputField);

        ClearAndUnfocusInputField(kepNameInputField);
        ClearAndUnfocusInputField(kepMassInputField);
        ClearAndUnfocusInputField(kepADegOrMetersInputField);
        ClearAndUnfocusInputField(kepEccInputField);
        ClearAndUnfocusInputField(kepIncDegInputField);
        ClearAndUnfocusInputField(kepRAANDegInputField);
        ClearAndUnfocusInputField(kepArgPDegInputField);
        ClearAndUnfocusInputField(kepTrueAnomDegInputField);
    }

    /// <summary>
    /// Cancels the current placement, if any, and returns camera to its previous tracking target.
    /// </summary>
    public void CancelPlacement()
    {
        if (lastPlacedGameObject != null)
        {
            Destroy(lastPlacedGameObject);
            lastPlacedGameObject = null;
        }

        SetFeedback(string.Empty);

        cameraTracker?.ReturnToTracking();
    }

    /// <summary>
    /// Runs a "silent" camera preview of a transform in free cam mode.
    /// </summary>
    private void PreviewSilently(Transform t)
    {
        if (cameraTracker == null || t == null) return;

        cameraTracker.BeginUiSuppress();
        cameraTracker.PreviewPlaceholderInFree(t);
        cameraTracker.EndUiSuppress();
    }

    /// <summary>
    /// Tutorial hook: validates mass entry and updates tutorial flags/feedback.
    /// </summary>
    private void OnMassInputChanged(string input)
    {
        if (mainCamera == null) return;

        if (string.IsNullOrWhiteSpace(input))
        {
            SetFeedback(string.Empty);
            return;
        }

        if (ParsingUtils.TryParseMass(input, out _))
        {
            tutorialController.hasMassBeenEnteredForSatellite = true;
            SetFeedback(string.Empty);
        }
        else
        {
            SetFeedback("Invalid Mass: Should be between 500-1,000,000. Units are in kg by default.");
        }
    }

    /// <summary>
    /// Tutorial hook: validates radius entry and updates tutorial flags/feedback.
    /// </summary>
    private void OnRadiusInputChanged(string input)
    {
        if (mainCamera == null) return;

        if (string.IsNullOrWhiteSpace(input))
        {
            SetFeedback(string.Empty);
            return;
        }

        if (ParsingUtils.TryParseVector3(input, out _))
        {
            tutorialController.hasRadiusBeenEnteredForSatellite = true;
            SetFeedback(string.Empty);
        }
        else
        {
            SetFeedback("Invalid Radius: Format is x,y,z. Example 1,2,1");
        }
    }

    /// <summary>
    /// Handles manual position changes, drives ghost preview visibility,
    /// and enforces a simple radial distance constraint.
    /// </summary>
    private void OnPositionInputChanged(string input)
    {
        if (mainCamera == null) return;

        if (ghostObjectPlaced && !clearingPosition && positionInput != null && positionInput.isFocused)
        {
            cameraTracker.BreakToFreeCam();
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
            float minDistance = 640f;
            float maxDistance = 5000f;

            if (distanceFromEarth < minDistance || distanceFromEarth > maxDistance)
            {
                ghostObjectPlaced = false;
                if (ghostInstance != null) ghostInstance.SetActive(false);

                SetFeedback("Position magnitude must be between 640 and 5000. Example: 641,0,0");
                return;
            }

            tutorialController.hasPositionBeenEnteredForSatellite = true;

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
    /// Shows or moves the ghost preview to a world-space position and previews it in the camera.
    /// </summary>
    private void ShowGhostAt(Vector3 pos)
    {
        if (!ghostInstance) return;

        ghostInstance.SetActive(true);
        ghostInstance.transform.position = pos;
        PreviewSilently(ghostInstance.transform);
        ghostObjectPlaced = true;
    }

    /// <summary>
    /// Hides the ghost preview, if present.
    /// </summary>
    private void HideGhost()
    {
        if (ghostInstance) ghostInstance.SetActive(false);
        ghostObjectPlaced = false;
    }

    /// <summary>
    /// Clears any "pending placement" state so another manual placement can start.
    /// </summary>
    public void ResetLastPlacedGameObject()
    {
        SetFeedback(string.Empty);
        lastPlacedGameObject = null;
    }
}
