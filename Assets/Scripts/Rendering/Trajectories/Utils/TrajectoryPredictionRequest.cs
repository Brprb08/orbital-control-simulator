using UnityEngine;

public readonly struct TrajectoryPredictionRequest
{
    public int Steps { get; }
    public float DeltaTime { get; }
    public float Epoch { get; }

    public TrajectoryPredictionRequest(int steps, float deltaTime, float epoch)
    {
        Steps = steps;
        DeltaTime = deltaTime;
        Epoch = epoch;
    }
}

public static class TrajectoryPredictionPlanner
{
    private const int MaxSteps = 100000;
    private const float DaySeconds = 24f * 60f * 60f;
    private const float MaxHorizonSeconds = 10f * DaySeconds;
    private const float MinHorizonSeconds = 20000f;
    private const float MaxFastHorizonSeconds = 2f * DaySeconds;
    private const int FastMinSteps = 2000;
    private const int FastMaxSteps = 12000;

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
        request = new TrajectoryPredictionRequest(steps, effectiveDeltaTime, epoch);
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

        float effectiveDeltaTime = preferredDeltaTime;
        int steps = Mathf.CeilToInt(horizonSeconds / effectiveDeltaTime);

        if (steps > MaxSteps)
        {
            effectiveDeltaTime = horizonSeconds / MaxSteps;
            steps = MaxSteps;
        }

        steps = Mathf.Clamp(steps + 8, 1500, MaxSteps);

        float epoch = runtimeCoordinator ? runtimeCoordinator.simulationTime : 0f;
        request = new TrajectoryPredictionRequest(steps, effectiveDeltaTime, epoch);
        return true;
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
}