using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Reflection;
using Unity.Mathematics;

/// <summary>
/// Edit-mode tests for TrajectoryRenderer:
/// verifies initialization, tracked-body changes, line setup,
/// clear/reset flows, and fresh-prediction checks.
/// </summary>
public class TrajectoryRenderer_EditModeTests
{
    private SimTestRig rig;
    private TrajectoryRenderer tr;

    [TearDown]
    public void TearDown()
    {
        rig?.Dispose();
        rig = null;
    }

    private static void SetPrivateField(object obj, string fieldName, object value)
    {
        var f = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(f, $"Field '{fieldName}' not found.");
        f.SetValue(obj, value);
    }

    private static T GetPrivateField<T>(object obj, string fieldName)
    {
        var f = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(f, $"Field '{fieldName}' not found.");
        return (T)f.GetValue(obj);
    }

    private static object InvokePrivateMethod(object obj, string methodName, params object[] args)
    {
        var m = obj.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(m, $"Method '{methodName}' not found.");
        return m.Invoke(obj, args);
    }

    private static Button MakeButton(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go.AddComponent<Button>();
    }

    public static SimTestRig CreateWithUIAndTrajectory(int satelliteCount = 2, bool withTMP = true)
    {
        var rig = SimTestBootstrap.CreateWithUI(satelliteCount, withTMP);

        var bodyRuntimeCoordinator = new GameObject("BodyRuntimeCoordinator").AddComponent<BodyRuntimeCoordinator>();
        bodyRuntimeCoordinator.transform.SetParent(rig.Root.transform, false);

        var thrustController = new GameObject("ThrustController").AddComponent<ThrustController>();
        thrustController.transform.SetParent(rig.Root.transform, false);

        rig.Ctx.BodyRuntimeCoordinator = bodyRuntimeCoordinator;
        rig.Ctx.ThrustController = thrustController;

        return rig;
    }

    private void BuildRenderer()
    {
        rig = SimTestBootstrap.CreateWithUI(2);

        var bodyRuntimeCoordinator = new GameObject("BodyRuntimeCoordinator").AddComponent<BodyRuntimeCoordinator>();
        bodyRuntimeCoordinator.transform.SetParent(rig.Root.transform, false);

        var thrustController = new GameObject("ThrustController").AddComponent<ThrustController>();
        thrustController.transform.SetParent(rig.Root.transform, false);

        rig.Ctx.BodyRuntimeCoordinator = bodyRuntimeCoordinator;
        rig.Ctx.ThrustController = thrustController;

        tr = new GameObject("TrajectoryRenderer").AddComponent<TrajectoryRenderer>();
        tr.transform.SetParent(rig.Root.transform, false);

        rig.Ctx.TrajectoryRenderer = tr;

        tr.Initialize(rig.Ctx);
    }

    private NBody CreateBodyWithState(
        string name,
        double3 position,
        double3 velocity,
        float dragCoefficient = 2.2f,
        float atmosphericDensity0 = 1.225e9f)
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
        body.radius = 1f;
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
        body.transform.position = position.ToVector3();
        body.velocity = velocity.ToVector3();
        return body;
    }

    private static float ComputePerigeeSpeed(float perigeeRadiusUnits, float apogeeRadiusUnits)
    {
        float mu = PhysicsConstants.G * 5.972e24f;
        float semiMajorAxis = (perigeeRadiusUnits + apogeeRadiusUnits) * 0.5f;
        return Mathf.Sqrt(mu * ((2f / perigeeRadiusUnits) - (1f / semiMajorAxis)));
    }

    private static float ComputeApoapsisSpeed(float perigeeRadiusUnits, float apogeeRadiusUnits)
    {
        float mu = PhysicsConstants.G * 5.972e24f;
        float semiMajorAxis = (perigeeRadiusUnits + apogeeRadiusUnits) * 0.5f;
        return Mathf.Sqrt(mu * ((2f / apogeeRadiusUnits) - (1f / semiMajorAxis)));
    }

    [Test]
    public void Initialize_creates_all_line_renderers()
    {
        BuildRenderer();

        Assert.NotNull(tr.predictionLine);
        Assert.NotNull(tr.originLine);
        Assert.NotNull(tr.apogeeLine);
        Assert.NotNull(tr.perigeeLine);
        Assert.NotNull(tr.preManeuverLine);
        Assert.NotNull(tr.previewLine);
        Assert.NotNull(tr.burnLine);
    }

    [Test]
    public void Initialize_caches_core_references()
    {
        BuildRenderer();

        Assert.AreEqual(rig.Ctx.BodyService, tr.bodyService);
        Assert.AreEqual(rig.Ctx.CameraMovement, tr.cameraMovement);
        Assert.AreEqual(rig.Ctx.CameraController, GetPrivateField<CameraController>(tr, "cameraController"));
    }

    [Test]
    public void SetTrackedBody_sets_body_and_marks_orbit_dirty()
    {
        BuildRenderer();

        var sat = rig.Satellites[0];
        tr.orbitIsDirty = false;

        tr.SetTrackedBody(sat);

        Assert.AreEqual(sat, tr.trackedBody);
        Assert.IsTrue(tr.orbitIsDirty);
    }

    [Test]
    public void SetTrackedBody_null_clears_tracked_body_and_stops_prediction_state()
    {
        BuildRenderer();

        tr.SetTrackedBody(rig.Satellites[0]);
        tr.SetTrackedBody(null);

        Assert.IsNull(tr.trackedBody);
        Assert.IsFalse(tr.orbitIsDirty);
    }

    [Test]
    public void SetTrackedBody_same_body_keeps_body_and_marks_orbit_dirty()
    {
        BuildRenderer();

        var sat = rig.Satellites[0];
        tr.SetTrackedBody(sat);
        tr.orbitIsDirty = false;

        tr.SetTrackedBody(sat);

        Assert.AreEqual(sat, tr.trackedBody);
        Assert.IsTrue(tr.orbitIsDirty);
    }

    [Test]
    public void SetTrackedBody_raises_TrackedBodyChanged_event()
    {
        BuildRenderer();

        tr.SetTrackedBody(null);

        NBody oldBody = null;
        NBody newBody = null;

        tr.TrackedBodyChanged += (oldB, newB) =>
        {
            oldBody = oldB;
            newBody = newB;
        };

        var sat1 = rig.Satellites[0];
        tr.SetTrackedBody(sat1);

        Assert.IsNull(oldBody);
        Assert.AreEqual(sat1, newBody);
    }

    [Test]
    public void ClearAllLines_clears_prediction_state()
    {
        BuildRenderer();

        tr.latestPrediction = new List<Vector3>
        {
            Vector3.zero,
            Vector3.one
        };
        tr.latestPredictionBody = rig.Satellites[0];
        tr.latestPredictionStartTime = 123f;
        tr.latestPredictionDeltaTime = 7f;

        tr.ClearAllLines();

        Assert.NotNull(tr.latestPrediction);
        Assert.AreEqual(0, tr.latestPrediction.Count);
        Assert.IsNull(tr.latestPredictionBody);
        Assert.AreEqual(0f, tr.latestPredictionStartTime);
        Assert.AreEqual(0f, tr.latestPredictionDeltaTime);
    }

    [Test]
    public void ClearPreManeuverOrbit_clears_reference_orbit_body()
    {
        BuildRenderer();

        var refObj = new GameObject("ReferenceOrbit");
        tr.referenceOrbitBody = refObj.AddComponent<NBody>();

        tr.ClearPreManeuverOrbit();

        Assert.IsNull(tr.referenceOrbitBody);
    }

    [Test]
    public void RequestFullOrbitPass_does_not_throw()
    {
        BuildRenderer();

        Assert.DoesNotThrow(() => tr.RequestFullOrbitPass());
    }

    [Test]
    public void SetLineVisibility_does_not_throw_when_lines_exist()
    {
        BuildRenderer();

        Assert.DoesNotThrow(() => tr.SetLineVisibility(true, true, true));
        Assert.DoesNotThrow(() => tr.SetLineVisibility(false, false, false));
    }

    [Test]
    public void HasFreshPredictionFor_returns_false_when_body_is_null()
    {
        BuildRenderer();

        Assert.IsFalse(tr.HasFreshPredictionFor(null));
    }

    [Test]
    public void HasFreshPredictionFor_returns_false_when_orbit_is_dirty()
    {
        BuildRenderer();

        var sat = rig.Satellites[0];
        tr.latestPredictionBody = sat;
        tr.latestPrediction = new List<Vector3> { sat.transform.position, sat.transform.position + Vector3.forward };
        tr.orbitIsDirty = true;

        Assert.IsFalse(tr.HasFreshPredictionFor(sat));
    }

    [Test]
    public void HasFreshPredictionFor_returns_false_when_prediction_body_does_not_match()
    {
        BuildRenderer();

        tr.orbitIsDirty = false;
        tr.latestPredictionBody = rig.Satellites[0];
        tr.latestPrediction = new List<Vector3>
        {
            rig.Satellites[0].transform.position,
            rig.Satellites[0].transform.position + Vector3.forward
        };

        Assert.IsFalse(tr.HasFreshPredictionFor(rig.Satellites[1]));
    }

    [Test]
    public void HasFreshPredictionFor_returns_false_when_prediction_has_too_few_points()
    {
        BuildRenderer();

        var sat = rig.Satellites[0];
        tr.orbitIsDirty = false;
        tr.latestPredictionBody = sat;
        tr.latestPrediction = new List<Vector3> { sat.transform.position };

        Assert.IsFalse(tr.HasFreshPredictionFor(sat));
    }

    [Test]
    public void HasFreshPredictionFor_returns_true_when_prediction_matches_body_and_is_clean()
    {
        BuildRenderer();

        var sat = rig.Satellites[0];
        tr.orbitIsDirty = false;
        tr.latestPredictionBody = sat;
        tr.latestPrediction = new List<Vector3>
        {
            sat.transform.position,
            sat.transform.position + Vector3.forward * 10f
        };

        SetPrivateField(tr, "isComputingPrediction", false);

        Assert.IsTrue(tr.HasFreshPredictionFor(sat));
    }

    [Test]
    public void HasFreshPredictionFor_returns_false_when_first_point_is_too_far_from_body()
    {
        BuildRenderer();

        var sat = rig.Satellites[0];
        tr.orbitIsDirty = false;
        tr.latestPredictionBody = sat;
        tr.latestPrediction = new List<Vector3>
        {
            sat.transform.position + Vector3.right * 100f,
            sat.transform.position + Vector3.forward * 10f
        };

        SetPrivateField(tr, "isComputingPrediction", false);

        Assert.IsFalse(tr.HasFreshPredictionFor(sat));
    }

    [Test]
    public void ShouldContinuouslyRefreshPrediction_returns_false_for_gpu_requests()
    {
        BuildRenderer();

        var sat = rig.Satellites[0];
        tr.SetTrackedBody(sat);

        SetPrivateField(tr, "lastPredictionRequest",
            new TrajectoryPredictionRequest(
                steps: 1024,
                deltaTime: 5f,
                epoch: 10f,
                refreshInterval: 1f,
                requiresContinuousRefresh: true,
                backend: TrajectoryPredictionBackend.GpuGravity,
                maxOutputPoints: 256));

        bool shouldRefresh = (bool)InvokePrivateMethod(tr, "ShouldContinuouslyRefreshPrediction", sat);

        Assert.IsFalse(shouldRefresh);
    }

    [Test]
    public void ShouldContinuouslyRefreshPrediction_returns_false_for_drag_relevant_orbit_outside_drag_passage()
    {
        BuildRenderer();

        var sat = rig.Satellites[0];
        tr.SetTrackedBody(sat);

        SetPrivateField(tr, "dragRefreshOrbitActive", true);
        SetPrivateField(tr, "longDragPassageRefreshActive", false);
        SetPrivateField(tr, "hasPredictionSourceState", true);
        SetPrivateField(tr, "nextContinuousPredictionTime", 0f);
        SetPrivateField(tr, "lastPredictionRequest",
            new TrajectoryPredictionRequest(
                steps: 1024,
                deltaTime: 5f,
                epoch: 10f,
                refreshInterval: 1f,
                requiresContinuousRefresh: true,
                backend: TrajectoryPredictionBackend.NativeMatched,
                maxOutputPoints: 256));
        tr.latestPredictionBody = sat;
        tr.latestPrediction = new List<Vector3> { sat.transform.position, sat.transform.position + Vector3.forward };

        bool shouldRefresh = (bool)InvokePrivateMethod(tr, "ShouldContinuouslyRefreshPrediction", sat);

        Assert.IsFalse(shouldRefresh);
    }

    [Test]
    public void ClearAllLines_resets_continuous_refresh_state()
    {
        BuildRenderer();

        SetPrivateField(tr, "nextContinuousPredictionTime", 2f);
        SetPrivateField(tr, "nextContinuousHighQualityTime", 3f);
        SetPrivateField(tr, "lastPredictionEpoch", 4f);
        SetPrivateField(tr, "hasPredictionSourceState", true);
        SetPrivateField(tr, "lastPredictionRequest",
            new TrajectoryPredictionRequest(
                steps: 512,
                deltaTime: 2f,
                epoch: 6f,
                refreshInterval: 1f,
                requiresContinuousRefresh: true,
                backend: TrajectoryPredictionBackend.NativeMatched,
                maxOutputPoints: 128));

        tr.ClearAllLines();

        Assert.AreEqual(0f, GetPrivateField<float>(tr, "nextContinuousPredictionTime"));
        Assert.AreEqual(0f, GetPrivateField<float>(tr, "nextContinuousHighQualityTime"));
        Assert.AreEqual(0f, GetPrivateField<float>(tr, "lastPredictionEpoch"));
        Assert.IsFalse(GetPrivateField<bool>(tr, "hasPredictionSourceState"));
        Assert.AreEqual(default(TrajectoryPredictionRequest), GetPrivateField<TrajectoryPredictionRequest>(tr, "lastPredictionRequest"));
    }

    [Test]
    public void UpdateTrackedPredictionOwnership_losing_camera_ownership_invalidates_prediction_state()
    {
        BuildRenderer();

        var sat = rig.Satellites[0];
        tr.SetTrackedBody(sat);

        SetPrivateField(tr, "trackedPredictionOwnershipActive", true);
        SetPrivateField(tr, "isComputingPrediction", true);
        SetPrivateField(tr, "hasBufferedPredictionResult", true);
        SetPrivateField(tr, "bufferedPredictionBody", sat);
        SetPrivateField(tr, "hasPredictionSourceState", true);

        InvokePrivateMethod(tr, "UpdateTrackedPredictionOwnership", false);

        Assert.IsFalse(GetPrivateField<bool>(tr, "trackedPredictionOwnershipActive"));
        Assert.IsFalse(GetPrivateField<bool>(tr, "isComputingPrediction"));
        Assert.IsFalse(GetPrivateField<bool>(tr, "hasBufferedPredictionResult"));
        Assert.IsFalse(GetPrivateField<bool>(tr, "hasPredictionSourceState"));
    }

    [Test]
    public void UpdateTrackedPredictionOwnership_regaining_camera_ownership_requests_fresh_prediction()
    {
        BuildRenderer();

        var sat = rig.Satellites[0];
        tr.SetTrackedBody(sat);
        tr.orbitIsDirty = false;
        SetPrivateField(tr, "trackedPredictionOwnershipActive", false);
        SetPrivateField(tr, "fullPassRequested", false);
        SetPrivateField(tr, "forceFastSwitchPreview", false);

        InvokePrivateMethod(tr, "UpdateTrackedPredictionOwnership", true);

        Assert.IsTrue(GetPrivateField<bool>(tr, "trackedPredictionOwnershipActive"));
        Assert.IsTrue(tr.orbitIsDirty);
        Assert.IsTrue(GetPrivateField<bool>(tr, "fullPassRequested"));
        Assert.IsTrue(GetPrivateField<bool>(tr, "forceFastSwitchPreview"));
    }

    [Test]
    public void UpdateLongDragTransferRefreshState_entering_drag_passage_marks_orbit_dirty()
    {
        BuildRenderer();

        const float perigeeRadiusUnits = 651f;
        const float apogeeRadiusUnits = 2636.7f;
        var transferSat = CreateBodyWithState(
            "TransferSatPerigee",
            position: new double3(perigeeRadiusUnits, 0f, 0f),
            velocity: new double3(0f, 0f, ComputePerigeeSpeed(perigeeRadiusUnits, apogeeRadiusUnits)));

        tr.SetTrackedBody(transferSat);
        tr.orbitIsDirty = false;
        SetPrivateField(tr, "fullPassRequested", true);
        SetPrivateField(tr, "forceFastSwitchPreview", true);

        InvokePrivateMethod(tr, "UpdateLongDragTransferRefreshState");

        Assert.IsTrue(GetPrivateField<bool>(tr, "dragRefreshOrbitActive"));
        Assert.IsTrue(GetPrivateField<bool>(tr, "longDragPassageRefreshActive"));
        Assert.IsTrue(tr.orbitIsDirty);
        Assert.IsFalse(GetPrivateField<bool>(tr, "fullPassRequested"));
        Assert.IsFalse(GetPrivateField<bool>(tr, "forceFastSwitchPreview"));
    }

    [Test]
    public void UpdateLongDragTransferRefreshState_exiting_drag_passage_requests_locked_recompute()
    {
        BuildRenderer();

        const float perigeeRadiusUnits = 651f;
        const float apogeeRadiusUnits = 2636.7f;
        var transferSat = CreateBodyWithState(
            "TransferSatApogee",
            position: new double3(apogeeRadiusUnits, 0f, 0f),
            velocity: new double3(0f, 0f, ComputeApoapsisSpeed(perigeeRadiusUnits, apogeeRadiusUnits)));

        tr.SetTrackedBody(transferSat);
        tr.orbitIsDirty = false;
        SetPrivateField(tr, "longDragPassageRefreshActive", true);
        SetPrivateField(tr, "hasPredictionSourceState", true);
        SetPrivateField(tr, "fullPassRequested", false);

        InvokePrivateMethod(tr, "UpdateLongDragTransferRefreshState");

        Assert.IsTrue(GetPrivateField<bool>(tr, "dragRefreshOrbitActive"));
        Assert.IsFalse(GetPrivateField<bool>(tr, "longDragPassageRefreshActive"));
        Assert.IsTrue(tr.orbitIsDirty);
        Assert.IsTrue(GetPrivateField<bool>(tr, "fullPassRequested"));
        Assert.IsFalse(GetPrivateField<bool>(tr, "hasPredictionSourceState"));
    }
}
