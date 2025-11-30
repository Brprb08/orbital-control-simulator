using UnityEngine;

/// <summary>
/// Builds a stable orbital basis (r̂, v̂, n̂) with hysteresis, blending, and sign continuity.
/// Solves direction flips when v ≈ radial during free fall.
/// </summary>
public sealed class OrbitalFrameFilter
{
    const float EPS2 = 1e-10f;
    const float H_MIN = 0.02f; // min sin(angle) between r and v (~1.1°)
    const float TAU = 0.35f;   // seconds; lower = snappier, higher = smoother

    private Vector3 rPrev = Vector3.right;
    private Vector3 vPrev = Vector3.forward;
    private Vector3 nPrev = Vector3.up;
    private bool hasPrev = false;

    /// <summary>
    /// Clears cached state so the next GetBasis call re-anchors the frame.
    /// </summary>
    public void Reset()
    {
        hasPrev = false;
    }

    /// <summary>
    /// Forces the filter to anchor to the current basis (no blending, no sign continuity).
    /// Use this right after switching targets.
    /// </summary>
    public void SnapTo(Vector3 rFresh, Vector3 vFresh, Vector3 nFresh)
    {
        rPrev = rFresh.normalized;
        nPrev = nFresh.normalized;
        vPrev = Vector3.Cross(nPrev, rPrev).normalized;
        hasPrev = true;
    }

    /// <summary>
    /// Computes a filtered basis. Returns true if the instantaneous r–v frame is strong enough
    /// (not dominated by the fallback plane).
    /// </summary>
    public bool GetBasis(
        Vector3 position,
        Vector3 velocity,
        Vector3 centerPos,
        out Vector3 rHat,
        out Vector3 vHat,
        out Vector3 nHat)
    {
        Vector3 r = position - centerPos;
        float r2 = r.sqrMagnitude;
        Vector3 rN = (r2 > EPS2) ? (r / Mathf.Sqrt(r2)) : Vector3.right;

        float v2 = velocity.sqrMagnitude;
        Vector3 vN = (v2 > EPS2) ? (velocity / Mathf.Sqrt(v2)) : Vector3.zero;

        float sinTheta = 0f;
        if (r2 > EPS2 && v2 > EPS2)
        {
            sinTheta = Vector3.Cross(rN, vN).magnitude;
        }

        bool trueFrameUsable = sinTheta >= H_MIN;

        Vector3 refUp = (Mathf.Abs(Vector3.Dot(rN, Vector3.up)) > 0.98f) ? Vector3.right : Vector3.up;
        Vector3 nFallback = Vector3.Cross(rN, refUp);
        if (nFallback.sqrMagnitude < EPS2) nFallback = Vector3.forward;
        nFallback.Normalize();

        Vector3 vFallback = Vector3.Cross(nFallback, rN).normalized;

        Vector3 rFresh = rN;
        Vector3 vFresh = trueFrameUsable ? vN : vFallback;
        Vector3 nFresh = Vector3.Cross(rFresh, vFresh);
        if (nFresh.sqrMagnitude < EPS2)
        {
            nFresh = nFallback;
        }
        else
        {
            nFresh.Normalize();
        }

        vFresh = Vector3.Cross(nFresh, rFresh).normalized;

        if (!hasPrev)
        {
            rPrev = rFresh;
            vPrev = vFresh;
            nPrev = nFresh;
            hasPrev = true;
        }

        if (Vector3.Dot(nFresh, nPrev) < 0f)
        {
            nFresh = -nFresh;
            vFresh = -vFresh;
        }

        float alpha = 1f - Mathf.Exp(-Time.deltaTime / TAU);
        rPrev = Vector3.Slerp(rPrev, rFresh, alpha).normalized;
        nPrev = Vector3.Slerp(nPrev, nFresh, alpha).normalized;
        vPrev = Vector3.Cross(nPrev, rPrev).normalized;

        rHat = rPrev;
        vHat = vPrev;
        nHat = nPrev;

        return trueFrameUsable;
    }
}
