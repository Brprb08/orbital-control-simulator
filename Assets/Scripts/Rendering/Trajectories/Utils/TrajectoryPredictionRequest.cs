using UnityEngine;

public enum TrajectoryPredictionBackend
{
    GpuGravity,
    NativeMatched
}

public readonly struct TrajectoryPredictionRequest
{
    public int Steps { get; }
    public float DeltaTime { get; }
    public float Epoch { get; }
    public float RefreshInterval { get; }
    public bool RequiresContinuousRefresh { get; }
    public TrajectoryPredictionBackend Backend { get; }
    public int MaxOutputPoints { get; }

    public TrajectoryPredictionRequest(
        int steps,
        float deltaTime,
        float epoch,
        float refreshInterval,
        bool requiresContinuousRefresh,
        TrajectoryPredictionBackend backend,
        int maxOutputPoints)
    {
        Steps = steps;
        DeltaTime = deltaTime;
        Epoch = epoch;
        RefreshInterval = refreshInterval;
        RequiresContinuousRefresh = requiresContinuousRefresh;
        Backend = backend;
        MaxOutputPoints = Mathf.Max(2, maxOutputPoints);
    }

    public TrajectoryPredictionRequest WithMaxOutputPoints(int maxOutputPoints)
    {
        return new TrajectoryPredictionRequest(
            Steps,
            DeltaTime,
            Epoch,
            RefreshInterval,
            RequiresContinuousRefresh,
            Backend,
            maxOutputPoints
        );
    }

    public TrajectoryPredictionRequest WithBackend(TrajectoryPredictionBackend backend)
    {
        return new TrajectoryPredictionRequest(
            Steps,
            DeltaTime,
            Epoch,
            RefreshInterval,
            RequiresContinuousRefresh,
            backend,
            MaxOutputPoints
        );
    }

}

public static class TrajectoryPredictionPlanner
{
    private const int MaxSteps = 100000;
    private const float DaySeconds = 24f * 60f * 60f;
    private const float MaxHorizonSeconds = 10f * DaySeconds;
    private const float MinHorizonSeconds = 20000f;
    private const float MaxFastHorizonSeconds = 2f * DaySeconds;
    public const float MaxMatchedHorizonSeconds = 20000f;
    private const float DragRefreshNearSeconds = 0.6f;
    private const float DragRefreshMidSeconds = 1.2f;
    private const float DragRefreshFarSeconds = 2.5f;
    private const float BallisticRefreshSeconds = 4f;
    public const float DragPeriapsisThresholdKm = 500f;
    private const int FastMinSteps = 2000;
    private const int FastMaxSteps = 12000;
    private const int DefaultGpuMaxOutputPoints = 2500;
    private const int MatchedRealtimeMaxOutputPoints = 1200;
    private const int MatchedFinalMaxOutputPoints = 2200;

    public const float FastTimescaleThreshold = 5f;

    public static bool TryBuildRealtimeRequest(
        NBody body,
        BodyService bodyService,
        BodyRuntimeCoordinator runtimeCoordinator,
        float preferredDeltaTime,
        bool isThrusting,
        float timeScale,
        out TrajectoryPredictionRequest request)
    {
        request = default;

        if (body == null)
            return false;

        OrbitalParameters orbitalParameters = OrbitalCalculations.TryParams(body, bodyService);
        if (!orbitalParameters.isValid)
            return false;

        bool useFastPath = isThrusting || timeScale > FastTimescaleThreshold;
        float horizonSeconds = ComputeHorizonSeconds(body, useFastPath, orbitalParameters);
        bool useMatchedBackend = ShouldUseMatchedBackend(body, bodyService, orbitalParameters, isThrusting);
        float refreshInterval = ResolveRefreshInterval(body, bodyService, orbitalParameters, useMatchedBackend);
        if (useMatchedBackend)
            horizonSeconds = Mathf.Min(horizonSeconds, MaxMatchedHorizonSeconds);

        float effectiveDeltaTime = preferredDeltaTime;
        int steps = Mathf.CeilToInt(horizonSeconds / effectiveDeltaTime);

        if (useFastPath)
        {
            steps = Mathf.Clamp(steps, FastMinSteps, FastMaxSteps);
            effectiveDeltaTime = Mathf.Max(0.0001f, horizonSeconds / steps);
        }
        else
        {
            steps = Mathf.Clamp(steps, 500, MaxSteps);
        }

        float epoch = runtimeCoordinator ? runtimeCoordinator.simulationTime : 0f;
        request = new TrajectoryPredictionRequest(
            steps,
            effectiveDeltaTime,
            epoch,
            refreshInterval,
            useMatchedBackend,
            useMatchedBackend ? TrajectoryPredictionBackend.NativeMatched : TrajectoryPredictionBackend.GpuGravity,
            ResolveMaxOutputPoints(useMatchedBackend, isFinalPass: false)
        );
        return true;
    }

    public static bool TryBuildFinalPassRequest(
        NBody body,
        BodyService bodyService,
        BodyRuntimeCoordinator runtimeCoordinator,
        float preferredDeltaTime,
        out TrajectoryPredictionRequest request)
    {
        request = default;

        if (body == null)
            return false;

        OrbitalParameters orbitalParameters = OrbitalCalculations.TryParams(body, bodyService);
        if (!orbitalParameters.isValid)
            return false;

        float horizonSeconds = ComputeHorizonSeconds(body, fast: false, orbitalParameters);
        bool useMatchedBackend = ShouldUseMatchedBackend(body, bodyService, orbitalParameters, isThrusting: false);

        // Matched drag-aware prediction is intentionally horizon-limited. For long transfer
        // orbits (for example, LEO -> GEO raises with periapsis still in drag-relevant range),
        // that cap can truncate the final rendered orbit. Prefer the uncapped final GPU pass
        // so the user still gets a full post-burn path.
        if (useMatchedBackend && horizonSeconds > MaxMatchedHorizonSeconds)
            useMatchedBackend = false;

        float refreshInterval = ResolveRefreshInterval(body, bodyService, orbitalParameters, useMatchedBackend);
        if (useMatchedBackend)
            horizonSeconds = Mathf.Min(horizonSeconds, MaxMatchedHorizonSeconds);

        float effectiveDeltaTime = preferredDeltaTime;
        int steps = Mathf.CeilToInt(horizonSeconds / effectiveDeltaTime);

        if (steps > MaxSteps)
        {
            effectiveDeltaTime = horizonSeconds / MaxSteps;
            steps = MaxSteps;
        }

        steps = Mathf.Clamp(steps + 8, 1500, MaxSteps);

        float epoch = runtimeCoordinator ? runtimeCoordinator.simulationTime : 0f;
        request = new TrajectoryPredictionRequest(
            steps,
            effectiveDeltaTime,
            epoch,
            refreshInterval,
            useMatchedBackend,
            useMatchedBackend ? TrajectoryPredictionBackend.NativeMatched : TrajectoryPredictionBackend.GpuGravity,
            ResolveMaxOutputPoints(useMatchedBackend, isFinalPass: true)
        );
        return true;
    }

    public static bool ShouldContinuouslyRefresh(TrajectoryPredictionRequest request)
    {
        return request.RequiresContinuousRefresh && request.RefreshInterval > 0f;
    }

    public static bool IsLongDragTransferOrbit(NBody body, BodyService bodyService)
    {
        OrbitalParameters orbitalParameters = OrbitalCalculations.TryParams(body, bodyService);
        if (!orbitalParameters.isValid)
            return false;

        return IsLongDragTransferOrbit(body, bodyService, orbitalParameters);
    }

    public static bool IsLongDragTransferOrbit(
        NBody body,
        BodyService bodyService,
        OrbitalParameters orbitalParameters)
    {
        if (bodyService == null || bodyService.CentralBody == null)
            return false;

        if (!IsDragRelevant(body, bodyService.CentralBody, orbitalParameters))
            return false;

        float horizonSeconds = ComputeHorizonSeconds(body, fast: false, orbitalParameters);
        return horizonSeconds > MaxMatchedHorizonSeconds;
    }

    private static float ComputeHorizonSeconds(NBody body, bool fast, OrbitalParameters orbitalParameters)
    {
        if (!orbitalParameters.isValid)
            return Mathf.Clamp(30000f, MinHorizonSeconds, MaxHorizonSeconds);

        float mu = PhysicsConstants.G * body.state.centralBodyMass;
        bool isBoundOrbit = orbitalParameters.eccentricity < 1f && orbitalParameters.semiMajorAxis > 0f;

        float orbitalPeriodSeconds = isBoundOrbit
            ? 2f * Mathf.PI * Mathf.Sqrt(Mathf.Pow(orbitalParameters.semiMajorAxis, 3f) / mu)
            : 60000f;

        if (fast)
        {
            float fastHorizon = Mathf.Clamp(orbitalPeriodSeconds * 1.2f, 10000f, MaxFastHorizonSeconds);
            return Mathf.Min(fastHorizon, MaxHorizonSeconds);
        }

        return Mathf.Clamp(orbitalPeriodSeconds * 1.25f, MinHorizonSeconds, MaxHorizonSeconds);
    }

    private static bool ShouldUseMatchedBackend(
        NBody body,
        BodyService bodyService,
        OrbitalParameters orbitalParameters,
        bool isThrusting)
    {
        if (body == null || bodyService == null || bodyService.CentralBody == null)
            return false;

        if (isThrusting)
            return false;

        if (!bodyService.DrivePhysics)
            return false;

        return IsDragRelevant(body, bodyService.CentralBody, orbitalParameters);
    }

    private static float ResolveRefreshInterval(
        NBody body,
        BodyService bodyService,
        OrbitalParameters orbitalParameters,
        bool useMatchedBackend)
    {
        if (!useMatchedBackend || body == null || bodyService == null || bodyService.CentralBody == null)
            return BallisticRefreshSeconds;

        float currentAltitudeKm = (float)body.altitude * 10f;
        float periapsisAltitudeKm = (orbitalParameters.perigeeRadius - bodyService.CentralBody.radius) * 10f;
        float representativeAltitudeKm = Mathf.Min(currentAltitudeKm, periapsisAltitudeKm);

        if (representativeAltitudeKm <= 300f)
            return DragRefreshNearSeconds;

        if (representativeAltitudeKm <= 1000f)
            return DragRefreshMidSeconds;

        return DragRefreshFarSeconds;
    }

    private static bool IsDragRelevant(NBody body, NBody centralBody, OrbitalParameters orbitalParameters)
    {
        if (body == null || centralBody == null || !orbitalParameters.isValid)
            return false;

        if (!(body.dragCoefficient > 0f) || !(body.atmosphericDensity0 > 0f))
            return false;

        float periapsisAltitudeKm = (orbitalParameters.perigeeRadius - centralBody.radius) * 10f;
        return periapsisAltitudeKm <= DragPeriapsisThresholdKm;
    }

    private static int ResolveMaxOutputPoints(bool useMatchedBackend, bool isFinalPass)
    {
        if (!useMatchedBackend)
            return DefaultGpuMaxOutputPoints;

        return isFinalPass
            ? MatchedFinalMaxOutputPoints
            : MatchedRealtimeMaxOutputPoints;
    }
}
