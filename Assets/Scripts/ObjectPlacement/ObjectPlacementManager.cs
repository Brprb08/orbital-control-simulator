using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System;                 // for Math, ArgumentException, etc.
using System.Globalization;   // if you're using double.TryParse(..., CultureInfo.InvariantCulture)


public class ObjectPlacementManager : MonoBehaviour
{
    [Header("References - Core")]
    public Camera mainCamera;
    public GameObject spherePrefab; // placeholder without NBody
    public TrajectoryRenderer trajectoryRenderer;
    private ICameraTracker cameraTracker;
    public VelocityDragManager velocityDragManager;
    public TutorialController tutorialController;
    private UIManager uIManager;
    private NBody lastManualNBody;

    [Header("References - UI")]
    public TMP_InputField objectNameInputField;
    public TMP_InputField nameInputField;
    public TMP_InputField massInput;
    public TMP_InputField massInputField;
    public TMP_InputField radiusInput;
    public TMP_InputField positionInput;
    public TextMeshProUGUI feedbackText;
    public Button placeObjectButton;

    [Header("Keplerian Placement")]
    public TMP_InputField kepNameInputField;
    public TMP_InputField kepMassInputField;
    public TMP_InputField kepADegOrMetersInputField;   // "a" (semi-major axis)
    public TMP_InputField kepEccInputField;            // e
    public TMP_InputField kepIncDegInputField;         // i (deg)
    public TMP_InputField kepRAANDegInputField;        // Ω (deg)
    public TMP_InputField kepArgPDegInputField;        // ω (deg)
    public TMP_InputField kepTrueAnomDegInputField;    // ν (deg)
    public Button placeKeplerObjectButton;

    [Header("Units & Central Body")]
    [Tooltip("Meters per 1 sim unit. If your world units are kilometers, set this to 1000.")]
    public double metersPerUnit = 10000.0;

    [Tooltip("Standard gravitational parameter μ = GM of central body, in m^3/s^2. Earth default.")]
    public double mu = 3.986004418e14;

    [Tooltip("Earth radius in meters (used only for sanity checks if you enable them).")]
    public double earthRadiusMeters = 6378137.0;

    [Header("TLE Placement")]
    public TMP_InputField tleNameInputField;
    public TMP_InputField tleMassInputField;
    public TMP_InputField tleLine1InputField;
    public TMP_InputField tleLine2InputField;
    public Button placeTLEObjectButton;

    [Header("Ghost Preview")]
    public GameObject ghostPreviewPrefab;
    private GameObject ghostInstance;
    private bool ghostObjectPlaced = false;
    private bool clearingPosition = false;

    [Header("Placement State")]
    public GameObject lastPlacedGameObject; // last placeholder
    private int satelliteCount = 0;

    private const int MaxSatelliteNameLength = 15;

    private SimContext ctx;

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        trajectoryRenderer = ctx.TrajectoryRenderer;
        tutorialController = ctx.TutorialController;
        cameraTracker = ctx.CameraTracker; // make sure this is set
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
            ghostInstance.SetActive(false);
        }
    }

    public void StartPlacement()
    {
        if (lastPlacedGameObject != null)
        {
            feedbackText.text = "You must set the velocity of the current planet before placing another.";
            return;
        }

        if (cameraTracker.Mode != CameraMode.Free)
        {
            feedbackText.text = "You must be in FreeCam mode to place planets.";
            return;
        }

        string customName = objectNameInputField?.text;
        if (!string.IsNullOrWhiteSpace(customName) && customName.Length > MaxSatelliteNameLength)
        {
            feedbackText.text = $"Satellite name too long. Max {MaxSatelliteNameLength} characters.";
            return;
        }
        if (string.IsNullOrWhiteSpace(customName)) customName = $"Satellite {satelliteCount}";

        Vector3 parsedPosition;
        if (string.IsNullOrWhiteSpace(positionInput.text))
        {
            parsedPosition = mainCamera.transform.position + mainCamera.transform.forward * 10f;
        }
        else
        {
            if (!ParsingUtils.TryParseVector3(positionInput.text, out parsedPosition))
            {
                feedbackText.text = "Invalid position input. Please use numeric x,y,z format.";
                return;
            }

            float distanceFromEarth = Vector3.Distance(Vector3.zero, parsedPosition);
            float minDistance = 638f;
            float maxDistance = 5000f;
            if (distanceFromEarth < minDistance || distanceFromEarth > maxDistance)
            {
                feedbackText.text = $"Invalid position: must be between {minDistance * 10f:N0} km and {maxDistance * 10f:N0} km from Earth's center.";
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(radiusInput.text))
        {
            feedbackText.text = "Please enter a radius in the format x,y,z. Numbers only.";
            return;
        }
        if (!ParsingUtils.TryParseVector3(radiusInput.text, out Vector3 parsedRadius))
        {
            feedbackText.text = "Invalid radius. Use numeric x,y,z.";
            return;
        }

        if (string.IsNullOrWhiteSpace(massInput.text))
        {
            feedbackText.text = "Please enter a numeric mass between 500 and 1,000,000 kg.";
            return;
        }
        if (!ParsingUtils.TryParseMass(massInput.text, out float mass))
        {
            feedbackText.text = "Invalid mass. Enter a number between 500 and 1,000,000.";
            return;
        }

        float placeholderMass = mass;
        parsedRadius = new Vector3(
            Mathf.Clamp(parsedRadius.x, .5f, 1f),
            Mathf.Clamp(parsedRadius.y, .5f, 1f),
            Mathf.Clamp(parsedRadius.z, .5f, 1f)
        );

        lastPlacedGameObject = Instantiate(spherePrefab);
        lastPlacedGameObject.transform.localScale = new Vector3(parsedRadius.x, parsedRadius.y, parsedRadius.z);
        lastPlacedGameObject.transform.position = parsedPosition;

        if (ghostInstance != null) ghostInstance.SetActive(false);

        satelliteCount++;
        lastPlacedGameObject.name = customName;
        lastPlacedGameObject.tag = "Planet";

        if (velocityDragManager != null)
        {
            velocityDragManager.ResetDragManager();
            velocityDragManager.planet = lastPlacedGameObject;
            velocityDragManager.placeholderMass = placeholderMass;
        }

        // TrackSilently(lastPlacedGameObject.transform);
        PreviewSilently(lastPlacedGameObject.transform);

        uIManager.trackCamButton.interactable = false;
        uIManager.placementModeButton.interactable = false;

        ClearAndUnfocusInputField(radiusInput);
        ClearAndUnfocusInputField(positionInput);
        ClearAndUnfocusInputField(objectNameInputField);
        ClearAndUnfocusInputField(massInput);

        if (nameInputField != null && massInputField != null && radiusInput != null)
        {
            nameInputField.interactable = false;
            positionInput.interactable = false;
            massInputField.interactable = false;
            radiusInput.interactable = false;
            placeObjectButton.interactable = false;
        }

        tutorialController.hasSatelliteBeenPlaced = true;

        feedbackText.text =
            "Setting Satellite Velocity:\n\n" +
            "• Click the satellite and drag.\n" +
            "• Set the desired direction.\n" +
            "• Use input field to adjust speed.";
        EventSystem.current.SetSelectedGameObject(null);
    }

    public void PlaceObjectFromTLE()
    {
        if (!TLEParser.TryParseTLE(tleLine1InputField.text, tleLine2InputField.text, out Vector3 position, out Vector3 velocity))
        {
            feedbackText.text = "Invalid TLE input. Check formatting.";
            return;
        }

        string name = !string.IsNullOrWhiteSpace(tleNameInputField.text) ? tleNameInputField.text : $"TLE Satellite {satelliteCount + 1}";
        if (!ParsingUtils.TryParseMass(tleMassInputField.text, out float mass))
        {
            feedbackText.text = "Invalid mass. Enter a number between 500 and 1,000,000.";
            return;
        }

        // Clear any lines that might have been left from manual placement mode
        velocityDragManager.trajectoryRenderer.preManeuverLine.Clear();

        satelliteCount++;
        lastPlacedGameObject = Instantiate(spherePrefab);
        lastPlacedGameObject.name = name;
        lastPlacedGameObject.tag = "Satellite";
        lastPlacedGameObject.transform.position = position;
        lastPlacedGameObject.transform.localScale = Vector3.one;

        if (velocityDragManager != null)
        {
            velocityDragManager.planet = lastPlacedGameObject;
            velocityDragManager.placeholderMass = mass;
        }

        cameraTracker.RefreshBodiesList();

        if (velocityDragManager != null)
        {
            velocityDragManager.planet = lastPlacedGameObject;
            velocityDragManager.placeholderMass = mass;
            velocityDragManager.ApplyVelocityToPlanet(velocity);
        }

        ClearAndUnfocusInputField(tleNameInputField);
        ClearAndUnfocusInputField(tleMassInputField);
        ClearAndUnfocusInputField(tleLine1InputField);
        ClearAndUnfocusInputField(tleLine2InputField);
    }

    public void PlaceObjectFromKepler()
    {
        // Basic guarding like your other entry points
        if (lastPlacedGameObject != null)
        {
            feedbackText.text = "Finish setting the current satellite's velocity before placing another.";
            return;
        }
        if (cameraTracker.Mode != CameraMode.Free)
        {
            feedbackText.text = "Switch to FreeCam to place satellites.";
            return;
        }

        // --- Parse inputs ---
        string name = !string.IsNullOrWhiteSpace(kepNameInputField?.text)
            ? kepNameInputField.text.Trim()
            : $"Kepler Sat {satelliteCount + 1}";

        if (!ParsingUtils.TryParseMass(kepMassInputField.text, out float mass))
        {
            feedbackText.text = "Invalid mass. Enter a number between 500 and 1,000,000.";
            return;
        }

        if (!double.TryParse(kepADegOrMetersInputField.text, System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out double aInput))
        {
            feedbackText.text = "Invalid semi-major axis 'a'.";
            return;
        }
        if (!double.TryParse(kepEccInputField.text, out double e) || e < 0.0)
        {
            feedbackText.text = "Invalid eccentricity 'e'. Use 0 ≤ e < 1.";
            return;
        }
        if (!double.TryParse(kepIncDegInputField.text, out double iDeg) ||
            !double.TryParse(kepRAANDegInputField.text, out double raanDeg) ||
            !double.TryParse(kepArgPDegInputField.text, out double argpDeg) ||
            !double.TryParse(kepTrueAnomDegInputField.text, out double trueAnomDeg))
        {
            feedbackText.text = "Invalid angle(s): i / RAAN / ω / ν.";
            return;
        }

        try
        {
            // Treat 'a' as METERS by default; change here if you prefer km input:
            double aMeters = aInput; // if your UI is in km: aMeters = aInput * 1000.0;

            // after FromElements(...)
            var (rEciMeters, vEciMps) = KeplerUtils.FromElements(
                aMeters, e, iDeg, raanDeg, argpDeg, trueAnomDeg, mu);








            // --- ECI diagnostics (all variables exist here) ---
            double Re = earthRadiusMeters;
            double rp = aMeters * (1.0 - e);
            // double ra = aMeters * (1.0 + e);
            // double rNow = Math.Sqrt(rEciMeters.x * rEciMeters.x + rEciMeters.y * rEciMeters.y + rEciMeters.z * rEciMeters.z);
            // double vNow = Math.Sqrt(vEciMps.x * vEciMps.x + vEciMps.y * vEciMps.y + vEciMps.z * vEciMps.z);

            // // h = r × v
            // double hx = rEciMeters.y * vEciMps.z - rEciMeters.z * vEciMps.y;
            // double hy = rEciMeters.z * vEciMps.x - rEciMeters.x * vEciMps.z;
            // double hz = rEciMeters.x * vEciMps.y - rEciMeters.y * vEciMps.x;
            // double hmag = Math.Sqrt(hx * hx + hy * hy + hz * hz);

            // // i, Ω directly in ECI (ECI is Z-up)
            // double i_fromECI = Math.Acos(Math.Max(-1, Math.Min(1, hz / hmag))) * 180.0 / Math.PI;
            // double nx = -hy, ny = hx;                    // n = k×h, k=(0,0,1)
            // double nmag = Math.Sqrt(nx * nx + ny * ny);
            // double Omega_fromECI = (nmag < 1e-12) ? 0.0 :
            //     ((Math.Atan2(ny, nx) * 180.0 / Math.PI + 360.0) % 360.0);

            // Debug.Log(
            //   $"[ECI] a={aMeters / 1000.0:F3} km, e={e:F4}, i_in={iDeg:F2}°, Ω_in={raanDeg:F2}°, ω_in={argpDeg:F2}°, ν_in={trueAnomDeg:F2}°\n" +
            //   $"      rp={rp / 1000.0:F1} km (alt={(rp - Re) / 1000.0:F1} km), ra={ra / 1000.0:F1} km (alt={(ra - Re) / 1000.0:F1} km)\n" +
            //   $"      r_now={rNow / 1000.0:F1} km, v_now={vNow / 1000.0:F3} km/s, i_fromECI={i_fromECI:F2}°, Ω_fromECI={Omega_fromECI:F2}°"
            // );

            // Hard-stop if physically invalid
            if (rp <= Re * 1.001)
            {
                feedbackText.text = $"Orbit intersects Earth (perigee alt {(rp - Re) / 1000.0:F1} km). Increase 'a' or reduce 'e'.";
                return;
            }









            // scale factors
            // scale
            double unitsPerMeter = 1.0 / Math.Max(1e-9, metersPerUnit);

            // ECI (X, Y, Z)  --->  Unity (x, y, z)  [Y-up, right-handed to match your OrbitalCalculations]
            // Unity.Y  =  ECI.Z
            // Unity.Z  =  ECI.Y      // <-- NO minus here
            Vector3 spawnPos = new Vector3(
                (float)(rEciMeters.x * unitsPerMeter),
                (float)(rEciMeters.z * unitsPerMeter),
                (float)(rEciMeters.y * unitsPerMeter)
            );

            Vector3 spawnVel = new Vector3(
                (float)(vEciMps.x * unitsPerMeter),
                (float)(vEciMps.z * unitsPerMeter),
                (float)(vEciMps.y * unitsPerMeter)
            );


            // --- Instantiate & wire up like your other flows ---
            satelliteCount++;
            lastPlacedGameObject = Instantiate(spherePrefab);
            lastPlacedGameObject.name = name;
            lastPlacedGameObject.tag = "Satellite";
            lastPlacedGameObject.transform.position = spawnPos;
            lastPlacedGameObject.transform.localScale = Vector3.one;

            cameraTracker.RefreshBodiesList();

            if (velocityDragManager != null)
            {
                velocityDragManager.ResetDragManager();
                velocityDragManager.planet = lastPlacedGameObject;
                velocityDragManager.placeholderMass = mass;
                velocityDragManager.ApplyVelocityToPlanet(spawnVel);
            }

            // UI state cleanup (mirrors your TLE flow)
            ClearAndUnfocusInputField(kepNameInputField);
            ClearAndUnfocusInputField(kepMassInputField);
            ClearAndUnfocusInputField(kepADegOrMetersInputField);
            ClearAndUnfocusInputField(kepEccInputField);
            ClearAndUnfocusInputField(kepIncDegInputField);
            ClearAndUnfocusInputField(kepRAANDegInputField);
            ClearAndUnfocusInputField(kepArgPDegInputField);
            ClearAndUnfocusInputField(kepTrueAnomDegInputField);

            feedbackText.text = $"Placed '{name}' from Keplerian elements.";
        }
        catch (Exception ex)
        {
            feedbackText.text = $"Kepler placement failed: {ex.Message}";
        }
    }


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

    private void PreviewSilently(Transform t)
    {
        if (cameraTracker == null || t == null) return;
        cameraTracker.BeginUiSuppress();
        cameraTracker.PreviewPlaceholderInFree(t);   // <-- stays in Free
        cameraTracker.EndUiSuppress();
    }

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

    private void OnPositionInputChanged(string input)
    {
        if (mainCamera == null) return;

        if (ghostObjectPlaced && !clearingPosition && positionInput != null && positionInput.isFocused)
        {
            cameraTracker.BreakToFreeCam();
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            if (ghostInstance != null) ghostInstance.SetActive(false);
            feedbackText.text = "";
            ghostObjectPlaced = false;
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

            if (ghostInstance != null)
            {
                ghostInstance.SetActive(true);
                ghostInstance.transform.position = targetPosition;
            }
            // TrackSilently(ghostInstance.transform);
            PreviewSilently(ghostInstance.transform);

            // Change this ghost object placed after TrackSilently call to make sure it doesnt conflict with ReturnToTracking
            ghostObjectPlaced = true;
            feedbackText.text = "";
        }
        else
        {
            ghostObjectPlaced = false;
            if (ghostInstance != null) ghostInstance.SetActive(false);
            feedbackText.text = "Invalid format. Use numeric x,y,z values.";
        }
    }

    public void ClearManualPlacementCompletely()
    {
        // 1) clear drag & preview artifacts
        velocityDragManager?.ClearManualArtifacts();
        trajectoryRenderer?.ClearPreview();
        trajectoryRenderer?.ClearPreManeuverLine();

        // 2) kill any placeholder sphere (pre-velocity object)
        if (lastPlacedGameObject != null)
        {
            Destroy(lastPlacedGameObject);
            lastPlacedGameObject = null;
        }

        // 4) ghost
        if (ghostInstance != null) ghostInstance.SetActive(false);
    }


    private void ClearAndUnfocusInputField(TMP_InputField inputField)
    {
        if (inputField == null) return;
        clearingPosition = true;
        inputField.text = "";
        EventSystem.current.SetSelectedGameObject(null);
        clearingPosition = false;
    }

    public void ResetLastPlacedGameObject()
    {
        feedbackText.text = "";
        lastPlacedGameObject = null;
    }
}

public struct Vector3d
{
    public double x, y, z;
    public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
    public double magnitude => Math.Sqrt(x * x + y * y + z * z);
    public static Vector3d operator *(double s, Vector3d v) => new Vector3d(s * v.x, s * v.y, s * v.z);
    public static implicit operator Vector3(Vector3d v) => new Vector3((float)v.x, (float)v.y, (float)v.z);
}

static class KeplerUtils
{
    private struct M33
    {
        public double m00, m01, m02, m10, m11, m12, m20, m21, m22;
        public static M33 operator *(M33 A, M33 B)
        {
            M33 C;
            C.m00 = A.m00 * B.m00 + A.m01 * B.m10 + A.m02 * B.m20;
            C.m01 = A.m00 * B.m01 + A.m01 * B.m11 + A.m02 * B.m21;
            C.m02 = A.m00 * B.m02 + A.m01 * B.m12 + A.m02 * B.m22;
            C.m10 = A.m10 * B.m00 + A.m11 * B.m10 + A.m12 * B.m20;
            C.m11 = A.m10 * B.m01 + A.m11 * B.m11 + A.m12 * B.m21;
            C.m12 = A.m10 * B.m02 + A.m11 * B.m12 + A.m12 * B.m22;
            C.m20 = A.m20 * B.m00 + A.m21 * B.m10 + A.m22 * B.m20;
            C.m21 = A.m20 * B.m01 + A.m21 * B.m11 + A.m22 * B.m21;
            C.m22 = A.m20 * B.m02 + A.m21 * B.m12 + A.m22 * B.m22;
            return C;
        }
    }
    private static Vector3d Mul(M33 A, Vector3d v) =>
        new Vector3d(A.m00 * v.x + A.m01 * v.y + A.m02 * v.z,
                     A.m10 * v.x + A.m11 * v.y + A.m12 * v.z,
                     A.m20 * v.x + A.m21 * v.y + A.m22 * v.z);

    private static M33 R3(double a)
    {
        var c = Math.Cos(a); var s = Math.Sin(a);
        return new M33 { m00 = c, m01 = -s, m02 = 0, m10 = s, m11 = c, m12 = 0, m20 = 0, m21 = 0, m22 = 1 };
    }
    private static M33 R1(double a)
    {
        var c = Math.Cos(a); var s = Math.Sin(a);
        return new M33 { m00 = 1, m01 = 0, m02 = 0, m10 = 0, m11 = c, m12 = -s, m20 = 0, m21 = s, m22 = c };
    }
    private static double Deg2Rad(double d) => d * Math.PI / 180.0;
    private static double Wrap2Pi(double x) { var t = 2 * Math.PI; x %= t; if (x < 0) x += t; return x; }

    /// Returns (rECI [m], vECI [m/s]) from Keplerian elements.
    public static (Vector3d r, Vector3d v) FromElements(
        double a, double e, double iDeg, double raanDeg, double argpDeg, double trueAnomDeg, double mu)
    {
        if (e >= 1.0) throw new ArgumentException("Only elliptical orbits supported (e < 1).");
        if (a <= 0) throw new ArgumentException("Semi-major axis must be > 0.");

        double i = Deg2Rad(iDeg);
        double Ω = Deg2Rad(raanDeg);
        double ω = Deg2Rad(argpDeg);
        double ν = Deg2Rad(trueAnomDeg);

        // Handle degeneracies gently
        if (Math.Abs(e) < 1e-8) { ν = Wrap2Pi(ω + ν); ω = 0.0; }        // circular
        if (Math.Abs(Math.Sin(i)) < 1e-8) { ω = Wrap2Pi(Ω + ω); Ω = 0; } // equatorial

        double p = a * (1 - e * e);
        double cν = Math.Cos(ν), sν = Math.Sin(ν);
        double rMag = p / (1 + e * cν);

        var r_pf = new Vector3d(rMag * cν, rMag * sν, 0);
        var v_pf = new Vector3d(-Math.Sqrt(mu / p) * sν, Math.Sqrt(mu / p) * (e + cν), 0);

        var Q = Mul(R3(Ω), Mul(R1(i), R3(ω))); // Q = R3(Ω)*R1(i)*R3(ω)
        var rEci = Mul(Q, r_pf);
        var vEci = Mul(Q, v_pf);

        return (rEci, vEci);
    }

    private static M33 Mul(M33 A, M33 B) => A * B;
}