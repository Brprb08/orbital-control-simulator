using UnityEngine;

public class ManeuverNodeVisualController : MonoBehaviour
{
    [Header("Materials")]
    [SerializeField] private Material green;
    [SerializeField] private Material red;

    [Header("Visual Settings")]
    [SerializeField] private float nodeVisualScale = 1f;

    public void SetupNodeVisuals(ManeuverNode node, bool isPreview, ManeuverNodeManager manager)
    {
        if (node == null)
            return;

        if (node.marker == null)
            node.marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);

        node.marker.name = isPreview ? "ManeuverNodePreview" : "ManeuverNode";
        node.marker.transform.position = node.position;
        node.marker.transform.localScale = Vector3.one * (5f * nodeVisualScale);

        var rend = node.marker.GetComponent<Renderer>();
        if (rend != null)
        {
            if (isPreview)
            {
                rend.material = new Material(green);
                CopyColorIfPresent(green, rend.material);
            }
            else
            {
                rend.material = new Material(red);
                CopyColorIfPresent(red, rend.material);
            }

            rend.material.renderQueue = 5000;
        }

        var col = node.marker.GetComponent<SphereCollider>();
        if (col != null)
        {
            col.isTrigger = isPreview;
            col.radius = 0.9f;
        }

        var giz = node.marker.GetComponent<NodeGizmo>();
        if (giz == null)
            giz = node.marker.AddComponent<NodeGizmo>();

        if (isPreview)
        {
            Color previewBase = ResolveMaterialColor(green, giz.baseColor);
            giz.SetColors(previewBase, giz.hoverColor, enableHover: true, applyImmediately: true);

            var drag = node.marker.GetComponent<NodeDragHandle>();
            if (drag == null)
            {
                drag = node.marker.AddComponent<NodeDragHandle>();
                drag.Init(manager);
            }
        }
        else
        {
            giz.SetPulse(false);
            Color finalizedBase = ResolveMaterialColor(red, giz.baseColor);
            giz.SetColors(finalizedBase, finalizedBase, enableHover: false, applyImmediately: true);

            var drag = node.marker.GetComponent<NodeDragHandle>();
            if (drag != null)
                Destroy(drag);

            if (col != null)
                col.enabled = false;
        }
    }

    public void DestroyVisual(ManeuverNode node)
    {
        if (node != null && node.marker != null)
            Destroy(node.marker);
    }

    public void FocusCameraOn(Vector3 worldPos)
    {
        var cam = Camera.main;
        if (cam == null)
            return;

        var dir = (cam.transform.position - worldPos).normalized;
        var targetPos = worldPos + dir * 30f;
        cam.transform.position = Vector3.Lerp(cam.transform.position, targetPos, 0.25f);
    }

    private static void CopyColorIfPresent(Material src, Material dst)
    {
        if (src == null || dst == null)
            return;

        if (src.HasProperty("_BaseColor") && dst.HasProperty("_BaseColor"))
            dst.SetColor("_BaseColor", src.GetColor("_BaseColor"));
        else if (src.HasProperty("_Color") && dst.HasProperty("_Color"))
            dst.SetColor("_Color", src.GetColor("_Color"));
    }

    private static Color ResolveMaterialColor(Material material, Color fallback)
    {
        if (material == null)
            return fallback;

        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");

        if (material.HasProperty("_Color"))
            return material.GetColor("_Color");

        return fallback;
    }
}
