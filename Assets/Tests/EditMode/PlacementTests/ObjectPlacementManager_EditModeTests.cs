using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Reflection;
using System.Collections;
using UnityEngine.TestTools;

/// <summary>
/// Edit-mode tests for ObjectPlacementManager:
/// verifies initialization, field clearing, cancel/pending behavior,
/// placement gating, and manual position input / ghost preview behavior.
/// </summary>
public class ObjectPlacementManager_EditModeTests
{
    private SimTestRig rig;
    private ObjectPlacementManager mgr;

    [TearDown]
    public void TearDown()
    {
        rig?.Dispose();
        rig = null;
    }

    private static void SetPrivateField(object obj, string fieldName, object value)
    {
        var f = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(f, $"Field '{fieldName}' not found.");
        f.SetValue(obj, value);
    }

    private static T GetPrivateField<T>(object obj, string fieldName)
    {
        var f = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(f, $"Field '{fieldName}' not found.");
        return (T)f.GetValue(obj);
    }

    private static void InvokePrivate(object obj, string methodName, params object[] args)
    {
        var mi = obj.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(mi, $"Method '{methodName}' not found.");
        mi.Invoke(obj, args);
    }

    private static TMP_InputField MakeInput(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var text = textGo.AddComponent<TextMeshProUGUI>();

        var placeholderGo = new GameObject("Placeholder");
        placeholderGo.transform.SetParent(go.transform, false);
        var placeholder = placeholderGo.AddComponent<TextMeshProUGUI>();

        var input = go.AddComponent<TMP_InputField>();
        input.textComponent = text;
        input.placeholder = placeholder;
        return input;
    }

    private static Button MakeButton(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go.AddComponent<Button>();
    }

    private static TextMeshProUGUI MakeText(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<TextMeshProUGUI>();
    }

    private static EventSystem MakeEventSystem(Transform parent)
    {
        var go = new GameObject("EventSystem");
        go.transform.SetParent(parent, false);
        var es = go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
        return es;
    }

    private void BuildManager(bool withUIRig = true)
    {
        rig = withUIRig ? SimTestBootstrap.CreateWithUI(1) : SimTestBootstrap.CreateBasic(1);

        if (EventSystem.current == null)
            MakeEventSystem(rig.Root.transform);

        mgr = new GameObject("ObjectPlacementManager").AddComponent<ObjectPlacementManager>();
        mgr.transform.SetParent(rig.Root.transform, false);

        SetPrivateField(mgr, "_mainCamera", rig.CamMove.MainCamera);
        SetPrivateField(mgr, "_tutorialController", rig.Ctx.TutorialController);

        // Minimal prefab for ghost preview
        var ghostPrefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ghostPrefab.name = "GhostPrefab";
        ghostPrefab.SetActive(false);
        SetPrivateField(mgr, "_ghostPreviewPrefab", ghostPrefab);

        var spawner = new GameObject("Spawner").AddComponent<SatelliteSpawner>();
        spawner.transform.SetParent(rig.Root.transform, false);
        SetPrivateField(mgr, "_satelliteSpawner", spawner);

        var vdm = new GameObject("VelocityDragManager").AddComponent<VelocityDragManager>();
        vdm.transform.SetParent(rig.Root.transform, false);
        SetPrivateField(mgr, "_velocityDragManager", vdm);

        rig.Ctx.ObjectPlacementManager = mgr;

        mgr.Initialize(rig.Ctx);

        Object.DestroyImmediate(ghostPrefab);
    }

    [Test]
    public void Initialize_creates_hidden_ghost_instance()
    {
        BuildManager();

        var ghost = GetPrivateField<GameObject>(mgr, "_ghostInstance");
        Assert.NotNull(ghost);
        Assert.IsFalse(ghost.activeSelf);
    }

    [UnityTest]
    public IEnumerator ClearAllFields_empties_manual_kepler_and_tle_inputs()
    {
        BuildManager();

        yield return null; // let EventSystem.current initialize

        rig.UIRefs.nameInputField.text = "Sat";
        rig.UIRefs.massInputField.text = "5000";
        rig.UIRefs.radiusInputField.text = "1,1,1";
        rig.UIRefs.positionInputField.text = "700,0,0";

        rig.UIRefs.kepNameInputField.text = "KSat";
        rig.UIRefs.kepMassInputField.text = "5000";

        rig.UIRefs.tleNameInputField.text = "TSat";
        rig.UIRefs.tleMassInputField.text = "5000";
        rig.UIRefs.tleLine1InputField.text = "L1";
        rig.UIRefs.tleLine2InputField.text = "L2";

        mgr.ClearAllFields();

        Assert.AreEqual("", rig.UIRefs.nameInputField.text);
        Assert.AreEqual("", rig.UIRefs.massInputField.text);
        Assert.AreEqual("", rig.UIRefs.radiusInputField.text);
        Assert.AreEqual("", rig.UIRefs.positionInputField.text);

        Assert.AreEqual("", rig.UIRefs.kepNameInputField.text);
        Assert.AreEqual("", rig.UIRefs.kepMassInputField.text);

        Assert.AreEqual("", rig.UIRefs.tleNameInputField.text);
        Assert.AreEqual("", rig.UIRefs.tleMassInputField.text);
        Assert.AreEqual("", rig.UIRefs.tleLine1InputField.text);
        Assert.AreEqual("", rig.UIRefs.tleLine2InputField.text);
    }

    [Test]
    public void CancelPlacement_destroys_pending_satellite_and_clears_feedback()
    {
        BuildManager();

        var placed = new GameObject("Placed");
        placed.transform.SetParent(rig.Root.transform, false);
        var pending = new PendingSatellitePlacement();
        pending.Set(placed);
        SetPrivateField(mgr, "_pendingPlacement", pending);

        var feedback = rig.UIRefs.feedbackText;
        feedback.text = "hello";

        mgr.CancelPlacement();

        Assert.IsFalse(GetPrivateField<PendingSatellitePlacement>(mgr, "_pendingPlacement").HasSatellite);
        Assert.AreEqual("", feedback.text);
    }

    [Test]
    public void ClearPendingPlacement_clears_pending_reference_and_feedback()
    {
        BuildManager();

        var pending = new PendingSatellitePlacement();
        var placed = new GameObject("Pending");
        placed.transform.SetParent(rig.Root.transform, false);
        pending.Set(placed);
        SetPrivateField(mgr, "_pendingPlacement", pending);

        var feedback = rig.UIRefs.feedbackText;
        feedback.text = "pending";

        mgr.ClearPendingPlacement();

        Assert.IsFalse(GetPrivateField<PendingSatellitePlacement>(mgr, "_pendingPlacement").HasSatellite);
        Assert.AreEqual("", feedback.text);
    }

    [Test]
    public void StartPlacement_fails_when_not_in_freecam()
    {
        BuildManager();

        rig.Controller.ReturnToTracking(); // should be Track
        var feedback = rig.UIRefs.feedbackText;

        mgr.StartPlacement();

        Assert.That(feedback.text, Does.Contain("Switch to FreeCam"));
    }

    [Test]
    public void StartPlacement_fails_when_previous_placeholder_still_pending()
    {
        BuildManager();

        rig.Controller.BreakToFreeCam();
        var pending = new PendingSatellitePlacement();
        var placed = new GameObject("PendingSat");
        placed.transform.SetParent(rig.Root.transform, false);
        pending.Set(placed);
        SetPrivateField(mgr, "_pendingPlacement", pending);

        var feedback = rig.UIRefs.feedbackText;
        mgr.StartPlacement();

        Assert.That(feedback.text, Does.Contain("Finish setting velocity"));
    }

    [Test]
    public void OnPositionInputChanged_with_invalid_text_hides_ghost_and_sets_feedback()
    {
        BuildManager();

        InvokePrivate(mgr, "OnPositionInputChanged", "not-a-vector");

        var ghost = GetPrivateField<GameObject>(mgr, "_ghostInstance");
        var feedback = rig.UIRefs.feedbackText;

        Assert.IsFalse(ghost.activeSelf);
        Assert.That(feedback.text, Does.Contain("Invalid Position"));
    }

    [Test]
    public void OnPositionInputChanged_with_out_of_bounds_position_hides_ghost_and_sets_feedback()
    {
        BuildManager();

        InvokePrivate(mgr, "OnPositionInputChanged", "100,0,0"); // below 638

        var ghost = GetPrivateField<GameObject>(mgr, "_ghostInstance");
        var feedback = rig.UIRefs.feedbackText;

        Assert.IsFalse(ghost.activeSelf);
        Assert.That(feedback.text, Does.Contain("Position magnitude must be between"));
    }

    [Test]
    public void OnPositionInputChanged_with_valid_position_shows_ghost_at_position()
    {
        BuildManager();

        InvokePrivate(mgr, "OnPositionInputChanged", "700,0,0");

        var ghost = GetPrivateField<GameObject>(mgr, "_ghostInstance");
        var feedback = rig.UIRefs.feedbackText;
        var ghostPlaced = GetPrivateField<bool>(mgr, "_ghostObjectPlaced");

        Assert.IsTrue(ghost.activeSelf);
        Assert.IsTrue(ghostPlaced);
        Assert.AreEqual(new Vector3(700f, 0f, 0f), ghost.transform.position);
        Assert.AreEqual("", feedback.text);
    }

    [Test]
    public void OnPositionInputChanged_with_empty_input_hides_ghost_and_clears_feedback()
    {
        BuildManager();

        InvokePrivate(mgr, "OnPositionInputChanged", "700,0,0");
        InvokePrivate(mgr, "OnPositionInputChanged", "");

        var ghost = GetPrivateField<GameObject>(mgr, "_ghostInstance");
        var feedback = rig.UIRefs.feedbackText;

        Assert.IsFalse(ghost.activeSelf);
        Assert.AreEqual("", feedback.text);
    }
}
