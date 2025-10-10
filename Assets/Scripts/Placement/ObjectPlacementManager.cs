using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System;

/// <summary>
/// Manages user-driven object placement via three workflows:
/// 1) Manual placement with drag-to-set-velocity
/// 2) Keplerian element placement
/// 3) TLE-based placement
///
/// The manager validates inputs, spawns placeholder bodies, coordinates the
/// velocity-drag flow, and updates UI feedback. It requires Free camera mode
/// before placement begins and temporarily locks UI where appropriate.
/// </summary>
public class ObjectPlacementManager : MonoBehaviour
{
    [Header("References - Core")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private GameObject spherePrefab;                 // Visual placeholder prefab (no NBody component)
    [SerializeField] private TrajectoryRenderer trajectoryRenderer;
    [SerializeField] private VelocityDragManager velocityDragManager;
    [SerializeField] private TutorialController tutorialController;

    // Set at runtime (Initialize)
    private ICameraTracker cameraTracker;
    private UIManager uIManager;
    private SimContext ctx;

    [Header("References - UI (Manual)")]
    [SerializeField] private TMP_InputField objectNameInputField;
    [SerializeField] private TMP_InputField massInput;
    [SerializeField] private TMP_InputField radiusInput;
    [SerializeField] private TMP_InputField positionInput;
    [SerializeField] private Button placeObjectButton;                // Optional hookup
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
    [SerializeField] private Button placeKeplerObjectButton;             // Optional hookup

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
    private bool ghostObjectPlaced = false;
    private bool clearingPosition = false;

    [Header("Placement State")]
    [SerializeField] private GameObject lastPlacedGameObject;         // Active manual-placement blocker
    private int satelliteCount = 0;

    private const int MaxSatelliteNameLength = 15;

    // Centralized validation ranges (mirrors PlacementValidators)
    private static readonly PlacementValidators.RangeF MassRange = new(500f, 1_000_000f);
    private static readonly PlacementValidators.RangeF RadiusClamp = new(0.5f, 1.0f);
    private static readonly PlacementValidators.DistanceBoundsF PosBounds = new(638f, 5000f);

    /// <summary>
    /// Injects the simulation context and wires dependencies and UI listeners.
    /// Also creates and hides the ghost preview if a prefab is provided.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        trajectoryRenderer = ctx.TrajectoryRenderer;
        tutorialController = ctx.TutorialController;
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

    // ========== 1) Manual placement ==========

    /// <summary>
    /// Begins manual placement after validating state and inputs. Spawns a satellite
    /// placeholder, enters the velocity-drag step, and locks related UI until the
    /// user completes or cancels.
    /// </summary>
    public void StartPlacement()
    {
        if (!CanStartPlacement(out var gateErr)) { feedbackText.text = gateErr; return; }

        if (!PlacementValidators.TryGetName(objectNameInputField, "Satellite", satelliteCount, MaxSatelliteNameLength, out var name, out var err)) { feedbackText.text = err; return; }
        if (!PlacementValidators.TryGetPositionOrDefault(positionInput, mainCamera.transform, 10f, PosBounds, out var pos, out err)) { feedbackText.text = err; return; }
        if (!PlacementValidators.TryGetRadius(radiusInput, RadiusClamp, out var radius, out err)) { feedbackText.text = err; return; }
        if (!PlacementValidators.TryGetMass(massInput, MassRange, out var mass, out err)) { feedbackText.text = err; return; }

        HideGhost();

        lastPlacedGameObject = CreateSatellite(name, pos, radius, mass, null);
        PreviewSilently(lastPlacedGameObject.transform);

        // Manual placement expects a drag-to-set-velocity step -> lock inputs until user finishes/cancels
        LockManualPlacementInputs(true);
        ClearAllFields();

        tutorialController.hasSatelliteBeenPlaced = true;
        feedbackText.text =
            "Setting Satellite Velocity:\n\n" +
            "• Click the satellite and drag.\n" +
            "• Set the desired direction.\n" +
            "• Use input field to adjust speed.";
        EventSystem.current.SetSelectedGameObject(null);
    }

    // ========== 2) Keplerian placement ==========

    /// <summary>
    /// Places an object using Keplerian orbital elements. Validates elements,
    /// converts to ECI position/velocity, transforms into Unity space, and spawns
    /// a fully-initialized satellite (no velocity-drag step).
    /// </summary>
    public void PlaceObjectFromKepler()
    {
        if (!CanStartPlacement(out var gateErr)) { feedbackText.text = gateErr; return; }

        if (!PlacementValidators.TryGetName(kepNameInputField, "Kepler Sat", satelliteCount + 1, MaxSatelliteNameLength, out var name, out var err)) { feedbackText.text = err; return; }
        if (!PlacementValidators.TryGetMass(kepMassInputField, MassRange, out var mass, out err)) { feedbackText.text = err; return; }

        if (!PlacementValidators.TryGetDouble(kepADegOrMetersInputField, out double aMeters)) { feedbackText.text = "Invalid semi-major axis 'a'."; return; }
        if (!PlacementValidators.TryGetDouble(kepEccInputField, out double e) || e < 0.0 || e >= 1.0) { feedbackText.text = "Invalid eccentricity 'e'. Use 0 ≤ e < 1."; return; }
        if (!PlacementValidators.TryGetDouble(kepIncDegInputField, out double iDeg) ||
            !PlacementValidators.TryGetDouble(kepRAANDegInputField, out double raanDeg) ||
            !PlacementValidators.TryGetDouble(kepArgPDegInputField, out double argpDeg) ||
            !PlacementValidators.TryGetDouble(kepTrueAnomDegInputField, out double trueAnomDeg))
        { feedbackText.text = "Invalid angle(s): i / RAAN / ω / ν."; return; }

        try
        {
            var (rEci, vEci) = KeplerUtils.FromElements(aMeters, e, iDeg, raanDeg, argpDeg, trueAnomDeg, mu);

            // Perigee must remain above Earth
            double rp = aMeters * (1.0 - e);
            if (rp <= earthRadiusMeters * 1.001)
            {
                feedbackText.text = $"Orbit intersects Earth (perigee alt {(rp - earthRadiusMeters) / 1000.0:F1} km). Increase 'a' or reduce 'e'.";
                return;
            }

            var pos = FrameUtils.EciToUnity(rEci, metersPerUnit);
            var vel = FrameUtils.VelEciToUnity(vEci, metersPerUnit);

            lastPlacedGameObject = CreateSatellite(name, pos, null, mass, vel);
            ClearAllFields();
            feedbackText.text = $"Placed '{name}' from Keplerian elements.";

            // Complete immediately (no manual drag step)
            lastPlacedGameObject = null;
            UpdateTrackCamButtonState();
        }
        catch (Exception ex)
        {
            feedbackText.text = $"Kepler placement failed: {ex.Message}";
        }
    }

    // ========== 3) TLE placement ==========

    /// <summary>
    /// Places an object using TLE lines propagated to the current UTC time.
    /// Validates TLE input, converts the propagated ECI state to Unity space,
    /// and spawns a fully-initialized satellite (no velocity-drag step).
    /// </summary>
    public void PlaceObjectFromTLE()
    {
        if (!CanStartPlacement(out var gateErr)) { feedbackText.text = gateErr; return; }

        if (!PlacementValidators.TryGetMass(tleMassInputField, MassRange, out var mass, out var err)) { feedbackText.text = err; return; }

        string name = !string.IsNullOrWhiteSpace(tleNameInputField?.text)
            ? tleNameInputField.text.Trim()
            : $"TLE Satellite {satelliteCount + 1}";

        DateTime whenUtc = DateTime.UtcNow;

        if (!TLEParser.TryPropagate(tleLine1InputField.text, tleLine2InputField.text, whenUtc,
                                    out Vector3d rEci_m, out Vector3d vEci_mps, out DateTime epochUtc))
        {
            feedbackText.text = "Invalid TLE input or propagation failed.";
            return;
        }

        // Basic safety: current radius must be above Earth
        if (rEci_m.magnitude <= earthRadiusMeters * 1.001)
        {
            feedbackText.text = "Computed position intersects Earth. Check TLE/time.";
            return;
        }

        var spawnPos = FrameUtils.EciToUnity(rEci_m, metersPerUnit);
        var spawnVel = FrameUtils.VelEciToUnity(vEci_mps, metersPerUnit);

        // Clear any pre-maneuver line from manual flow
        velocityDragManager?.trajectoryRenderer?.preManeuverLine?.Clear();

        lastPlacedGameObject = CreateSatellite(name, spawnPos, null, mass, spawnVel);
        ClearAllFields();
        feedbackText.text = $"Placed '{name}' from TLE at {whenUtc:yyyy-MM-dd HH:mm:ss}Z (epoch {epochUtc:yyyy-MM-dd HH:mm:ss}Z).";

        // Complete immediately (no manual drag step)
        lastPlacedGameObject = null;
        UpdateTrackCamButtonState();
    }

    /// <summary>
    /// Instantiates the satellite placeholder, updates VelocityDragManager, and refreshes
    /// the camera tracker's body list. Applies an initial velocity if provided.
    /// </summary>
    private GameObject CreateSatellite(string name, Vector3 position, Vector3? scale, float mass, Vector3? initialVelocity)
    {
        satelliteCount++;
        var go = Instantiate(spherePrefab);
        go.name = name;
        go.tag = "Satellite";    // Ensure consistent tagging
        go.transform.position = position;
        go.transform.localScale = scale ?? Vector3.one;

        cameraTracker.RefreshBodiesList();

        if (velocityDragManager != null)
        {
            velocityDragManager.ResetDragManager();
            velocityDragManager.planet = go;
            velocityDragManager.placeholderMass = mass;
            if (initialVelocity.HasValue)
                velocityDragManager.ApplyVelocityToPlanet(initialVelocity.Value);
        }
        return go;
    }

    /// <summary>
    /// Validates that a placement can begin:
    /// - No unfinished manual placement is active
    /// - CameraTracker is available
    /// - Camera is in Free mode
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
    /// Enables/disables manual placement inputs and related UI controls during the drag step.
    /// </summary>
    private void LockManualPlacementInputs(bool locked)
    {
        if (objectNameInputField != null) objectNameInputField.interactable = !locked;
        if (positionInput != null) positionInput.interactable = !locked;
        if (massInput != null) massInput.interactable = !locked;
        if (radiusInput != null) radiusInput.interactable = !locked;
        if (placeObjectButton != null) placeObjectButton.interactable = !locked;
        if (uIManager.placementModeButton != null) uIManager.placementModeButton.interactable = !locked;
        UpdateTrackCamButtonState();
    }

    /// <summary>
    /// Disables the track-cam button when placement locks the UI.
    /// </summary>
    private void UpdateTrackCamButtonState()
    {
        if (uIManager?.trackCamButton == null) return;
        uIManager.trackCamButton.interactable = false;
    }

    /// <summary>
    /// Clears a TMP input field and removes focus to avoid accidental re-entry.
    /// </summary>
    private void ClearAndUnfocusInputField(TMP_InputField inputField)
    {
        if (inputField == null) return;
        clearingPosition = true;
        inputField.text = "";
        EventSystem.current.SetSelectedGameObject(null);
        clearingPosition = false;
    }

    /// <summary>
    /// Clears all supported input fields across Manual, TLE, and Kepler tabs.
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
    /// Cancels an in-progress manual placement, removes the placeholder, clears
    /// feedback and drag visuals, and returns the camera to its prior tracking state.
    /// </summary>
    public void CancelPlacement()
    {
        if (lastPlacedGameObject != null)
        {
            Destroy(lastPlacedGameObject);
            lastPlacedGameObject = null;
        }

        feedbackText.text = "";

        if (velocityDragManager != null && velocityDragManager.dragLineRenderer != null)
            velocityDragManager.dragLineRenderer.positionCount = 0;

        if (cameraTracker != null) cameraTracker.ReturnToTracking();
    }

    /// <summary>
    /// Previews a transform for the camera tracker without UI side effects,
    /// maintaining Free mode.
    /// </summary>
    private void PreviewSilently(Transform t)
    {
        if (cameraTracker == null || t == null) return;
        cameraTracker.BeginUiSuppress();
        cameraTracker.PreviewPlaceholderInFree(t);   // Stays in Free mode
        cameraTracker.EndUiSuppress();
    }

    /// <summary>
    /// Tutorial hook: validates mass input and updates tutorial flags and feedback.
    /// </summary>
    private void OnMassInputChanged(string input)
    {
        if (mainCamera == null) return;

        if (string.IsNullOrWhiteSpace(input))
        {
            feedbackText.text = "";
            return;
        }

        if (ParsingUtils.TryParseMass(input, out _))
        {
            tutorialController.hasMassBeenEnteredForSatellite = true;
            feedbackText.text = "";
        }
        else
        {
            feedbackText.text = "Invalid. Mass should be 500-1,000,000 kg.";
        }
    }

    /// <summary>
    /// Tutorial hook: validates radius input and updates tutorial flags and feedback.
    /// Expects a numeric Vector3 format.
    /// </summary>
    private void OnRadiusInputChanged(string input)
    {
        if (mainCamera == null) return;

        if (string.IsNullOrWhiteSpace(input))
        {
            feedbackText.text = "";
            return;
        }

        if (ParsingUtils.TryParseVector3(input, out _))
        {
            tutorialController.hasRadiusBeenEnteredForSatellite = true;
            feedbackText.text = "";
        }
        else
        {
            feedbackText.text = "Invalid format. Use numeric x,y,z values.";
        }
    }

    /// <summary>
    /// Validates manual position input, manages ghost preview visibility/position,
    /// and breaks to FreeCam when the user edits the field during an active preview.
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
            feedbackText.text = "";
            return;
        }

        if (ParsingUtils.TryParseVector3(input, out Vector3 targetPosition))
        {
            float distanceFromEarth = Vector3.Distance(Vector3.zero, targetPosition);
            float minDistance = 638f;
            float maxDistance = 5000f;

            if (distanceFromEarth < minDistance || distanceFromEarth > maxDistance)
            {
                ghostObjectPlaced = false;
                if (ghostInstance != null) ghostInstance.SetActive(false);
                feedbackText.text = $"Distance must be between {minDistance * 10f:N0} km and {maxDistance * 10f:N0} km from Earth.";
                return;
            }

            tutorialController.hasPositionBeenEnteredForSatellite = true;

            ShowGhostAt(targetPosition);
            feedbackText.text = "";
        }
        else
        {
            HideGhost();
            feedbackText.text = "Invalid format. Use numeric x,y,z values.";
        }
    }

    /// <summary>
    /// Activates and positions the ghost preview, and requests a silent camera preview.
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
    /// Hides the ghost preview and resets its active state flag.
    /// </summary>
    private void HideGhost()
    {
        if (ghostInstance) ghostInstance.SetActive(false);
        ghostObjectPlaced = false;
    }

    /// <summary>
    /// Clears the active manual-placement blocker without destroying the object.
    /// Useful when external flows complete placement.
    /// </summary>
    public void ResetLastPlacedGameObject()
    {
        feedbackText.text = "";
        lastPlacedGameObject = null;
    }
}
