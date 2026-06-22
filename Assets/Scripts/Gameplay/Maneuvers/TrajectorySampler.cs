using System.Collections.Generic;
using UnityEngine;

public static class TrajectorySampler
{
    public static bool TrySampleAtBurnTime(
        ManeuverNode node,
        out Vector3 position,
        out Vector3 velocity,
        out float floatIndex)
    {
        position = Vector3.zero;
        velocity = Vector3.zero;
        floatIndex = 0f;

        if (node == null || node.trajectorySnapshot == null || node.trajectorySnapshot.Count < 2)
            return false;

        var traj = node.trajectorySnapshot;
        float sampleDt = Mathf.Max(1e-5f, node.snapshotDeltaTime);
        int count = traj.Count;

        // fractional index from burnTime
        floatIndex = (node.burnTime - node.snapshotStartTime) / sampleDt;
        floatIndex = Mathf.Clamp(floatIndex, 0f, count - 1.0001f);

        // smooth position at this index
        position = SampleAtIndex(traj, floatIndex);

        velocity = EstimateVelocityAtIndex(traj, floatIndex, sampleDt);

        return true;
    }

    public static bool TrySampleAtBurnTimeWrapped(
        ManeuverNode node,
        out Vector3 position,
        out Vector3 velocity,
        out float floatIndex)
    {
        position = Vector3.zero;
        velocity = Vector3.zero;
        floatIndex = 0f;

        if (node == null || node.trajectorySnapshot == null || node.trajectorySnapshot.Count < 2)
            return false;

        var traj = node.trajectorySnapshot;
        float sampleDt = Mathf.Max(1e-5f, node.snapshotDeltaTime);
        int count = traj.Count;
        float maxIndex = count - 1.0001f;
        float sampleSpan = Mathf.Max(sampleDt, (count - 1) * sampleDt);

        float timeFromSnapshotStart = node.burnTime - node.snapshotStartTime;
        if (timeFromSnapshotStart < 0f || timeFromSnapshotStart > sampleSpan)
            timeFromSnapshotStart = Mathf.Repeat(timeFromSnapshotStart, sampleSpan);

        floatIndex = Mathf.Clamp(timeFromSnapshotStart / sampleDt, 0f, maxIndex);

        position = SampleAtIndex(traj, floatIndex);

        velocity = EstimateVelocityAtIndex(traj, floatIndex, sampleDt);

        return true;
    }

    public static Vector3 EstimateVelocityAtIndex(List<Vector3> trajectory, float floatIndex, float dt)
    {
        if (trajectory == null || trajectory.Count < 2)
            return Vector3.zero;

        float sampleDt = Mathf.Max(1e-5f, dt);
        float maxIndex = trajectory.Count - 1.0001f;
        float centerIndex = Mathf.Clamp(floatIndex, 0f, maxIndex);
        float beforeIndex = Mathf.Max(0f, centerIndex - 0.5f);
        float afterIndex = Mathf.Min(maxIndex, centerIndex + 0.5f);

        if (afterIndex - beforeIndex < 1e-4f)
            return Vector3.zero;

        Vector3 before = SampleAtIndex(trajectory, beforeIndex);
        Vector3 after = SampleAtIndex(trajectory, afterIndex);
        float seconds = (afterIndex - beforeIndex) * sampleDt;

        return seconds > 1e-5f ? (after - before) / seconds : Vector3.zero;
    }

    public static Vector3 EstimateVelocity(List<Vector3> trajectory, int step, float dt)
    {
        if (trajectory == null || trajectory.Count < 3)
            return Vector3.zero;

        if (step <= 0 || step >= trajectory.Count - 1)
            return Vector3.zero;

        return (trajectory[step + 1] - trajectory[step - 1]) / (2f * dt);
    }

    public static Vector3 SampleAtIndex(List<Vector3> traj, float floatIndex)
    {
        if (traj == null || traj.Count == 0)
            return Vector3.zero;

        int count = traj.Count;
        if (count == 1)
            return traj[0];

        // Clamp to [0, count-1]
        floatIndex = Mathf.Clamp(floatIndex, 0f, count - 1.0001f);

        int i = Mathf.FloorToInt(floatIndex);
        float t = floatIndex - i;

        if (i <= 0 || i >= count - 2)
        {
            int i0 = Mathf.Clamp(i, 0, count - 2);
            int i1 = i0 + 1;
            return Vector3.Lerp(traj[i0], traj[i1], t);
        }

        // Catmull-Rom
        Vector3 p0 = traj[i - 1];
        Vector3 p1 = traj[i];
        Vector3 p2 = traj[i + 1];
        Vector3 p3 = traj[i + 2];

        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }
}
