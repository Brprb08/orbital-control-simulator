using System.Collections;
using UnityEngine;

/// <summary>
/// Handles continuous & one-shot trajectory previews using GPU compute.
/// TrajectoryRenderer forwards its public preview API into this.
/// 
/// This version mirrors the main TrajectoryRenderer's horizon logic:
/// - Uses Kepler period from μ and a (vis-viva)
/// - Scales horizon ~1.25 * orbital period
/// - Clamps horizon like the main renderer
/// - Chooses dt ~ 7s and adjusts if steps exceed a preview-max
/// </summary>
public sealed class TrajectoryPreviewModule
{
    private readonly MonoBehaviour owner;
    private readonly ProceduralLineRenderer line;
    private readonly SimContext ctx;
    private readonly System.Func<Vector3[], Vector3[]> clipper;
    private readonly System.Func<Vector3[], Vector3[]> singleOrbitClipper;

    private Coroutine previewCo;
    private Vector3 previewPos, previewVel;
    private float previewMass;
    private bool previewDirty;
    private uint previewRequestGeneration;

    // Same DAY / horizon style as TrajectoryRenderer
    private const float DAY = 24f * 60f * 60f;
    private const float MAX_HORIZON_SECONDS = 10f * DAY;
    private const float MIN_HORIZON_SECONDS = 20000f;   // same as renderer

    // For hyperbolic / weird cases, same fallback as ComputeHorizonSeconds
    private const float HYPERBOLIC_FALLBACK_T = 60000f;

    private const int PREVIEW_MIN_STEPS = 500;
    private const int PREVIEW_MAX_STEPS = 6000;

    private const float BASE_DT = 7f;

    public TrajectoryPreviewModule(
        MonoBehaviour owner,
        ProceduralLineRenderer previewLine,
        SimContext ctx,
        System.Func<Vector3[], Vector3[]> clipper,
        System.Func<Vector3[], Vector3[]> singleOrbitClipper)
    {
        this.owner = owner;
        this.line = previewLine;
        this.ctx = ctx;
        this.clipper = clipper ?? (pts => pts);
        this.singleOrbitClipper = singleOrbitClipper ?? (pts => pts);
    }

    public void Reset()
    {
        unchecked
        {
            previewRequestGeneration++;
        }

        previewDirty = false;
        if (line != null) line.Clear();

        if (previewCo != null && owner != null)
        {
            owner.StopCoroutine(previewCo);
            previewCo = null;
        }
    }

    public void QuickPreviewFromState(Vector3 startPos, Vector3 startVel, float bodyMass)
    {
        previewPos = startPos;
        previewVel = startVel;
        previewMass = Mathf.Max(1f, bodyMass);
        previewDirty = true;

        if (previewCo == null && owner != null)
            previewCo = owner.StartCoroutine(QuickPreviewLoop());
    }

    public void ClearPreview() => Reset();

    /// <summary>
    /// One-shot long preview. Callers can supply explicit steps/dt for cheap
    /// interaction previews; otherwise we derive them from the orbit state.
    /// </summary>
    public void QuickPreviewOnceLong(
        Vector3 startPos,
        Vector3 startVel,
        float bodyMass,
        int steps,
        float dt,
        bool singleOrbit)
    {
        if (previewCo != null && owner != null)
        {
            owner.StopCoroutine(previewCo);
            previewCo = null;
        }

        var svc = ctx?.BodyService;
        if (svc == null || svc.CentralBody == null)
        {
            Debug.LogWarning("[QuickPreviewOnceLong] No BodyService or CentralBody.");
            line?.Clear();
            return;
        }

        float usedDt = dt;
        int usedSteps = steps;
        if (usedDt <= 0f || usedSteps <= 0)
            ComputePreviewSettings(startPos, startVel, svc, out usedDt, out usedSteps);
        else
        {
            usedDt = Mathf.Max(0.0001f, usedDt);
            usedSteps = Mathf.Max(2, usedSteps);
        }

        var cb = svc.CentralBody;
        Vector3[] attractorPos = { cb.transform.position };
        float[] attractorMass = { (float)cb.trueMass };
        uint requestGeneration = unchecked(++previewRequestGeneration);

        ctx.TrajectoryComputeController.CalculateTrajectoryGPU_Async(
            startPos, startVel, Mathf.Max(1f, bodyMass),
            attractorPos, attractorMass,
            usedDt, usedSteps,
            points =>
            {
                if (requestGeneration != previewRequestGeneration)
                    return;

                if (points == null || points.Length < 2)
                {
                    line?.Clear();
                    return;
                }

                var clipped = clipper(points);
                if (singleOrbit)
                    clipped = singleOrbitClipper(clipped);

                line.UpdateLine(clipped);
                previewDirty = false;
            });
    }

    /// <summary>
    /// Compute dt & steps for the preview in the same spirit as
    /// TrajectoryRenderer.ComputeFinalLongPass + ComputeHorizonSeconds.
    /// 
    /// 1. Use vis-viva to get semi-major axis 'a' from r, v, μ.
    /// 2. Compute period T = 2π sqrt(a^3 / μ) for bound orbits.
    /// 3. Clamp horizon ~1.25 * T into [MIN_HORIZON_SECONDS, MAX_HORIZON_SECONDS].
    /// 4. Choose dt ~ BASE_DT and clamp steps into [PREVIEW_MIN_STEPS, PREVIEW_MAX_STEPS].
    /// </summary>
    private void ComputePreviewSettings(
        Vector3 startPos,
        Vector3 startVel,
        BodyService bodyService,
        out float dt,
        out int steps)
    {
        // Fallback defaults
        dt = BASE_DT;
        steps = PREVIEW_MIN_STEPS;

        if (bodyService == null || bodyService.CentralBody == null)
            return;

        var cb = bodyService.CentralBody;

        Vector3 rVec = startPos - cb.transform.position;
        float r = rVec.magnitude;
        float v = startVel.magnitude;

        if (r < 1e-3f || v < 1e-5f)
        {
            // Can't get a meaningful orbit; leave defaults
            return;
        }

        // μ = G * M
        float mu = (float)(PhysicsConstants.G * cb.trueMass);

        // Specific orbital energy ε = v^2/2 - μ / r
        float energy = 0.5f * v * v - mu / r;

        float horizonSeconds;

        if (energy >= 0f)
        {
            // Unbound / parabolic/hyperbolic – match renderer’s fallback behavior
            horizonSeconds = Mathf.Clamp(HYPERBOLIC_FALLBACK_T, MIN_HORIZON_SECONDS, MAX_HORIZON_SECONDS);
        }
        else
        {
            // Bound: a = -μ / (2ε)
            float a = -mu / (2f * energy);
            if (a <= 0f)
            {
                horizonSeconds = Mathf.Clamp(HYPERBOLIC_FALLBACK_T, MIN_HORIZON_SECONDS, MAX_HORIZON_SECONDS);
            }
            else
            {
                // T = 2π * sqrt(a^3 / μ)
                float T = 2f * Mathf.PI * Mathf.Sqrt(a * a * a / mu);

                // Match ComputeHorizonSeconds(..., fast:false) style: ~1.25T
                horizonSeconds = Mathf.Clamp(T * 1.25f, MIN_HORIZON_SECONDS, MAX_HORIZON_SECONDS);
            }
        }

        float effectiveDt = BASE_DT;
        int stepsNeeded = Mathf.CeilToInt(horizonSeconds / effectiveDt);

        if (stepsNeeded > PREVIEW_MAX_STEPS)
        {
            effectiveDt = horizonSeconds / PREVIEW_MAX_STEPS;
            stepsNeeded = PREVIEW_MAX_STEPS;
        }

        stepsNeeded = Mathf.Clamp(stepsNeeded, PREVIEW_MIN_STEPS, PREVIEW_MAX_STEPS);

        dt = Mathf.Max(0.0001f, effectiveDt);
        steps = stepsNeeded;
    }

    private IEnumerator QuickPreviewLoop()
    {
        const float tick = 0.1f;

        while (true)
        {
            if (!previewDirty)
            {
                yield return new WaitForSecondsRealtime(tick);
                continue;
            }

            previewDirty = false;

            var svc = ctx.BodyService;
            if (svc == null || svc.CentralBody == null)
            {
                line?.Clear();
                yield return new WaitForSecondsRealtime(tick);
                continue;
            }

            float dt;
            int steps;
            ComputePreviewSettings(previewPos, previewVel, svc, out dt, out steps);

            var cb = svc.CentralBody;
            Vector3[] attractorPos = { cb.transform.position };
            float[] attractorMass = { (float)cb.trueMass };
            uint requestGeneration = unchecked(++previewRequestGeneration);

            ctx.TrajectoryComputeController.CalculateTrajectoryGPU_Async(
                previewPos, previewVel, previewMass,
                attractorPos, attractorMass,
                dt, steps,
                points =>
                {
                    if (requestGeneration != previewRequestGeneration)
                        return;

                    if (points == null || points.Length < 2)
                    {
                        line?.Clear();
                        return;
                    }

                    var clipped = clipper(points);
                    line.UpdateLine(clipped);
                });

            yield return new WaitForSecondsRealtime(tick);
        }
    }
}
