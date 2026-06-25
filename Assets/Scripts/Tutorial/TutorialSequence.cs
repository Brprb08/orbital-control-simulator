/// <summary>
/// Provides the tutorial sequence for the current main user flow:
/// camera control, manual placement, velocity staging, launch, thrust, and maneuver nodes.
/// </summary>
public static class TutorialSequence
{
    public static TutorialStep[] Default()
    {
        return new TutorialStep[]
        {
            new TutorialStep
            {
                body =
                "<b>Welcome</b>\n\n" +
                "This tutorial follows the main flow of the simulator: inspect an orbit, create a satellite, stage its launch velocity, track it, and plan a burn.\n\n" +
                "- Click Minimize to hide this panel without losing your place.\n" +
                "- Click Open Tutorial to bring it back.\n" +
                "- Drag this tutorial panel to move it.",
                requirements = new RequirementDef[0]
            },

            new TutorialStep
            {
                body =
                "<b>Track Camera Basics</b>\n\n" +
                "Start by getting comfortable with the orbit camera.\n\n" +
                "- Hold right mouse and drag to rotate.\n" +
                "- Scroll to zoom in or out.\n" +
                "- The blue trajectory shows the currently tracked satellite.",
                requirements = new RequirementDef[]
                {
                    new RequirementDef { type = RequirementType.RotateViewRMB, label = "Rotate the view with right mouse" },
                    new RequirementDef { type = RequirementType.ZoomScroll, label = "Zoom with the scroll wheel" },
                },
                showInterstitialAfterComplete = true,
                interstitialBody = "Good. Next, try the camera modes so you know where to go when placing or inspecting satellites.",
                autoAdvanceFromInterstitial = true,
                autoAdvanceDelay = 3f
            },

            new TutorialStep
            {
                body =
                "<b>Camera Modes</b>\n\n" +
                "The main camera modes are useful at different moments.\n\n" +
                "- Track Cam follows the selected satellite.\n" +
                "- Earth Cam frames the whole orbit.\n" +
                "- Free Cam lets you move around before placing objects.\n" +
                "- If more than one satellite exists, the dropdown switches the tracked satellite.",
                requirements = new RequirementDef[]
                {
                    new RequirementDef { type = RequirementType.SwitchToEarthCam, label = "Switch to Earth Cam" },
                    new RequirementDef { type = RequirementType.SwitchToFreeCam, label = "Switch to Free Cam" },
                },
                showInterstitialAfterComplete = true,
                interstitialBody = "Nice. While in Free Cam, you can move around the scene directly.",
                autoAdvanceFromInterstitial = true,
                autoAdvanceDelay = 3f
            },

            new TutorialStep
            {
                body =
                "<b>Free Cam Movement</b>\n\n" +
                "Use Free Cam to position your view before placing a satellite.\n\n" +
                "- W/A/S/D moves relative to the camera.\n" +
                "- Hold right mouse to look around.",
                requirements = new RequirementDef[]
                {
                    new RequirementDef { type = RequirementType.PressW, label = "Press W" },
                    new RequirementDef { type = RequirementType.PressA, label = "Press A" },
                    new RequirementDef { type = RequirementType.PressS, label = "Press S" },
                    new RequirementDef { type = RequirementType.PressD, label = "Press D" },
                    new RequirementDef { type = RequirementType.RotateViewRMBFree, label = "Look around with right mouse" },
                },
                showInterstitialAfterComplete = true,
                interstitialBody = "Good movement. Now create a placeholder satellite.",
                autoAdvanceFromInterstitial = true,
                autoAdvanceDelay = 3f
            },

            new TutorialStep
            {
                body =
                "<b>Create A Satellite</b>\n\n" +
                "In Free Cam, open the placement controls and create a satellite.\n\n" +
                "- Enter mass and radius.\n" +
                "- Enter a position such as 700,0,0 so the preview location is intentional.\n" +
                "- Click Place Satellite to create the ghost satellite.\n\n" +
                "After placement, the simulator switches to Earth Cam for launch preview.",
                requirements = new RequirementDef[]
                {
                    new RequirementDef { type = RequirementType.EnterPosition, label = "Enter a valid position" },
                    new RequirementDef { type = RequirementType.EnterMass, label = "Enter a valid mass" },
                    new RequirementDef { type = RequirementType.EnterRadius, label = "Enter a valid radius" },
                    new RequirementDef { type = RequirementType.PlaceSatellite, label = "Place the satellite" },
                },
                showInterstitialAfterComplete = true,
                interstitialBody = "The ghost satellite is ready. Next, stage a launch velocity before making it real.",
                autoAdvanceFromInterstitial = true,
                autoAdvanceDelay = 3f
            },

            new TutorialStep
            {
                body =
                "<b>Stage Launch Velocity</b>\n\n" +
                "Use the launch preview controls to choose the initial orbit.\n\n" +
                "- Circularize stages a stable starting velocity.\n" +
                "- Prograde and retrograde choose the base direction.\n" +
                "- Raise Apogee and Lower Perigee shape the orbit.\n" +
                "- Radial and tilt buttons bend the preview.\n" +
                "- The speed slider trims power after a direction is staged.\n\n" +
                "Watch the launch preview and orbit readout, then click Set Velocity.",
                requirements = new RequirementDef[]
                {
                    new RequirementDef { type = RequirementType.AddVelocity, label = "Stage or adjust launch velocity" },
                    new RequirementDef { type = RequirementType.SetVelocity, label = "Click Set Velocity" },
                },
                showInterstitialAfterComplete = true,
                interstitialBody = "Satellite launched. It is now tracked like a normal satellite.",
                autoAdvanceFromInterstitial = true,
                autoAdvanceDelay = 3f
            },

            new TutorialStep
            {
                body =
                "<b>Watch The Orbit</b>\n\n" +
                "Use the orbit readout and trajectory lines to inspect the satellite.\n\n" +
                "- Earth Cam is useful for full-orbit shape.\n" +
                "- Track Cam is useful for close inspection.\n" +
                "- The apogee and perigee readout updates as the orbit changes.\n\n" +
                "Increase the time scale to watch the orbit evolve faster.",
                requirements = new RequirementDef[]
                {
                    new RequirementDef { type = RequirementType.ChangedTimeScale, label = "Change the time scale" },
                },
                showInterstitialAfterComplete = true,
                interstitialBody = "Time controls are working. Next, use thrust to change the orbit live.",
                autoAdvanceFromInterstitial = true,
                autoAdvanceDelay = 3f
            },

            new TutorialStep
            {
                body =
                "<b>Manual Thrust</b>\n\n" +
                "Burn controls apply thrust to the tracked satellite.\n\n" +
                "- Choose an attitude direction.\n" +
                "- Apply thrust and watch the trajectory update.\n" +
                "- Heavier satellites need more thrust or more burn time.\n\n" +
                "Manual thrust is good for experimenting. Maneuver nodes are better for planning.",
                requirements = new RequirementDef[]
                {
                    new RequirementDef { type = RequirementType.ApplyThrust, label = "Apply thrust to the tracked satellite" },
                },
                showInterstitialAfterComplete = true,
                interstitialBody = "Good burn. Now plan a future burn with a maneuver node.",
                autoAdvanceFromInterstitial = true,
                autoAdvanceDelay = 3f
            },

            new TutorialStep
            {
                body =
                "<b>Maneuver Nodes</b>\n\n" +
                "Maneuver nodes let you preview a future burn before committing to it.\n\n" +
                "- Pick a burn direction.\n" +
                "- Click setup to create a preview node.\n" +
                "- Adjust timing, burn duration, and thrust scale.\n" +
                "- Place the node to finalize the planned burn.",
                requirements = new RequirementDef[]
                {
                    new RequirementDef { type = RequirementType.ClickSetupForNode, label = "Create a maneuver preview" },
                    new RequirementDef { type = RequirementType.PlaceManeuverNode, label = "Place/finalize the maneuver node" },
                },
                showInterstitialAfterComplete = true,
                interstitialBody = "The node is finalized. When simulation time reaches it, the burn will execute automatically.",
                autoAdvanceFromInterstitial = true,
                autoAdvanceDelay = 3f
            },

            new TutorialStep
            {
                body =
                "<b>Done</b>\n\n" +
                "You have completed the main loop: create satellite, set velocity, track it, modify the orbit, and plan a burn.\n\n" +
                "From here, experiment with different launch shapes, inclinations, and maneuver directions.",
                requirements = new RequirementDef[0],
                showInterstitialAfterComplete = false
            }
        };
    }
}
