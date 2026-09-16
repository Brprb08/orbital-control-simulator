using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;

/// <summary>
/// Handles the computation of orbital trajectories using a compute shader. 
/// Enables GPU-accelerated calculations of body trajectories based on initial
/// conditions, masses, and other bodies' positions and masses. 
/// Results can be asynchronously retrieved using callbacks.
/// </summary>
public class TrajectoryComputeController : MonoBehaviour
{
    [Header("Compute Shader")]
    public ComputeShader trajectoryComputeShader;

    [Header("LOD")]
    private int lodFactor = 1;
    private int outputCount = 0;

    private SimContext ctx;
    private int rungeKuttaKernelIndex = -1;

    // The GPU driver will happily queue every drag/slider update. Keeping only the
    // newest request for each consumer prevents stale previews from accumulating
    // VRAM allocations and readbacks behind the useful result.
    private readonly Dictionary<string, QueuedTrajectoryRequest> pendingRequests = new();
    private readonly Queue<string> pendingRequestOrder = new();
    private bool requestInFlight;
    private bool shuttingDown;

    // Requests are serialized, so these buffers can be retained and grown instead
    // of allocating/releasing six GPU resources for every preview refresh.
    private ComputeBuffer initialPositionBuffer;
    private ComputeBuffer initialVelocityBuffer;
    private ComputeBuffer massBuffer;
    private ComputeBuffer bodyPositionsBuffer;
    private ComputeBuffer bodyMassesBuffer;
    private ComputeBuffer outputTrajectoryBuffer;

    private static readonly Vector3[] EmptyBodyPositionData = { Vector3.zero };
    private static readonly float[] EmptyBodyMassData = { 0f };

    private sealed class TrajectoryRequestContext
    {
        public ComputeBuffer initialPositionBuffer;
        public ComputeBuffer initialVelocityBuffer;
        public ComputeBuffer massBuffer;
        public ComputeBuffer bodyPositionsBuffer;
        public ComputeBuffer bodyMassesBuffer;
        public ComputeBuffer outputTrajectoryBuffer;
        public Action<Vector3[]> onComplete;
        public int outputCount;
        private bool cleanedUp;

        public void Cleanup()
        {
            if (cleanedUp)
                return;

            cleanedUp = true;

            initialPositionBuffer = null;
            initialVelocityBuffer = null;
            massBuffer = null;
            bodyPositionsBuffer = null;
            bodyMassesBuffer = null;
            outputTrajectoryBuffer = null;
            onComplete = null;
        }
    }

    private sealed class QueuedTrajectoryRequest
    {
        public string Key;
        public Vector3 StartPos;
        public Vector3 StartVel;
        public float BodyMass;
        public Vector3[] OtherBodyPositions;
        public float[] OtherBodyMasses;
        public float DeltaTime;
        public int Steps;
        public Action<Vector3[]> OnComplete;
    }

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
    }

    /// <summary>
    /// Calculates the trajectory of a body using a GPU compute shader, asynchronously.
    /// </summary>
    /// <param name="startPos">The initial position of the body.</param>
    /// <param name="startVel">The initial velocity of the body.</param>
    /// <param name="bodyMass">The mass of the body.</param>
    /// <param name="otherBodyPositions">Array of positions of other influencing bodies.</param>
    /// <param name="otherBodyMasses">Array of masses of other influencing bodies.</param>
    /// <param name="dt">The time step for the simulation.</param>
    /// <param name="steps">The total number of simulation steps.</param>
    /// <param name="onComplete">
    /// Callback function invoked when the trajectory calculation is complete. 
    /// Provides the trajectory as an array of Vector3.
    /// </param>
    public void CalculateTrajectoryGPU_Async(
        Vector3 startPos,
        Vector3 startVel,
        float bodyMass,
        Vector3[] otherBodyPositions,
        float[] otherBodyMasses,
        float dt,
        int steps,
        Action<Vector3[]> onComplete,  // callback once data is ready
        string coalesceKey = null
    )
    {
        if (shuttingDown)
        {
            onComplete?.Invoke(null);
            return;
        }

        if (steps <= 0 || !float.IsFinite(dt) || dt <= 0f)
        {
            onComplete?.Invoke(null);
            return;
        }

        if (trajectoryComputeShader == null)
        {
            Debug.LogError("[TrajectoryComputeController] Missing compute shader.");
            onComplete?.Invoke(null);
            return;
        }

        if (otherBodyPositions == null || otherBodyMasses == null || otherBodyPositions.Length != otherBodyMasses.Length)
        {
            Debug.LogError("[TrajectoryComputeController] Invalid other-body input buffers.");
            onComplete?.Invoke(null);
            return;
        }

        var queuedRequest = new QueuedTrajectoryRequest
        {
            Key = string.IsNullOrEmpty(coalesceKey) ? Guid.NewGuid().ToString("N") : coalesceKey,
            StartPos = startPos,
            StartVel = startVel,
            BodyMass = bodyMass,
            OtherBodyPositions = otherBodyPositions,
            OtherBodyMasses = otherBodyMasses,
            DeltaTime = dt,
            Steps = steps,
            OnComplete = onComplete
        };

        if (requestInFlight)
        {
            bool alreadyQueued = pendingRequests.ContainsKey(queuedRequest.Key);
            pendingRequests[queuedRequest.Key] = queuedRequest;
            if (!alreadyQueued)
                pendingRequestOrder.Enqueue(queuedRequest.Key);
            return;
        }

        StartRequest(queuedRequest);
    }

    private void OnDestroy()
    {
        shuttingDown = true;
        pendingRequests.Clear();
        pendingRequestOrder.Clear();
        // The readback owns the buffers until its callback finishes.
        if (!requestInFlight)
            ReleaseBuffers();
    }

    private void ReleaseBuffers()
    {
        ReleaseBuffer(ref initialPositionBuffer);
        ReleaseBuffer(ref initialVelocityBuffer);
        ReleaseBuffer(ref massBuffer);
        ReleaseBuffer(ref bodyPositionsBuffer);
        ReleaseBuffer(ref bodyMassesBuffer);
        ReleaseBuffer(ref outputTrajectoryBuffer);
    }

    private void StartRequest(QueuedTrajectoryRequest queuedRequest)
    {
        if (shuttingDown)
            return;

        requestInFlight = true;
        try
        {
            DispatchRequest(queuedRequest);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            try
            {
                if (!shuttingDown)
                    queuedRequest.OnComplete?.Invoke(null);
            }
            finally
            {
                FinishRequest();
            }
        }
    }

    private void DispatchRequest(QueuedTrajectoryRequest queuedRequest)
    {
        Vector3 startPos = queuedRequest.StartPos;
        Vector3 startVel = queuedRequest.StartVel;
        float bodyMass = queuedRequest.BodyMass;
        Vector3[] otherBodyPositions = queuedRequest.OtherBodyPositions;
        float[] otherBodyMasses = queuedRequest.OtherBodyMasses;
        float dt = queuedRequest.DeltaTime;
        int steps = queuedRequest.Steps;
        Action<Vector3[]> onComplete = queuedRequest.OnComplete;

        float bodyMassFloat = bodyMass;
        const int maxPoints = 2500;
        lodFactor = Mathf.Max(1, steps / maxPoints);
        outputCount = (int)Mathf.Ceil((float)steps / lodFactor);

        var requestContext = new TrajectoryRequestContext
        {
            initialPositionBuffer = AcquireBuffer(ref initialPositionBuffer, 1, sizeof(float) * 3),
            initialVelocityBuffer = AcquireBuffer(ref initialVelocityBuffer, 1, sizeof(float) * 3),
            massBuffer = AcquireBuffer(ref massBuffer, 1, sizeof(float)),
            bodyPositionsBuffer = AcquireBuffer(ref bodyPositionsBuffer, Mathf.Max(1, otherBodyPositions.Length), sizeof(float) * 3),
            bodyMassesBuffer = AcquireBuffer(ref bodyMassesBuffer, Mathf.Max(1, otherBodyMasses.Length), sizeof(float)),
            outputTrajectoryBuffer = AcquireBuffer(ref outputTrajectoryBuffer, outputCount, sizeof(float) * 3),
            onComplete = onComplete,
            outputCount = outputCount
        };

        requestContext.initialPositionBuffer.SetData(new[] { startPos });
        requestContext.initialVelocityBuffer.SetData(new[] { startVel });
        requestContext.massBuffer.SetData(new[] { bodyMassFloat });

        requestContext.bodyPositionsBuffer.SetData(
            otherBodyPositions.Length > 0 ? otherBodyPositions : EmptyBodyPositionData
        );
        requestContext.bodyMassesBuffer.SetData(
            otherBodyMasses.Length > 0 ? otherBodyMasses : EmptyBodyMassData
        );

        int kernelIndex = GetRungeKuttaKernelIndex();
        trajectoryComputeShader.SetBuffer(kernelIndex, "initialPosition", requestContext.initialPositionBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "initialVelocity", requestContext.initialVelocityBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "mass", requestContext.massBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "bodyPositions", requestContext.bodyPositionsBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "bodyMasses", requestContext.bodyMassesBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "outTrajectory", requestContext.outputTrajectoryBuffer);

        // Pass constants
        trajectoryComputeShader.SetFloat("deltaTime", dt);
        trajectoryComputeShader.SetInt("steps", steps);
        trajectoryComputeShader.SetFloat("gravitationalConstant", PhysicsConstants.G);
        trajectoryComputeShader.SetInt("numOtherBodies", otherBodyPositions.Length);

        trajectoryComputeShader.SetInt("lodFactor", lodFactor);
        trajectoryComputeShader.SetInt("outputCount", outputCount);

        trajectoryComputeShader.Dispatch(kernelIndex, 1, 1, 1);

        // Use AsyncGPUReadback to avoid blocking the CPU
        AsyncGPUReadback.Request(
            requestContext.outputTrajectoryBuffer,
            (AsyncGPUReadbackRequest request) =>
            {
                OnAsyncReadbackComplete(request, requestContext);
            }
        );
    }

    private int GetRungeKuttaKernelIndex()
    {
        if (rungeKuttaKernelIndex < 0)
            rungeKuttaKernelIndex = trajectoryComputeShader.FindKernel("RungeKutta");

        return rungeKuttaKernelIndex;
    }

    /// <summary>
    /// Handles the completion of an asynchronous GPU readback request.
    /// </summary>
    private void OnAsyncReadbackComplete(AsyncGPUReadbackRequest request, TrajectoryRequestContext requestContext)
    {
        try
        {
            if (shuttingDown)
                return;

            if (request.hasError)
            {
                Debug.LogError("AsyncGPUReadbackRequest error when reading trajectory buffer!");
                requestContext.onComplete?.Invoke(null);
                return;
            }

            var data = request.GetData<Vector3>();
            Vector3[] result = new Vector3[requestContext.outputCount];
            for (int i = 0; i < result.Length; i++)
                result[i] = data[i];
            requestContext.onComplete?.Invoke(result);
        }
        finally
        {
            requestContext?.Cleanup();
            FinishRequest();
        }
    }

    private void FinishRequest()
    {
        requestInFlight = false;
        if (shuttingDown)
            ReleaseBuffers();
        else
            StartNextQueuedRequest();
    }

    private void StartNextQueuedRequest()
    {
        if (shuttingDown)
            return;

        while (pendingRequestOrder.Count > 0)
        {
            string key = pendingRequestOrder.Dequeue();
            if (!pendingRequests.TryGetValue(key, out QueuedTrajectoryRequest queuedRequest))
                continue;

            pendingRequests.Remove(key);
            StartRequest(queuedRequest);
            return;
        }
    }

    private static ComputeBuffer AcquireBuffer(ref ComputeBuffer buffer, int count, int stride)
    {
        if (buffer == null || buffer.count < count || buffer.stride != stride)
        {
            ReleaseBuffer(ref buffer);
            buffer = new ComputeBuffer(count, stride);
        }

        return buffer;
    }

    private static void ReleaseBuffer(ref ComputeBuffer buffer)
    {
        if (buffer == null)
            return;

        buffer.Release();
        buffer = null;
    }
}
