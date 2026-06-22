using UnityEngine;

public class ManeuverNodeVisualController : MonoBehaviour
{
    private const float MarkerPickupRadius = 4.5f;

    public void SetupNodeVisuals(ManeuverNode node, bool isPreview, ManeuverNodeManager manager)
    {
        if (node == null)
            return;

        if (node.marker == null)
            node.marker = new GameObject();

        node.marker.name = isPreview ? "ManeuverNodePreview" : "ManeuverNode";
        node.marker.transform.position = node.position;

        var col = node.marker.GetComponent<SphereCollider>();
        if (col == null)
            col = node.marker.AddComponent<SphereCollider>();

        col.isTrigger = isPreview;
        col.radius = MarkerPickupRadius;
        col.enabled = true;

        if (isPreview)
        {
            var drag = node.marker.GetComponent<NodeDragHandle>();
            if (drag == null)
            {
                drag = node.marker.AddComponent<NodeDragHandle>();
                drag.Init(manager);
            }
        }
        else
        {
            var drag = node.marker.GetComponent<NodeDragHandle>();
            if (drag != null)
                Destroy(drag);

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
}
