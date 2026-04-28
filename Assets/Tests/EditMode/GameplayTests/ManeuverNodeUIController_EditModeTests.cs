using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ManeuverNodeUIController_EditModeTests
{
    private GameObject root;
    private ManeuverNodeUIController controller;

    [TearDown]
    public void TearDown()
    {
        if (root != null)
            Object.DestroyImmediate(root);
    }

    [Test]
    public void BurnDurationInput_live_updates_slider_and_event()
    {
        BuildController();
        controller.Initialize(defaultBurnDuration: 20f, defaultThrustScale: 1f, allowNodeSlider: true);

        float observed = -1f;
        controller.BurnDurationChanged += value => observed = value;

        TMP_InputField input = GetPrivateField<TMP_InputField>(controller, "burnDurationInputField");
        input.onValueChanged.Invoke("42.5");

        float expected = Mathf.Ceil(42.5f / Time.fixedDeltaTime) * Time.fixedDeltaTime;
        Assert.AreEqual(expected, controller.burnDurationSlider.value, 0.001f);
        Assert.AreEqual(expected, controller.BurnDuration, 0.001f);
        Assert.AreEqual(expected, observed, 0.001f);
    }

    [Test]
    public void ThrustScaleInput_live_updates_slider_and_event()
    {
        BuildController();
        controller.Initialize(defaultBurnDuration: 20f, defaultThrustScale: 1f, allowNodeSlider: true);

        float observed = -1f;
        controller.ThrustScaleChanged += value => observed = value;

        TMP_InputField input = GetPrivateField<TMP_InputField>(controller, "thrustScaleInputField");
        input.onValueChanged.Invoke("2.25");

        Assert.AreEqual(2.25f, controller.thrustScaleSlider.value, 0.001f);
        Assert.AreEqual(2.25f, controller.ThrustScale, 0.001f);
        Assert.AreEqual(2.25f, observed, 0.001f);
    }

    [Test]
    public void NodeTimeInput_live_updates_slider_and_event()
    {
        BuildController();
        controller.Initialize(defaultBurnDuration: 20f, defaultThrustScale: 1f, allowNodeSlider: true);

        float observedIndex = -1f;
        controller.NodeTimeSliderChanged += value => observedIndex = value;

        controller.SetupNodeSlider(new ManeuverNode
        {
            burnTime = 6f,
            snapshotStartTime = 0f,
            snapshotDeltaTime = 2f,
            trajectorySnapshot = new System.Collections.Generic.List<Vector3>
            {
                Vector3.zero,
                Vector3.right,
                Vector3.right * 2f,
                Vector3.right * 3f,
                Vector3.right * 4f,
                Vector3.right * 5f
            }
        });

        observedIndex = -1f;
        SetPrivateField(controller, "nextNodeSliderAllowed", -1f);

        TMP_InputField input = GetPrivateField<TMP_InputField>(controller, "nodeTimeInputField");
        input.onValueChanged.Invoke("8.0");

        Assert.AreEqual(4f, controller.nodeTimeSlider.value, 0.001f);
        Assert.AreEqual(4f, observedIndex, 0.001f);
    }

    private void BuildController()
    {
        root = new GameObject("ManeuverNodeUIControllerTests");
        controller = root.AddComponent<ManeuverNodeUIController>();

        controller.nodeTimeSlider = CreateSlider(root.transform, "NodeTimeSlider");
        controller.burnDurationSlider = CreateSlider(root.transform, "BurnDurationSlider");
        controller.thrustScaleSlider = CreateSlider(root.transform, "ThrustScaleSlider");
        controller.burnDurationLabel = CreateText(root.transform, "BurnDurationLabel");
        controller.thrustScaleLabel = CreateText(root.transform, "ThrustScaleLabel");
        controller.burnDropdown = CreateDropdown(root.transform, "BurnDropdown");
        controller.placeNodeButton = CreateButton(root.transform, "PlaceButton");
        controller.removeNodeButton = CreateButton(root.transform, "RemoveButton");

        SetPrivateField(controller, "nodeTimeInputField", CreateInputField(root.transform, "NodeTimeInput"));
        SetPrivateField(controller, "burnDurationInputField", CreateInputField(root.transform, "BurnDurationInput"));
        SetPrivateField(controller, "thrustScaleInputField", CreateInputField(root.transform, "ThrustScaleInput"));
    }

    private static Button CreateButton(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go.AddComponent<Button>();
    }

    private static TMP_Dropdown CreateDropdown(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go.AddComponent<TMP_Dropdown>();
    }

    private static Slider CreateSlider(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go.AddComponent<Slider>();
    }

    private static TMP_Text CreateText(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go.AddComponent<TextMeshProUGUI>();
    }

    private static TMP_InputField CreateInputField(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();

        var input = go.AddComponent<TMP_InputField>();

        var textGo = new GameObject(name + "_Text");
        textGo.transform.SetParent(go.transform, false);
        textGo.AddComponent<RectTransform>();
        var text = textGo.AddComponent<TextMeshProUGUI>();
        input.textComponent = text;

        return input;
    }

    private static void SetPrivateField(object obj, string fieldName, object value)
    {
        FieldInfo field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field, $"Field '{fieldName}' not found.");
        field.SetValue(obj, value);
    }

    private static T GetPrivateField<T>(object obj, string fieldName)
    {
        FieldInfo field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field, $"Field '{fieldName}' not found.");
        return (T)field.GetValue(obj);
    }
}
