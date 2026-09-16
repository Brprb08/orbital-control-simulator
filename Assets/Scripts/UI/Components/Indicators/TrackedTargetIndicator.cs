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
            if (earth != null && CameraVisibilityPolicy.IsOccludedByCentralBody(camPos, targetPos, earth))
            {
                SetIconVisible(false);
                return;
            }
        }

        if (!CameraVisibilityPolicy.IsTargetIndicatorFarEnough(_cameraController, camPos, targetPos, _showAtDistanceSqr))
        {
            SetIconVisible(false);
            return;
        }

        Vector3 viewportPos = _mainCamera.WorldToViewportPoint(targetPos);

        float viewportInset = isEarthCam ? -_earthViewViewportMargin : _innerViewportMargin;
        if (!CameraVisibilityPolicy.IsInsideViewport(viewportPos, viewportInset))
        {
            SetIconVisible(false);
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



    private void SetIconVisible(bool visible)
    {
        if (_iconRect.gameObject.activeSelf != visible)
            _iconRect.gameObject.SetActive(visible);

        if (!visible)
            _lastRenderedTarget = null;
    }
}
