using System;
using UnityEngine;

public class AttitudeController : MonoBehaviour
{
    public enum PointingMode
    {
        Velocity,
        Retrograde,
        Nadir,
        Zenith,
        Normal,
        AntiNormal,
        Inertial,
        Manual,
        HoldCurrent
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
    public Vector3 primaryWorld;
    public Vector3 upHintWorld;

    private Vector3 vCache = Vector3.right;
    private Vector3 hCache = Vector3.up;

    private const float V_MIN = 0.01f;
    private const float H_MIN = 1e-5f;
    private const float EPS = 1e-8f;
    private const float CROSS_EPS2 = 1e-10f;

    private NBody nbody;
    private ICameraTracker cameraTracker;
    private BodyService bodyService;
    private SimContext ctx;

    private bool loggedMissingBodyService = false;

    private bool wasTracked = false;

    private bool holdValid;
    private Vector3 holdX, holdY;

    // Reserved for future parity-lock behavior during burns.
    // Currently not consumed by attitude math directly.
    [NonSerialized] public bool lockNormalParity;

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
        {
            if (!loggedMissingBodyService)
            {
                Debug.LogWarning($"[AttitudeController] {name} missing BodyService reference. Skipping attitude update.");
                loggedMissingBodyService = true;
            }
            return;
        }

        Vector3 center = (bodyService && bodyService.CentralBody)
            ? bodyService.CentralBody.transform.position
            : Vector3.zero;

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

        Vector3 x = xTarget.normalized;
        Vector3 z = Vector3.Cross(x, yUpHint);
        if (z.sqrMagnitude < CROSS_EPS2) z = Vector3.Cross(x, Vector3.up);
        z.Normalize();
        Vector3 y = Vector3.Cross(z, x);

        var target = Quaternion.LookRotation(x, y);
        float maxStep = maxSlewRateDegPerSec * Time.fixedDeltaTime;
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

    private void ComputeTargetAxes(Vector3 r, Vector3 v, out Vector3 xHat, out Vector3 yUp)
    {
        Vector3 center = Vector3.zero;
        Vector3 worldPos = center + r;

        OrbitalFrame frame = OrbitalFrameUtility.Build(
            worldPos,
            v,
            center,
            ref vCache,
            ref hCache
        );

        switch (mode)
        {
            case PointingMode.Velocity:
                xHat = frame.prograde;
                yUp = frame.radialOut;
                break;

            case PointingMode.Retrograde:
                xHat = frame.retrograde;
                yUp = frame.radialOut;
                break;

            case PointingMode.Nadir:
                xHat = frame.radialIn;
                yUp = frame.prograde;
                break;

            case PointingMode.Zenith:
                xHat = frame.radialOut;
                yUp = frame.prograde;
                break;

            case PointingMode.Normal:
                if (frame.hasNormal)
                {
                    xHat = frame.normal;
                    yUp = frame.prograde;
                }
                else
                {
                    BuildTangentFrame(frame.radialOut, out var tHat, out var nFb);
                    xHat = nFb;
                    Vector3 vT = v - Vector3.Dot(v, frame.radialOut) * frame.radialOut;
                    yUp = (vT.sqrMagnitude > CROSS_EPS2) ? vT.normalized : tHat;
                }
                break;

            case PointingMode.AntiNormal:
                if (frame.hasNormal)
                {
                    xHat = frame.antiNormal;
                    yUp = frame.prograde;
                }
                else
                {
                    BuildTangentFrame(frame.radialOut, out var tHat, out var nFb);
                    xHat = -nFb;
                    Vector3 vT = v - Vector3.Dot(v, frame.radialOut) * frame.radialOut;
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

        if (rollHold < 1f && rollHold > 0f)
        {
            yUp = Vector3.Slerp(Vector3.up, yUp, rollHold).normalized;
        }
    }

    public Vector3 GetBurnDirection(Vector3 center, out bool lateral)
    {
        lateral = (mode == PointingMode.Normal || mode == PointingMode.AntiNormal);

        Vector3 fwd = transform.forward;
        if (fwd.sqrMagnitude < EPS)
            fwd = Vector3.forward;

        return fwd.normalized;
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
