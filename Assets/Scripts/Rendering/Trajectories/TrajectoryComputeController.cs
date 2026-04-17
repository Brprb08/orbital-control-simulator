using UnityEngine;
using UnityEngine.Rendering;
using System;

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
        private bool cleanedUp;

        public void Cleanup()
        {
            if (cleanedUp)
                return;

            cleanedUp = true;

            initialPositionBuffer?.Release();
            initialVelocityBuffer?.Release();
            massBuffer?.Release();
            bodyPositionsBuffer?.Release();
            bodyMassesBuffer?.Release();
            outputTrajectoryBuffer?.Release();

            initialPositionBuffer = null;
            initialVelocityBuffer = null;
            massBuffer = null;
            bodyPositionsBuffer = null;
            bodyMassesBuffer = null;
            outputTrajectoryBuffer = null;
            onComplete = null;
        }
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
        Action<Vector3[]> onComplete   // callback once data is ready
    )
    {
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

        float bodyMassFloat = bodyMass;
        const int maxPoints = 2500;
        lodFactor = Mathf.Max(1, steps / maxPoints);
        outputCount = (int)Mathf.Ceil((float)steps / lodFactor);

        var requestContext = new TrajectoryRequestContext
        {
            initialPositionBuffer = new ComputeBuffer(1, sizeof(float) * 3),
            initialVelocityBuffer = new ComputeBuffer(1, sizeof(float) * 3),
            massBuffer = new ComputeBuffer(1, sizeof(float)),
            bodyPositionsBuffer = new ComputeBuffer(Mathf.Max(1, otherBodyPositions.Length), sizeof(float) * 3),
            bodyMassesBuffer = new ComputeBuffer(Mathf.Max(1, otherBodyMasses.Length), sizeof(float)),
            outputTrajectoryBuffer = new ComputeBuffer(outputCount, sizeof(float) * 3),
            onComplete = onComplete
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
            if (request.hasError)
            {
                Debug.LogError("AsyncGPUReadbackRequest error when reading trajectory buffer!");
                requestContext.onComplete?.Invoke(null);
                return;
            }

            Vector3[] result = request.GetData<Vector3>().ToArray();
            requestContext.onComplete?.Invoke(result);
        }
        finally
        {
            requestContext?.Cleanup();
        }
    }
}
