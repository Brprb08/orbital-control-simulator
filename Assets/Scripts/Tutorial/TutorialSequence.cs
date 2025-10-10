using UnityEngine;

/// <summary>
/// Provides the hardcoded tutorial sequence for the Orbital Control Simulator.
/// Each step defines instructional text, required actions, optional interstitial transitions,
/// and auto-advance behavior to guide the player through camera controls, satellite creation,
/// orbit manipulation, and maneuver planning.
/// </summary>
public static class TutorialSequence
{
    public static TutorialStep[] Default()
    {
        return new TutorialStep[]
        {
            // STEP 1 — Welcome
            new TutorialStep {
                body =
                "<b>Welcome</b>\n\n" +
                "Welcome to Orbital Control Simulator! In this tutorial, you’ll learn the basics of placing satellites, applying thrust, and getting comfortable with orbital mechanics.\n\n" +
                "Tip: You can drag this panel around if its in the way.",
                requirements = new RequirementDef[0]
            },

            // STEP 2 — Camera Controls
            new TutorialStep {
                body =
                "<b>Camera Controls</b>\n\n" +
                "• Right Mouse Drag: rotate view\n" +
                "• Scroll Wheel: zoom\n" +
                "• Use them at the same time for more control!",
                requirements = new RequirementDef[] {
                    new RequirementDef { type = RequirementType.RotateViewRMB, label = "Rotate view (hold RMB & move)" },
                    new RequirementDef { type = RequirementType.ZoomScroll,   label = "Zoom with scroll wheel" },
                },
                showInterstitialAfterComplete = true,
                interstitialBody = "Nice! Next we’ll explore camera options like Earth Cam and Free Cam.",
                autoAdvanceFromInterstitial = true,
                autoAdvanceDelay = 4f
            },

             // STEP 3 — Camera Options
            new TutorialStep {
                body =
                "<b>Camera Options</b>\n\n" +
                "• Track Cam: Follows your Satellite\n" +
                "• Free Cam: Allows free movement around Earth\n" +
                "• Earth Cam: Centers Earth in camera for better orbit view\n\n" +
                "• Switch Satellites from the dropdown on the right-hand side.",
                requirements = new RequirementDef[] {
                    new RequirementDef { type = RequirementType.SwitchSatelliteTrack, label = "Switched Satellite" },
                    new RequirementDef { type = RequirementType.SwitchToEarthCam, label = "Earth Cam active" },
                    new RequirementDef { type = RequirementType.SwitchToFreeCam, label = "Free Cam active" },
                },
                showInterstitialAfterComplete = true,
                interstitialBody = "Perfect! Now let’s move around freely using WASD and the mouse.",
                autoAdvanceFromInterstitial = true,
                autoAdvanceDelay = 4f
            },

            // STEP 4 — Free Cam Controls
            new TutorialStep {
                body =
                "<b>Free Cam Controls</b>\n\n" +
                "• WASD: Move forward, left, backward, right\n" +
                "• Hold RMB to look around\n" +
                "• Tip: Movement is always relative to camera direction.",
                requirements = new RequirementDef[] {
                    new RequirementDef { type = RequirementType.PressW, label = "Press W to move Forward" },
                    new RequirementDef { type = RequirementType.PressA, label = "Press A to move Left" },
                    new RequirementDef { type = RequirementType.PressS, label = "Press S to move Backward" },
                    new RequirementDef { type = RequirementType.PressD, label = "Press D to move Right" },
                    new RequirementDef { type = RequirementType.RotateViewRMBFree, label = "Rotate view (hold RMB & move)" },
                },
                showInterstitialAfterComplete = true,
                interstitialBody = "Awesome movement! Next, let’s add your first satellite.",
                autoAdvanceFromInterstitial = true,
                autoAdvanceDelay = 4f
            },

            // STEP 5 — Adding a Satellite
            new TutorialStep {
                body =
                "<b>Adding a Satellite</b>\n\n" +
                "• In Free Cam, click <b>Place Satellite</b> to add a new object in orbit.\n" +
                "• You’ll need to enter mass and radius.\n" +
                "• Name and position are optional.\n" +
                "• If position is blank, the satellite is placed in front of the camera.",
                requirements = new RequirementDef[] {
                    new RequirementDef { type = RequirementType.EnterMass, label = "Mass Entered" },
                    new RequirementDef { type = RequirementType.EnterRadius, label = "Radius Entered" },
                    new RequirementDef { type = RequirementType.PlaceSatellite, label = "Satellite Placed" },
                },
                showInterstitialAfterComplete = true,
                interstitialBody = "Satellite placed! Let's add some velocity to get it moving.",
                autoAdvanceFromInterstitial = true,
                autoAdvanceDelay = 4f
            },

            // STEP 6 — Reading Orbital Data
            new TutorialStep {
                body =
                "<b><u>Adding Velocity</u></b>\n\n" +
                "• Click your satellite and drag to set a <color=red>direction line</color>.\n" +
                "• Notice the <color=orange>trajectory</color> — it falls into Earth with no speed!\n" +
                "• Use the velocity slider (or enter <i>x.x,y.y,z.z</i>) to add speed.\n" +
                "• Watch the orbit update, adjust until it circles Earth.\n" +
                "• Click <b>Set Velocity</b> to launch.",
                requirements = new RequirementDef[] {
                    new RequirementDef { type = RequirementType.ClickSatelliteAndDrag, label = "Click Satellite and set Direction" },
                    new RequirementDef { type = RequirementType.AddVelocity, label = "Add Velocity with Slider(or manually)" },
                    new RequirementDef { type = RequirementType.SetVelocity, label = "Click Set velocity" },
                },
                showInterstitialAfterComplete = true,
                interstitialBody = "Great! Now let's increase time scale to speed up the simulation and watch orbits evolve faster. \n\n" +
                                    "NOTE: You can also place satellites with TLE input if you have the TLE inputs.",
                autoAdvanceFromInterstitial = true,
                autoAdvanceDelay = 4f
            },

            // STEP 7 — Time Scaling
            new TutorialStep {
                body =
                "<b><u>Time Scaling</u></b>\n\n" +
                "• In the top right, you can use the slider to increase time scale.\n" +
                "• Speed up or slow down the simulation.\n" +
                "• 1x to 100x lets you watch long-term orbital effects like drift or decay.",
                requirements = new RequirementDef[] {
                    new RequirementDef { type = RequirementType.ChangedTimeScale, label = "Increase Time Scale" },
                },
                showInterstitialAfterComplete = true,
                interstitialBody = "Now lets add some thrust to your satellite and watch its orbit change.",
                autoAdvanceFromInterstitial = true,
                autoAdvanceDelay = 4f
            },


            // STEP 8 — Applying Thrust
            new TutorialStep {
                body =
                "<b>Applying Thrust</b>\n\n" +
                "• Apply thrust in any direction: prograde, retrograde, radial, or normal.\n" +
                "• Watch your orbital path shift instantly.\n" +
                "• Blue line = new orbit, gray line = old orbit.\n" +
                "• Hint: Higher time scale = stronger visible effect.\n" +
                "• Hint: Heavier Satellite needs more thrust time.",
                requirements = new RequirementDef[] {
                    new RequirementDef { type = RequirementType.ApplyThrust, label = "Use Burn Controls and add thrust" },
                },
                showInterstitialAfterComplete = true,
                interstitialBody = "Great! You’re almost done, let’s cover maneuver nodes.",
                autoAdvanceFromInterstitial = true,
                autoAdvanceDelay = 4f
            },

            // STEP 9 — Maneuver Nodes
            new TutorialStep {
                body =
                "<b>Maneuver Nodes</b>\n\n" +
                "NOTE: These are in an early stage and being improved.\n" +
                "• Click Use Maneuver Nodes to plan future burns.\n" +
                "• Choose burn direction in dropdown then click Setup.\n" +
                "• Move the node with the slider.\n" +
                "• When ready click Place and the node will turn red (Ready)\n" +
                "• The satellite will auto burn when it hits the node.",
                requirements = new RequirementDef[] {
                    new RequirementDef { type = RequirementType.ClickSetupForNode, label = "Choose burn direction and click Setup" },
                    new RequirementDef { type = RequirementType.PlaceManeuverNode, label = "Place Node" },
                },
                showInterstitialAfterComplete = false,
                autoAdvanceFromInterstitial = true,
                autoAdvanceDelay = 4f
            },

            // STEP 10 — Wrap-Up
            new TutorialStep {
                body =
                "<b>Wrap-Up</b>\n\n" +
                "You’ve completed the basics! Experiment with thrusts, nodes, and multiple satellites.\n" +
                "Your goal: master orbital mechanics and keep your satellites from crashing.",
                requirements = new RequirementDef[0],
                showInterstitialAfterComplete = false
            }
        };
    }
}
