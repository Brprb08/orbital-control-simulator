using UnityEngine;

public class AttitudeController : MonoBehaviour
{
    public enum PointingMode
    {
        Velocity,        // prograde
        Retrograde,
        Nadir,           // toward Earth
        Zenith,          // away from Earth
        Normal,          // +h (with 90°-swap behavior by design)
        AntiNormal,      // -h (with 90°-swap behavior by design)
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
    [Range(0f, 1f)] public float rollHold = 1.0f;

    [Header("Debug")]
    public Vector3 primaryWorld; // x-axis target
    public Vector3 upHintWorld;  // y-axis roll reference

    // caches to survive degeneracy
    private Vector3 vCache = Vector3.right;
    private Vector3 hCache = Vector3.up;

    private const float V_MIN = 0.01f;
    private const float H_MIN = 1e-5f;
    private const float ANG_MIN_DEG = 5f;
    private const float EPS = 1e-8f;
    private const float CROSS_EPS2 = 1e-10f;

    private NBody nbody;
    private ICameraTracker cameraTracker;
    private BodyService bodyService;
    private SimContext ctx;

    private bool wasTracked = false;

    private bool holdValid;
    private Vector3 holdX, holdY;

    [Header("Thrust parity sync")]
    public bool useThrustParityWhenThrusting = true;

    private bool _thrustingExternal;      // set by caller each frame
    private sbyte _latchedParityExternal; // −1/0/+1 (0 = no latch)

    [SerializeField] private int thrustGraceFrames = 6; // ~0.1s @60 Hz
    private int _thrustGraceCounter = 0;
    private bool _prevThrusting = false;
    private int _paritySignForBurn = 0; // +1 “prograde side”, -1 “retro side”, 0 = unset

    public void SyncThrustParity(bool isThrusting, sbyte latchedParity)
    {
        _thrustingExternal = isThrusting;
        _latchedParityExternal = latchedParity;
    }

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.cameraTracker = ctx.CameraTracker;
        this.bodyService = ctx.BodyService;
    }

    void FixedUpdate()
    {
        if (!nbody) nbody = GetComponent<NBody>();
        if (!nbody) return;

        bool isTracked = (cameraTracker == null) || (cameraTracker.CurrentBody == nbody);
        if (!isTracked)
        {
            wasTracked = false;
            return;
        }
        bool snapNow = !wasTracked;
        wasTracked = true;

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
            if (!holdValid) CaptureHoldFromCurrent();
            xTarget = holdX;
            yUpHint = holdY;
        }
        else
        {
            ComputeTargetAxes(r, v, out xTarget, out yUpHint);
            holdValid = false;
        }

        primaryWorld = xTarget;
        upHintWorld = yUpHint;

        // Build orthonormal basis
        Vector3 x = xTarget.normalized;
        Vector3 z = Vector3.Cross(x, yUpHint);
        if (z.sqrMagnitude < CROSS_EPS2) z = Vector3.Cross(x, Vector3.up);
        z.Normalize();
        Vector3 y = Vector3.Cross(z, x);

        var target = Quaternion.LookRotation(x, y);
        float maxStep = maxSlewRateDegPerSec * Time.unscaledDeltaTime;
        bool doSnap = snapAttitude || snapNow;

        transform.rotation = doSnap
            ? target
            : Quaternion.RotateTowards(transform.rotation, target, maxStep);
    }

    public void SetMode(PointingMode newMode)
    {
        if (newMode == PointingMode.HoldCurrent) CaptureHoldFromCurrent();
        else holdValid = false;

        mode = newMode;
        Debug.Log($"[AttitudeController] {name} mode -> {mode}");
    }

    public void FreezeCurrentAttitude() => SetMode(PointingMode.HoldCurrent);

    private void CaptureHoldFromCurrent()
    {
        holdX = (transform.forward.sqrMagnitude > EPS) ? transform.forward : Vector3.forward;
        holdY = (transform.up.sqrMagnitude > EPS) ? transform.up : Vector3.up;
        holdValid = true;
    }

    public void SetInertialDirection(Vector3 worldDir)
    {
        if (worldDir.sqrMagnitude > EPS) inertialDirection = worldDir.normalized;
    }

    /// <summary>
    /// Compute target primary (x̂) and roll up-hint (ŷ) in world space with safe fallbacks.
    /// </summary>
    private void ComputeTargetAxes(Vector3 r, Vector3 v, out Vector3 xHat, out Vector3 yUp)
    {
        Vector3 rHat = SafeNorm(r, Vector3.up);
        Vector3 vHat = SafeNorm(v, vCache);

        float alpha = Vector3.Angle(rHat, vHat); // deg
        Vector3 h = Vector3.Cross(r, v);
        Vector3 hHat = SafeNorm(h, hCache);

        bool okV = v.magnitude > V_MIN;
        bool okH = h.magnitude > H_MIN && alpha > ANG_MIN_DEG;

        // live sign from current side of 90° boundary
        int liveSign = (h.y < 0f) ? +1 : -1;

        bool internalThrusting = (nbody && nbody.thrustController != null) && nbody.thrustController.IsThrusting;

        bool thrusting = IsThrustingEffective(internalThrusting);

        if (thrusting && !_prevThrusting)
        {
            _paritySignForBurn = okH ? liveSign
                                     : (_paritySignForBurn != 0 ? _paritySignForBurn : liveSign);
            _thrustGraceCounter = thrustGraceFrames;
        }

        if (thrusting) _thrustGraceCounter = thrustGraceFrames;

        _prevThrusting = thrusting;

        if (_thrustGraceCounter > 0) _thrustGraceCounter--;

        bool holdParity = thrusting || (_thrustGraceCounter > 0);

        int parityForMapping = holdParity ? EffectiveParity(liveSign) : liveSign;
        bool progradeForMapping = (parityForMapping > 0);

        switch (mode)
        {
            case PointingMode.Velocity:
                xHat = okV ? vHat : vCache;
                yUp = rHat; // right = v × (-r) = +h
                break;

            case PointingMode.Retrograde:
                xHat = -(okV ? vHat : vCache);
                yUp = rHat; // right = (-v) × r = +h
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
                if (okH)
                {
                    // swap at 90°, but parity is latched during thrust/grace
                    xHat = progradeForMapping ? -hHat : hHat;
                    yUp = okV ? vHat : vCache;
                }
                else
                {
                    BuildTangentFrame(rHat, out var tHat, out var nFb);
                    xHat = nFb;
                    Vector3 vT = v - Vector3.Dot(v, rHat) * rHat;
                    yUp = (vT.sqrMagnitude > CROSS_EPS2) ? vT.normalized : tHat;
                }
                break;

            case PointingMode.AntiNormal:
                if (okH)
                {
                    xHat = progradeForMapping ? hHat : -hHat;
                    yUp = okV ? vHat : vCache;
                }
                else
                {
                    BuildTangentFrame(rHat, out var tHat, out var nFb);
                    xHat = -nFb;
                    Vector3 vT = v - Vector3.Dot(v, rHat) * rHat;
                    yUp = (vT.sqrMagnitude > CROSS_EPS2) ? vT.normalized : tHat;
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
            yUp = Vector3.Slerp(Vector3.up, yUp, rollHold).normalized;
        }
    }

    // Public API for thrust, returns world burnDir and whether it's a lateral burn
    public Vector3 GetBurnDirection(Vector3 center, out bool lateral)
    {
        lateral = false;

        if (!nbody) nbody = GetComponent<NBody>();
        Vector3 r = transform.position - center;
        Vector3 v = nbody ? nbody.velocity : Vector3.zero;

        Vector3 rHat = SafeNorm(r, Vector3.up);
        Vector3 vHat = SafeNorm(v, vCache);
        Vector3 h = Vector3.Cross(r, v);
        Vector3 hHat = SafeNorm(h, hCache);

        float alpha = Vector3.Angle(rHat, vHat);
        bool okV = v.magnitude > V_MIN;
        bool okH = h.magnitude > H_MIN && alpha > ANG_MIN_DEG;

        BuildTangentFrame(rHat, out var tHat, out var nFallback);

        int liveSign = (h.y < 0f) ? +1 : -1;
        bool internalThrusting = (nbody && nbody.thrustController != null) && nbody.thrustController.IsThrusting;
        bool thrusting = IsThrustingEffective(internalThrusting);
        bool holdParity = thrusting || (_thrustGraceCounter > 0);
        int parityForMapping = holdParity ? EffectiveParity(liveSign) : liveSign;
        bool progradeForMapping = (parityForMapping > 0);

        switch (mode)
        {
            case PointingMode.Velocity: return okV ? vHat : vCache;
            case PointingMode.Retrograde: return -(okV ? vHat : vCache);
            case PointingMode.Nadir: return -rHat;
            case PointingMode.Zenith: return rHat;

            case PointingMode.Normal:
                lateral = true;
                if (okH) return progradeForMapping ? -hHat : hHat;
                return -nFallback;

            case PointingMode.AntiNormal:
                lateral = true;
                if (okH) return progradeForMapping ? hHat : -hHat;
                return nFallback;

            case PointingMode.Inertial: return SafeNorm(inertialDirection, Vector3.right);
            case PointingMode.HoldCurrent:
            case PointingMode.Manual:
            default: return transform.forward;
        }
    }

    // ---- helpers ----

    private bool IsThrustingEffective(bool internalThrusting)
    {
        if (useThrustParityWhenThrusting && _thrustingExternal) return true;
        return internalThrusting;
    }

    private int EffectiveParity(int liveSign) // +1 prograde-side, -1 retro-side
    {
        if (_latchedParityExternal != 0) return _latchedParityExternal;
        if (_paritySignForBurn != 0) return _paritySignForBurn;
        return liveSign;
    }

    private static void BuildTangentFrame(Vector3 rHat, out Vector3 tHat, out Vector3 nFallback)
    {
        Vector3 refUp = (Mathf.Abs(Vector3.Dot(rHat, Vector3.up)) < 0.9f) ? Vector3.up : Vector3.forward;
        tHat = Vector3.Cross(refUp, rHat);
        if (tHat.sqrMagnitude < EPS)
        {
            refUp = Vector3.right;
            tHat = Vector3.Cross(refUp, rHat);
        }
        tHat.Normalize();

        nFallback = Vector3.Cross(rHat, tHat);
        nFallback.Normalize();
    }

    private static Vector3 SafeNorm(Vector3 v, Vector3 fallback)
    {
        float m = v.magnitude;
        return (m > EPS) ? (v / m) : (fallback.sqrMagnitude > 0f ? fallback.normalized : Vector3.forward);
    }
}
