using System;
using Unity.Mathematics;
using UnityEngine;

public readonly struct TrajectoryMatchedPredictionWorkItem
{
    public double3 StartPosition { get; }
    public double3 StartVelocity { get; }
    public double Mass { get; }
    public float DragCoefficient { get; }
    public float CrossSectionArea { get; }
    public double Mu { get; }
    public int Steps { get; }
    public float DeltaTime { get; }
    public int MaxOutputPoints { get; }

    public TrajectoryMatchedPredictionWorkItem(
        double3 startPosition,
        double3 startVelocity,
        double mass,
        float dragCoefficient,
        float crossSectionArea,
        double mu,
        int steps,
        float deltaTime,
        int maxOutputPoints)
    {
        StartPosition = startPosition;
        StartVelocity = startVelocity;
        Mass = mass;
        DragCoefficient = dragCoefficient;
        CrossSectionArea = crossSectionArea;
        Mu = mu;
        Steps = Mathf.Max(1, steps);
        DeltaTime = Mathf.Max(1e-5f, deltaTime);
        MaxOutputPoints = Mathf.Max(2, maxOutputPoints);
    }
}

public readonly struct TrajectoryMatchedPredictionResult
{
    public Vector3[] Points { get; }
    public float SampleDeltaTime { get; }

    public TrajectoryMatchedPredictionResult(Vector3[] points, float sampleDeltaTime)
    {
        Points = points ?? Array.Empty<Vector3>();
        SampleDeltaTime = Mathf.Max(0f, sampleDeltaTime);
    }
}

public static class TrajectoryMatchedPredictor
{
    private const float MaxNativeStepDt = 0.02f;
    private const double GUnity = 6.67430e-23;

    public static bool TryBuildWorkItem(
        NBody body,
        BodyService bodyService,
        TrajectoryPredictionRequest request,
        out TrajectoryMatchedPredictionWorkItem workItem)
    {
        workItem = default;

        if (body == null || bodyService == null || bodyService.CentralBody == null)
            return false;

        double muUnity = GUnity * bodyService.CentralBody.trueMass;
        workItem = new TrajectoryMatchedPredictionWorkItem(
            body.state.position,
            body.state.velocity,
            body.state.mass,
            body.dragCoefficient,
            ResolveArea(body),
            muUnity,
            request.Steps,
            request.DeltaTime,
            request.MaxOutputPoints
        );
        return true;
    }

    public static TrajectoryMatchedPredictionResult Predict(TrajectoryMatchedPredictionWorkItem workItem)
    {
        if (workItem.Steps <= 0 || workItem.MaxOutputPoints <= 1)
            return new TrajectoryMatchedPredictionResult(Array.Empty<Vector3>(), 0f);

        int lodFactor = Mathf.Max(1, workItem.Steps / workItem.MaxOutputPoints);
        int outputCount = Mathf.CeilToInt((float)workItem.Steps / lodFactor);
        if (outputCount <= 0)
            return new TrajectoryMatchedPredictionResult(Array.Empty<Vector3>(), 0f);

        double3[] positions = { workItem.StartPosition };
        double3[] velocities = { workItem.StartVelocity };
        double[] masses = { workItem.Mass };
        Vector3[] thrusts = { Vector3.zero };
        float[] dragCoeffs = { workItem.DragCoefficient };
        float[] areas = { workItem.CrossSectionArea };
        sbyte[] normalSigns = { 0 };
        byte[] isThrusting = { 0 };
        sbyte[] latchedParity = { 0 };

        Vector3[] result = new Vector3[outputCount];
        int remainingSteps = workItem.Steps;

        for (int i = 0; i < outputCount && remainingSteps > 0; i++)
        {
            int stepChunk = Mathf.Min(lodFactor, remainingSteps);
            float chunkDt = stepChunk * workItem.DeltaTime;
            int substeps = Mathf.Max(1, Mathf.CeilToInt(chunkDt / MaxNativeStepDt));

            NativePhysics.BatchTwoBodyIntegrateMuEx(
                positions,
                velocities,
                masses,
                thrusts,
                dragCoeffs,
                areas,
                normalSigns,
                isThrusting,
                latchedParity,
                1,
                workItem.Mu,
                chunkDt,
                substeps
            );

            result[i] = new Vector3(
                (float)positions[0].x,
                (float)positions[0].y,
                (float)positions[0].z
            );

            remainingSteps -= stepChunk;
        }

        return new TrajectoryMatchedPredictionResult(result, workItem.DeltaTime * lodFactor);
    }

    private static float ResolveArea(NBody body)
    {
        if (body == null)
            return 0f;

        float area = (float)body.state.crossSectionArea;
        if (area > 0f)
            return area;

        double radius = body.radius;
        return (float)(math.PI * radius * radius);
    }
}
