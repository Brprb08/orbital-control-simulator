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
    public static TrajectoryComputeController Instance { get; private set; }


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

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
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

        float bodyMassFloat = bodyMass;
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
        massBuffer.SetData(new float[] { bodyMassFloat });

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

        // Pass constants
        trajectoryComputeShader.SetFloat("deltaTime", dt);
        trajectoryComputeShader.SetInt("steps", steps);
        trajectoryComputeShader.SetFloat("gravitationalConstant", PhysicsConstants.G);
        trajectoryComputeShader.SetInt("numOtherBodies", otherBodyPositions.Length);

        trajectoryComputeShader.SetInt("lodFactor", lodFactor);
        trajectoryComputeShader.SetInt("outputCount", outputCount);

        trajectoryComputeShader.Dispatch(kernelIndex, 8, 8, 1);

        // Use AsyncGPUReadback to avoid blocking the CPU
        AsyncGPUReadback.Request(
            outputTrajectoryBuffer,
            (AsyncGPUReadbackRequest request) =>
            {
                OnAsyncReadbackComplete(request, onComplete);
            }
        );
    }

    /// <summary>
    /// Handles the completion of an asynchronous GPU readback request.
    /// </summary>
    /// <param name="request">The readback request from the GPU.</param>
    /// <param name="onComplete">Callback function to handle the resulting trajectory data.</param>
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

    /// <summary>
    /// Cleans up and releases any GPU buffers that were allocated.
    /// </summary>
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