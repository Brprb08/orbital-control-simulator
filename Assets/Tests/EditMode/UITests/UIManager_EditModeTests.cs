using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Edit-mode UI tests for UIManager: verifies panel visibility and instructional text
/// across Free, Track, and Earth Cam modes, plus Earth/Satellite label toggling.
/// </summary>
public class UIManager_EditModeTests
{
    private SimTestRig rig;

    [TearDown]
    public void TearDown() => rig?.Dispose();

    /// <summary>
    /// Free mode should show placement UI and hide tracking/telemetry panels.
    /// </summary>
    [UnityTest]
    public IEnumerator Free_mode_configures_panels_and_buttons()
    {
        rig = SimTestBootstrap.CreateWithUI(satelliteCount: 2, withTMP: true);
        yield return null; // allow UI Initialize paint

        // Controller starts in Track after init—force Free mode for this test.
        rig.Controller.BreakToFreeCam();
        yield return null;

        Assert.IsTrue(rig.UI.placementSelectPanel.activeSelf);
        Assert.IsTrue(rig.UI.objectPlacementPanel.activeSelf || rig.UI.placeTLEPanel.activeSelf);
        Assert.IsFalse(rig.UI.objectInfoPanel.activeSelf);
        Assert.IsFalse(rig.UI.apogeePerigeePanel.activeSelf);
        Assert.IsFalse(rig.UI.timeControlsPanel.activeSelf);
    }

    /// <summary>
    /// Switching to Track mode should enable orbit/telemetry panels and update instruction text.
    /// </summary>
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

    /// <summary>
    /// Earth Cam button label should toggle between "Earth Cam" and "Satellite Cam".
    /// </summary>
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
