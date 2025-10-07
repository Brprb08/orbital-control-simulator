using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class UIManager_EditModeTests
{
    private SimTestRig rig;

    [TearDown]
    public void TearDown() => rig?.Dispose();

    [UnityTest]
    public IEnumerator Free_mode_configures_panels_and_buttons()
    {
        rig = SimTestBootstrap.CreateWithUI(satelliteCount: 2, withTMP: true);
        yield return null; // allow UI Initialize paint

        // Free mode by default? Controller starts in Track after init. Force to Free:
        rig.Controller.BreakToFreeCam();
        yield return null;

        Assert.IsTrue(rig.UI.placementSelectPanel.activeSelf);
        Assert.IsTrue(rig.UI.objectPlacementPanel.activeSelf || rig.UI.placeTLEPanel.activeSelf);
        Assert.IsFalse(rig.UI.objectInfoPanel.activeSelf);
        Assert.IsFalse(rig.UI.apogeePerigeePanel.activeSelf);
        Assert.IsFalse(rig.UI.timeControlsPanel.activeSelf);
    }

    [UnityTest]
    public IEnumerator Switch_to_Track_updates_UI_and_instruction_text()
    {
        rig = SimTestBootstrap.CreateWithUI(2, true);
        yield return null;

        rig.Controller.ReturnToTracking(); // ensure Track
        yield return null;

        Assert.IsTrue(rig.UI.objectInfoPanel.activeSelf);
        Assert.IsTrue(rig.UI.apogeePerigeePanel.activeSelf);
        Assert.IsTrue(rig.UI.timeControlsPanel.activeSelf);
        StringAssert.Contains("Track Cam Mode", rig.UI.instructionText.text);
    }

    [UnityTest]
    public IEnumerator EarthCam_label_toggles()
    {
        rig = SimTestBootstrap.CreateWithUI(2, true);
        yield return null;

        rig.Controller.ReturnToTracking();
        yield return null;
        Assert.AreEqual("Earth Cam", rig.UI.earthCamButtonText.text);

        rig.Controller.SwitchToEarthCam();
        yield return null;
        Assert.AreEqual("Satellite Cam", rig.UI.earthCamButtonText.text);
    }
}

