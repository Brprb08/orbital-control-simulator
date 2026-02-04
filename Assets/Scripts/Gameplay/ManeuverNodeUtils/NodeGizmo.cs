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

    [Header("Label Size Control")]
    [Tooltip("Label world size when the node is at its minimum distance scale.")]
    public float minLabelWorldScale = 0.25f;
    [Tooltip("Label world size when the node is at its maximum distance scale.")]
    public float maxLabelWorldScale = 4f;

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

        // Distance-based node scale + pulse
        float d = Vector3.Distance(transform.position, cam.transform.position);
        float s = Mathf.Clamp(d * distanceScale, minScreenScale, maxScreenScale);
        float p = pulseEnabled
            ? 1f + Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * pulseSpeed) * pulseAmplitude
            : 1f;

        const float nodeSizeFactor = 0.5f; // node is half the old size
        transform.localScale = baseScale * s * p * nodeSizeFactor;

        if (worldLabel != null)
        {
            // t = how far between min and max node scale
            float t = 0f;
            if (!Mathf.Approximately(maxScreenScale, minScreenScale))
            {
                t = Mathf.InverseLerp(minScreenScale, maxScreenScale, s);
            }

            // World-space size of the label
            float targetWorldScale = Mathf.Lerp(minLabelWorldScale, maxLabelWorldScale, t);

            // Parent is already scaled by s * p * nodeSizeFactor
            float parentScale = Mathf.Max(transform.lossyScale.x, 1e-4f);
            float localScale = targetWorldScale / parentScale;

            worldLabel.transform.localScale = Vector3.one * localScale;
            worldLabel.transform.forward = transform.forward;
        }
    }

    public void SetPulse(bool enabled) => pulseEnabled = enabled;

    public void SetTimeToNode(string prefix, float seconds)
    {
        if (!worldLabel) return;
        string sign = seconds >= 0 ? "T+" : "T–";
        worldLabel.text = $"{prefix} ({sign}{Mathf.Abs(seconds):0}s)";
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
