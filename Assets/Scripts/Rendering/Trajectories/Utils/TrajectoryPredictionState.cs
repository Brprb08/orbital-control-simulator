using System.Collections.Generic;
using UnityEngine;

public sealed class TrajectoryPredictionState
{
    public float NextContinuousPredictionTime { get; private set; }
    public float NextContinuousHighQualityTime { get; private set; }
    public Vector3 LastSourcePosition { get; private set; }
    public Vector3 LastSourceVelocity { get; private set; }
    public float LastEpoch { get; private set; }
    public bool HasSourceState { get; private set; }
    public TrajectoryPredictionRequest LastRequest { get; private set; }

    public void Reset()
    {
        NextContinuousPredictionTime = 0f;
        NextContinuousHighQualityTime = 0f;
        LastSourcePosition = Vector3.zero;
        LastSourceVelocity = Vector3.zero;
        LastEpoch = 0f;
        HasSourceState = false;
        LastRequest = default;
    }

    public void CacheSourceState(
        NBody body,
        TrajectoryPredictionRequest request,
        float unscaledTime,
        float minimumRefreshInterval)
    {
        if (body == null)
        {
            Reset();
            return;
        }

        LastSourcePosition = body.transform.position;
        LastSourceVelocity = body.velocity;
        LastEpoch = request.Epoch;
        LastRequest = request;
        HasSourceState = true;
        NextContinuousPredictionTime = unscaledTime +
                                       Mathf.Max(minimumRefreshInterval, request.RefreshInterval);
    }

    public void ScheduleNextHighQualityPass(float unscaledTime, float interval)
    {
        NextContinuousHighQualityTime = unscaledTime + interval;
    }

    public bool IsHighQualityPassDue(float unscaledTime)
    {
        return unscaledTime >= NextContinuousHighQualityTime;
    }

    public bool ShouldContinuouslyRefresh(
        NBody body,
        NBody latestPredictionBody,
        IReadOnlyList<Vector3> latestPrediction,
        float unscaledTime,
        float simulationTime,
        float minimumRefreshInterval,
        float positionDriftThreshold,
        float velocityDriftThreshold)
    {
        if (body == null)
            return false;

        if (LastRequest.Backend != TrajectoryPredictionBackend.NativeMatched)
            return false;

        if (!TrajectoryPredictionPlanner.ShouldContinuouslyRefresh(LastRequest))
            return false;

        if (!HasSourceState || latestPredictionBody != body || latestPrediction == null || latestPrediction.Count < 2)
            return true;

        if (unscaledTime < NextContinuousPredictionTime)
            return false;

        float positionThresholdSq = positionDriftThreshold * positionDriftThreshold;
        float velocityThresholdSq = velocityDriftThreshold * velocityDriftThreshold;
        bool positionDrifted = (body.transform.position - LastSourcePosition).sqrMagnitude >= positionThresholdSq;
        bool velocityDrifted = (body.velocity - LastSourceVelocity).sqrMagnitude >= velocityThresholdSq;

        float epochDrift = Mathf.Abs(simulationTime - LastEpoch);
        float refreshInterval = Mathf.Max(minimumRefreshInterval, LastRequest.RefreshInterval);

        return positionDrifted || velocityDrifted || epochDrift >= refreshInterval;
    }
}
