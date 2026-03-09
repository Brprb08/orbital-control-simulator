using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Reflection;

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

        if (rig.UI.removePreManeuverLineButton == null)
            rig.UI.removePreManeuverLineButton = MakeButton(rig.Root.transform, "RemovePreManeuverBtn");

        tr = new GameObject("TrajectoryRenderer").AddComponent<TrajectoryRenderer>();
        tr.transform.SetParent(rig.Root.transform, false);

        rig.Ctx.TrajectoryRenderer = tr;

        tr.Initialize(rig.Ctx);
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
}