using UnityEngine;
using UnityEngine.Rendering;
using System;

/**
* This class handles the computation of orbital trajectories using a compute shader. 
* It allows for GPU-accelerated calculations of body trajectories based on initial
* conditions, masses, and other bodies' positions and masses. 
* Results can be asynchronously retrieved using callbacks.
**/
public class TrajectoryComputeController : MonoBehaviour
{
    [Header("Compute Shader/Buffers")]
    public ComputeShader trajectoryComputeShader;

    private ComputeBuffer initialPositionBuffer;
    private ComputeBuffer initialVelocityBuffer;
    private ComputeBuffer massBuffer;
    private ComputeBuffer bodyPositionsBuffer;
    private ComputeBuffer bodyMassesBuffer;
    private ComputeBuffer outputTrajectoryBuffer;

    [Header("LOD")]
    private int lodFactor = 1;
    private int outputCount = 0;

    /**
    * Calculates the trajectory of a body using a GPU compute shader, asynchronously.
    * @param startPos The initial position of the body.
    * @param startVel The initial velocity of the body.
    * @param bodyMass The mass of the body.
    * @param otherBodyPositions Array of positions of other influencing bodies.
    * @param otherBodyMasses Array of masses of other influencing bodies.
    * @param dt The time step for the simulation.
    * @param steps The total number of simulation steps.
    * @param onComplete Callback function invoked when the trajectory calculation is complete. 
    *                   Provides the trajectory as an array of Vector3.
    **/
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
        int maxPoints = 2500;
        lodFactor = Mathf.Max(1, steps / maxPoints);
        outputCount = (int)Mathf.Ceil((float)steps / lodFactor);

        // Create GPU buffers
        initialPositionBuffer = new ComputeBuffer(1, sizeof(float) * 3);
        initialVelocityBuffer = new ComputeBuffer(1, sizeof(float) * 3);
        massBuffer = new ComputeBuffer(1, sizeof(float));

        bodyPositionsBuffer = new ComputeBuffer(otherBodyPositions.Length, sizeof(float) * 3);
        bodyMassesBuffer = new ComputeBuffer(otherBodyMasses.Length, sizeof(float));

        // Final output buffer is only outputCount in size, not steps
        outputTrajectoryBuffer = new ComputeBuffer(outputCount, sizeof(float) * 3);

        // Set data on the buffers
        initialPositionBuffer.SetData(new Vector3[] { startPos });
        initialVelocityBuffer.SetData(new Vector3[] { startVel });
        massBuffer.SetData(new float[] { bodyMass });

        bodyPositionsBuffer.SetData(otherBodyPositions);
        bodyMassesBuffer.SetData(otherBodyMasses);

        // Find kernel & bind buffers
        int kernelIndex = trajectoryComputeShader.FindKernel("RungeKutta");
        trajectoryComputeShader.SetBuffer(kernelIndex, "initialPosition", initialPositionBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "initialVelocity", initialVelocityBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "mass", massBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "bodyPositions", bodyPositionsBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "bodyMasses", bodyMassesBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "outTrajectory", outputTrajectoryBuffer);

        // Pass uniforms
        trajectoryComputeShader.SetFloat("deltaTime", dt);
        trajectoryComputeShader.SetInt("steps", steps);
        trajectoryComputeShader.SetFloat("gravitationalConstant", PhysicsConstants.G);
        trajectoryComputeShader.SetInt("numOtherBodies", otherBodyPositions.Length);

        // New: pass in LOD and output size
        trajectoryComputeShader.SetInt("lodFactor", lodFactor);
        trajectoryComputeShader.SetInt("outputCount", outputCount);

        // Dispatch. numthreads(8,8,1) in the shader, you can do (1,1,1) here too.
        trajectoryComputeShader.Dispatch(kernelIndex, 1, 1, 1);

        // Use AsyncGPUReadback to avoid blocking the CPU
        AsyncGPUReadback.Request(
            outputTrajectoryBuffer,
            (AsyncGPUReadbackRequest request) =>
            {
                OnAsyncReadbackComplete(request, onComplete);
            }
        );
    }

    /**
    * Handles the completion of an asynchronous GPU readback request.
    *
    * @param request The readback request from the GPU.
    * @param onComplete Callback function to handle the resulting trajectory data.
    **/
    private void OnAsyncReadbackComplete(AsyncGPUReadbackRequest request, Action<Vector3[]> onComplete)
    {
        if (request.hasError)
        {
            Debug.LogError("AsyncGPUReadbackRequest error when reading trajectory buffer!");
            onComplete?.Invoke(null);
        }
        else
        {
            Vector3[] result = request.GetData<Vector3>().ToArray();

            Cleanup();

            onComplete?.Invoke(result);
        }
    }

    /**
    * Cleans up and releases any GPU buffers that were allocated.
    **/
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