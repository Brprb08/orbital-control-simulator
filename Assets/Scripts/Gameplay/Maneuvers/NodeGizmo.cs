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

    [Header("Rendering")]
    public int renderQueue = 5000;
    public bool renderOnTopIfPossible = true;

    [Header("Label")]
    public TMP_Text worldLabel;
    public string labelPrefix = "Node";
    public float labelYOffset = 1.2f;

    [Header("Label Size Control")]
    [Tooltip("Label world size when the node is at its minimum distance scale.")]
    public float minLabelWorldScale = 0.25f;

    [Tooltip("Label world size when the node is at its maximum distance scale.")]
    public float maxLabelWorldScale = 4f;

    private Renderer rend;
    private Collider nodeCollider;
    private Camera cam;
    private Vector3 baseScale;
    private bool visualsVisible = true;
    private bool hoverEnabled = true;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        nodeCollider = GetComponent<Collider>();
        cam = Camera.main;
        baseScale = transform.localScale;

        ApplyRenderSettings();
        ApplyColor(baseColor);

        if (worldLabel == null)
        {
            var go = new GameObject("NodeLabel");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * labelYOffset;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 1000;

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
            tmp.text = labelPrefix;
            worldLabel = tmp;
        }
    }

    void Update()
    {
        if (!visualsVisible)
            return;

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector3 toCam = transform.position - cam.transform.position;
        if (toCam.sqrMagnitude < 1e-8f) return;

        transform.forward = toCam.normalized;

        float d = toCam.magnitude;
        float s = Mathf.Clamp(d * distanceScale, minScreenScale, maxScreenScale);

        float p = pulseEnabled
            ? 1f + Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * pulseSpeed) * pulseAmplitude
            : 1f;

        transform.localScale = baseScale * s * p;

        if (worldLabel != null && worldLabel.gameObject.activeSelf)
        {
            float t = 0f;
            if (!Mathf.Approximately(maxScreenScale, minScreenScale))
                t = Mathf.InverseLerp(minScreenScale, maxScreenScale, s);

            float targetWorldScale = Mathf.Lerp(minLabelWorldScale, maxLabelWorldScale, t);

            float parentScale = Mathf.Max(transform.lossyScale.x, 1e-4f);
            float localScale = targetWorldScale / parentScale;

            worldLabel.transform.localScale = Vector3.one * localScale;
            worldLabel.transform.forward = transform.forward;
        }
    }

    public void SetPulse(bool enabled)
    {
        pulseEnabled = enabled;
    }

    public void SetColors(Color baseColor, Color hoverColor, bool enableHover, bool applyImmediately = true)
    {
        this.baseColor = baseColor;
        this.hoverColor = hoverColor;
        hoverEnabled = enableHover;

        if (applyImmediately)
            ApplyColor(this.baseColor);
    }

    public void SetTimeToNode(string prefix, float seconds)
    {
        if (!worldLabel) return;
        string sign = seconds >= 0 ? "T+" : "T–";
        worldLabel.text = $"{prefix} ({sign}{Mathf.Abs(seconds):0}s)";
    }

    public void SetWorldLabelVisible(bool visible)
    {
        if (worldLabel != null)
            worldLabel.gameObject.SetActive(visible);
    }

    public void SetVisualsVisible(bool visible)
    {
        visualsVisible = visible;

        if (rend != null)
            rend.enabled = visible;

        if (worldLabel != null)
            worldLabel.gameObject.SetActive(visible);

        if (nodeCollider != null)
            nodeCollider.enabled = visible;
    }

    void OnMouseEnter()
    {
        if (visualsVisible && hoverEnabled)
            ApplyColor(hoverColor);
    }

    void OnMouseExit()
    {
        if (visualsVisible)
            ApplyColor(baseColor);
    }

    private void ApplyRenderSettings()
    {
        if (rend == null) return;

        foreach (var m in rend.materials)
        {
            if (m == null) continue;

            m.renderQueue = renderQueue;

            if (renderOnTopIfPossible && m.HasProperty("_ZTest"))
                m.SetInt("_ZTest", 8);
        }
    }

    private void ApplyColor(Color c)
    {
        int baseProp = Shader.PropertyToID("_BaseColor");

        foreach (var m in rend.materials)
        {
            if (m == null) continue;

            if (m.HasProperty(baseProp)) m.SetColor(baseProp, c);
            else if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }
    }
}
