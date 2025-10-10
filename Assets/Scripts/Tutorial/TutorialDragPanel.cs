using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Draggable UI panel that follows the pointer while keeping itself fully within the parent canvas.
/// Optionally confines the cursor to the game window during the drag.
/// </summary>
public class TutorialDragPanel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
{
    [SerializeField] private Canvas canvas; // Assign in Inspector
    [SerializeField] private bool confineCursorWhileDragging = true;

    private RectTransform rt;
    private RectTransform canvasRect;

    private CursorLockMode prevLock;
    private bool prevVisible;

    /// <summary>
    /// Caches RectTransform references for the panel and its canvas.
    /// </summary>
    void Awake()
    {
        rt = GetComponent<RectTransform>();
        canvasRect = canvas.GetComponent<RectTransform>();
    }

    /// <summary>
    /// Brings the panel to the front when clicked.
    /// </summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        transform.SetAsLastSibling();
    }

    /// <summary>
    /// Starts a drag operation and (optionally) confines the cursor to the window.
    /// </summary>
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (confineCursorWhileDragging)
        {
            prevLock = Cursor.lockState;
            prevVisible = Cursor.visible;

            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = true;
        }
    }

    /// <summary>
    /// Moves the panel with the pointer and clamps it to the canvas bounds.
    /// </summary>
    public void OnDrag(PointerEventData eventData)
    {
        rt.anchoredPosition += eventData.delta / canvas.scaleFactor;
        ClampToCanvas();
    }

    /// <summary>
    /// Ends the drag operation and restores the previous cursor state.
    /// </summary>
    public void OnEndDrag(PointerEventData eventData)
    {
        if (confineCursorWhileDragging)
        {
            Cursor.lockState = prevLock;
            Cursor.visible = prevVisible;
        }
    }

    /// <summary>
    /// Keeps the panel fully on-screen by clamping its world-space corners to the canvas.
    /// </summary>
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

    /// <summary>
    /// Restores a safe cursor state if the panel is disabled mid-drag.
    /// </summary>
    void OnDisable()
    {
        if (confineCursorWhileDragging)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
