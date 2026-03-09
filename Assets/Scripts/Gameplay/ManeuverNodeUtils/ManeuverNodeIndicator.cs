using UnityEngine;
using TMPro;

public class ManeuverNodeIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Canvas _canvas;
    [SerializeField] private Camera _mainCamera;
    [SerializeField] private RectTransform _iconRect;
    [SerializeField] private TMP_Text _label;
    [SerializeField] private ManeuverNodeManager _nodeManager;
    [SerializeField] private BodyService _bodyService;

    [Header("Behavior")]
    [Tooltip("Distance from target (world units) beyond which the indicator is shown.")]
    [SerializeField] private float _showAtDistance = 1000f;

    [Tooltip("Optional extra offset above the node in world space.")]
    [SerializeField] private Vector3 _worldOffset = Vector3.zero;

    [Tooltip("Padding in pixels from the screen edges.")]
    [SerializeField] private float _screenEdgePadding = 0f;

    [SerializeField] private bool _hideWhenOccludedByCentralBody = true;
    [SerializeField] private bool _hide3DNodeWhenIndicatorActive = true;

    [Header("Viewport Rules")]
    [SerializeField, Range(0f, 0.2f)] private float _innerViewportMargin = 0.02f;

    [Header("Label Layout")]
    [Tooltip("Shrinks just the label.")]
    [SerializeField] private float _labelScale = 0.5f;

    [Tooltip("Offset of the label relative to the indicator icon.")]
    [SerializeField] private Vector2 _labelOffset = new Vector2(0f, -5f);

    private float _showAtDistanceSqr;
    private RectTransform _labelRect;

    private void Awake()
    {
        if (_mainCamera == null)
            _mainCamera = Camera.main;

        if (_canvas == null && _iconRect != null)
            _canvas = _iconRect.GetComponentInParent<Canvas>();

        if (_label != null)
            _labelRect = _label.rectTransform;

        _showAtDistanceSqr = _showAtDistance * _showAtDistance;

        if (_labelRect != null)
            _labelRect.localScale = Vector3.one * _labelScale;

        if (_iconRect != null)
            _iconRect.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (_iconRect == null || _canvas == null || _nodeManager == null)
            return;

        if (_mainCamera == null)
            _mainCamera = Camera.main;

        if (_mainCamera == null)
        {
            SetIndicatorVisible(false);
            RestoreNodeVisuals();
            return;
        }

        ManeuverNode node = GetCurrentNode();
        if (node == null || node.marker == null)
        {
            SetIndicatorVisible(false);
            return;
        }

        Transform target = node.marker.transform;
        Vector3 targetPos = target.position + _worldOffset;
        Vector3 camPos = _mainCamera.transform.position;

        var gizmo = target.GetComponent<NodeGizmo>();

        if (_hideWhenOccludedByCentralBody && _bodyService != null)
        {
            var earth = _bodyService.CentralBody;
            if (earth != null && IsOccludedByCentralBody(camPos, targetPos, earth))
            {
                SetIndicatorVisible(false);
                if (gizmo != null) gizmo.SetVisualsVisible(true);
                return;
            }
        }

        float distSqr = (camPos - targetPos).sqrMagnitude;
        if (distSqr < _showAtDistanceSqr)
        {
            SetIndicatorVisible(false);
            if (gizmo != null) gizmo.SetVisualsVisible(true);
            return;
        }

        Vector3 viewportPos = _mainCamera.WorldToViewportPoint(targetPos);

        if (viewportPos.z <= 0f)
        {
            SetIndicatorVisible(false);
            if (gizmo != null) gizmo.SetVisualsVisible(true);
            return;
        }

        bool inView =
            viewportPos.x > _innerViewportMargin &&
            viewportPos.x < 1f - _innerViewportMargin &&
            viewportPos.y > _innerViewportMargin &&
            viewportPos.y < 1f - _innerViewportMargin;

        if (!inView)
        {
            SetIndicatorVisible(false);
            if (gizmo != null) gizmo.SetVisualsVisible(true);
            return;
        }

        Vector3 screenPos = _mainCamera.WorldToScreenPoint(targetPos);

        float padding = _screenEdgePadding;
        screenPos.x = Mathf.Clamp(screenPos.x, padding, Screen.width - padding);
        screenPos.y = Mathf.Clamp(screenPos.y, padding, Screen.height - padding);

        RectTransform canvasRect = _canvas.transform as RectTransform;
        Camera uiCam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _mainCamera;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
        canvasRect, screenPos, uiCam, out var localPoint))
        {
            _iconRect.anchoredPosition = localPoint;

            if (_labelRect != null)
                _labelRect.anchoredPosition = _labelOffset;

            if (_label != null)
                _label.text = BuildLabel(node);

            SetIndicatorVisible(true);

            if (gizmo != null && _hide3DNodeWhenIndicatorActive)
                gizmo.SetVisualsVisible(false);
            else if (gizmo != null)
                gizmo.SetVisualsVisible(true);
        }
        else
        {
            SetIndicatorVisible(false);
            if (gizmo != null) gizmo.SetVisualsVisible(true);
        }
    }

    private ManeuverNode GetCurrentNode()
    {
        if (_nodeManager == null || !_nodeManager.HasNode)
            return null;

        return _nodeManager.CurrentNode;
    }

    private string BuildLabel(ManeuverNode node)
    {
        if (node == null || _nodeManager.bodyRuntimeCoordinator == null)
            return "Node";

        float tMinus = node.burnTime - _nodeManager.bodyRuntimeCoordinator.simulationTime;
        string burnName = node.burnType.ToDisplayName();
        string sign = tMinus >= 0f ? "T+" : "T–";
        return $"{burnName} ({sign}{Mathf.Abs(tMinus):0}s)";
    }

    private void RestoreNodeVisuals()
    {
        ManeuverNode node = GetCurrentNode();
        if (node == null || node.marker == null)
            return;

        var gizmo = node.marker.GetComponent<NodeGizmo>();
        if (gizmo != null)
            gizmo.SetVisualsVisible(true);
    }

    private bool IsOccludedByCentralBody(Vector3 camPos, Vector3 targetPos, NBody central)
    {
        if (central == null)
            return false;

        Vector3 center = central.transform.position;
        float radius = (float)central.radius;
        if (radius <= 0f)
            return false;

        Vector3 camToTarget = targetPos - camPos;
        float segLength = camToTarget.magnitude;
        if (segLength <= Mathf.Epsilon)
            return false;

        Vector3 dir = camToTarget / segLength;
        Vector3 camToCenter = center - camPos;

        float t = Vector3.Dot(camToCenter, dir);

        if (t <= 0f || t >= segLength)
            return false;

        Vector3 closestPoint = camPos + dir * t;
        float distanceToCenter = (closestPoint - center).magnitude;

        return distanceToCenter < radius;
    }

    private void SetIndicatorVisible(bool visible)
    {
        if (_iconRect != null && _iconRect.gameObject.activeSelf != visible)
            _iconRect.gameObject.SetActive(visible);
    }

    public bool IsIndicatorVisible()
    {
        return _iconRect != null && _iconRect.gameObject.activeSelf;
    }

    public bool IsPointerOverIndicator(Vector2 screenPoint, float extraPaddingPixels = 0f)
    {
        if (!IsIndicatorVisible() || _iconRect == null)
            return false;

        Camera uiCam = (_canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            ? null
            : _mainCamera;

        return RectTransformUtility.RectangleContainsScreenPoint(_iconRect, screenPoint, uiCam) ||
               IsScreenPointNearIndicator(screenPoint, extraPaddingPixels, uiCam);
    }

    private bool IsScreenPointNearIndicator(Vector2 screenPoint, float padding, Camera uiCam)
    {
        if (_iconRect == null)
            return false;

        Vector3[] corners = new Vector3[4];
        _iconRect.GetWorldCorners(corners);

        Vector2 min = RectTransformUtility.WorldToScreenPoint(uiCam, corners[0]);
        Vector2 max = min;

        for (int i = 1; i < 4; i++)
        {
            Vector2 p = RectTransformUtility.WorldToScreenPoint(uiCam, corners[i]);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        min -= Vector2.one * padding;
        max += Vector2.one * padding;

        return screenPoint.x >= min.x && screenPoint.x <= max.x &&
               screenPoint.y >= min.y && screenPoint.y <= max.y;
    }
}