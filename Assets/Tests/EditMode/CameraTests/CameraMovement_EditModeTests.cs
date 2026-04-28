using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Edit-mode tests for CameraMovement:
/// verifies applied focus behavior, LateUpdate positioning, UI updates,
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

    private static void InvokeLateUpdate(MonoBehaviour behaviour)
    {
        var mi = behaviour.GetType().GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(mi, $"Could not find private LateUpdate() on {behaviour.GetType().Name} via reflection.");
        mi.Invoke(behaviour, null);
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field, $"Could not find private field {fieldName}.");
        return (T)field.GetValue(target);
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
    public void Initialize_sets_main_camera()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        rig.CamMove.Initialize(rig.Ctx);

        Assert.NotNull(rig.CamMove.MainCamera);
    }

    [UnityTest]
    public IEnumerator ApplyBodyFocus_configures_rig_safely()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var body = CreateBody(rig.Root.transform, "Sat", 20f, 40f);

        rig.CamMove.ApplyBodyFocus(body);
        yield return null;

        Assert.AreEqual(body, GetPrivateField<NBody>(rig.CamMove, "focusBody"));
        Assert.IsNull(GetPrivateField<Transform>(rig.CamMove, "focusPlaceholder"));

        var q = rig.CamMove.cameraPivotTransform.rotation;
        Assert.IsFalse(float.IsNaN(q.x + q.y + q.z + q.w));
    }

    [UnityTest]
    public IEnumerator ClearFocus_clears_applied_focus()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var body = CreateBody(rig.Root.transform, "Sat", 20f, 40f);

        rig.CamMove.ApplyBodyFocus(body);
        yield return null;
        Assert.AreEqual(body, GetPrivateField<NBody>(rig.CamMove, "focusBody"));

        rig.CamMove.ClearFocus();
        yield return null;

        Assert.IsNull(GetPrivateField<NBody>(rig.CamMove, "focusBody"));
        Assert.IsNull(GetPrivateField<Transform>(rig.CamMove, "focusPlaceholder"));
        Assert.IsNull(GetPrivateField<NBody>(rig.CamMove, "earthFocusBody"));
    }

    [UnityTest]
    public IEnumerator ApplyBodyFocus_small_body_uses_close_default_distance()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var body = CreateBody(rig.Root.transform, "Tiny", radius: 5f, camRadius: 10f);

        rig.CamMove.ApplyBodyFocus(body);
        yield return null;

        Assert.AreEqual(body, GetPrivateField<NBody>(rig.CamMove, "focusBody"));
        Assert.That(rig.CamMove.distance, Is.GreaterThan(0f));
        Assert.That(rig.CamMove.distance, Is.LessThan(10000f));
    }

    [UnityTest]
    public IEnumerator ApplyBodyFocus_uses_default_distance_override()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var body = CreateBody(rig.Root.transform, "Sat", radius: 20f, camRadius: 40f);

        rig.CamMove.ApplyBodyFocus(body, defaultDistanceOverride: 2500f);
        yield return null;

        Assert.AreEqual(2500f, rig.CamMove.distance, 0.001f);
    }

    [UnityTest]
    public IEnumerator ApplyEarthFocus_sets_earth_focus_and_distance()
    {
        rig = SimTestBootstrap.CreateBasic(1);

        rig.CamMove.ApplyEarthFocus(rig.Earth);
        yield return null;

        Assert.AreEqual(rig.Earth, GetPrivateField<NBody>(rig.CamMove, "earthFocusBody"));
        Assert.IsTrue(GetPrivateField<bool>(rig.CamMove, "inEarthFocus"));
        Assert.That(rig.CamMove.distance, Is.EqualTo(2000f).Within(0.001f));
    }

    [UnityTest]
    public IEnumerator ApplyEarthFocus_clears_placeholder_focus()
    {
        rig = SimTestBootstrap.CreateBasic(1);
        var ph = new GameObject("PH").transform;
        ph.SetParent(rig.Root.transform, false);

        rig.CamMove.ApplyPlaceholderFocus(ph);
        rig.CamMove.ApplyEarthFocus(rig.Earth);
        yield return null;

        Assert.IsNull(GetPrivateField<Transform>(rig.CamMove, "focusPlaceholder"));
        Assert.AreEqual(rig.Earth, GetPrivateField<NBody>(rig.CamMove, "earthFocusBody"));
    }

    [UnityTest]
    public IEnumerator ClearEarthFocus_clears_only_earth_focus()
    {
        rig = SimTestBootstrap.CreateBasic(1);

        rig.CamMove.ApplyEarthFocus(rig.Earth);
        rig.CamMove.ClearEarthFocus();
        yield return null;

        Assert.IsFalse(GetPrivateField<bool>(rig.CamMove, "inEarthFocus"));
        Assert.IsNull(GetPrivateField<NBody>(rig.CamMove, "earthFocusBody"));
    }

    [UnityTest]
    public IEnumerator ApplyPlaceholderFocus_sets_distance_from_scale()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var ph = new GameObject("PH").transform;
        ph.SetParent(rig.Root.transform, false);
        ph.localScale = new Vector3(3f, 3f, 3f);

        rig.CamMove.ApplyPlaceholderFocus(ph);
        yield return null;

        Assert.IsNull(GetPrivateField<NBody>(rig.CamMove, "focusBody"));
        Assert.AreEqual(ph, GetPrivateField<Transform>(rig.CamMove, "focusPlaceholder"));
        Assert.AreEqual(30f, rig.CamMove.distance, 0.001f);
        Assert.AreEqual(0.6f, rig.CamMove.height, 0.001f);
    }

    [UnityTest]
    public IEnumerator ApplyPlaceholderFocus_overwrites_existing_body_focus()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var body = CreateBody(rig.Root.transform, "Sat", 20f, 40f);
        var ph = new GameObject("PH").transform;
        ph.SetParent(rig.Root.transform, false);
        ph.localScale = Vector3.one * 2f;

        rig.CamMove.ApplyBodyFocus(body);
        yield return null;
        Assert.AreEqual(body, GetPrivateField<NBody>(rig.CamMove, "focusBody"));

        rig.CamMove.ApplyPlaceholderFocus(ph);
        yield return null;

        Assert.IsNull(GetPrivateField<NBody>(rig.CamMove, "focusBody"));
        Assert.AreEqual(ph, GetPrivateField<Transform>(rig.CamMove, "focusPlaceholder"));
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
    public void LateUpdate_returns_when_no_focus_exists()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        rig.CamMove.ClearFocus();

        Assert.DoesNotThrow(() => InvokeLateUpdate(rig.CamMove));
    }

    [Test]
    public void LateUpdate_moves_rig_transform_to_body_focus_position()
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

        rig.CamMove.ApplyBodyFocus(body);
        InvokeLateUpdate(rig.CamMove);

        Assert.AreEqual(body.transform.position, rig.CamMove.transform.position);
    }

    [Test]
    public void LateUpdate_moves_rig_transform_to_placeholder_position()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var ph = new GameObject("PH").transform;
        ph.SetParent(rig.Root.transform, false);
        ph.position = new Vector3(4f, 5f, 6f);
        ph.localScale = Vector3.one * 3f;

        rig.CamMove.ApplyPlaceholderFocus(ph);
        InvokeLateUpdate(rig.CamMove);

        Assert.AreEqual(ph.position, rig.CamMove.transform.position);
    }

    [Test]
    public void LateUpdate_with_earth_focus_uses_earth_position()
    {
        rig = SimTestBootstrap.CreateBasic(1);

        rig.Earth.transform.position = new Vector3(100f, 200f, 300f);

        rig.CamMove.ApplyEarthFocus(rig.Earth);
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

        rig.CamMove.ApplyBodyFocus(body);
        InvokeLateUpdate(rig.CamMove);

        Assert.AreNotEqual(Vector3.zero, rig.CamMove.MainCamera.transform.localPosition);
    }

    [Test]
    public void CameraInfoUI_updates_velocity_altitude_and_name_text_for_tracked_body()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        var velocityText = new GameObject("VelocityText").AddComponent<TextMeshProUGUI>();
        var altitudeText = new GameObject("AltitudeText").AddComponent<TextMeshProUGUI>();
        var nameText = new GameObject("NameText").AddComponent<TextMeshProUGUI>();
        rig.CameraInfoUI.SetTextReferences(velocityText, altitudeText, nameText);

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
        InvokeLateUpdate(rig.CameraInfoUI);

        Assert.That(velocityText.text, Does.Contain("Velocity:"));
        Assert.That(velocityText.text, Does.Contain("20000.00"));

        Assert.That(altitudeText.text, Does.Contain("Altitude:"));
        Assert.That(altitudeText.text, Does.Contain("1000.000"));

        Assert.That(nameText.text, Is.EqualTo("SatX"));
    }

    [Test]
    public void CameraInfoUI_does_not_update_without_tracked_body()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        var velocityText = new GameObject("VelocityText").AddComponent<TextMeshProUGUI>();
        var altitudeText = new GameObject("AltitudeText").AddComponent<TextMeshProUGUI>();
        var nameText = new GameObject("NameText").AddComponent<TextMeshProUGUI>();
        rig.CameraInfoUI.SetTextReferences(velocityText, altitudeText, nameText);

        velocityText.text = "unchanged-v";
        altitudeText.text = "unchanged-a";
        nameText.text = "unchanged-n";

        var ph = new GameObject("PH").transform;
        ph.SetParent(rig.Root.transform, false);
        ph.localScale = Vector3.one * 3f;

        rig.Controller.BreakToFreeCam();
        rig.CamMove.ApplyPlaceholderFocus(ph);
        InvokeLateUpdate(rig.CameraInfoUI);

        Assert.AreEqual("unchanged-v", velocityText.text);
        Assert.AreEqual("unchanged-a", altitudeText.text);
        Assert.AreEqual("unchanged-n", nameText.text);
    }

    [Test]
    public void SetFreeCamMode_updates_public_free_cam_state()
    {
        rig = SimTestBootstrap.CreateBasic(0);

        rig.CamMove.SetFreeCamMode(true);
        Assert.IsTrue(rig.CamMove.IsFreeCamMode);

        rig.CamMove.SetFreeCamMode(false);
        Assert.IsFalse(rig.CamMove.IsFreeCamMode);
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
