using UnityEngine;

public class AttitudeController : MonoBehaviour
{
    public enum PointingMode
    {
        Velocity,        // prograde
        Retrograde,
        Nadir,           // toward Earth
        Zenith,          // away from Earth
        Normal,          // +h
        AntiNormal,      // -h
        Inertial,        // fixed world vector
        Manual,          // leave rotation as-is
        HoldCurrent      // freeze current world orientation
    }

    [Header("Mode")]
    public PointingMode mode = PointingMode.Velocity;
    public Vector3 inertialDirection = Vector3.right;
    public bool snapAttitude = false;

    [Header("Slew")]
    [Tooltip("Max body pointing slew rate (deg/s) for smooth mode.")]
    public float maxSlewRateDegPerSec = 60f;
    [Tooltip("Roll hold weight (0..1) toward the 'upHint' vector.")]
    public float rollHold = 1.0f;

    [Header("Debug")]
    public Vector3 primaryWorld; // x-axis target
    public Vector3 upHintWorld;  // y-axis roll reference

    // caches to survive degeneracy
    private Vector3 vCache = Vector3.right;
    private Vector3 hCache = Vector3.up;

    // thresholds
    const float V_MIN = 0.01f;
    const float H_MIN = 1e-5f;
    const float ANG_MIN_DEG = 5f;

    private NBody nbody;
    private BodyService bodyService;
    private SimContext ctx;

    // ---- Hold state ----
    private bool holdValid;
    private Quaternion holdTarget;   // frozen world rotation
    private Vector3 holdX, holdY;    // frozen axes (for inspector/debug)

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.bodyService = ctx.BodyService;
    }

    void FixedUpdate()
    {
        if (!nbody) nbody = GetComponent<NBody>();
        if (!nbody) return;

        if (!bodyService)
            bodyService = FindFirstObjectByType<BodyService>();

        Vector3 center = (bodyService && bodyService.CentralBody)
            ? bodyService.CentralBody.transform.position
            : Vector3.zero;

        // THIS craft only
        Vector3 r = transform.position - center;
        Vector3 v = nbody.velocity;

        Vector3 xTarget, yUpHint;

        if (mode == PointingMode.HoldCurrent)
        {
            // If we just switched into Hold but haven’t captured yet, capture now.
            if (!holdValid)
                CaptureHoldFromCurrent();

            // Use frozen axes; skip recomputation.
            xTarget = holdX;
            yUpHint = holdY;
        }
        else
        {
            // live compute per mode
            ComputeTargetAxes(r, v, center, out xTarget, out yUpHint);
            // since we’re not in hold, mark hold as invalid
            holdValid = false;
        }

        // Store for inspector
        primaryWorld = xTarget;
        upHintWorld = yUpHint;

        // Build orthonormal basis
        Vector3 x = xTarget.normalized;
        Vector3 z = Vector3.Cross(x, yUpHint);
        if (z.sqrMagnitude < 1e-10f) z = Vector3.Cross(x, Vector3.up);
        z.Normalize();
        Vector3 y = Vector3.Cross(z, x);

        var target = Quaternion.LookRotation(x, y);
        float maxStep = maxSlewRateDegPerSec * Time.unscaledDeltaTime;
        transform.rotation = snapAttitude
            ? target
            : Quaternion.RotateTowards(transform.rotation, target, maxStep);
    }

    public void SetMode(PointingMode newMode)
    {
        // If the user explicitly chooses Hold, freeze immediately at click time
        if (newMode == PointingMode.HoldCurrent)
            CaptureHoldFromCurrent();
        else
            holdValid = false;

        mode = newMode;
        Debug.Log($"[AttitudeController] {name} mode -> {mode}");
    }

    /// <summary>
    /// Call this to freeze the current world orientation, regardless of current mode.
    /// Useful for a dedicated "Hold Here" button.
    /// </summary>
    public void FreezeCurrentAttitude()
    {
        SetMode(PointingMode.HoldCurrent);
    }

    private void CaptureHoldFromCurrent()
    {
        holdTarget = transform.rotation;
        // Store stable world axes corresponding to this rotation
        holdX = transform.forward; // Unity forward (Z) is our primary
        holdY = transform.up;
        if (holdX.sqrMagnitude < 1e-8f) holdX = Vector3.forward;
        if (holdY.sqrMagnitude < 1e-8f) holdY = Vector3.up;
        holdValid = true;
    }

    public void SetInertialDirection(Vector3 worldDir)
    {
        if (worldDir.sqrMagnitude > 1e-12f) inertialDirection = worldDir.normalized;
    }

    /// <summary>
    /// Compute target primary (x̂) and roll up-hint (ŷ) in world space with safe fallbacks.
    /// </summary>
    private void ComputeTargetAxes(Vector3 r, Vector3 v, Vector3 center, out Vector3 xHat, out Vector3 yUp)
    {
        Vector3 rHat = SafeNorm(r, Vector3.up);
        Vector3 vHat = SafeNorm(v, vCache);
        float alpha = Vector3.Angle(rHat, vHat); // deg
        Vector3 h = Vector3.Cross(r, v);
        Vector3 hHat = SafeNorm(h, hCache);

        bool okV = v.magnitude > V_MIN;
        bool okH = h.magnitude > H_MIN && alpha > ANG_MIN_DEG;

        switch (mode)
        {
            case PointingMode.Velocity:
                xHat = okV ? vHat : vCache;
                yUp = rHat; // right = v × (-r) = +h
                break;

            case PointingMode.Retrograde:
                xHat = -(okV ? vHat : vCache);
                yUp = rHat;  // right = (-v) × r = +h
                break;

            case PointingMode.Nadir:
                xHat = -rHat;
                yUp = okV ? vHat : vCache;
                break;

            case PointingMode.Zenith:
                xHat = rHat;
                yUp = okV ? vHat : vCache;
                break;

            case PointingMode.Normal:
                {
                    if (okH)
                    {
                        var p = OrbitalCalculations.TryParams(nbody, bodyService);
                        if (p.inclination <= 90)
                        {
                            xHat = -hHat;                      // forward ~ +h   (FIX: was -hHat)
                        }
                        else
                        {
                            xHat = hHat;                      // forward ~ +h   (FIX: was -hHat)
                        }
                        yUp = okV ? vHat : vCache;       // roll with velocity
                    }
                    else
                    {
                        BuildTangentFrame(rHat, out var tHat, out var nFb);
                        xHat = nFb;                        // fallback +normal
                        Vector3 vT = v - Vector3.Dot(v, rHat) * rHat;
                        yUp = (vT.sqrMagnitude > 1e-10f) ? vT.normalized : tHat;
                    }
                }
                break;

            case PointingMode.AntiNormal:
                {
                    if (okH)
                    {
                        var p = OrbitalCalculations.TryParams(nbody, bodyService);
                        if (p.inclination <= 90)
                        {
                            xHat = hHat;                      // forward ~ +h   (FIX: was -hHat)
                        }
                        else
                        {
                            xHat = -hHat;                      // forward ~ +h   (FIX: was -hHat)
                        }
                        yUp = okV ? vHat : vCache;
                    }
                    else
                    {
                        BuildTangentFrame(rHat, out var tHat, out var nFb);
                        xHat = -nFb;                       // fallback -normal
                        Vector3 vT = v - Vector3.Dot(v, rHat) * rHat;
                        yUp = (vT.sqrMagnitude > 1e-10f) ? vT.normalized : tHat;
                    }
                }
                break;


            case PointingMode.Inertial:
                xHat = SafeNorm(inertialDirection, Vector3.right);
                yUp = Vector3.up;
                break;

            case PointingMode.Manual:
            default:
                xHat = transform.forward;
                yUp = transform.up;
                break;
        }

        if (okV) vCache = vHat;
        if (okH) hCache = hHat;

        if (rollHold < 1f && rollHold > 0f)
        {
            Vector3 worldUp = Vector3.up;
            yUp = Vector3.Slerp(worldUp, yUp, rollHold).normalized;
        }
    }

    // Add near the bottom of AttitudeController
    private static void BuildTangentFrame(Vector3 rHat, out Vector3 tHat, out Vector3 nFallback)
    {
        // pick a world reference that isn't parallel to rHat
        Vector3 refUp = (Mathf.Abs(Vector3.Dot(rHat, Vector3.up)) < 0.9f) ? Vector3.up : Vector3.forward;

        // tHat lies in the local horizontal plane (perpendicular to rHat)
        tHat = Vector3.Cross(refUp, rHat);
        if (tHat.sqrMagnitude < 1e-12f)
        {
            // super edge case: try another axis
            refUp = Vector3.right;
            tHat = Vector3.Cross(refUp, rHat);
        }
        tHat.Normalize();

        // fallback orbit normal
        nFallback = Vector3.Cross(rHat, tHat);
        nFallback.Normalize();
    }


    private static Vector3 SafeNorm(Vector3 v, Vector3 fallback)
    {
        float m = v.magnitude;
        return (m > 1e-8f) ? v / m : (fallback.sqrMagnitude > 0f ? fallback.normalized : Vector3.forward);
    }

    // Public API for thrust: returns world burnDir and whether it's a lateral burn
    public Vector3 GetBurnDirection(Vector3 center, out bool lateral)
    {
        lateral = false;

        // local r, v for THIS body
        if (!nbody) nbody = GetComponent<NBody>();
        Vector3 r = transform.position - center;
        Vector3 v = nbody ? nbody.velocity : Vector3.zero;

        // Reuse the exact same logic/fallbacks you use to aim the vehicle
        Vector3 rHat = SafeNorm(r, Vector3.up);
        Vector3 vHat = SafeNorm(v, vCache);
        Vector3 h = Vector3.Cross(r, v);
        Vector3 hHat = SafeNorm(h, hCache);

        float alpha = Vector3.Angle(rHat, vHat);
        bool okV = v.magnitude > V_MIN;
        bool okH = h.magnitude > H_MIN && alpha > ANG_MIN_DEG;

        // For fallback normal/tangent, use the same helper your attitude uses
        BuildTangentFrame(rHat, out var tHat, out var nFallback);

        switch (mode)
        {
            case PointingMode.Velocity: return okV ? vHat : vCache;
            case PointingMode.Retrograde: return -(okV ? vHat : vCache);
            case PointingMode.Nadir: return -rHat;
            case PointingMode.Zenith: return rHat;

            case PointingMode.Normal:
                lateral = true;
                if (okH) return -hHat;              // your attitude uses -hHat for +Normal
                return -nFallback;

            case PointingMode.AntiNormal:
                lateral = true;
                if (okH) return hHat;
                return nFallback;

            case PointingMode.Inertial: return SafeNorm(inertialDirection, Vector3.right);
            case PointingMode.HoldCurrent: return transform.forward; // frozen direction
            case PointingMode.Manual:
            default: return transform.forward; // don't second-guess manual
        }
    }

}
