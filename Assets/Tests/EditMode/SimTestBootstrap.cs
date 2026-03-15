using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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

        // Required child transforms + a Camera so LateUpdate is safe.
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
        };

        // Newer UI code reads ctx.CameraTracker/ObjectPlacementManager/etc.
        TrySetMember(ctx, "CameraTracker", controller);

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

        // Register bodies BEFORE controller.Initialize so Co_InitializeCamera starts tracking.
        bodyService.Register(earth);
        for (int i = 0; i < satellites.Count; i++)
            bodyService.Register(satellites[i]);

        // Initialize controller last.
        controller.Initialize(ctx);

        return new SimTestRig(
            root,
            ctx,
            controller,
            camMove,
            freeCam,
            bodyService,
            earth,
            satellites,
            null,
            null
        );
    }

    public static SimTestRig CreateWithUI(int satelliteCount = 2, bool withTMP = true)
    {
        var rig = CreateBasic(satelliteCount);

        // Canvas root for all UI test objects.
        var canvasGO = new GameObject(
            "Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        canvasGO.transform.SetParent(rig.Root.transform, false);

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        MakeEventSystem(rig.Root.transform);

        var objectPlacementManager = new GameObject("ObjectPlacementManager").AddComponent<ObjectPlacementManager>();
        objectPlacementManager.transform.SetParent(rig.Root.transform, false);

        var vectorOverlayController = new GameObject("NBodyVectorOverlayController").AddComponent<NBodyVectorOverlayController>();
        vectorOverlayController.transform.SetParent(rig.Root.transform, false);

        TrySetMember(rig.Ctx, "ObjectPlacementManager", objectPlacementManager);
        TrySetMember(rig.Ctx, "CameraTracker", rig.Controller);
        TrySetMember(rig.Ctx, "TutorialController", rig.Ctx.TutorialController);

        var uiRoot = new GameObject("UIRoot").AddComponent<UIRoot>();
        uiRoot.transform.SetParent(canvasGO.transform, false);

        var uiRefs = CreateUiReferences(canvasGO.transform, withTMP);

        SetPrivateMember(uiRoot, "refs", uiRefs);
        SetPrivateMember(uiRoot, "vectorOverlayController", vectorOverlayController);

        TrySetMember(rig.Ctx, "UIRoot", uiRoot);

        uiRoot.Initialize(rig.Ctx);

        return new SimTestRig(
            rig.Root,
            rig.Ctx,
            rig.Controller,
            rig.CamMove,
            rig.FreeCam,
            rig.BodyService,
            rig.Earth,
            rig.Satellites,
            uiRoot,
            uiRefs
        );
    }

    public static T GetUiMember<T>(UIReferences refs, string memberName) where T : class
    {
        if (refs == null) return null;

        const BindingFlags flags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;

        var field = typeof(UIReferences).GetField(memberName, flags);
        if (field != null)
            return field.GetValue(refs) as T;

        var prop = typeof(UIReferences).GetProperty(memberName, flags);
        if (prop != null && prop.GetIndexParameters().Length == 0)
            return prop.GetValue(refs) as T;

        return null;
    }

    private static UIReferences CreateUiReferences(Transform parent, bool withTMP)
    {
        var go = new GameObject("UIReferences");
        go.transform.SetParent(parent, false);

        var refs = go.AddComponent<UIReferences>();

        const BindingFlags flags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;

        foreach (var field in typeof(UIReferences).GetFields(flags))
        {
            if (field.IsInitOnly) continue;
            if (field.Name.Contains("k__BackingField")) continue;

            var value = CreateValueForType(field.FieldType, parent, field.Name, withTMP);
            if (value != null && field.FieldType.IsAssignableFrom(value.GetType()))
                field.SetValue(refs, value);
        }

        foreach (var prop in typeof(UIReferences).GetProperties(flags))
        {
            if (!prop.CanWrite) continue;
            if (prop.GetIndexParameters().Length > 0) continue;

            var value = CreateValueForType(prop.PropertyType, parent, prop.Name, withTMP);
            if (value != null && prop.PropertyType.IsAssignableFrom(value.GetType()))
                prop.SetValue(refs, value);
        }

        return refs;
    }

    private static object CreateValueForType(Type type, Transform parent, string name, bool withTMP)
    {
        if (type == typeof(GameObject))
            return MakePanel(parent, name);

        if (type == typeof(Transform))
            return MakePanel(parent, name).transform;

        if (type == typeof(RectTransform))
            return MakePanel(parent, name).GetComponent<RectTransform>();

        if (type == typeof(Button))
            return MakeButton(parent, name, withTMP);

        if (type == typeof(Toggle))
            return MakeToggle(parent, name);

        if (type == typeof(Slider))
            return MakeSlider(parent, name);

        if (type == typeof(Dropdown))
            return MakeDropdown(parent, name);

        if (type == typeof(TMP_Dropdown))
            return withTMP ? MakeTMPDropdown(parent, name) : null;

        if (type == typeof(InputField))
            return MakeInputField(parent, name);

        if (type == typeof(TMP_InputField))
            return withTMP ? MakeTMPInputField(parent, name) : null;

        if (type == typeof(Text))
            return MakeLegacyText(parent, name);

        if (type == typeof(TMP_Text) || type == typeof(TextMeshProUGUI))
            return withTMP ? MakeTMPText(parent, name) : null;

        if (type == typeof(Image))
            return MakeComponent<Image>(parent, name);

        if (type == typeof(CanvasGroup))
            return MakeComponent<CanvasGroup>(parent, name);

        if (type == typeof(ScrollRect))
            return MakeComponent<ScrollRect>(parent, name);

        if (type == typeof(Scrollbar))
            return MakeComponent<Scrollbar>(parent, name);

        if (type == typeof(RawImage))
            return MakeComponent<RawImage>(parent, name);

        if (type == typeof(Graphic))
            return MakeComponent<Image>(parent, name);

        if (type == typeof(Selectable))
            return MakeButton(parent, name, withTMP);

        if (type.IsArray)
            return Array.CreateInstance(type.GetElementType(), 0);

        if (typeof(Component).IsAssignableFrom(type) && !type.IsAbstract)
            return MakeComponent(parent, name, type);

        if (type == typeof(string))
            return name;

        if (type.IsClass && type.GetConstructor(Type.EmptyTypes) != null)
            return Activator.CreateInstance(type, true);

        return null;
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

    private static Button MakeButton(Transform parent, string name, bool withTMP)
    {
        var go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button)
        );
        go.transform.SetParent(parent, false);

        if (withTMP)
        {
            var textGO = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI)
            );
            textGO.transform.SetParent(go.transform, false);
            textGO.GetComponent<TextMeshProUGUI>().text = name;
        }

        return go.GetComponent<Button>();
    }

    private static Toggle MakeToggle(Transform parent, string name)
    {
        var go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Toggle)
        );
        go.transform.SetParent(parent, false);
        return go.GetComponent<Toggle>();
    }

    private static Slider MakeSlider(Transform parent, string name)
    {
        var go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Slider)
        );
        go.transform.SetParent(parent, false);
        return go.GetComponent<Slider>();
    }

    private static Dropdown MakeDropdown(Transform parent, string name)
    {
        var go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Dropdown)
        );
        go.transform.SetParent(parent, false);
        return go.GetComponent<Dropdown>();
    }

    private static TMP_Dropdown MakeTMPDropdown(Transform parent, string name)
    {
        var go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(TMP_Dropdown)
        );
        go.transform.SetParent(parent, false);
        return go.GetComponent<TMP_Dropdown>();
    }

    private static InputField MakeInputField(Transform parent, string name)
    {
        var go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(InputField)
        );
        go.transform.SetParent(parent, false);
        return go.GetComponent<InputField>();
    }

    private static TMP_InputField MakeTMPInputField(Transform parent, string name)
    {
        var go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(TMP_InputField)
        );
        go.transform.SetParent(parent, false);
        return go.GetComponent<TMP_InputField>();
    }

    private static TextMeshProUGUI MakeTMPText(Transform parent, string name)
    {
        var go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = name;
        return text;
    }

    private static Text MakeLegacyText(Transform parent, string name)
    {
        var go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Text)
        );
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<Text>();
        text.text = name;
        return text;
    }

    private static T MakeComponent<T>(Transform parent, string name) where T : Component
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.AddComponent<T>();
    }

    private static Component MakeComponent(Transform parent, string name, Type componentType)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.AddComponent(componentType);
    }

    private static GameObject MakePanel(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
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

    private static void SetPrivateMember(object target, string memberName, object value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var field = target.GetType().GetField(memberName, flags);
        if (field != null)
        {
            field.SetValue(target, value);
            return;
        }

        var prop = target.GetType().GetProperty(memberName, flags);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(target, value);
        }
    }

    private static void TrySetMember(object target, string memberName, object value)
    {
        if (target == null) return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var field = target.GetType().GetField(memberName, flags);
        if (field != null)
        {
            if (value == null || field.FieldType.IsAssignableFrom(value.GetType()))
                field.SetValue(target, value);
            return;
        }

        var prop = target.GetType().GetProperty(memberName, flags);
        if (prop != null && prop.CanWrite)
        {
            if (value == null || prop.PropertyType.IsAssignableFrom(value.GetType()))
                prop.SetValue(target, value);
        }
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
    public UIRoot UI { get; }
    public UIReferences UIRefs { get; }

    internal SimTestRig(
        GameObject root,
        SimContext ctx,
        CameraController controller,
        CameraMovement camMove,
        FreeCamera freeCam,
        BodyService bodyService,
        NBody earth,
        IReadOnlyList<NBody> sats,
        UIRoot ui,
        UIReferences uiRefs)
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
        UIRefs = uiRefs;
    }

    public void Dispose()
    {
        if (Root != null)
            UnityEngine.Object.DestroyImmediate(Root);
    }
}
