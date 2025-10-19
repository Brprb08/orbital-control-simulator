using UnityEngine;

/// <summary>
/// Builds a stable orbital basis (r̂, v̂, n̂) with hysteresis, blending, and sign continuity.
/// Solves "direction flips" when v ~ radial during free fall.
/// </summary>
public sealed class OrbitalFrameFilter
{
    // thresholds & smoothing
    const float EPS2 = 1e-10f;    // tiny length^2 guard
    const float H_MIN = 0.02f;    // min sin(angle) between r and v for "true frame" (~1.1°)
    const float TAU = 0.35f;     // seconds; lower = snappier transition, higher = smoother

    // cached, stable basis
    private Vector3 rPrev = Vector3.right;
    private Vector3 vPrev = Vector3.forward;
    private Vector3 nPrev = Vector3.up;
    private bool hasPrev = false;

    public void Reset()
    {
        hasPrev = false;
    }

    /// <summary>
    /// Force the filter to anchor to the current fresh basis (no blending, no sign continuity).
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
    /// Compute a filtered basis. Returns true if using the true r–v frame (not fallback-dominated).
    /// </summary>
    public bool GetBasis(Vector3 position, Vector3 velocity, Vector3 centerPos,
                         out Vector3 rHat, out Vector3 vHat, out Vector3 nHat)
    {
        // raw geometry
        Vector3 r = position - centerPos;
        float r2 = r.sqrMagnitude;
        Vector3 rN = (r2 > EPS2) ? (r / Mathf.Sqrt(r2)) : Vector3.right;

        float v2 = velocity.sqrMagnitude;
        Vector3 vN = (v2 > EPS2) ? (velocity / Mathf.Sqrt(v2)) : Vector3.zero;

        // how "non-radial" is the motion? (|r×v| / |r||v| = sin θ)
        float sinTheta = 0f;
        if (r2 > EPS2 && v2 > EPS2)
        {
            sinTheta = Vector3.Cross(rN, vN).magnitude; // in [0,1]
        }

        bool trueFrameUsable = sinTheta >= H_MIN;

        // --- choose a reference plane when frame is weak ---
        Vector3 refUp = (Mathf.Abs(Vector3.Dot(rN, Vector3.up)) > 0.98f) ? Vector3.right : Vector3.up;
        Vector3 nFallback = Vector3.Cross(rN, refUp);
        if (nFallback.sqrMagnitude < EPS2) nFallback = Vector3.forward;
        nFallback.Normalize();

        Vector3 vFallback = Vector3.Cross(nFallback, rN).normalized;

        // --- intended fresh basis (from physics if strong, else fallback) ---
        Vector3 rFresh = rN;
        Vector3 vFresh = trueFrameUsable ? vN : vFallback;
        // normal points to complete right-handed frame
        Vector3 nFresh = Vector3.Cross(rFresh, vFresh);
        if (nFresh.sqrMagnitude < EPS2)
        {
            nFresh = nFallback;
        }
        else nFresh.Normalize();

        // enforce right-handedness strictly
        vFresh = Vector3.Cross(nFresh, rFresh).normalized;

        // --- first run: initialize cache ---
        if (!hasPrev)
        {
            rPrev = rFresh; vPrev = vFresh; nPrev = nFresh;
            hasPrev = true;
        }

        // --- sign continuity (avoid 180° flips from tiny noise) ---
        if (Vector3.Dot(nFresh, nPrev) < 0f)
        {
            nFresh = -nFresh;
            vFresh = -vFresh; // keep right-handed frame with rFresh
        }

        // --- blend (complementary filter) ---
        float alpha = 1f - Mathf.Exp(-Time.deltaTime / TAU);   // 0..1
        rPrev = Vector3.Slerp(rPrev, rFresh, alpha).normalized;
        nPrev = Vector3.Slerp(nPrev, nFresh, alpha).normalized;
        vPrev = Vector3.Cross(nPrev, rPrev).normalized;        // re-orthogonalize

        rHat = rPrev;
        vHat = vPrev;
        nHat = nPrev;

        return trueFrameUsable;
    }
}
