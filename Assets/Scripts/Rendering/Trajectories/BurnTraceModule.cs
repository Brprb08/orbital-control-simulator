using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maintains a red trace of the spacecraft path while thrust is active.
/// Designed to be used by TrajectoryRenderer (or similar orchestrators).
/// </summary>
public sealed class BurnTraceModule
{
    private readonly ProceduralLineRenderer line;
    private readonly float sampleInterval;
    private readonly float minDistanceSqr;
    private readonly int maxPoints;

    private readonly List<Vector3> points = new();
    private float nextSampleTime;
    private bool tracingActive;

    public BurnTraceModule(
        ProceduralLineRenderer line,
        float sampleInterval,
        float minDistance,
        int maxPoints)
    {
        this.line = line;
        this.sampleInterval = Mathf.Max(0.0001f, sampleInterval);
        this.minDistanceSqr = Mathf.Max(0f, minDistance * minDistance);
        this.maxPoints = Mathf.Max(8, maxPoints);
    }

    public void Reset()
    {
        points.Clear();
        tracingActive = false;
        line?.Clear();
    }

    /// <summary>
    /// Call once per frame from Update.
    /// </summary>
    public void Update(bool thrusting, Transform bodyTransform, float unscaledTime)
    {
        if (line == null || bodyTransform == null) return;

        // start tracing when thrust begins
        if (!tracingActive && thrusting)
        {
            tracingActive = true;
            points.Clear();
            nextSampleTime = unscaledTime;
            points.Add(bodyTransform.position);
            line.UpdateLine(points.ToArray());
        }

        // sample while thrusting
        if (tracingActive && thrusting)
        {
            if (unscaledTime >= nextSampleTime)
            {
                Vector3 pos = bodyTransform.position;

                bool farEnough =
                    points.Count == 0 ||
                    (pos - points[points.Count - 1]).sqrMagnitude >= minDistanceSqr;

                if (farEnough)
                {
                    points.Add(pos);
                    if (points.Count > maxPoints)
                        points.RemoveRange(0, points.Count - maxPoints);

                    line.UpdateLine(points.ToArray());
                }

                nextSampleTime = unscaledTime + sampleInterval;
            }
        }

        // finalize when thrust stops
        if (tracingActive && !thrusting)
        {
            Vector3 pos = bodyTransform.position;
            if (points.Count == 0 ||
                (pos - points[points.Count - 1]).sqrMagnitude >= minDistanceSqr)
            {
                points.Add(pos);
                if (points.Count > maxPoints)
                    points.RemoveRange(0, points.Count - maxPoints);
                line.UpdateLine(points.ToArray());
            }

            tracingActive = false;
        }
    }
}
