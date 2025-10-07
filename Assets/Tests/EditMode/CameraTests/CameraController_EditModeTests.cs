using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class CameraController_EditModeTests
{
    private SimTestRig rig;

    [TearDown]
    public void TearDown() => rig?.Dispose();

    [UnityTest]
    public IEnumerator Initializes_and_tracks_first_satellite_in_Track_mode()
    {
        rig = SimTestBootstrap.CreateBasic(satelliteCount: 2);
        yield return null; // Co_InitializeCamera waits a frame

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Track));
        Assert.NotNull(rig.Controller.CurrentBody);
        Assert.AreEqual("Sat1", rig.Controller.CurrentBody.name);

        Assert.IsTrue(rig.CamMove.enabled);
        Assert.IsFalse(rig.CamMove.isFreeCamMode);
        Assert.IsFalse(rig.CamMove.inEarthCam);
    }

    [UnityTest]
    public IEnumerator SwitchToEarthCam_toggles_and_back()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        var prior = rig.Controller.CurrentBody;

        rig.Controller.SwitchToEarthCam();
        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Earth));
        Assert.IsTrue(rig.CamMove.inEarthCam);

        rig.Controller.SwitchToEarthCam(); // back
        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Track));
        Assert.That(rig.Controller.CurrentBody, Is.EqualTo(prior));
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
    public IEnumerator BreakToFreeCam_enters_free_and_disables_CameraMovement()
    {
        rig = SimTestBootstrap.CreateBasic(2);
        yield return null;

        rig.Controller.BreakToFreeCam();

        Assert.That(rig.Controller.Mode, Is.EqualTo(CameraMode.Free));
        Assert.IsNull(rig.Controller.CurrentBody);
        Assert.IsNull(rig.Controller.CurrentPlaceholder);
        Assert.IsFalse(rig.CamMove.enabled);
        Assert.IsTrue(rig.CamMove.isFreeCamMode);
        Assert.IsFalse(rig.CamMove.inEarthCam);
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
}
