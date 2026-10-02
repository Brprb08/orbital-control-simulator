using NUnit.Framework;
using System.Reflection;
using Unity.Mathematics;
using UnityEngine;

public class TimeController_EditModeTests
{
    private GameObject owner;
    private TimeController controller;
    private SimTestRig rig;
    private float originalScale, originalFixedDelta;

    [SetUp]
    public void SetUp()
    {
        originalScale = Time.timeScale;
        originalFixedDelta = Time.fixedDeltaTime;
        owner = new GameObject("Time control test");
        controller = owner.AddComponent<TimeController>();
    }

    [TearDown]
    public void TearDown()
    {
        rig?.Dispose();
        Time.timeScale = originalScale;
        Time.fixedDeltaTime = originalFixedDelta;
        Object.DestroyImmediate(owner);
    }

    [Test]
    public void Temporary_limit_restores_requested_warp_after_release()
    {
        controller.SetTimeScale(20);
        Assert.IsTrue(controller.BeginTemporaryMaxTimeScale(5));
        Assert.AreEqual(5, Time.timeScale);
        controller.EndTemporaryMaxTimeScale();
        Assert.AreEqual(20, Time.timeScale);
    }

    [Test]
    public void Temporary_limit_during_planning_hold_restores_prior_warp()
    {
        controller.SetTimeScale(20);
        controller.BeginPlanningHold();
        Assert.AreEqual(0, Time.timeScale);
        Assert.IsTrue(controller.BeginTemporaryMaxTimeScale(5));
        controller.EndPlanningHold();
        Assert.AreEqual(5, Time.timeScale);
        controller.EndTemporaryMaxTimeScale();
        Assert.AreEqual(20, Time.timeScale);
    }

    [Test]
    public void Planning_hold_preserves_pause_and_resume_state()
    {
        controller.SetTimeScale(10);
        controller.BeginPlanningHold();
        controller.SetTimeScale(30);
        Assert.AreEqual(0, Time.timeScale);
        controller.EndPlanningHold();
        Assert.AreEqual(30, Time.timeScale);
    }

    [TestCase(0, 250f)]
    [TestCase(10, 250f)]
    [TestCase(11, 150f)]
    [TestCase(25, 150f)]
    [TestCase(26, 100f)]
    [TestCase(50, 100f)]
    [TestCase(51, 75f)]
    [TestCase(100, 75f)]
    [TestCase(150, 75f)]
    [TestCase(200, 75f)]
    [TestCase(201, 75f)]
    [TestCase(299, 75f)]
    [TestCase(300, 50f)]
    [TestCase(301, 50f)]
    public void Satellite_count_sets_warp_ceiling(int count, float expected)
    {
        Assert.AreEqual(expected, TimeController.MaxTimeScaleForSatelliteCount(count));
    }

    [Test]
    public void Membership_changes_clamp_active_warp_and_update_slider_range()
    {
        rig = SimTestBootstrap.CreateWithUI(50);
        controller.Initialize(rig.Ctx);
        var slider = rig.UIRefs.timeSlider;

        Assert.AreEqual(100f, slider.maxValue);
        controller.SetTimeScale(250f);
        Assert.AreEqual(100f, controller.ActiveTimeScale);
        Assert.AreEqual(100f, Time.timeScale);
        Assert.AreEqual(BodyRuntimeCoordinator.BaseSimulationStep * 100f, controller.SimulationStepSeconds, 0.0001f);
        Assert.AreEqual(BodyRuntimeCoordinator.BaseSimulationStep * 100f, Time.fixedDeltaTime, 0.0001f);

        var extra = new GameObject("Satellite 51").AddComponent<NBody>();
        extra.transform.SetParent(rig.Root.transform, false);
        extra.isCentralBody = false;
        extra.radius = 5f;
        rig.BodyService.Register(extra);

        Assert.AreEqual(75f, slider.maxValue);
        Assert.AreEqual(75f, controller.ActiveTimeScale);
        Assert.AreEqual(75f, Time.timeScale);
        Assert.AreEqual(75f, slider.value);
        Assert.AreEqual(BodyRuntimeCoordinator.BaseSimulationStep * 75f, controller.SimulationStepSeconds, 0.0001f);

        controller.SetTimeScale(250f);
        Assert.AreEqual(75f, controller.ActiveTimeScale, "Programmatic changes also obey the limit.");

        rig.BodyService.Deregister(extra);
        Assert.AreEqual(100f, slider.maxValue);
        Assert.AreEqual(75f, controller.ActiveTimeScale, "Removing a body must not unexpectedly speed up time.");
        controller.SetTimeScale(250f);
        Assert.AreEqual(100f, controller.ActiveTimeScale);
        Assert.AreEqual(100f, Time.timeScale);
    }

    [Test]
    public void Population_limit_applies_while_paused_and_when_temporary_limit_ends()
    {
        rig = SimTestBootstrap.CreateBasic(51);
        controller.Initialize(rig.Ctx);
        controller.SetTimeScale(250f);
        Assert.AreEqual(75f, controller.ActiveTimeScale);
        Assert.AreEqual(75f, Time.timeScale);

        controller.TogglePause();
        Assert.AreEqual(0f, Time.timeScale);
        controller.SetTimeScale(250f);
        controller.TogglePause();
        Assert.AreEqual(75f, controller.ActiveTimeScale);
        Assert.AreEqual(75f, Time.timeScale);

        Assert.IsTrue(controller.BeginTemporaryMaxTimeScale(30f));
        Assert.AreEqual(30f, Time.timeScale);
        controller.EndTemporaryMaxTimeScale();
        Assert.AreEqual(75f, controller.ActiveTimeScale);
        Assert.AreEqual(75f, Time.timeScale);
    }

    [Test]
    public void Physics_step_advances_selected_250x_time_with_Unity_clock_capped_at_100x()
    {
        rig = SimTestBootstrap.CreateBasic(1);
        var runtime = new GameObject("Runtime").AddComponent<BodyRuntimeCoordinator>();
        runtime.transform.SetParent(rig.Root.transform, false);
        rig.Ctx.BodyRuntimeCoordinator = runtime;
        rig.Ctx.TimeController = controller;
        runtime.Initialize(rig.Ctx);
        controller.Initialize(rig.Ctx);

        var satellite = rig.Satellites[0];
        satellite.state.position = new double3(700, 0, 0);
        satellite.state.velocity = new double3(0, 0, 0.75);
        controller.SetTimeScale(250f);

        Assert.AreEqual(100f, Time.timeScale);
        typeof(BodyService).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(rig.BodyService, null);
        Assert.AreEqual(5.0, runtime.SimulationTimeSeconds, 0.0001);
    }
}
