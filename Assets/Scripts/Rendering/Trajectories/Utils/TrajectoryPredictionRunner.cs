using System;
using System.Threading.Tasks;
using UnityEngine;

public readonly struct TrajectoryPredictionResult
{
    public NBody Body { get; }
    public Vector3[] Points { get; }
    public TrajectoryPredictionRequest Request { get; }
    public float SampleDeltaTime { get; }

    public TrajectoryPredictionResult(
        NBody body,
        Vector3[] points,
        TrajectoryPredictionRequest request,
        float sampleDeltaTime)
    {
        Body = body;
        Points = points ?? Array.Empty<Vector3>();
        Request = request;
        SampleDeltaTime = sampleDeltaTime;
    }
}

public sealed class TrajectoryPredictionRunner
{
    private uint generation;
    private Task<TrajectoryMatchedPredictionResult> matchedTask;
    private uint matchedTaskGeneration;
    private NBody matchedTaskBody;
    private TrajectoryPredictionRequest matchedTaskRequest;

    private TrajectoryPredictionResult bufferedResult;
    private bool hasBufferedResult;

    public bool IsComputing { get; private set; }
    public bool HasBufferedResult => hasBufferedResult;

    public bool Begin(
        NBody body,
        BodyService bodyService,
        TrajectoryPredictionRequest request,
        Func<bool> ownerIsAlive)
    {
        if (body == null)
            return false;

        IsComputing = true;
        uint requestGeneration = ++generation;

        if (request.Backend == TrajectoryPredictionBackend.NativeMatched)
        {
            if (!TrajectoryMatchedPredictor.TryBuildWorkItem(
                    body,
                    bodyService,
                    request,
                    out TrajectoryMatchedPredictionWorkItem workItem))
            {
                IsComputing = false;
                return false;
            }

            matchedTaskGeneration = requestGeneration;
            matchedTaskBody = body;
            matchedTaskRequest = request;
            matchedTask = Task.Run(() => TrajectoryMatchedPredictor.Predict(workItem));
            return true;
        }

        body.CalculatePredictedTrajectoryGPU_Async(
            steps: request.Steps,
            deltaTime: request.DeltaTime,
            onComplete: resultArray =>
            {
                if (ownerIsAlive != null && !ownerIsAlive())
                    return;

                if (requestGeneration != generation)
                    return;

                QueueResult(
                    body,
                    resultArray,
                    request,
                    ResolveSampleDeltaTime(request, resultArray)
                );
            }
        );

        return true;
    }

    public void Invalidate()
    {
        unchecked
        {
            generation++;
        }

        matchedTask = null;
        matchedTaskBody = null;
        matchedTaskRequest = default;
        matchedTaskGeneration = 0;
        ClearBufferedResult();
        IsComputing = false;
    }

    public void PumpCompletedWork()
    {
        if (matchedTask == null || !matchedTask.IsCompleted)
            return;

        Task<TrajectoryMatchedPredictionResult> completedTask = matchedTask;
        uint taskGeneration = matchedTaskGeneration;
        NBody taskBody = matchedTaskBody;
        TrajectoryPredictionRequest taskRequest = matchedTaskRequest;

        matchedTask = null;
        matchedTaskBody = null;
        matchedTaskRequest = default;
        matchedTaskGeneration = 0;

        if (taskGeneration != generation)
        {
            IsComputing = false;
            return;
        }

        if (completedTask.IsCanceled)
        {
            IsComputing = false;
            return;
        }

        if (completedTask.IsFaulted)
        {
            Debug.LogException(completedTask.Exception);
            IsComputing = false;
            return;
        }

        TrajectoryMatchedPredictionResult result = completedTask.Result;
        QueueResult(taskBody, result.Points, taskRequest, result.SampleDeltaTime);
    }

    public bool TryTakeCompletedResult(out TrajectoryPredictionResult result)
    {
        if (!hasBufferedResult)
        {
            result = default;
            return false;
        }

        result = bufferedResult;
        ClearBufferedResult();
        IsComputing = false;
        return true;
    }

    private void QueueResult(
        NBody body,
        Vector3[] resultArray,
        TrajectoryPredictionRequest request,
        float sampleDeltaTime)
    {
        bufferedResult = new TrajectoryPredictionResult(
            body,
            resultArray,
            request,
            sampleDeltaTime
        );
        hasBufferedResult = true;
    }

    private void ClearBufferedResult()
    {
        bufferedResult = default;
        hasBufferedResult = false;
    }

    private static float ResolveSampleDeltaTime(TrajectoryPredictionRequest request, Vector3[] resultArray)
    {
        int resultCount = resultArray != null ? resultArray.Length : 0;
        if (resultCount <= 0)
            return request.DeltaTime;

        int lodFactor = Mathf.Max(1, Mathf.CeilToInt((float)request.Steps / resultCount));
        return request.DeltaTime * lodFactor;
    }
}
