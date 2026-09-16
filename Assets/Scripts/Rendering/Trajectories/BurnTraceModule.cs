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

    private readonly Vector3[] points;
    private readonly Vector3[] lineBuffer;
    private int firstPoint;
    private int pointCount;
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
        points = new Vector3[this.maxPoints];
        lineBuffer = new Vector3[this.maxPoints];
    }

    public void Reset()
    {
        firstPoint = 0;
        pointCount = 0;
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
            firstPoint = 0;
            pointCount = 0;
            nextSampleTime = unscaledTime;
            AddPoint(bodyTransform.position);
            RefreshLine();
        }

        // sample while thrusting
        if (tracingActive && thrusting)
        {
            if (unscaledTime >= nextSampleTime)
            {
                Vector3 pos = bodyTransform.position;

                bool farEnough =
                    pointCount == 0 ||
                    (pos - GetLastPoint()).sqrMagnitude >= minDistanceSqr;

                if (farEnough)
                {
                    AddPoint(pos);
                    RefreshLine();
                }

                nextSampleTime = unscaledTime + sampleInterval;
            }
        }

        // finalize when thrust stops
        if (tracingActive && !thrusting)
        {
            Vector3 pos = bodyTransform.position;
            if (pointCount == 0 ||
                (pos - GetLastPoint()).sqrMagnitude >= minDistanceSqr)
            {
                AddPoint(pos);
                RefreshLine();
            }

            tracingActive = false;
        }
    }

    private void AddPoint(Vector3 point)
    {
        if (pointCount < maxPoints)
        {
            points[(firstPoint + pointCount) % maxPoints] = point;
            pointCount++;
            return;
        }

        points[firstPoint] = point;
        firstPoint = (firstPoint + 1) % maxPoints;
    }

    private Vector3 GetLastPoint()
    {
        return points[(firstPoint + pointCount - 1) % maxPoints];
    }

    private void RefreshLine()
    {
        if (pointCount < 2)
            return;

        for (int i = 0; i < pointCount; i++)
            lineBuffer[i] = points[(firstPoint + i) % maxPoints];

        line.UpdateLine(lineBuffer, pointCount);
    }
}
