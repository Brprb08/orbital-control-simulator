using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Mathematics;
using System.Reflection;

/// <summary>
/// Edit-mode tests for VelocityDragManager:
/// verifies initialization, speed/manual input handling,
/// applying velocity to a placeholder, and reset/clear flows.
/// </summary>
public class VelocityDragManager_EditModeTests
{
    private SimTestRig rig;
    private VelocityDragManager mgr;

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

    private static Slider MakeSlider(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go.AddComponent<Slider>();
    }

    private static Button MakeButton(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go.AddComponent<Button>();
    }

    private void BuildManager()
    {
        rig = SimTestBootstrap.CreateBasic(1);

        mgr = new GameObject("VelocityDragManager").AddComponent<VelocityDragManager>();
        mgr.transform.SetParent(rig.Root.transform, false);

        SetPrivateField(mgr, "_mainCamera", rig.CamMove.MainCamera);
        SetPrivateField(mgr, "_tutorialController", rig.Ctx.TutorialController);

        SetPrivateField(mgr, "_velocityInputField", MakeInput(rig.Root.transform, "VelocityInput"));
        SetPrivateField(mgr, "_speedSlider", MakeSlider(rig.Root.transform, "SpeedSlider"));
        SetPrivateField(mgr, "_setVelocityButton", MakeButton(rig.Root.transform, "SetVelocityBtn"));
        SetPrivateField(mgr, "_feedbackText", new GameObject("Feedback").AddComponent<TextMeshProUGUI>());

        rig.Ctx.VelocityDragManager = mgr;

        mgr.Initialize(rig.Ctx);
    }

    [Test]
    public void Initialize_disables_velocity_ui_and_creates_drag_helpers()
    {
        BuildManager();

        var input = GetPrivateField<TMP_InputField>(mgr, "_velocityInputField");
        var slider = GetPrivateField<Slider>(mgr, "_speedSlider");
        var button = GetPrivateField<Button>(mgr, "_setVelocityButton");
        var dragSphere = GetPrivateField<GameObject>(mgr, "_dragSphereObject");
        var dragArrow = GetPrivateField<RuntimeArrow>(mgr, "_dragArrow");

        Assert.IsFalse(input.interactable);
        Assert.IsFalse(slider.interactable);
        Assert.IsFalse(button.interactable);
        Assert.NotNull(dragSphere);
        Assert.IsFalse(dragSphere.activeSelf);
        Assert.NotNull(dragArrow);
    }

    [Test]
    public void OnSpeedSliderChanged_updates_velocity_text_from_drag_direction()
    {
        BuildManager();

        var input = GetPrivateField<TMP_InputField>(mgr, "_velocityInputField");

        SetPrivateField(mgr, "_dragDirection", Vector3.forward);
        SetPrivateField(mgr, "_currentVelocity", Vector3.zero);

        mgr.OnSpeedSliderChanged(2f);

        Assert.That(input.text, Does.Contain("0.00"));
        Assert.That(input.text, Does.Contain("20.00"));
    }

    [Test]
    public void OnVelocityInputChanged_parses_manual_velocity_and_enables_apply_button()
    {
        BuildManager();

        var button = GetPrivateField<Button>(mgr, "_setVelocityButton");

        var mi = typeof(VelocityDragManager).GetMethod("OnVelocityInputChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(mi);
        mi.Invoke(mgr, new object[] { "1,2,3" });

        Assert.IsTrue(button.interactable);
    }

    [Test]
    public void ApplyVelocityToPlanet_adds_nbody_if_missing_and_registers_body()
    {
        BuildManager();

        var planet = new GameObject("PlaceholderPlanet");
        planet.transform.SetParent(rig.Root.transform, false);

        mgr.planet = planet;
        mgr.placeholderMass = 12345f;

        int beforeCount = rig.BodyService.Bodies.Count;

        mgr.ApplyVelocityToPlanet(new Vector3(1f, 2f, 3f));

        var nbody = planet.GetComponent<NBody>();
        var attitude = planet.GetComponent<AttitudeController>();

        Assert.NotNull(nbody);
        Assert.NotNull(attitude);
        Assert.AreEqual(new Vector3(1f, 2f, 3f), nbody.velocity);
        Assert.AreEqual(12345f, nbody.mass);
        Assert.AreEqual(beforeCount + 1, rig.BodyService.Bodies.Count);
        Assert.IsTrue(mgr.HasAppliedVelocity);
    }

    [Test]
    public void ApplyVelocityToPlanet_uses_default_mass_when_placeholder_mass_not_set()
    {
        BuildManager();

        var planet = new GameObject("PlaceholderPlanet");
        planet.transform.SetParent(rig.Root.transform, false);

        mgr.planet = planet;
        mgr.placeholderMass = 0f;

        mgr.ApplyVelocityToPlanet(Vector3.forward);

        var nbody = planet.GetComponent<NBody>();
        Assert.NotNull(nbody);
        Assert.AreEqual(400000f, nbody.mass);
        Assert.AreEqual(400000f, (float)nbody.trueMass);
    }

    [Test]
    public void ApplyVelocityToPlanet_clears_planet_and_disables_ui_after_apply()
    {
        BuildManager();

        var input = GetPrivateField<TMP_InputField>(mgr, "_velocityInputField");
        var slider = GetPrivateField<Slider>(mgr, "_speedSlider");
        var button = GetPrivateField<Button>(mgr, "_setVelocityButton");

        input.interactable = true;
        slider.interactable = true;
        button.interactable = true;
        input.text = "1,2,3";
        slider.value = 2f;

        var planet = new GameObject("PlaceholderPlanet");
        planet.transform.SetParent(rig.Root.transform, false);
        mgr.planet = planet;

        mgr.ApplyVelocityToPlanet(Vector3.forward);

        Assert.IsNull(mgr.planet);
        Assert.IsFalse(input.interactable);
        Assert.IsFalse(slider.interactable);
        Assert.IsFalse(button.interactable);
        Assert.AreEqual("", input.text);
        Assert.AreEqual(0f, slider.value);
    }

    [Test]
    public void ApplyVelocityToPlanet_tracks_new_body_after_apply()
    {
        BuildManager();

        var planet = new GameObject("PlaceholderPlanet");
        planet.transform.SetParent(rig.Root.transform, false);
        mgr.planet = planet;

        mgr.ApplyVelocityToPlanet(Vector3.forward);

        var nbody = planet.GetComponent<NBody>();
        Assert.That(rig.Controller.CurrentBody, Is.EqualTo(nbody));
    }

    [Test]
    public void ClearManualArtifacts_resets_drag_state_and_disables_ui()
    {
        BuildManager();

        var input = GetPrivateField<TMP_InputField>(mgr, "_velocityInputField");
        var slider = GetPrivateField<Slider>(mgr, "_speedSlider");
        var button = GetPrivateField<Button>(mgr, "_setVelocityButton");
        var dragSphere = GetPrivateField<GameObject>(mgr, "_dragSphereObject");

        input.interactable = true;
        input.text = "1,2,3";
        slider.interactable = true;
        slider.value = 3f;
        button.interactable = true;
        dragSphere.SetActive(true);
        mgr.planet = new GameObject("Planet");

        mgr.ClearManualArtifacts();

        Assert.IsFalse(input.interactable);
        Assert.AreEqual("", input.text);
        Assert.IsFalse(slider.interactable);
        Assert.AreEqual(0f, slider.value);
        Assert.IsFalse(button.interactable);
        Assert.IsFalse(dragSphere.activeSelf);
        Assert.IsNull(mgr.planet);
        Assert.IsFalse(mgr.HasAppliedVelocity);
    }

    [Test]
    public void ResetDragManager_reenables_velocity_input_and_clears_applied_flag()
    {
        BuildManager();

        var input = GetPrivateField<TMP_InputField>(mgr, "_velocityInputField");
        input.interactable = false;

        SetPrivateField(mgr, "_isVelocitySet", true);

        mgr.ResetDragManager();

        Assert.IsTrue(input.interactable);
        Assert.IsFalse(mgr.HasAppliedVelocity);
    }

    [Test]
    public void ApplyVelocityToPlanet_noop_when_planet_is_null()
    {
        BuildManager();

        Assert.DoesNotThrow(() => mgr.ApplyVelocityToPlanet(Vector3.one));
        Assert.IsFalse(mgr.HasAppliedVelocity);
    }
}