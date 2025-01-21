using UnityEngine;
using UnityEngine.Rendering;
using System;

public class TrajectoryComputeController : MonoBehaviour
{
    public ComputeShader trajectoryComputeShader;

    private ComputeBuffer initialPositionBuffer;
    private ComputeBuffer initialVelocityBuffer;
    private ComputeBuffer massBuffer;
    private ComputeBuffer bodyPositionsBuffer;
    private ComputeBuffer bodyMassesBuffer;
    private ComputeBuffer outputTrajectoryBuffer;

    // LOD factor example:
    // e.g. if steps=30000, LOD=12 => outputCount=2500
    private int lodFactor = 1;
    private int outputCount = 0;

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
        // 0) Decide your LOD factor. For instance:
        //    - we want at most ~2500 points
        //    - so LOD factor = steps / 2500
        int maxPoints = 2500;  // or your own threshold
        lodFactor = Mathf.Max(1, steps / maxPoints);

        // The actual number of positions we'll store
        outputCount = (int)Mathf.Ceil((float)steps / lodFactor);

        // 1) Create GPU buffers
        initialPositionBuffer = new ComputeBuffer(1, sizeof(float) * 3);
        initialVelocityBuffer = new ComputeBuffer(1, sizeof(float) * 3);
        massBuffer = new ComputeBuffer(1, sizeof(float));

        bodyPositionsBuffer = new ComputeBuffer(otherBodyPositions.Length, sizeof(float) * 3);
        bodyMassesBuffer = new ComputeBuffer(otherBodyMasses.Length, sizeof(float));

        // Our final output buffer is only outputCount in size, not steps
        outputTrajectoryBuffer = new ComputeBuffer(outputCount, sizeof(float) * 3);

        // 2) Set data on the buffers
        initialPositionBuffer.SetData(new Vector3[] { startPos });
        initialVelocityBuffer.SetData(new Vector3[] { startVel });
        massBuffer.SetData(new float[] { bodyMass });

        bodyPositionsBuffer.SetData(otherBodyPositions);
        bodyMassesBuffer.SetData(otherBodyMasses);

        // 3) Find kernel & bind buffers
        int kernelIndex = trajectoryComputeShader.FindKernel("RungeKutta");
        trajectoryComputeShader.SetBuffer(kernelIndex, "initialPosition", initialPositionBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "initialVelocity", initialVelocityBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "mass", massBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "bodyPositions", bodyPositionsBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "bodyMasses", bodyMassesBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "outTrajectory", outputTrajectoryBuffer);

        // 4) Pass uniforms
        trajectoryComputeShader.SetFloat("deltaTime", dt);
        trajectoryComputeShader.SetInt("steps", steps);
        trajectoryComputeShader.SetFloat("gravitationalConstant", PhysicsConstants.G);
        trajectoryComputeShader.SetInt("numOtherBodies", otherBodyPositions.Length);

        // New: pass in LOD and output size
        trajectoryComputeShader.SetInt("lodFactor", lodFactor);
        trajectoryComputeShader.SetInt("outputCount", outputCount);

        // 5) Dispatch. If you only want 1 thread, do (1,1,1). 
        //    If you have numthreads(8,8,1) in the shader, you can do (1,1,1) here too.
        trajectoryComputeShader.Dispatch(kernelIndex, 1, 1, 1);

        // 6) Use AsyncGPUReadback to avoid blocking the CPU
        AsyncGPUReadback.Request(
            outputTrajectoryBuffer,
            (AsyncGPUReadbackRequest request) =>
            {
                OnAsyncReadbackComplete(request, onComplete);
            }
        );
    }

    private void OnAsyncReadbackComplete(AsyncGPUReadbackRequest request, Action<Vector3[]> onComplete)
    {
        if (request.hasError)
        {
            Debug.LogError("AsyncGPUReadbackRequest error when reading trajectory buffer!");
            onComplete?.Invoke(null);
        }
        else
        {
            // Convert the data into an array
            Vector3[] result = request.GetData<Vector3>().ToArray();

            // Cleanup buffers
            Cleanup();

            // Invoke user callback
            onComplete?.Invoke(result);
        }
    }

    private void Cleanup()
    {
        if (initialPositionBuffer != null) initialPositionBuffer.Release();
        if (initialVelocityBuffer != null) initialVelocityBuffer.Release();
        if (massBuffer != null) massBuffer.Release();
        if (bodyPositionsBuffer != null) bodyPositionsBuffer.Release();
        if (bodyMassesBuffer != null) bodyMassesBuffer.Release();
        if (outputTrajectoryBuffer != null) outputTrajectoryBuffer.Release();
    }
}