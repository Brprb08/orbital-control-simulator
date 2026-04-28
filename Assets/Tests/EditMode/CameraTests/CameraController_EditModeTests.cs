using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Edit-mode tests for CameraController
/// verifies initialization, track/earth/free transitions, placeholder tracking,
/// fallback selection when bodies are removed, event emission, and UI suppression.
/// </summary>
public class CameraController_EditModeTests
{
    private SimTestRig rig;

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field, $"Could not find private field {fieldName}.");
        return (T)field.GetValue(target);
    }

    [TearDown]
    public void TearDown()
    {
        rig?.Dispose();
        rig = null;
    }

    [UnityTest]
    public IEnumerator Initializes_and_tracks_first_satellite_in_Track_mode()
    {
        rig = SimTestBootstrap.CreateBasic(satelliteCount: 2);
        yield return null;

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Track));
        Assert.NotNull(rig.Controller.CurrentBody);
        Assert.AreEqual("Sat1", rig.Controller.CurrentBody.name);

        Assert.IsTrue(rig.CamMove.enabled);
        Assert.IsFalse(rig.CamMove.IsFreeCamMode);
        Assert.IsFalse(GetPrivateField<bool>(rig.CamMove, "inEarthFocus"));
    }

    [UnityTest]
    public IEnumerator Initializes_with_no_satellites_falls_back_to_central_body_via_free_transition()
    {
        rig = SimTestBootstrap.CreateBasic(satelliteCount: 0);
        yield return null;

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Track));
        Assert.NotNull(rig.Controller.CurrentBody);
        Assert.That(rig.Controller.CurrentBody.isCentralBody, Is.True);
    }

    [Test]
    public void RefreshBodiesList_contains_only_satellites()
    {
        rig = SimTestBootstrap.CreateBasic(satelliteCount: 2);

        rig.Controller.RefreshBodiesList();

        Assert.That(rig.Controller.Bodies.Count, Is.EqualTo(2));
        Assert.That(rig.Controller.Bodies[0].name, Is.EqualTo("Sat1"));
        Assert.That(rig.Controller.Bodies[1].name, Is.EqualTo("Sat2"));
        Assert.That(rig.Controller.Bodies, Has.None.Matches<NBody>(b => b.isCentralBody));
    }

    [UnityTest]
    public IEnumerator SwitchToEarthCam_toggles_and_back()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        var prior = rig.Controller.CurrentBody;

        rig.Controller.SwitchToEarthCam();
        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Earth));
        Assert.IsTrue(GetPrivateField<bool>(rig.CamMove, "inEarthFocus"));

        rig.Controller.SwitchToEarthCam();
        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Track));
        Assert.That(rig.Controller.CurrentBody, Is.EqualTo(prior));
    }

    [UnityTest]
    public IEnumerator TrackEarth_with_null_breaks_to_free_cam()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        rig.Controller.TrackEarth(null);

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Free));
        Assert.IsNull(rig.Controller.CurrentBody);
        Assert.IsTrue(rig.CamMove.IsFreeCamMode);
        Assert.IsFalse(GetPrivateField<bool>(rig.CamMove, "inEarthFocus"));
    }

    [UnityTest]
    public IEnumerator ExitEarthView_without_previous_target_returns_to_current_body_or_free()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        yield return null;

        rig.Controller.TrackEarth(rig.Earth);
        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Earth));

        rig.Controller.ExitEarthView();

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Track));
        Assert.That(rig.Controller.CurrentBody, Is.EqualTo(rig.Earth));
    }

    [UnityTest]
    public IEnumerator BreakToFreeCam_enters_free_and_disables_CameraMovement()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        rig.Controller.BreakToFreeCam();

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Free));
        Assert.IsNull(rig.Controller.CurrentBody);
        Assert.IsNull(rig.Controller.CurrentPlaceholder);
        Assert.IsFalse(rig.CamMove.enabled);
        Assert.IsTrue(rig.CamMove.IsFreeCamMode);
        Assert.IsFalse(GetPrivateField<bool>(rig.CamMove, "inEarthFocus"));
    }

    [UnityTest]
    public IEnumerator ReturnToTracking_from_free_picks_first_satellite()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        rig.Controller.BreakToFreeCam();
        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Free));

        rig.Controller.ReturnToTracking();
        yield return null;

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Track));
        Assert.NotNull(rig.Controller.CurrentBody);
        Assert.AreEqual("Sat1", rig.Controller.CurrentBody.name);
    }

    [UnityTest]
    public IEnumerator ReturnToTracking_restores_last_tracked_before_free_when_still_available()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        var sat2 = rig.Satellites[1];
        rig.Controller.TrackBody(sat2);
        Assert.That(rig.Controller.CurrentBody, Is.EqualTo(sat2));

        rig.Controller.BreakToFreeCam();
        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Free));

        rig.Controller.ReturnToTracking();
        yield return null;

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Track));
        Assert.That(rig.Controller.CurrentBody, Is.EqualTo(sat2));
    }

    [UnityTest]
    public IEnumerator ReturnToTracking_without_any_satellites_stays_or_goes_free()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        yield return null;

        rig.Controller.BreakToFreeCam();
        rig.Controller.ReturnToTracking();
        yield return null;

        if (rig.BodyService.GetSatellites().Count == 0)
        {
            Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Free));
            Assert.IsNull(rig.Controller.CurrentBody);
        }
    }

    [UnityTest]
    public IEnumerator TrackBody_null_breaks_to_free()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        rig.Controller.TrackBody(null);

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Free));
        Assert.IsNull(rig.Controller.CurrentBody);
    }

    [UnityTest]
    public IEnumerator TrackBody_switches_to_requested_satellite()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        var sat2 = rig.Satellites[1];
        rig.Controller.TrackBody(sat2);

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Track));
        Assert.That(rig.Controller.CurrentBody, Is.EqualTo(sat2));
        Assert.IsNull(rig.Controller.CurrentPlaceholder);
    }

    [UnityTest]
    public IEnumerator TrackPlaceholder_sets_placeholder_tracking_state()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        var placeholder = new GameObject("Placeholder").transform;
        placeholder.SetParent(rig.Root.transform, false);

        rig.Controller.TrackPlaceholder(placeholder);

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Track));
        Assert.IsTrue(rig.Controller.IsTrackingPlaceholder);
        Assert.IsNull(rig.Controller.CurrentBody);
        Assert.That(rig.Controller.CurrentPlaceholder, Is.EqualTo(placeholder));
    }

    [UnityTest]
    public IEnumerator TrackPlaceholder_null_breaks_to_free()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        rig.Controller.TrackPlaceholder(null);

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Free));
        Assert.IsNull(rig.Controller.CurrentBody);
        Assert.IsNull(rig.Controller.CurrentPlaceholder);
    }

    [UnityTest]
    public IEnumerator ReturnToTracking_after_placeholder_restores_previous_body()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        var original = rig.Controller.CurrentBody;

        var placeholder = new GameObject("Placeholder").transform;
        placeholder.SetParent(rig.Root.transform, false);

        rig.Controller.TrackPlaceholder(placeholder);
        Assert.IsTrue(rig.Controller.IsTrackingPlaceholder);

        rig.Controller.ReturnToTracking();
        yield return null;

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Track));
        Assert.That(rig.Controller.CurrentBody, Is.EqualTo(original));
        Assert.IsNull(rig.Controller.CurrentPlaceholder);
    }

    [UnityTest]
    public IEnumerator PreviewPlaceholderInFree_sets_placeholder_only_in_free_mode()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        rig.Controller.BreakToFreeCam();

        var placeholder = new GameObject("PreviewPlaceholder").transform;
        placeholder.position = new Vector3(10, 0, 0);
        placeholder.SetParent(rig.Root.transform, false);

        rig.Controller.PreviewPlaceholderInFree(placeholder);

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Free));
        Assert.That(rig.Controller.CurrentPlaceholder, Is.EqualTo(placeholder));
        Assert.IsNull(rig.Controller.CurrentBody);
        Assert.IsFalse(rig.CamMove.IsFreeCamMode);
        Assert.IsFalse(GetPrivateField<bool>(rig.CamMove, "inEarthFocus"));
        Assert.IsTrue(rig.CamMove.enabled);
    }

    [UnityTest]
    public IEnumerator SwitchToEarthCam_from_free_placeholder_preview_toggles_earth_view_and_restores_placeholder()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        rig.Controller.BreakToFreeCam();

        var placeholder = new GameObject("PreviewPlaceholder").transform;
        placeholder.position = new Vector3(10f, 0f, 0f);
        placeholder.SetParent(rig.Root.transform, false);

        rig.Controller.PreviewPlaceholderInFree(placeholder);
        Assert.That(rig.Controller.CurrentPlaceholder, Is.EqualTo(placeholder));

        rig.Controller.SwitchToEarthCam();

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Free));
        Assert.IsFalse(rig.Controller.IsFree);
        Assert.IsTrue(rig.Controller.IsEarthView);
        Assert.IsFalse(rig.Controller.IsTrackingPlaceholder);
        Assert.That(rig.Controller.CurrentPlaceholder, Is.EqualTo(placeholder));
        Assert.IsTrue(GetPrivateField<bool>(rig.CamMove, "inEarthFocus"));
        Assert.That(GetPrivateField<NBody>(rig.CamMove, "earthFocusBody"), Is.EqualTo(rig.Earth));

        rig.Controller.SwitchToEarthCam();

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Free));
        Assert.IsTrue(rig.Controller.IsFree);
        Assert.IsFalse(rig.Controller.IsEarthView);
        Assert.IsTrue(rig.Controller.IsTrackingPlaceholder);
        Assert.That(rig.Controller.CurrentPlaceholder, Is.EqualTo(placeholder));
        Assert.IsFalse(GetPrivateField<bool>(rig.CamMove, "inEarthFocus"));
    }

    [UnityTest]
    public IEnumerator PreviewPlaceholderInFree_does_nothing_when_not_in_free_mode()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        var original = rig.Controller.CurrentBody;
        var placeholder = new GameObject("PreviewPlaceholder").transform;
        placeholder.SetParent(rig.Root.transform, false);

        rig.Controller.PreviewPlaceholderInFree(placeholder);

        Assert.That(rig.Controller.CurrentBody, Is.EqualTo(original));
        Assert.IsNull(rig.Controller.CurrentPlaceholder);
        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Track));
    }

    [UnityTest]
    public IEnumerator EndPreviewPlaceholder_disables_cameraMovement_only_in_free_mode()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        rig.Controller.BreakToFreeCam();

        var placeholder = new GameObject("PreviewPlaceholder").transform;
        placeholder.position = new Vector3(10, 0, 0);
        placeholder.SetParent(rig.Root.transform, false);

        rig.Controller.PreviewPlaceholderInFree(placeholder);
        Assert.IsTrue(rig.CamMove.enabled);

        rig.Controller.EndPreviewPlaceholder();

        Assert.IsFalse(rig.CamMove.enabled);
        Assert.IsNull(rig.Controller.CurrentBody);
    }

    [UnityTest]
    public IEnumerator Removing_current_body_falls_back_then_free()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        var sat2 = rig.Satellites[1];
        rig.Controller.TrackBody(sat2);
        Assert.That(rig.Controller.CurrentBody, Is.EqualTo(sat2));

        rig.BodyService.Deregister(sat2);
        yield return null;

        Assert.That(rig.Controller.CurrentBody, Is.EqualTo(rig.Satellites[0]));

        rig.BodyService.Deregister(rig.Satellites[0]);
        yield return null;

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Free));
        Assert.IsNull(rig.Controller.CurrentBody);
    }

    [UnityTest]
    public IEnumerator Removing_non_current_body_does_not_change_current_target()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        var current = rig.Controller.CurrentBody;
        var other = rig.Satellites[1];

        Assert.That(current, Is.Not.EqualTo(other));

        rig.BodyService.Deregister(other);
        yield return null;

        Assert.That(rig.Controller.CurrentBody, Is.EqualTo(current));
        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Track));
    }

    [UnityTest]
    public IEnumerator Adding_satellite_after_no_satellites_picks_initial_target()
    {
        rig = SimTestBootstrap.CreateBasic(satelliteCount: 0);
        yield return null;

        rig.Controller.BreakToFreeCam();

        var sat = new GameObject("AddedSat").AddComponent<NBody>();
        sat.transform.SetParent(rig.Root.transform, false);
        sat.tag = "Satellite";

        rig.BodyService.Register(sat);
        yield return null;

        rig.Controller.ReturnToTracking();
        yield return null;

        Assert.That(rig.Controller.CurrentBody, Is.EqualTo(sat));
        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Track));
    }

    [UnityTest]
    public IEnumerator Adding_non_satellite_does_not_enter_satellite_list()
    {
        rig = SimTestBootstrap.CreateBasic(satelliteCount: 0);
        yield return null;

        var body = new GameObject("Body").AddComponent<NBody>();
        body.transform.SetParent(rig.Root.transform, false);
        body.tag = "Untagged";

        rig.BodyService.Register(body);
        yield return null;

        Assert.That(rig.Controller.Bodies.Count, Is.EqualTo(0));
    }

    [UnityTest]
    public IEnumerator TrackBody_emits_body_changed_event()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        NBody emitted = null;
        rig.Controller.OnTrackedBodyChanged += b => emitted = b;

        var sat2 = rig.Satellites[1];
        rig.Controller.TrackBody(sat2);

        Assert.That(emitted, Is.EqualTo(sat2));
    }

    [UnityTest]
    public IEnumerator TrackPlaceholder_emits_placeholder_changed_event()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        Transform emitted = null;
        rig.Controller.OnTrackedPlaceholderChanged += t => emitted = t;

        var placeholder = new GameObject("Placeholder").transform;
        placeholder.SetParent(rig.Root.transform, false);

        rig.Controller.TrackPlaceholder(placeholder);

        Assert.That(emitted, Is.EqualTo(placeholder));
    }

    [UnityTest]
    public IEnumerator BreakToFreeCam_emits_mode_changed_event()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        CameraMode emitted = rig.Controller.Mode;
        rig.Controller.OnModeChanged += m => emitted = m;

        rig.Controller.BreakToFreeCam();

        Assert.That(emitted, Is.EqualTo(CameraMode.Free));
    }

    [UnityTest]
    public IEnumerator BeginUiSuppress_blocks_events_until_EndUiSuppress()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        int modeEvents = 0;
        int bodyEvents = 0;
        int placeholderEvents = 0;

        rig.Controller.OnModeChanged += _ => modeEvents++;
        rig.Controller.OnTrackedBodyChanged += _ => bodyEvents++;
        rig.Controller.OnTrackedPlaceholderChanged += _ => placeholderEvents++;

        rig.Controller.BeginUiSuppress();

        rig.Controller.BreakToFreeCam();
        rig.Controller.ReturnToTracking();

        var placeholder = new GameObject("Placeholder").transform;
        placeholder.SetParent(rig.Root.transform, false);
        rig.Controller.TrackPlaceholder(placeholder);

        Assert.That(modeEvents, Is.EqualTo(0));
        Assert.That(bodyEvents, Is.EqualTo(0));
        Assert.That(placeholderEvents, Is.EqualTo(0));

        rig.Controller.EndUiSuppress();

        rig.Controller.TrackBody(rig.Satellites[0]);

        Assert.That(bodyEvents, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator IsFree_IsEarthView_IsTrackingPlaceholder_reflect_current_state()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        Assert.IsFalse(rig.Controller.IsFree);
        Assert.IsFalse(rig.Controller.IsEarthView);
        Assert.IsFalse(rig.Controller.IsTrackingPlaceholder);

        rig.Controller.SwitchToEarthCam();
        Assert.IsFalse(rig.Controller.IsFree);
        Assert.IsTrue(rig.Controller.IsEarthView);
        Assert.IsFalse(rig.Controller.IsTrackingPlaceholder);

        rig.Controller.BreakToFreeCam();
        Assert.IsTrue(rig.Controller.IsFree);
        Assert.IsFalse(rig.Controller.IsEarthView);
        Assert.IsFalse(rig.Controller.IsTrackingPlaceholder);

        var placeholder = new GameObject("Placeholder").transform;
        placeholder.SetParent(rig.Root.transform, false);
        rig.Controller.TrackPlaceholder(placeholder);

        Assert.IsFalse(rig.Controller.IsFree);
        Assert.IsFalse(rig.Controller.IsEarthView);
        Assert.IsTrue(rig.Controller.IsTrackingPlaceholder);
    }
}
