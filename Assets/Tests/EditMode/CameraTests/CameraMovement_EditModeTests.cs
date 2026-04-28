using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Edit-mode tests for CameraMovement:
/// verifies initialization, target assignment, Earth-cam toggling,
/// placeholder targeting, LateUpdate positioning, UI updates,
/// and helper behaviors that do not depend on live input.
/// </summary>
public class CameraMovement_EditModeTests
{
    private SimTestRig rig;

    [TearDown]
    public void TearDown()
    {
        rig?.Dispose();
        rig = null;
    }

    private static void InvokeLateUpdate(CameraMovement camMove)
    {
        var mi = typeof(CameraMovement).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(mi, "Could not find private LateUpdate() via reflection.");
        mi.Invoke(camMove, null);
    }

    private static NBody CreateBody(Transform parent, string name, float radius, float camRadius, string tag = "Untagged")
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        if (!string.IsNullOrEmpty(tag)) go.tag = tag;

        var nb = go.AddComponent<NBody>();
        nb.isCentralBody = false;
        nb.radius = radius;
        nb.cameraDistanceRadius = camRadius;
        nb.state = new NBody.OrbitalState(
            new double3(go.transform.position.x, go.transform.position.y, go.transform.position.z),
            double3.zero,
            0f,
            nb.trueMass,
            nb.radius,
            nb.dragCoefficient,
            Vector3.zero
        );
        return nb;
    }

    [Test]
    public void Initialize_sets_tutorial_controller_and_main_camera()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        rig.CamMove.Initialize(rig.Ctx);

        Assert.AreEqual(rig.Ctx.TutorialController, rig.CamMove.tutorialController);
        Assert.NotNull(rig.CamMove.MainCamera);
    }

    [UnityTest]
    public IEnumerator SetTargetBody_configures_rig_safely()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var body = CreateBody(rig.Root.transform, "Sat", 20f, 40f);

        rig.CamMove.SetTargetBody(body);
        yield return null;

        Assert.AreEqual(body, rig.CamMove.targetBody);
        Assert.IsNull(rig.CamMove.targetPlaceholder);

        var q = rig.CamMove.cameraPivotTransform.rotation;
        Assert.IsFalse(float.IsNaN(q.x + q.y + q.z + q.w));
    }

    [UnityTest]
    public IEnumerator SetTargetBody_null_clears_body_and_placeholder()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var body = CreateBody(rig.Root.transform, "Sat", 20f, 40f);

        rig.CamMove.SetTargetBody(body);
        yield return null;
        Assert.AreEqual(body, rig.CamMove.targetBody);

        rig.CamMove.SetTargetBody(null);
        yield return null;

        Assert.IsNull(rig.CamMove.targetBody);
        Assert.IsNull(rig.CamMove.targetPlaceholder);
    }

    [UnityTest]
    public IEnumerator SetTargetBody_small_body_uses_close_default_distance()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var body = CreateBody(rig.Root.transform, "Tiny", radius: 5f, camRadius: 10f);

        rig.CamMove.inEarthCam = false;
        rig.CamMove.SetTargetBody(body);
        yield return null;

        Assert.AreEqual(body, rig.CamMove.targetBody);
        Assert.That(rig.CamMove.distance, Is.GreaterThan(0f));
        Assert.That(rig.CamMove.distance, Is.LessThan(10000f));
    }

    [UnityTest]
    public IEnumerator SetTargetBody_while_in_earth_cam_uses_earth_override_distance()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var body = CreateBody(rig.Root.transform, "Sat", radius: 20f, camRadius: 40f);

        rig.CamMove.inEarthCam = true;
        rig.CamMove.SetTargetBody(body);
        yield return null;

        Assert.AreEqual(2500f, rig.CamMove.distance, 0.001f);
    }

    [UnityTest]
    public IEnumerator SetTargetEarth_toggles_flag_and_distance_valid()
    {
        rig = SimTestBootstrap.CreateBasic(1);
        var earth = rig.Earth;

        bool wasEarth = rig.CamMove.inEarthCam;
        rig.CamMove.SetTargetEarth(earth);
        yield return null;

        Assert.AreNotEqual(wasEarth, rig.CamMove.inEarthCam);
        Assert.AreEqual(earth, rig.CamMove.tempEarthBody);
    }

    [UnityTest]
    public IEnumerator SetTargetEarth_clears_placeholder()
    {
        rig = SimTestBootstrap.CreateBasic(1);
        var ph = new GameObject("PH").transform;
        ph.SetParent(rig.Root.transform, false);
        rig.CamMove.targetPlaceholder = ph;

        rig.CamMove.SetTargetEarth(rig.Earth);
        yield return null;

        Assert.IsNull(rig.CamMove.targetPlaceholder);
    }

    [UnityTest]
    public IEnumerator SetTargetEarth_sets_tutorial_flag_when_in_tutorial_mode()
    {
        rig = SimTestBootstrap.CreateBasic(1);
        rig.Ctx.TutorialController.inTutorialMode = true;
        rig.Ctx.TutorialController.hasSwitchedToEarthCam = false;

        rig.CamMove.SetTargetEarth(rig.Earth);
        yield return null;

        Assert.IsTrue(rig.Ctx.TutorialController.hasSwitchedToEarthCam);
    }

    [UnityTest]
    public IEnumerator SetTargetEarth_null_still_toggles_mode_and_sets_temp_reference()
    {
        rig = SimTestBootstrap.CreateBasic(1);

        bool before = rig.CamMove.inEarthCam;
        rig.CamMove.SetTargetEarth(null);
        yield return null;

        Assert.AreNotEqual(before, rig.CamMove.inEarthCam);
        Assert.IsNull(rig.CamMove.tempEarthBody);
    }

    [UnityTest]
    public IEnumerator SetTargetBodyPlaceholder_sets_distance_from_scale()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var ph = new GameObject("PH").transform;
        ph.SetParent(rig.Root.transform, false);
        ph.localScale = new Vector3(3f, 3f, 3f);

        rig.CamMove.SetTargetBodyPlaceholder(ph);
        yield return null;

        Assert.IsNull(rig.CamMove.targetBody);
        Assert.AreEqual(ph, rig.CamMove.targetPlaceholder);
        Assert.AreEqual(30f, rig.CamMove.distance, 0.001f);
        Assert.AreEqual(0.6f, rig.CamMove.height, 0.001f);
    }

    [UnityTest]
    public IEnumerator SetTargetBodyPlaceholder_null_clears_body_and_keeps_placeholder_null()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var body = CreateBody(rig.Root.transform, "Sat", 20f, 40f);
        rig.CamMove.targetBody = body;

        rig.CamMove.SetTargetBodyPlaceholder(null);
        yield return null;

        Assert.IsNull(rig.CamMove.targetBody);
        Assert.IsNull(rig.CamMove.targetPlaceholder);
    }

    [UnityTest]
    public IEnumerator SetTargetBodyPlaceholder_overwrites_existing_body_target()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var body = CreateBody(rig.Root.transform, "Sat", 20f, 40f);
        var ph = new GameObject("PH").transform;
        ph.SetParent(rig.Root.transform, false);
        ph.localScale = Vector3.one * 2f;

        rig.CamMove.SetTargetBody(body);
        yield return null;
        Assert.AreEqual(body, rig.CamMove.targetBody);

        rig.CamMove.SetTargetBodyPlaceholder(ph);
        yield return null;

        Assert.IsNull(rig.CamMove.targetBody);
        Assert.AreEqual(ph, rig.CamMove.targetPlaceholder);
    }

    [Test]
    public void PointCameraTowardCentralBody_sets_valid_camera_and_pivot_state()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        rig.CamMove.cameraPivotTransform.position = new Vector3(10f, 0f, 0f);
        rig.CamMove.cameraTransform.position = Vector3.zero;

        rig.CamMove.PointCameraTowardCentralBody(
            centralBodyPos: Vector3.zero,
            targetPosition: new Vector3(100f, 0f, 0f)
        );

        var pivotRot = rig.CamMove.cameraPivotTransform.rotation;
        var camPos = rig.CamMove.cameraTransform.position;

        Assert.IsFalse(float.IsNaN(pivotRot.x + pivotRot.y + pivotRot.z + pivotRot.w));
        Assert.IsFalse(float.IsNaN(camPos.x + camPos.y + camPos.z));
    }

    [Test]
    public void PointCameraTowardCentralBody_moves_camera_transform_near_target_line()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        rig.CamMove.cameraPivotTransform.position = new Vector3(0f, 0f, 0f);

        var target = new Vector3(10f, 0f, 0f);
        rig.CamMove.PointCameraTowardCentralBody(Vector3.zero, target);

        Assert.AreNotEqual(target, rig.CamMove.cameraTransform.position);
    }

    [Test]
    public void LateUpdate_returns_when_no_target_exists()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        Assert.DoesNotThrow(() => InvokeLateUpdate(rig.CamMove));
    }

    [Test]
    public void LateUpdate_moves_rig_transform_to_target_body_position()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var body = CreateBody(rig.Root.transform, "Sat", 20f, 40f);
        body.transform.position = new Vector3(11f, 22f, 33f);
        body.velocity = new Vector3(1f, 2f, 3f);
        body.state = new NBody.OrbitalState(
            new double3(body.transform.position.x, body.transform.position.y, body.transform.position.z),
            new double3(1, 2, 3),
            0f,
            body.trueMass,
            body.radius,
            body.dragCoefficient,
            Vector3.zero
        );

        rig.CamMove.SetTargetBody(body);
        InvokeLateUpdate(rig.CamMove);

        Assert.AreEqual(body.transform.position, rig.CamMove.transform.position);
    }

    [Test]
    public void LateUpdate_moves_rig_transform_to_placeholder_position_when_tracking_placeholder()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var ph = new GameObject("PH").transform;
        ph.SetParent(rig.Root.transform, false);
        ph.position = new Vector3(4f, 5f, 6f);
        ph.localScale = Vector3.one * 3f;

        rig.CamMove.SetTargetBodyPlaceholder(ph);
        InvokeLateUpdate(rig.CamMove);

        Assert.AreEqual(ph.position, rig.CamMove.transform.position);
    }

    [Test]
    public void LateUpdate_in_earth_cam_uses_temp_earth_position()
    {
        rig = SimTestBootstrap.CreateBasic(1);

        rig.Earth.transform.position = new Vector3(100f, 200f, 300f);
        rig.CamMove.tempEarthBody = rig.Earth;
        rig.CamMove.inEarthCam = true;
        rig.CamMove.targetBody = rig.Satellites[0];

        InvokeLateUpdate(rig.CamMove);

        Assert.AreEqual(rig.Earth.transform.position, rig.CamMove.transform.position);
    }

    [Test]
    public void LateUpdate_interpolates_main_camera_local_position()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var body = CreateBody(rig.Root.transform, "Sat", 20f, 40f);
        body.transform.position = Vector3.zero;
        body.state = new NBody.OrbitalState(
            double3.zero,
            double3.zero,
            0f,
            body.trueMass,
            body.radius,
            body.dragCoefficient,
            Vector3.zero
        );

        rig.CamMove.distance = 50f;
        rig.CamMove.height = 10f;
        rig.CamMove.MainCamera.transform.localPosition = Vector3.zero;

        rig.CamMove.SetTargetBody(body);
        InvokeLateUpdate(rig.CamMove);

        Assert.AreNotEqual(Vector3.zero, rig.CamMove.MainCamera.transform.localPosition);
    }

    [Test]
    public void LateUpdate_updates_velocity_altitude_and_name_text_for_body_target()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        rig.CamMove.velocityText = new GameObject("VelocityText").AddComponent<TextMeshProUGUI>();
        rig.CamMove.altitudeText = new GameObject("AltitudeText").AddComponent<TextMeshProUGUI>();
        rig.CamMove.trackingObjectNameText = new GameObject("NameText").AddComponent<TextMeshProUGUI>();

        var body = CreateBody(rig.Root.transform, "SatX", 20f, 40f);
        body.transform.position = new Vector3(737.8137f, 0f, 0f);
        body.velocity = new Vector3(0f, 0f, 2f);
        body.state = new NBody.OrbitalState(
            new double3(737.8137, 0.0, 0.0),
            new double3(0.0, 0.0, 2.0),
            0f,
            body.trueMass,
            body.radius,
            body.dragCoefficient,
            Vector3.zero
        );

        rig.Controller.TrackBody(body);
        InvokeLateUpdate(rig.CamMove);

        Assert.That(rig.CamMove.velocityText.text, Does.Contain("Velocity:"));
        Assert.That(rig.CamMove.velocityText.text, Does.Contain("20000.00"));

        Assert.That(rig.CamMove.altitudeText.text, Does.Contain("Altitude:"));
        Assert.That(rig.CamMove.altitudeText.text, Does.Contain("1000.000"));

        Assert.That(rig.CamMove.trackingObjectNameText.text, Is.EqualTo("SatX"));
    }

    [Test]
    public void LateUpdate_does_not_update_ui_for_placeholder_target()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        rig.CamMove.velocityText = new GameObject("VelocityText").AddComponent<TextMeshProUGUI>();
        rig.CamMove.altitudeText = new GameObject("AltitudeText").AddComponent<TextMeshProUGUI>();
        rig.CamMove.trackingObjectNameText = new GameObject("NameText").AddComponent<TextMeshProUGUI>();

        rig.CamMove.velocityText.text = "unchanged-v";
        rig.CamMove.altitudeText.text = "unchanged-a";
        rig.CamMove.trackingObjectNameText.text = "unchanged-n";

        var ph = new GameObject("PH").transform;
        ph.SetParent(rig.Root.transform, false);
        ph.localScale = Vector3.one * 3f;

        rig.CamMove.SetTargetBodyPlaceholder(ph);
        InvokeLateUpdate(rig.CamMove);

        Assert.AreEqual("unchanged-v", rig.CamMove.velocityText.text);
        Assert.AreEqual("unchanged-a", rig.CamMove.altitudeText.text);
        Assert.AreEqual("unchanged-n", rig.CamMove.trackingObjectNameText.text);
    }

    [Test]
    public void IsPointerOverDropdown_returns_false_when_dropdown_missing()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        Assert.IsFalse(rig.CamMove.IsPointerOverDropdown());
    }

    [Test]
    public void IsPointerOverDropdown_returns_false_when_cached_dropdown_is_inactive()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        var dropdown = new GameObject("Dropdown List");
        dropdown.AddComponent<RectTransform>();
        dropdown.SetActive(false);

        try
        {
            Assert.IsFalse(rig.CamMove.IsPointerOverDropdown());
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(dropdown);
        }
    }
}