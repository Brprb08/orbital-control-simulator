using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Collider))]
public class NodeDragHandle : MonoBehaviour
{
    ManeuverNodeManager mgr;
    Camera cam;
    bool dragging;

    // cache to avoid allocations while dragging
    List<Vector3> traj => mgr?.nodes.Count > 0 ? mgr.nodes[0].trajectorySnapshot : null;

    public void Init(ManeuverNodeManager manager)
    {
        mgr = manager;
        cam = Camera.main;
    }

    void OnMouseDown()
    {
        if (mgr == null || mgr.nodes.Count == 0) return;
        if (mgr.nodes[0].isFinalized) return;
        dragging = true;
    }

    void OnMouseUp() { dragging = false; }

    void Update()
    {
        if (!dragging || mgr == null || traj == null || traj.Count < 2) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        // Find closest point on the polyline to the mouse ray
        int bestSeg = 0;
        float bestSegT = 0f;
        float bestDist2 = float.PositiveInfinity;

        for (int i = 0; i < traj.Count - 1; i++)
        {
            Vector3 a = traj[i];
            Vector3 b = traj[i + 1];
            // closest point between ray and segment (approximate: project ray origin to segment direction in screen space)
            ClosestPointsRaySegment(ray, a, b, out float segT, out Vector3 segPoint);
            float d2 = (segPoint - ray.origin).sqrMagnitude;
            if (d2 < bestDist2)
            {
                bestDist2 = d2;
                bestSeg = i;
                bestSegT = Mathf.Clamp01(segT);
            }
        }

        float floatIndex = bestSeg + bestSegT;
        mgr.SetNodeAtFloatIndex(floatIndex);
    }

    // Computes closest point on segment AB to ray R; returns segment t in [0,1]
    static void ClosestPointsRaySegment(Ray ray, Vector3 a, Vector3 b, out float segT, out Vector3 segPoint)
    {
        Vector3 u = ray.direction;
        Vector3 v = b - a;
        Vector3 w0 = ray.origin - a;

        float aUU = Vector3.Dot(u, u);
        float aVV = Vector3.Dot(v, v);
        float aUV = Vector3.Dot(u, v);
        float bU = Vector3.Dot(u, w0);
        float bV = Vector3.Dot(v, w0);

        float denom = aUU * aVV - aUV * aUV;
        float tV = denom > 1e-8f ? (aUV * bU - aUU * bV) / denom : 0f;
        tV = Mathf.Clamp01(tV);
        segT = tV;
        segPoint = a + v * tV;
    }
}
