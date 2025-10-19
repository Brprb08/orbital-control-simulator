using UnityEngine;
using TMPro;

[RequireComponent(typeof(Renderer))]
[DisallowMultipleComponent]
public class NodeGizmo : MonoBehaviour
{
    [Header("Visuals")]
    public float minScreenScale = 0.6f;
    public float maxScreenScale = 6f;
    public float distanceScale = 0.02f;
    public float pulseSpeed = 1.2f;
    public float pulseAmplitude = 0.05f;
    public bool pulseEnabled = true;
    public Color baseColor = new(0.3f, 1f, 0.7f, 1f);
    public Color hoverColor = new(1f, 0.95f, 0.2f, 1f);

    [Header("Label")]
    public TMP_Text worldLabel;
    public string labelPrefix = "Node";
    public float labelYOffset = 1.2f;

    Renderer rend;
    Camera cam;
    Vector3 baseScale;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        cam = Camera.main;
        baseScale = transform.localScale;

        foreach (var m in rend.materials)
            m.renderQueue = 5000;

        ApplyColor(baseColor);

        if (worldLabel == null)
        {
            var go = new GameObject("NodeLabel");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * labelYOffset;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var rect = textGo.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(4.0f, 1.2f);

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.enableAutoSizing = false;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 2.2f;
            tmp.text = "Node";
            worldLabel = tmp;
        }
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        // Face camera
        transform.forward = (transform.position - cam.transform.position).normalized;

        // Distance scale + pulse
        float d = Vector3.Distance(transform.position, cam.transform.position);
        float s = Mathf.Clamp(d * distanceScale, minScreenScale, maxScreenScale);
        float p = pulseEnabled
            ? 1f + Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * pulseSpeed) * pulseAmplitude
            : 1f;

        transform.localScale = baseScale * s * p;

        // Keep text constant-size and billboarded
        if (worldLabel != null)
        {
            float cancel = 1f / (s * p);
            worldLabel.transform.localScale = Vector3.one * cancel;
            worldLabel.transform.forward = transform.forward;
        }
    }

    public void SetPulse(bool enabled) => pulseEnabled = enabled;

    public void SetTimeToNode(float seconds)
    {
        if (!worldLabel) return;
        string sign = seconds >= 0 ? "T+" : "T–";
        worldLabel.text = $"{labelPrefix} ({sign}{Mathf.Abs(seconds):0}s)";
    }

    void OnMouseEnter() => ApplyColor(hoverColor);
    void OnMouseExit() => ApplyColor(baseColor);

    void ApplyColor(Color c)
    {
        var baseProp = Shader.PropertyToID("_BaseColor");
        foreach (var m in rend.materials)
        {
            if (m.HasProperty(baseProp)) m.SetColor(baseProp, c);
            else if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }
    }
}
