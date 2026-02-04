using UnityEngine;
using TMPro;

/// <summary>
/// World-space label for a vector gizmo.
/// Faces the camera, scales with distance, and fades in/out based on zoom.
/// </summary>
public class VectorLabel3D : MonoBehaviour
{
    [SerializeField] private TextMeshPro label;
    [SerializeField] private string labelText = "Label";

    [SerializeField] private float worldOffset = 0.25f;

    [Header("Scale vs distance")]
    [SerializeField] private float nearDistance = 20f;   // smallest scale
    [SerializeField] private float farDistance = 200f;  // largest scale 
    [SerializeField] private float nearScale = 0.2f;
    [SerializeField] private float farScale = 0.8f;

    [Header("Fade vs distance")]
    [SerializeField] private float closeFadeEnd = 5f;   // fully invisible at/below this
    [SerializeField] private float closeFadeStart = 10f;   // fully visible at/above this
    [SerializeField] private float farFadeStart = 90f;  // start fading out
    [SerializeField] private float farFadeEnd = 100f;  // fully invisible at/above this
    [SerializeField] private float minVisibleAlpha = 0.05f;

    private Camera _cam;

    private void Awake()
    {
        _cam = Camera.main;

        if (label != null && !string.IsNullOrEmpty(labelText))
            label.text = labelText;
    }

    /// <summary>
    /// Optional explicit initialization if you want to pass in a camera.
    /// </summary>
    public void Initialize(Camera cam)
    {
        _cam = cam;
        if (label != null && !string.IsNullOrEmpty(labelText))
            label.text = labelText;
    }

    /// <summary>
    /// Updates label position/orientation and applies distance-based scale and fade.
    /// </summary>
    public void UpdateLabel(Vector3 lineStart, Vector3 direction, float length, bool shouldShow)
    {
        if (label == null)
            return;

        if (_cam == null)
        {
            _cam = Camera.main;
            if (_cam == null) return;
        }

        if (!shouldShow || length <= 0f)
        {
            if (label.gameObject.activeSelf)
                label.gameObject.SetActive(false);
            return;
        }

        Vector3 dirNorm = direction.sqrMagnitude > 0f ? direction.normalized : Vector3.forward;
        Vector3 worldPos = lineStart + dirNorm * (length + worldOffset);
        transform.position = worldPos;

        transform.rotation = Quaternion.LookRotation(_cam.transform.forward, _cam.transform.up);

        float dist = Vector3.Distance(_cam.transform.position, transform.position);

        // scale: smaller when close, larger when far
        float scaleT = Mathf.InverseLerp(nearDistance, farDistance, dist);
        float scale = Mathf.Lerp(nearScale, farScale, scaleT);
        transform.localScale = Vector3.one * scale;

        float nearAlpha = Mathf.InverseLerp(closeFadeEnd, closeFadeStart, dist);
        float farAlpha = Mathf.InverseLerp(farFadeEnd, farFadeStart, dist);
        float alpha = nearAlpha * farAlpha;

        Color c = label.color;
        c.a = alpha;
        label.color = c;

        bool visible = alpha >= minVisibleAlpha;
        if (label.gameObject.activeSelf != visible)
            label.gameObject.SetActive(visible);
    }

    /// <summary>Immediately hides the label regardless of fade state.</summary>
    public void HideImmediate()
    {
        if (label != null)
            label.gameObject.SetActive(false);
    }
}
