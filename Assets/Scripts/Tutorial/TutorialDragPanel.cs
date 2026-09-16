using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shared panel dragging for tutorial and flight panels. Only background gestures
/// move the panel; child controls retain their normal pointer behavior.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class TutorialDragPanel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
{
    [SerializeField] private Canvas canvas;
    [SerializeField] private bool confineCursorWhileDragging = true;

    private RectTransform rt;
    private RectTransform canvasRect;
    private bool dragging;
    private bool cursorChanged;
    private int dragPointerId;
    private CursorLockMode prevLock;
    private bool prevVisible;
    private readonly Vector3[] panelCorners = new Vector3[4];

    private void Awake()
    {
        rt = GetComponent<RectTransform>();
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (canvas != null) canvasRect = canvas.rootCanvas.transform as RectTransform;
    }

    private bool CanStartDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || canvasRect == null)
            return false;

        // Inspect the original press, not whichever graphic the pointer crosses later.
        GameObject hit = eventData.pointerPressRaycast.gameObject;
        if (hit == null || !hit.transform.IsChildOf(transform)) return false;
        for (Transform child = hit.transform; child != transform; child = child.parent)
        {
            if (child.GetComponent<Selectable>() != null || child.GetComponent<ScrollRect>() != null)
                return false;
        }
        return true;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (CanStartDrag(eventData)) transform.SetAsLastSibling();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (dragging || !CanStartDrag(eventData)) return;
        dragging = true;
        dragPointerId = eventData.pointerId;
        if (confineCursorWhileDragging)
        {
            prevLock = Cursor.lockState;
            prevVisible = Cursor.visible;
            cursorChanged = true;
            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = true;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging || eventData.pointerId != dragPointerId) return;
        RectTransform parent = rt.parent as RectTransform;
        if (parent == null) return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,
                eventData.position - eventData.delta, eventData.pressEventCamera, out Vector2 previous) &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,
                eventData.position, eventData.pressEventCamera, out Vector2 current))
        {
            rt.anchoredPosition += current - previous;
            ClampToCanvas();
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (dragging && eventData.pointerId == dragPointerId) EndDrag();
    }

    private void ClampToCanvas()
    {
        rt.GetWorldCorners(panelCorners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3 corner in panelCorners)
        {
            Vector2 local = canvasRect.InverseTransformPoint(corner);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }
        Rect bounds = canvasRect.rect;
        float x = ClampOffset(min.x, max.x, bounds.xMin, bounds.xMax);
        float y = ClampOffset(min.y, max.y, bounds.yMin, bounds.yMax);
        rt.position += canvasRect.TransformVector(new Vector3(x, y, 0f));
    }

    private static float ClampOffset(float min, float max, float boundMin, float boundMax)
    {
        // Oversized panels cannot fit: center that axis instead of oscillating between edges.
        if (max - min > boundMax - boundMin)
            return (boundMin + boundMax - min - max) * 0.5f;
        if (min < boundMin) return boundMin - min;
        if (max > boundMax) return boundMax - max;
        return 0f;
    }

    private void EndDrag()
    {
        dragging = false;
        if (!cursorChanged) return;
        Cursor.lockState = prevLock;
        Cursor.visible = prevVisible;
        cursorChanged = false;
    }

    private void OnDisable() => EndDrag();
}
