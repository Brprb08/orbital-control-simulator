using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

public class TrajectoryPredictionPlanner_EditModeTests
{
    private SimTestRig rig;

    [TearDown]
    public void TearDown()
    {
        rig?.Dispose();
        rig = null;
    }

    [Test]
    public void TryBuildFinalPassRequest_uses_gpu_backend_for_long_drag_relevant_transfer_orbit()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        NBody body = CreateBodyWithState(
            "TransferSat",
            radiusUnits: 1f,
            position: new double3(677f, 0f, 0f),
            velocity: new double3(0f, 0f, ComputePerigeeSpeed(677f, 4216.4f)),
            dragCoefficient: 2.2f,
            atmosphericDensity0: 1.225e9f
        );

        bool built = TrajectoryPredictionPlanner.TryBuildFinalPassRequest(
            body,
            rig.BodyService,
            runtimeCoordinator: null,
            preferredDeltaTime: 7f,
            out TrajectoryPredictionRequest request
        );

        Assert.IsTrue(built);
        Assert.AreEqual(TrajectoryPredictionBackend.GpuGravity, request.Backend);
    }

    [Test]
    public void TryBuildFinalPassRequest_keeps_matched_backend_for_short_drag_relevant_orbit()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        NBody body = CreateBodyWithState(
            "LowOrbitSat",
            radiusUnits: 1f,
            position: new double3(677f, 0f, 0f),
            velocity: new double3(0f, 0f, ComputeCircularSpeed(677f)),
            dragCoefficient: 2.2f,
            atmosphericDensity0: 1.225e9f
        );

        bool built = TrajectoryPredictionPlanner.TryBuildFinalPassRequest(
            body,
            rig.BodyService,
            runtimeCoordinator: null,
            preferredDeltaTime: 7f,
            out TrajectoryPredictionRequest request
        );

        Assert.IsTrue(built);
        Assert.AreEqual(TrajectoryPredictionBackend.NativeMatched, request.Backend);
    }

    private NBody CreateBodyWithState(
        string name,
        float radiusUnits,
        double3 position,
        double3 velocity,
        float dragCoefficient,
        float atmosphericDensity0)
    {
        rig.Earth.trueMass = 5.972e24;
        rig.Earth.mass = 5.972e24f;
        rig.Earth.radius = 637f;
        rig.Earth.state = new NBody.OrbitalState(
            position: double3.zero,
            velocity: double3.zero,
            centralBodyMass: 0f,
            mass: rig.Earth.trueMass,
            radius: rig.Earth.radius,
            dragCoefficient: 0f,
            force: Vector3.zero
        );

        var go = new GameObject(name);
        go.transform.SetParent(rig.Root.transform, false);
        var body = go.AddComponent<NBody>();
        body.trueMass = 1000d;
        body.mass = 1000f;
        body.radius = radiusUnits;
        body.dragCoefficient = dragCoefficient;
        body.atmosphericDensity0 = atmosphericDensity0;
        body.state = new NBody.OrbitalState(
            position: position,
            velocity: velocity,
            centralBodyMass: (float)rig.Earth.trueMass,
            mass: body.trueMass,
            radius: body.radius,
            dragCoefficient: body.dragCoefficient,
            force: Vector3.zero
        );

        return body;
    }

    private static float ComputeCircularSpeed(float radiusUnits)
    {
        float mu = PhysicsConstants.G * 5.972e24f;
        return Mathf.Sqrt(mu / radiusUnits);
    }

    private static float ComputePerigeeSpeed(float perigeeRadiusUnits, float apogeeRadiusUnits)
    {
        float mu = PhysicsConstants.G * 5.972e24f;
        float semiMajorAxis = (perigeeRadiusUnits + apogeeRadiusUnits) * 0.5f;
        return Mathf.Sqrt(mu * ((2f / perigeeRadiusUnits) - (1f / semiMajorAxis)));
    }
}
