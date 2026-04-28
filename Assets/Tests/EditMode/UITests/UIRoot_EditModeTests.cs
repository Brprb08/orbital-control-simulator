using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Edit-mode tests for the new UIRoot-based UI wiring.
/// These replace the old UIManager-centric tests.
/// </summary>
public class UIRoot_EditModeTests
{
    private SimTestRig rig;

    [TearDown]
    public void TearDown()
    {
        rig?.Dispose();
        rig = null;
    }

    [UnityTest]
    public IEnumerator Initialize_builds_all_UI_facades()
    {
        rig = SimTestBootstrap.CreateWithUI(satelliteCount: 2, withTMP: true);
        yield return null;

        Assert.NotNull(rig.UI);
        Assert.NotNull(rig.UIRefs);

        Assert.NotNull(rig.UI.TimeUI);
        Assert.NotNull(rig.UI.TrajectoryUI);
        Assert.NotNull(rig.UI.InstructionsUI);
        Assert.NotNull(rig.UI.VectorUI);
        Assert.NotNull(rig.UI.TutorialUI);
        Assert.NotNull(rig.UI.PlacementUI);
        Assert.NotNull(rig.UI.FlightUI);
        Assert.NotNull(rig.UI.CameraModeUI);
    }

    [UnityTest]
    public IEnumerator FreeCam_and_TrackCam_buttons_drive_camera_mode()
    {
        rig = SimTestBootstrap.CreateWithUI(satelliteCount: 2, withTMP: true);
        yield return null;

        var freeCamButton = SimTestBootstrap.GetUiMember<Button>(rig.UIRefs, "freeCamButton");
        var trackCamButton = SimTestBootstrap.GetUiMember<Button>(rig.UIRefs, "trackCamButton");

        Assert.NotNull(freeCamButton, "UIReferences.freeCamButton was not created.");
        Assert.NotNull(trackCamButton, "UIReferences.trackCamButton was not created.");

        trackCamButton.onClick.Invoke();
        yield return null;

        freeCamButton.onClick.Invoke();
        yield return null;

        Assert.AreEqual(CameraMode.Free, rig.Controller.Mode);

        trackCamButton.onClick.Invoke();
        yield return null;

        Assert.AreNotEqual(CameraMode.Free, rig.Controller.Mode);
    }

    [Test]
    public void SetPlacementButtonsLocked_updates_button_interactable_state()
    {
        rig = SimTestBootstrap.CreateWithUI(satelliteCount: 2, withTMP: true);

        var placementModeButton = SimTestBootstrap.GetUiMember<Button>(rig.UIRefs, "placementModeButton");
        var randomSatelliteButton = SimTestBootstrap.GetUiMember<Button>(rig.UIRefs, "randomSatelliteButton");

        Assert.NotNull(placementModeButton, "UIReferences.placementModeButton was not created.");
        Assert.NotNull(randomSatelliteButton, "UIReferences.randomSatelliteButton was not created.");

        rig.UI.SetPlacementButtonsLocked(true);

        Assert.IsFalse(placementModeButton.interactable);
        Assert.IsFalse(randomSatelliteButton.interactable);

        rig.UI.SetPlacementButtonsLocked(false);

        Assert.IsTrue(placementModeButton.interactable);
        Assert.IsTrue(randomSatelliteButton.interactable);
    }

    [Test]
    public void SetTrackCamButtonInteractable_updates_button_interactable_state()
    {
        rig = SimTestBootstrap.CreateWithUI(satelliteCount: 2, withTMP: true);

        var trackCamButton = SimTestBootstrap.GetUiMember<Button>(rig.UIRefs, "trackCamButton");
        Assert.NotNull(trackCamButton, "UIReferences.trackCamButton was not created.");

        rig.UI.SetTrackCamButtonInteractable(false);
        Assert.IsFalse(trackCamButton.interactable);

        rig.UI.SetTrackCamButtonInteractable(true);
        Assert.IsTrue(trackCamButton.interactable);
    }

    [UnityTest]
    public IEnumerator EnterTrackingMode_returns_from_free_cam()
    {
        rig = SimTestBootstrap.CreateWithUI(satelliteCount: 2, withTMP: true);
        yield return null;

        rig.Controller.BreakToFreeCam();
        yield return null;

        Assert.AreEqual(CameraMode.Free, rig.Controller.Mode);

        rig.UI.EnterTrackingMode();
        yield return null;

        Assert.AreNotEqual(CameraMode.Free, rig.Controller.Mode);
    }

    [UnityTest]
    public IEnumerator Entering_free_cam_disables_free_button_and_enables_track_button()
    {
        rig = SimTestBootstrap.CreateWithUI(satelliteCount: 2, withTMP: true);
        yield return null;

        rig.Controller.BreakToFreeCam();
        rig.UI.RefreshAllUi();
        yield return null;

        var freeCamButton = SimTestBootstrap.GetUiMember<Button>(rig.UIRefs, "freeCamButton");
        var trackCamButton = SimTestBootstrap.GetUiMember<Button>(rig.UIRefs, "trackCamButton");

        Assert.NotNull(freeCamButton);
        Assert.NotNull(trackCamButton);
        Assert.IsFalse(freeCamButton.interactable);
        Assert.IsTrue(trackCamButton.interactable);
    }

    [UnityTest]
    public IEnumerator Free_cam_keeps_time_controls_visible()
    {
        rig = SimTestBootstrap.CreateWithUI(satelliteCount: 2, withTMP: true);
        yield return null;

        rig.Controller.BreakToFreeCam();
        yield return null;

        var timeControlsPanel = SimTestBootstrap.GetUiMember<GameObject>(rig.UIRefs, "timeControlsPanel");
        Assert.NotNull(timeControlsPanel);
        Assert.IsTrue(timeControlsPanel.activeSelf);
    }

    [UnityTest]
    public IEnumerator RefreshAllUi_shows_pending_manual_velocity_state_without_remove_confirmation()
    {
        rig = SimTestBootstrap.CreateWithUI(satelliteCount: 2, withTMP: true);
        yield return null;

        rig.Controller.BreakToFreeCam();
        yield return null;

        var dragManager = new GameObject("VelocityDragManager").AddComponent<VelocityDragManager>();
        dragManager.transform.SetParent(rig.Root.transform, false);
        rig.Ctx.VelocityDragManager = dragManager;

        var pendingPlaceholder = new GameObject("PendingPlaceholder");
        pendingPlaceholder.transform.SetParent(rig.Root.transform, false);
        dragManager.ConfigurePendingPlacement(pendingPlaceholder, 1000f);

        rig.UI.RefreshAllUi();

        var earthButton = SimTestBootstrap.GetUiMember<Button>(rig.UIRefs, "earthView");
        var apogeePerigeePanel = SimTestBootstrap.GetUiMember<GameObject>(rig.UIRefs, "apogeePerigeePanel");
        var toggleOptionsPanel = SimTestBootstrap.GetUiMember<GameObject>(rig.UIRefs, "toggleOptionsPanel");
        var objectPlacementPanel = SimTestBootstrap.GetUiMember<GameObject>(rig.UIRefs, "objectPlacementPanel");
        var placementSelectPanel = SimTestBootstrap.GetUiMember<GameObject>(rig.UIRefs, "placementSelectPanel");
        var freeCamButton = SimTestBootstrap.GetUiMember<Button>(rig.UIRefs, "freeCamButton");
        var trackCamButton = SimTestBootstrap.GetUiMember<Button>(rig.UIRefs, "trackCamButton");
        var removeSatelliteButton = SimTestBootstrap.GetUiMember<Button>(rig.UIRefs, "removeSatellite");
        var confirmRemoveSatPanel = SimTestBootstrap.GetUiMember<GameObject>(rig.UIRefs, "confirmRemoveSatPanel");

        Assert.NotNull(earthButton);
        Assert.NotNull(apogeePerigeePanel);
        Assert.NotNull(toggleOptionsPanel);
        Assert.NotNull(objectPlacementPanel);
        Assert.NotNull(placementSelectPanel);
        Assert.NotNull(freeCamButton);
        Assert.NotNull(trackCamButton);
        Assert.NotNull(removeSatelliteButton);
        Assert.NotNull(confirmRemoveSatPanel);

        confirmRemoveSatPanel.SetActive(true);
        rig.UI.RefreshAllUi();

        Assert.IsTrue(earthButton.gameObject.activeSelf);
        Assert.IsFalse(apogeePerigeePanel.activeSelf);
        Assert.IsFalse(toggleOptionsPanel.activeSelf);
        Assert.IsTrue(objectPlacementPanel.activeSelf);
        Assert.IsTrue(placementSelectPanel.activeSelf);
        Assert.IsTrue(freeCamButton.gameObject.activeSelf);
        Assert.IsTrue(trackCamButton.gameObject.activeSelf);
        Assert.IsFalse(freeCamButton.interactable);
        Assert.IsFalse(trackCamButton.interactable);
        Assert.IsFalse(removeSatelliteButton.gameObject.activeSelf);
        Assert.IsFalse(confirmRemoveSatPanel.activeSelf);

        dragManager.ResetDragManager();
        dragManager.planet = null;
        rig.UI.RefreshAllUi();

        Assert.IsFalse(earthButton.gameObject.activeSelf);
        Assert.IsFalse(apogeePerigeePanel.activeSelf);
        Assert.IsFalse(toggleOptionsPanel.activeSelf);
        Assert.IsTrue(objectPlacementPanel.activeSelf);
        Assert.IsTrue(placementSelectPanel.activeSelf);
    }

    [Test]
    public void SetGameplayUiVisibleForPause_false_hides_runtime_panels()
    {
        rig = SimTestBootstrap.CreateWithUI(satelliteCount: 2, withTMP: true);

        rig.UI.SetGameplayUiVisibleForPause(false);

        AssertPanelInactive("thrustButtons");
        AssertPanelInactive("maneuverNodePanel");
        AssertPanelInactive("burnControlsPanel");
        AssertPanelInactive("attitudeControlPanel");
        AssertPanelInactive("toggleOptionsPanel");
        AssertPanelInactive("dropdown");

        AssertPanelInactive("objectPlacementPanel");
        AssertPanelInactive("placeTLEPanel");
        AssertPanelInactive("placeKeplerPanel");
        AssertPanelInactive("placementSelectPanel");
        AssertPanelInactive("randomPlacementPanel");

        AssertPanelInactive("cameraControls");
        AssertPanelInactive("confirmRemoveSatPanel");
    }

    private void AssertPanelInactive(string memberName)
    {
        var panel = SimTestBootstrap.GetUiMember<GameObject>(rig.UIRefs, memberName);
        Assert.NotNull(panel, $"UIReferences.{memberName} was not created.");
        Assert.IsFalse(panel.activeSelf, $"{memberName} should be inactive.");
    }
}
