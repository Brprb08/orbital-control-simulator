using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Collider))]
public class NodeDragHandle : MonoBehaviour
{
    [Header("Optional")]
    [SerializeField] private ManeuverNodeIndicator indicator;
    [SerializeField] private float indicatorPickupPaddingPixels = 24f;

    [Header("Drag Performance")]
    [SerializeField, Min(8)] private int localSegmentSearchRadius = 96;
    [SerializeField, Min(16)] private int fallbackSegmentSearchRadius = 320;
    [SerializeField, Min(1f)] private float maxIndexJumpPerFrame = 32f;

    ManeuverNodeManager mgr;
    Camera cam;
    bool dragging;
    int lastBestSegment = -1;
    float lastDraggedFloatIndex = -1f;

    ManeuverNode CurrentNode =>
        (mgr != null && mgr.HasNode) ? mgr.CurrentNode : null;

    List<Vector3> Traj =>
        CurrentNode != null ? CurrentNode.trajectorySnapshot : null;

    public void Init(ManeuverNodeManager manager)
    {
        mgr = manager;
        cam = Camera.main;

        if (indicator == null)
            indicator = FindFirstObjectByType<ManeuverNodeIndicator>();
    }

    void OnMouseDown()
    {
        if (mgr == null) return;
        var node = CurrentNode;
        if (node == null || node.isFinalized) return;

        BeginDrag();
    }

    void OnMouseUp()
    {
        EndDrag();
    }

    void Update()
    {
        if (mgr == null) return;

        var node = CurrentNode;
        var traj = Traj;
        if (node == null || node.isFinalized || traj == null || traj.Count < 2)
        {
            dragging = false;
            lastBestSegment = -1;
            lastDraggedFloatIndex = -1f;
            return;
        }

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        // Added: allow starting drag from the maneuver indicator
        if (!dragging && Input.GetMouseButtonDown(0))
        {
            if (indicator != null &&
                indicator.IsIndicatorVisible() &&
                indicator.IsPointerOverIndicator(Input.mousePosition, indicatorPickupPaddingPixels))
            {
                BeginDrag();
            }
        }

        if (!dragging)
            return;

        if (!Input.GetMouseButton(0))
        {
            EndDrag();
            return;
        }

        if (!mgr.TryGetCurrentNodeIndex(out float currentFloatIndex))
            return;

        float referenceFloatIndex = lastDraggedFloatIndex >= 0f
            ? lastDraggedFloatIndex
            : currentFloatIndex;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        int count = traj.Count;
        int bestSeg = -1;
        float bestSegT = 0f;
        float bestScore = float.PositiveInfinity;

        int searchAnchor = lastBestSegment >= 0
            ? lastBestSegment
            : Mathf.Clamp(Mathf.RoundToInt(referenceFloatIndex), 0, count - 2);

        EvaluateSegmentRange(
            ray,
            traj,
            referenceFloatIndex,
            Mathf.Max(0, searchAnchor - localSegmentSearchRadius),
            Mathf.Min(count - 2, searchAnchor + localSegmentSearchRadius),
            ref bestSeg,
            ref bestSegT,
            ref bestScore
        );

        if (bestSeg < 0 && lastBestSegment < 0)
        {
            int fallbackAnchor = Mathf.Clamp(Mathf.RoundToInt(referenceFloatIndex), 0, count - 2);
            EvaluateSegmentRange(
                ray,
                traj,
                referenceFloatIndex,
                Mathf.Max(0, fallbackAnchor - fallbackSegmentSearchRadius),
                Mathf.Min(count - 2, fallbackAnchor + fallbackSegmentSearchRadius),
                ref bestSeg,
                ref bestSegT,
                ref bestScore
            );
        }

        if (bestSeg < 0 && lastBestSegment < 0)
        {
            EvaluateSegmentRange(
                ray,
                traj,
                referenceFloatIndex,
                0,
                count - 2,
                ref bestSeg,
                ref bestSegT,
                ref bestScore
            );
        }

        if (bestSeg < 0)
            return;

        lastBestSegment = bestSeg;

        float newFloatIndex = bestSeg + bestSegT;
        newFloatIndex = Mathf.MoveTowards(referenceFloatIndex, newFloatIndex, maxIndexJumpPerFrame);
        lastDraggedFloatIndex = newFloatIndex;

        // Move node along trajectory -> updates burnTime + position
        mgr.DragNodeToFloatIndex(newFloatIndex);
    }

    private void BeginDrag()
    {
        dragging = true;
        lastBestSegment = -1;

        if (mgr != null && mgr.TryGetCurrentNodeIndex(out float currentFloatIndex))
            lastDraggedFloatIndex = currentFloatIndex;
        else
            lastDraggedFloatIndex = -1f;
    }

    private void EndDrag()
    {
        dragging = false;
        lastBestSegment = -1;
        lastDraggedFloatIndex = -1f;
    }

    private static void EvaluateSegmentRange(
        Ray ray,
        List<Vector3> traj,
        float currentFloatIndex,
        int startSeg,
        int endSeg,
        ref int bestSeg,
        ref float bestSegT,
        ref float bestScore)
    {
        const float indexWeight = 0.1f;

        for (int i = startSeg; i <= endSeg; i++)
        {
            Vector3 a = traj[i];
            Vector3 b = traj[i + 1];

            ClosestPointsRaySegment(ray, a, b, out float segT, out _, out float sqrDist);
            segT = Mathf.Clamp01(segT);

            float candidateIndex = i + segT;
            float deltaIndex = Mathf.Abs(candidateIndex - currentFloatIndex);
            float score = sqrDist + (deltaIndex * indexWeight);

            if (score < bestScore)
            {
                bestScore = score;
                bestSeg = i;
                bestSegT = segT;
            }
        }
    }

    /// <summary>
    /// Closest point on segment AB to ray. Returns:
    /// - segT: segment parameter [0,1]
    /// - segPoint: point on segment
    /// - sqrDist: squared distance between that segPoint and closest point on ray
    /// </summary>
    static void ClosestPointsRaySegment(
        Ray ray,
        Vector3 a,
        Vector3 b,
        out float segT,
        out Vector3 segPoint,
        out float sqrDist)
    {
        Vector3 p = ray.origin;
        Vector3 r = ray.direction.normalized;
        Vector3 q = a;
        Vector3 s = b - a;

        float rDotr = Vector3.Dot(r, r); // ~1
        float sDots = Vector3.Dot(s, s);
        float rDots = Vector3.Dot(r, s);
        Vector3 w0 = p - q;
        float rDotw0 = Vector3.Dot(r, w0);
        float sDotw0 = Vector3.Dot(s, w0);

        float denom = rDotr * sDots - rDots * rDots;

        float tSeg;
        float tRay;

        if (denom < 1e-6f || sDots < 1e-8f)
        {
            // Degenerate / almost parallel, project onto segment only
            tSeg = sDots > 1e-8f ? -sDotw0 / sDots : 0f;
            tSeg = Mathf.Clamp01(tSeg);
            segPoint = q + s * tSeg;

            Vector3 diff = segPoint - p;
            tRay = Vector3.Dot(diff, r) / Mathf.Max(1e-6f, rDotr);
        }
        else
        {
            tSeg = (rDots * rDotw0 - rDotr * sDotw0) / denom;
            tSeg = Mathf.Clamp01(tSeg);
            segPoint = q + s * tSeg;

            Vector3 diff = segPoint - p;
            tRay = Vector3.Dot(diff, r) / Mathf.Max(1e-6f, rDotr);
        }

        Vector3 rayPoint = p + r * Mathf.Max(tRay, 0f);
        sqrDist = (segPoint - rayPoint).sqrMagnitude;
        segT = tSeg;
    }
}
