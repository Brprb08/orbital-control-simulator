using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Tests-only scene builder for integration-style EditMode tests.
/// Creates a minimal SimContext and core components, registers a central Earth and optional satellites,
/// wires dependencies similarly to runtime bootstrap, and returns a disposable SimTestRig.
/// </summary>
public static class SimTestBootstrap
{
    public static SimTestRig CreateBasic(int satelliteCount = 2, bool ensureSatelliteTag = true)
    {
        var root = new GameObject($"TestRig_{Guid.NewGuid():N}");

        var controller = new GameObject("CameraController").AddComponent<CameraController>();
        controller.transform.SetParent(root.transform, false);

        var camMove = new GameObject("CameraMovement").AddComponent<CameraMovement>();
        camMove.transform.SetParent(root.transform, false);

        // Required child transforms + a Camera so LateUpdate is safe
        var pivot = new GameObject("Pivot").transform;
        var camT = new GameObject("Cam").transform;
        pivot.SetParent(camMove.transform, false);
        camT.SetParent(camMove.transform, false);
        camMove.cameraPivotTransform = pivot;
        camMove.cameraTransform = camT;
        camT.gameObject.AddComponent<Camera>();

        var freeCam = new GameObject("FreeCamera").AddComponent<FreeCamera>();
        freeCam.transform.SetParent(root.transform, false);

        var bodyService = new GameObject("BodyService").AddComponent<BodyService>();
        bodyService.transform.SetParent(root.transform, false);

        // TutorialController (CameraMovement.SetTargetEarth() touches it)
        var tut = new GameObject("TutorialController").AddComponent<TutorialController>();
        tut.transform.SetParent(root.transform, false);
        tut.inTutorialMode = false;

        var ctx = new SimContext
        {
            CameraController = controller,
            CameraMovement = camMove,
            FreeCamera = freeCam,
            BodyService = bodyService,
            TutorialController = tut,
            UIManager = null
        };

        // Initialize deps (order mirrors runtime)
        bodyService.Initialize(ctx);
        camMove.Initialize(ctx);
        freeCam.Initialize(ctx);

        var earth = MakeCentralBody(root.transform, "Earth", radius: 637f, camRadius: 637f);

        var satellites = new List<NBody>();
        for (int i = 0; i < satelliteCount; i++)
        {
            var sat = MakeSatelliteBody(
                root.transform,
                $"Sat{i + 1}",
                radius: 5f + i,
                camRadius: 10f + i,
                ensureSatelliteTag: ensureSatelliteTag
            );
            satellites.Add(sat);
        }

        // Register bodies BEFORE controller.Initialize so Co_InitializeCamera starts tracking
        bodyService.Register(earth);
        for (int i = 0; i < satellites.Count; i++) bodyService.Register(satellites[i]);

        // Initialize controller last
        controller.Initialize(ctx);

        return new SimTestRig(root, ctx, controller, camMove, freeCam, bodyService, earth, satellites, null);
    }

    public static SimTestRig CreateWithUI(int satelliteCount = 2, bool withTMP = true)
    {
        var rig = CreateBasic(satelliteCount);

        var ui = new GameObject("UIManager").AddComponent<UIManager>();
        ui.transform.SetParent(rig.Root.transform, false);

        ui.freeCamButton = MakeButton(rig.Root.transform, "FreeBtn");
        ui.trackCamButton = MakeButton(rig.Root.transform, "TrackBtn");
        ui.instructionsButton = MakeButton(rig.Root.transform, "InstructionsBtn");
        ui.placementModeButton = MakeButton(rig.Root.transform, "PlacementModeBtn");
        ui.placeObjectButton = MakeButton(rig.Root.transform, "PlaceObjectBtn");
        ui.burnControlButton = MakeButton(rig.Root.transform, "BurnControlBtn");
        ui.randomSatelliteButton = MakeButton(rig.Root.transform, "RandomSatelliteBtn");
        ui.removePreManeuverLineButton = MakeButton(rig.Root.transform, "RemovePreManeuverBtn");

        ui.objectPlacementPanel = MakePanel(rig.Root.transform, "Placement");
        ui.objectInfoPanel = MakePanel(rig.Root.transform, "Info");
        ui.thrustButtons = MakePanel(rig.Root.transform, "ThrustButtons");
        ui.maneuverNodePanel = MakePanel(rig.Root.transform, "Maneuver");
        ui.burnControlsPanel = MakePanel(rig.Root.transform, "BurnControls");
        ui.apogeePerigeePanel = MakePanel(rig.Root.transform, "ApPerPanel");
        ui.timeControlsPanel = MakePanel(rig.Root.transform, "TimeControls");
        ui.instructionsPanel = MakePanel(rig.Root.transform, "InstructionsPanel");
        ui.toggleOptionsPanel = MakePanel(rig.Root.transform, "ToggleOptions");
        ui.dropdown = MakePanel(rig.Root.transform, "Dropdown");
        ui.placeTLEPanel = MakePanel(rig.Root.transform, "PlaceTLE");
        ui.placementSelectPanel = MakePanel(rig.Root.transform, "PlacementSelect");
        ui.cameraControls = MakePanel(rig.Root.transform, "CameraControls");
        ui.attitudeControlPanel = MakePanel(rig.Root.transform, "AttitudeController");

        if (withTMP)
        {
            ui.earthCamButtonText = new GameObject("EarthCamText").AddComponent<TextMeshProUGUI>();
            ui.instructionText = new GameObject("InstructionText").AddComponent<TextMeshProUGUI>();
            ui.deltaVText = new GameObject("DeltaVText").AddComponent<TextMeshProUGUI>();
            ui.apogeeText = new GameObject("ApogeeText").AddComponent<TextMeshProUGUI>();
            ui.perigeeText = new GameObject("PerigeeText").AddComponent<TextMeshProUGUI>();
            ui.semiMajorAxisText = new GameObject("SMA").AddComponent<TextMeshProUGUI>();
            ui.eccentricityText = new GameObject("Ecc").AddComponent<TextMeshProUGUI>();
            ui.orbitalPeriodText = new GameObject("Period").AddComponent<TextMeshProUGUI>();
            ui.inclinationText = new GameObject("Incl").AddComponent<TextMeshProUGUI>();
            ui.raanText = new GameObject("RAAN").AddComponent<TextMeshProUGUI>();
        }

        rig.Ctx.UIManager = ui;

        // Initialize UI last (it will subscribe to ctx.CameraTracker = controller)
        ui.Initialize(rig.Ctx);

        return new SimTestRig(rig.Root, rig.Ctx, rig.Controller, rig.CamMove, rig.FreeCam, rig.BodyService, rig.Earth, rig.Satellites, ui);
    }

    private static NBody MakeCentralBody(Transform parent, string name, float radius, float camRadius)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.tag = "Untagged";

        var nb = go.AddComponent<NBody>();
        nb.isCentralBody = true;
        nb.radius = radius;
        nb.cameraDistanceRadius = camRadius;
        return nb;
    }

    private static NBody MakeSatelliteBody(Transform parent, string name, float radius, float camRadius, bool ensureSatelliteTag)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.tag = ensureSatelliteTag ? "Satellite" : "Untagged";

        var nb = go.AddComponent<NBody>();
        nb.isCentralBody = false;
        nb.radius = radius;
        nb.cameraDistanceRadius = camRadius;
        return nb;
    }

    private static Button MakeButton(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var btn = go.AddComponent<Button>();

        var text = new GameObject("Text");
        text.transform.SetParent(go.transform, false);
        text.AddComponent<TextMeshProUGUI>();
        return btn;
    }

    private static GameObject MakePanel(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    public static EventSystem MakeEventSystem(Transform parent)
    {
        var go = new GameObject("EventSystem");
        go.transform.SetParent(parent, false);
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
        return go.GetComponent<EventSystem>();
    }
}

public sealed class SimTestRig : IDisposable
{
    public GameObject Root { get; }
    public SimContext Ctx { get; }
    public CameraController Controller { get; }
    public CameraMovement CamMove { get; }
    public FreeCamera FreeCam { get; }
    public BodyService BodyService { get; }
    public NBody Earth { get; }
    public IReadOnlyList<NBody> Satellites { get; }
    public UIManager UI { get; }

    internal SimTestRig(GameObject root, SimContext ctx, CameraController controller, CameraMovement camMove, FreeCamera freeCam,
                        BodyService bodyService, NBody earth, IReadOnlyList<NBody> sats, UIManager ui)
    {
        Root = root;
        Ctx = ctx;
        Controller = controller;
        CamMove = camMove;
        FreeCam = freeCam;
        BodyService = bodyService;
        Earth = earth;
        Satellites = sats;
        UI = ui;
    }

    public void Dispose()
    {
        if (Root != null)
            UnityEngine.Object.DestroyImmediate(Root);
    }
}