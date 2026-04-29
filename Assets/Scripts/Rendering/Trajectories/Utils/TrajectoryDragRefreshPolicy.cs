using System;
using UnityEngine;

public enum TrajectoryDragRefreshTransition
{
    None,
    EnteredDragPassage,
    ExitedDragPassage
}

[Serializable]
public sealed class TrajectoryDragRefreshPolicy
{
    [SerializeField, Min(0f)] private float enterAltitudeKm = 480f;
    [SerializeField, Min(0f)] private float exitAltitudeKm = 520f;

    public bool DragRefreshOrbitActive { get; private set; }
    public bool LongDragPassageRefreshActive { get; private set; }

    public void Reset()
    {
        DragRefreshOrbitActive = false;
        LongDragPassageRefreshActive = false;
    }

    public TrajectoryDragRefreshTransition Update(NBody trackedBody, BodyService bodyService, bool isThrusting)
    {
        bool wasPassageActive = LongDragPassageRefreshActive;
        DragRefreshOrbitActive = false;

        if (trackedBody == null || bodyService == null || bodyService.CentralBody == null || isThrusting)
        {
            LongDragPassageRefreshActive = false;
        }
        else
        {
            OrbitalParameters orbitalParameters = OrbitalCalculations.TryParams(trackedBody, bodyService);
            if (orbitalParameters.isValid)
                DragRefreshOrbitActive =
                    trackedBody.dragCoefficient > 0f &&
                    trackedBody.atmosphericDensity0 > 0f &&
                    (orbitalParameters.perigeeRadius - bodyService.CentralBody.radius) * 10f <=
                    TrajectoryPredictionPlanner.DragPeriapsisThresholdKm;

            if (!DragRefreshOrbitActive)
            {
                LongDragPassageRefreshActive = false;
            }
            else
            {
                float currentAltitudeKm = (float)trackedBody.altitude * 10f;
                float thresholdKm = wasPassageActive ? exitAltitudeKm : enterAltitudeKm;
                LongDragPassageRefreshActive = currentAltitudeKm <= thresholdKm;
            }
        }

        if (LongDragPassageRefreshActive == wasPassageActive)
            return TrajectoryDragRefreshTransition.None;

        return LongDragPassageRefreshActive
            ? TrajectoryDragRefreshTransition.EnteredDragPassage
            : TrajectoryDragRefreshTransition.ExitedDragPassage;
    }

    public TrajectoryPredictionRequest ResolveRequest(
        NBody body,
        NBody trackedBody,
        TrajectoryPredictionRequest request)
    {
        if (body == null || body != trackedBody || !DragRefreshOrbitActive || LongDragPassageRefreshActive)
            return request;

        if (request.Backend == TrajectoryPredictionBackend.NativeMatched)
            return request.WithBackend(TrajectoryPredictionBackend.GpuGravity);

        return request;
    }

    public bool ShouldSuppressContinuousRefresh(NBody body, NBody trackedBody)
    {
        return body == trackedBody && DragRefreshOrbitActive && !LongDragPassageRefreshActive;
    }
}
