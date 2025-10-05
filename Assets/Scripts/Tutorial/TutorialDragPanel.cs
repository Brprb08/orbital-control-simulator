using UnityEngine;
using UnityEngine.EventSystems;

public class TutorialDragPanel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
{
    [SerializeField] private Canvas canvas; // assign in Inspector
    [SerializeField] private bool confineCursorWhileDragging = true;

    private RectTransform rt;
    private RectTransform canvasRect;

    private CursorLockMode prevLock;
    private bool prevVisible;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        canvasRect = canvas.GetComponent<RectTransform>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        transform.SetAsLastSibling(); // bring to front
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (confineCursorWhileDragging)
        {
            prevLock = Cursor.lockState;
            prevVisible = Cursor.visible;

            // Keep the cursor inside the game window while dragging
            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = true; // keep visible for UI
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Move by pointer delta, corrected for canvas scale
        rt.anchoredPosition += eventData.delta / canvas.scaleFactor;

        // Keep the panel fully on-screen
        ClampToCanvas();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (confineCursorWhileDragging)
        {
            Cursor.lockState = prevLock;
            Cursor.visible = prevVisible;
        }
    }

    private void ClampToCanvas()
    {
        Vector3[] canvasCorners = new Vector3[4];
        Vector3[] panelCorners = new Vector3[4];

        canvasRect.GetWorldCorners(canvasCorners);
        rt.GetWorldCorners(panelCorners);

        Vector3 pos = rt.position;

        if (panelCorners[0].x < canvasCorners[0].x) pos.x += canvasCorners[0].x - panelCorners[0].x; // left
        if (panelCorners[2].x > canvasCorners[2].x) pos.x -= panelCorners[2].x - canvasCorners[2].x; // right
        if (panelCorners[0].y < canvasCorners[0].y) pos.y += canvasCorners[0].y - panelCorners[0].y; // bottom
        if (panelCorners[1].y > canvasCorners[1].y) pos.y -= panelCorners[1].y - canvasCorners[1].y; // top

        rt.position = pos;
    }

    void OnDisable()
    {
        // Safety restore if the object gets disabled mid-drag
        if (confineCursorWhileDragging)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
