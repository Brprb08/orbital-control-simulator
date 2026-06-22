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
    private static readonly Vector3 TestRadiusMeters = Vector3.one * 20f;

    private SimTestRig rig;
    private VelocityDragManager mgr;
    private ManualOrbitReadout.References manualOrbitRefs;

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

    private static TextMeshProUGUI MakeText(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<TextMeshProUGUI>();
    }

    private ManualOrbitReadout.References MakeManualOrbitRefs()
    {
        var refs = new ManualOrbitReadout.References();
        SetPrivateField(refs, "panel", new GameObject("ManualOrbitPanel"));
        refs.Panel.transform.SetParent(rig.Root.transform, false);
        SetPrivateField(refs, "apogeeText", MakeText(rig.Root.transform, "Apogee"));
        SetPrivateField(refs, "perigeeText", MakeText(rig.Root.transform, "Perigee"));
        SetPrivateField(refs, "inclinationText", MakeText(rig.Root.transform, "Inclination"));
        SetPrivateField(refs, "eccentricityText", MakeText(rig.Root.transform, "Eccentricity"));
        return refs;
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
        manualOrbitRefs = MakeManualOrbitRefs();
        SetPrivateField(mgr, "_manualOrbitReadoutRefs", manualOrbitRefs);

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
        var planet = new GameObject("PlaceholderPlanet");
        planet.transform.SetParent(rig.Root.transform, false);
        mgr.ConfigurePendingPlacement(planet, 1000f, TestRadiusMeters);

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
        var currentVelocityField = typeof(VelocityDragManager).GetField("_currentVelocity", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(currentVelocityField);
        var planet = new GameObject("PlaceholderPlanet");
        planet.transform.SetParent(rig.Root.transform, false);
        mgr.ConfigurePendingPlacement(planet, 1000f, TestRadiusMeters);

        var mi = typeof(VelocityDragManager).GetMethod("OnVelocityInputChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(mi);
        mi.Invoke(mgr, new object[] { "1,2,3" });

        Assert.IsTrue(button.interactable);

        Vector3 velocity = (Vector3)currentVelocityField.GetValue(mgr);
        Assert.That(velocity.x, Is.EqualTo(0.1f).Within(0.0001f));
        Assert.That(velocity.y, Is.EqualTo(0.3f).Within(0.0001f));
        Assert.That(velocity.z, Is.EqualTo(0.2f).Within(0.0001f));
    }

    [Test]
    public void OnVelocityInputChanged_round_trips_slider_style_velocity_text()
    {
        BuildManager();

        var input = GetPrivateField<TMP_InputField>(mgr, "_velocityInputField");
        var currentVelocityField = typeof(VelocityDragManager).GetField("_currentVelocity", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(currentVelocityField);

        var planet = new GameObject("PlaceholderPlanet");
        planet.transform.SetParent(rig.Root.transform, false);
        mgr.ConfigurePendingPlacement(planet, 1000f, TestRadiusMeters);

        SetPrivateField(mgr, "_dragDirection", new Vector3(0.2545512f, 0.0145458f, 0.9669532f));
        mgr.OnSpeedSliderChanged(0.2750886f);

        Assert.AreEqual("0.70, 2.66, 0.04", input.text);

        var mi = typeof(VelocityDragManager).GetMethod("OnVelocityInputChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(mi);
        mi.Invoke(mgr, new object[] { "0.70, 2.68, 0.04" });

        Vector3 roundTrippedVelocity = (Vector3)currentVelocityField.GetValue(mgr);
        Assert.That(roundTrippedVelocity.x, Is.EqualTo(0.07f).Within(0.0001f));
        Assert.That(roundTrippedVelocity.y, Is.EqualTo(0.004f).Within(0.0001f));
        Assert.That(roundTrippedVelocity.z, Is.EqualTo(0.268f).Within(0.0001f));
    }

    [Test]
    public void OnVelocityInputChanged_updates_manual_orbit_readout()
    {
        BuildManager();

        var apogee = manualOrbitRefs.ApogeeText;
        var perigee = manualOrbitRefs.PerigeeText;
        var inclination = manualOrbitRefs.InclinationText;
        var eccentricity = manualOrbitRefs.EccentricityText;

        var planet = new GameObject("PlaceholderPlanet");
        planet.transform.SetParent(rig.Root.transform, false);
        planet.transform.position = new Vector3(700f, 0f, 0f);
        mgr.ConfigurePendingPlacement(planet, 1000f, TestRadiusMeters);

        var mi = typeof(VelocityDragManager).GetMethod("OnVelocityInputChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(mi);
        mi.Invoke(mgr, new object[] { "0,0,754618" });

        Assert.That(apogee.text, Does.StartWith("Apogee:"));
        Assert.That(perigee.text, Does.StartWith("Perigee:"));
        Assert.That(perigee.text, Does.Not.Contain("--"));
        Assert.That(inclination.text, Does.StartWith("Inclination:"));
        Assert.That(eccentricity.text, Does.StartWith("Ecc:"));
    }

    [Test]
    public void ConfigurePendingPlacement_shows_manual_orbit_panel_until_velocity_is_applied()
    {
        BuildManager();

        var panel = manualOrbitRefs.Panel;
        Assert.IsFalse(panel.activeSelf);

        var planet = new GameObject("PlaceholderPlanet");
        planet.transform.SetParent(rig.Root.transform, false);

        mgr.ConfigurePendingPlacement(planet, 12345f, TestRadiusMeters);

        Assert.IsTrue(panel.activeSelf);
        Assert.That(mgr.planet, Is.EqualTo(planet));

        mgr.ApplyVelocityToPlanet(Vector3.forward);

        Assert.IsFalse(panel.activeSelf);
    }

    [Test]
    public void ApplyVelocityToPlanet_adds_nbody_if_missing_and_registers_body()
    {
        BuildManager();

        var planet = new GameObject("PlaceholderPlanet");
        planet.transform.SetParent(rig.Root.transform, false);

        mgr.ConfigurePendingPlacement(planet, 12345f, TestRadiusMeters);

        int beforeCount = rig.BodyService.Bodies.Count;

        mgr.ApplyVelocityToPlanet(new Vector3(1f, 2f, 3f));

        var nbody = planet.GetComponent<NBody>();
        var attitude = planet.GetComponent<AttitudeController>();

        Assert.NotNull(nbody);
        Assert.NotNull(attitude);
        Assert.AreEqual(new Vector3(1f, 2f, 3f), nbody.velocity);
        Assert.AreEqual(12345f, nbody.mass);
        Assert.AreEqual(0.002f, nbody.radius, 0.000001f);
        Assert.AreEqual(System.Math.PI * 0.002 * 0.002, nbody.state.crossSectionArea, 1e-10);
        Assert.AreEqual(beforeCount + 1, rig.BodyService.Bodies.Count);
        Assert.IsTrue(mgr.HasAppliedVelocity);
    }

    [Test]
    public void ApplyVelocityToPlanet_uses_default_mass_when_placeholder_mass_not_set()
    {
        BuildManager();

        var planet = new GameObject("PlaceholderPlanet");
        planet.transform.SetParent(rig.Root.transform, false);

        mgr.ConfigurePendingPlacement(planet, 0f, TestRadiusMeters);

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
        mgr.ConfigurePendingPlacement(planet, 1000f, TestRadiusMeters);

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
        mgr.ConfigurePendingPlacement(planet, 1000f, TestRadiusMeters);

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
    public void ResetDragManager_disables_velocity_ui_and_clears_pending_state()
    {
        BuildManager();

        var input = GetPrivateField<TMP_InputField>(mgr, "_velocityInputField");
        var slider = GetPrivateField<Slider>(mgr, "_speedSlider");
        var button = GetPrivateField<Button>(mgr, "_setVelocityButton");
        var dragSphere = GetPrivateField<GameObject>(mgr, "_dragSphereObject");

        input.interactable = true;
        input.text = "1,2,3";
        slider.interactable = true;
        slider.value = 2f;
        button.interactable = true;
        dragSphere.SetActive(true);

        SetPrivateField(mgr, "_isVelocitySet", true);
        SetPrivateField(mgr, "_manualVelocityPlacementUiActive", true);
        mgr.planet = new GameObject("PlaceholderPlanet");

        mgr.ResetDragManager();

        Assert.IsFalse(input.interactable);
        Assert.AreEqual("", input.text);
        Assert.IsFalse(slider.interactable);
        Assert.AreEqual(0f, slider.value);
        Assert.IsFalse(button.interactable);
        Assert.IsFalse(dragSphere.activeSelf);
        Assert.IsNull(mgr.planet);
        Assert.IsFalse(mgr.IsManualVelocityPlacementActive);
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
