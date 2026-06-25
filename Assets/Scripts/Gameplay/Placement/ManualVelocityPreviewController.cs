using System.Collections;
using UnityEngine;

/// <summary>
/// Owns quick and debounced long trajectory preview timing for manual placement.
/// </summary>
public sealed class ManualVelocityPreviewController
{
    private readonly MonoBehaviour owner;
    private readonly System.Func<TrajectoryRenderer> trajectoryRendererProvider;
    private readonly System.Func<BodyService> bodyServiceProvider;
    private readonly float minPreviewInterval;
    private readonly float directionAngleThresholdDeg;
    private readonly float speedThreshold;
    private readonly float longPreviewDelay;

    private Coroutine longPreviewCoroutine;
    private float lastPreviewTime = -999f;
    private Vector3 lastPreviewVel = new(float.NaN, float.NaN, float.NaN);
    private Vector3 lastPreviewDir = new(float.NaN, float.NaN, float.NaN);

    public ManualVelocityPreviewController(
        MonoBehaviour owner,
        System.Func<TrajectoryRenderer> trajectoryRendererProvider,
        System.Func<BodyService> bodyServiceProvider,
        float minPreviewInterval,
        float directionAngleThresholdDeg,
        float speedThreshold,
        float longPreviewDelay)
    {
        this.owner = owner;
        this.trajectoryRendererProvider = trajectoryRendererProvider;
        this.bodyServiceProvider = bodyServiceProvider;
        this.minPreviewInterval = minPreviewInterval;
        this.directionAngleThresholdDeg = directionAngleThresholdDeg;
        this.speedThreshold = speedThreshold;
        this.longPreviewDelay = longPreviewDelay;
    }

    public void ResetChangeTracking()
    {
        lastPreviewTime = -999f;
        lastPreviewDir = new Vector3(float.NaN, float.NaN, float.NaN);
        lastPreviewVel = new Vector3(float.NaN, float.NaN, float.NaN);
    }

    public void RequestPreview(
        bool hasPendingPlacement,
        GameObject pendingBody,
        Vector3 velocity,
        Vector3 stagedDirection,
        float bodyMass)
    {
        TryQuickPreview(hasPendingPlacement, pendingBody, velocity, stagedDirection, bodyMass);
        ScheduleLongPreview(hasPendingPlacement, pendingBody, velocity, bodyMass);
    }

    public void CancelLongPreview()
    {
        if (longPreviewCoroutine != null && owner != null)
            owner.StopCoroutine(longPreviewCoroutine);

        longPreviewCoroutine = null;
    }

    private void TryQuickPreview(
        bool hasPendingPlacement,
        GameObject pendingBody,
        Vector3 velocity,
        Vector3 stagedDirection,
        float bodyMass)
    {
        TrajectoryRenderer trajectoryRenderer = trajectoryRendererProvider?.Invoke();
        if (!hasPendingPlacement || pendingBody == null || trajectoryRenderer == null)
            return;

        BodyService bodyService = bodyServiceProvider?.Invoke();
        if (bodyService == null || bodyService.CentralBody == null)
            return;

        if ((Time.unscaledTime - lastPreviewTime) < minPreviewInterval)
            return;

        if (!ChangedEnough(velocity, stagedDirection))
            return;

        trajectoryRenderer.QuickPreviewFromState(pendingBody.transform.position, velocity, bodyMass);

        lastPreviewTime = Time.unscaledTime;
        lastPreviewDir = stagedDirection;
        lastPreviewVel = velocity;
    }

    private bool ChangedEnough(Vector3 velocity, Vector3 stagedDirection)
    {
        if (stagedDirection == Vector3.zero)
            return false;

        bool firstDir = float.IsNaN(lastPreviewDir.x);
        bool firstVel = float.IsNaN(lastPreviewVel.x);
        if (firstDir || firstVel)
            return true;

        bool dirChanged = Vector3.Angle(lastPreviewDir, stagedDirection) > directionAngleThresholdDeg;
        bool spdChanged = Mathf.Abs(velocity.magnitude - lastPreviewVel.magnitude) > speedThreshold;

        return dirChanged || spdChanged;
    }

    private void ScheduleLongPreview(
        bool hasPendingPlacement,
        GameObject pendingBody,
        Vector3 velocity,
        float bodyMass)
    {
        if (owner == null)
            return;

        TrajectoryRenderer trajectoryRenderer = trajectoryRendererProvider?.Invoke();
        if (!hasPendingPlacement || pendingBody == null || trajectoryRenderer == null)
            return;

        CancelLongPreview();
        longPreviewCoroutine = owner.StartCoroutine(LongPreviewAfterIdle(pendingBody, velocity, bodyMass));
    }

    private IEnumerator LongPreviewAfterIdle(GameObject pendingBody, Vector3 velocity, float bodyMass)
    {
        float t = 0f;
        while (t < longPreviewDelay)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        TrajectoryRenderer trajectoryRenderer = trajectoryRendererProvider?.Invoke();
        if (trajectoryRenderer == null || pendingBody == null)
        {
            longPreviewCoroutine = null;
            yield break;
        }

        trajectoryRenderer.QuickPreviewOnceLong(
            pendingBody.transform.position,
            velocity,
            bodyMass,
            steps: 0,
            dt: 0f,
            singleOrbit: true,
            smoothClosedLoop: false
        );
        longPreviewCoroutine = null;
    }
}
