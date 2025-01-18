using UnityEngine;

public class TrajectoryComputeController : MonoBehaviour
{
    public ComputeShader trajectoryComputeShader; // Assign in Inspector

    // Buffers for a single dispatch
    private ComputeBuffer initialPositionBuffer;
    private ComputeBuffer initialVelocityBuffer;
    private ComputeBuffer massBuffer;
    private ComputeBuffer bodyPositionsBuffer;
    private ComputeBuffer bodyMassesBuffer;
    private ComputeBuffer outputTrajectoryBuffer;

    /// <summary>
    /// Calculates a trajectory via the GPU-based Runge-Kutta.
    /// Returns an array of positions for the orbit path.
    /// </summary>
    public Vector3[] CalculateTrajectoryGPU(
        Vector3 startPos,
        Vector3 startVel,
        float bodyMass,
        Vector3[] otherBodyPositions,
        float[] otherBodyMasses,
        float dt,
        int steps
    )
    {
        Debug.LogError(startPos);
        // 1) Create arrays for the output
        Vector3[] outputPositions = new Vector3[steps];

        // 2) Create GPU buffers
        initialPositionBuffer = new ComputeBuffer(1, sizeof(float) * 3);
        initialVelocityBuffer = new ComputeBuffer(1, sizeof(float) * 3);
        massBuffer = new ComputeBuffer(1, sizeof(float));

        bodyPositionsBuffer = new ComputeBuffer(otherBodyPositions.Length, sizeof(float) * 3);
        bodyMassesBuffer = new ComputeBuffer(otherBodyMasses.Length, sizeof(float));
        outputTrajectoryBuffer = new ComputeBuffer(steps, sizeof(float) * 3);

        // 3) Set data
        initialPositionBuffer.SetData(new Vector3[] { startPos });
        initialVelocityBuffer.SetData(new Vector3[] { startVel });
        massBuffer.SetData(new float[] { bodyMass });

        bodyPositionsBuffer.SetData(otherBodyPositions);
        bodyMassesBuffer.SetData(otherBodyMasses);

        // 4) Find kernel & bind buffers
        int kernelIndex = trajectoryComputeShader.FindKernel("CSMain");
        trajectoryComputeShader.SetBuffer(kernelIndex, "initialPosition", initialPositionBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "initialVelocity", initialVelocityBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "mass", massBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "bodyPositions", bodyPositionsBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "bodyMasses", bodyMassesBuffer);
        trajectoryComputeShader.SetBuffer(kernelIndex, "outTrajectory", outputTrajectoryBuffer);

        // 5) Pass uniforms
        trajectoryComputeShader.SetFloat("deltaTime", dt);
        trajectoryComputeShader.SetInt("steps", steps);
        trajectoryComputeShader.SetFloat("gravitationalConstant", PhysicsConstants.G);
        trajectoryComputeShader.SetInt("numOtherBodies", otherBodyPositions.Length);

        // 6) Dispatch with (1,1,1)
        trajectoryComputeShader.Dispatch(kernelIndex, 1, 1, 1);

        // 7) Retrieve data
        outputTrajectoryBuffer.GetData(outputPositions);

        Debug.LogError(outputPositions[0]);
        // 8) Release
        initialPositionBuffer.Release();
        initialVelocityBuffer.Release();
        massBuffer.Release();
        bodyPositionsBuffer.Release();
        bodyMassesBuffer.Release();
        outputTrajectoryBuffer.Release();

        return outputPositions;
    }
}