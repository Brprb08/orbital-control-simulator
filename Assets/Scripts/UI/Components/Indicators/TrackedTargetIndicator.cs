using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows a UI icon for the currently tracked target when the camera is far away,
/// using CameraController's tracking events.
/// </summary>
[DefaultExecutionOrder(10000)]
public class TrackedTargetIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Canvas _canvas;
    [SerializeField] private CameraController _cameraController;
    [SerializeField] private Camera _mainCamera;
    [SerializeField] private RectTransform _iconRect;
    [SerializeField] private BodyService _bodyService;

    [Header("Behavior")]
    [Tooltip("Distance from target (world units) beyond which the icon is shown.")]
    [SerializeField] private float _showAtDistance = 450f;

    [Tooltip("Optional extra offset above the target in world space.")]
    [SerializeField] private Vector3 _worldOffset = Vector3.zero;

    [SerializeField] private float _followSmoothing = 15f;

    [Tooltip("Padding in pixels from the screen edges for the indicator icon.")]
    [SerializeField] private float _screenEdgePadding = 0f;

    [SerializeField] private bool _hideWhenOccludedByCentralBody = true;

    private Transform _currentTarget;
    private Transform _lastRenderedTarget;
    private Vector2 _smoothedPos;
    private float _showAtDistanceSqr;

    [Tooltip("Extra viewport margin for EarthCam before hiding the icon (0–0.5).")]
    [SerializeField, Range(0f, 0.5f)] private float _earthViewViewportMargin = 0f;
    [SerializeField, Range(0f, 0.2f)] private float _innerViewportMargin = 0.02f;

    private void Awake()
    {
        if (_mainCamera == null)
            _mainCamera = Camera.main;

        if (_canvas == null && _iconRect != null)
            _canvas = _iconRect.GetComponentInParent<Canvas>();

        RefreshShowDistanceCache();

        if (_iconRect != null)
        {
            _iconRect.gameObject.SetActive(false);
            _smoothedPos = _iconRect.anchoredPosition;
        }
    }

    private void OnValidate()
    {
        RefreshShowDistanceCache();
    }

    private void OnEnable()
    {
        if (_cameraController == null)
            return;

        _cameraController.OnTrackedBodyChanged += HandleTrackedBodyChanged;
        _cameraController.OnTrackedPlaceholderChanged += HandleTrackedPlaceholderChanged;
        _cameraController.OnModeChanged += HandleModeChanged;

        RefreshShowDistanceCache();
        UpdateCurrentTargetFromController();
    }

    private void RefreshShowDistanceCache()
    {
        _showAtDistanceSqr = _showAtDistance * _showAtDistance;
    }

    private void OnDisable()
    {
        if (_cameraController == null)
            return;

        _cameraController.OnTrackedBodyChanged -= HandleTrackedBodyChanged;
        _cameraController.OnTrackedPlaceholderChanged -= HandleTrackedPlaceholderChanged;
        _cameraController.OnModeChanged -= HandleModeChanged;
    }

    private void HandleTrackedBodyChanged(NBody body)
    {
        UpdateCurrentTargetFromController();
    }

    private void HandleTrackedPlaceholderChanged(Transform placeholder)
    {
        UpdateCurrentTargetFromController();
    }

    private void HandleModeChanged(CameraMode mode)
    {
        UpdateCurrentTargetFromController();
    }

    private void UpdateCurrentTargetFromController()
    {
        if (_cameraController == null)
            return;

        _currentTarget = _cameraController.IndicatorTarget;
    }

    private void LateUpdate()
    {
        if (_iconRect == null || _mainCamera == null || _canvas == null)
            return;

        if (_cameraController == null)
        {
            SetIconVisible(false);
            return;
        }

        UpdateCurrentTargetFromController();

        bool isEarthCam = _cameraController.IsEarthView;

        if (_currentTarget == null)
        {
            SetIconVisible(false);
            return;
        }

        Vector3 targetPos = _currentTarget.position + _worldOffset;
        Vector3 camPos = _mainCamera.transform.position;

        if (_hideWhenOccludedByCentralBody && _bodyService != null)
        {
            var earth = _bodyService.CentralBody;
            if (earth != null && IsOccludedByCentralBody(camPos, targetPos, earth))
            {
                SetIconVisible(false);
                return;
            }
        }

        float distSqr = (camPos - targetPos).sqrMagnitude;
        if (!isEarthCam && distSqr < _showAtDistanceSqr)
        {
            SetIconVisible(false);
            return;
        }

        Vector3 viewportPos = _mainCamera.WorldToViewportPoint(targetPos);

        if (viewportPos.z <= 0f)
        {
            SetIconVisible(false);
            return;
        }

        if (isEarthCam)
        {
            float outer = _earthViewViewportMargin;
            bool nearView =
                viewportPos.x > -outer && viewportPos.x < 1f + outer &&
                viewportPos.y > -outer && viewportPos.y < 1f + outer;

            if (!nearView)
            {
                SetIconVisible(false);
                return;
            }
        }
        else
        {
            float inner = _innerViewportMargin;
            bool inView =
                viewportPos.x > inner && viewportPos.x < 1f - inner &&
                viewportPos.y > inner && viewportPos.y < 1f - inner;

            if (!inView)
            {
                SetIconVisible(false);
                return;
            }
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
            bool wasHidden = !_iconRect.gameObject.activeSelf;
            bool targetChanged = _lastRenderedTarget != _currentTarget;

            if (wasHidden || targetChanged || _followSmoothing <= 0f)
                _smoothedPos = localPoint;
            else
                _smoothedPos = Vector2.Lerp(
                    _smoothedPos,
                    localPoint,
                    Mathf.Clamp01(Time.unscaledDeltaTime * _followSmoothing)
                );

            _iconRect.anchoredPosition = _smoothedPos;
            _lastRenderedTarget = _currentTarget;
            SetIconVisible(true);
        }
        else
        {
            SetIconVisible(false);
        }
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

    private void SetIconVisible(bool visible)
    {
        if (_iconRect.gameObject.activeSelf != visible)
            _iconRect.gameObject.SetActive(visible);

        if (!visible)
            _lastRenderedTarget = null;
    }
}
