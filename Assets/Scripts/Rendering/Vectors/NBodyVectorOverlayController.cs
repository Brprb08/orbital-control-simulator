using UnityEngine;

/// <summary>
/// Draws orbit-related vectors (velocity, radial, normal) for the currently tracked NBody
/// and optional world-space labels. Lives once in the scene (e.g., on a RenderManager) and
/// follows whatever body the camera is tracking.
/// </summary>
[DefaultExecutionOrder(100)]
public class NBodyVectorOverlayController : MonoBehaviour
{
    private const float EPS = 1e-8f;
    private const float H_MIN = 1e-5f;

    [Header("Core References")]
    private CameraController _cameraController;
    private CameraMovement _cameraMovement;
    private BodyService _bodyService;
    private Camera _mainCamera;

    [Header("Vector Lines")]
    [SerializeField] private ProceduralLineRenderer velocityLine;
    [SerializeField] private ProceduralLineRenderer radialLine;
    [SerializeField] private ProceduralLineRenderer normalLine;

    [Header("Appearance")]
    [Min(0.01f)]
    [SerializeField] private float vectorLength = 2f;
    [SerializeField] private Color velocityColor = new(0x00 / 255f, 0xE5 / 255f, 0xFF / 255f, 1f);
    [SerializeField] private Color radialColor = new(0xFF / 255f, 0xD1 / 255f, 0x00 / 255f, 1f);
    [SerializeField] private Color normalColor = new(0xFF / 255f, 0x4F / 255f, 0xA8 / 255f, 1f);

    [Header("Toggles")]
    [SerializeField] public bool showVectors = false;
    [SerializeField] private bool showVelocity = true;
    [SerializeField] private bool showRadial = true;
    [SerializeField] private bool showNormal = false;

    [Header("Vector Labels")]
    [SerializeField] private VectorLabel3D velocityLabel3D;
    [SerializeField] private VectorLabel3D radialLabel3D;
    [SerializeField] private VectorLabel3D normalLabel3D;

    // Internal state
    private readonly Vector3[] _twoPoints = new Vector3[2];
    private Transform _lineRoot;
    private NBody _trackedBody;

    // Cached per-frame data (computed in Update, consumed in LateUpdate)
    private Vector3 _posCached;
    private Vector3 _velDirCached;
    private Vector3 _radialDirCached;
    private Vector3 _normalDirCached;
    private float _scaledLengthCached;

    private bool _hasVelCached;
    private bool _hasRadialCached;
    private bool _hasNormalCached;
    private bool _canDrawCached;

    /// <summary>
    /// Injects dependencies from the simulation context and initializes line renderers and labels.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        _cameraController = ctx.CameraController ?? _cameraController;
        _cameraMovement = ctx.CameraMovement ?? _cameraMovement;
        _bodyService = ctx.BodyService ?? _bodyService;
        _mainCamera = _mainCamera != null ? _mainCamera : Camera.main;

        EnsureLineRenderers();

        if (_cameraController != null)
        {
            _cameraController.OnTrackedBodyChanged += HandleTrackedBodyChanged;

            var current = _cameraController.CurrentBody;
            if (current != null)
                _trackedBody = current;
        }

        if (velocityLabel3D != null) velocityLabel3D.Initialize(_mainCamera);
        if (radialLabel3D != null) radialLabel3D.Initialize(_mainCamera);
        if (normalLabel3D != null) normalLabel3D.Initialize(_mainCamera);
    }

    private void Awake()
    {
        if (_mainCamera == null)
            _mainCamera = Camera.main;

        if (_lineRoot == null && (velocityLine || radialLine || normalLine))
            _lineRoot = transform;
    }

    private void OnDestroy()
    {
        if (_cameraController != null)
            _cameraController.OnTrackedBodyChanged -= HandleTrackedBodyChanged;
    }

    private void LateUpdate()
    {
        if (!showVectors || _trackedBody == null)
        {
            InvalidateCachedVectors();
            SetAllVisible(false);
            return;
        }

        if (_trackedBody.isCentralBody)
        {
            InvalidateCachedVectors();
            SetAllVisible(false);
            return;
        }

        // No camera controller, or not tracking a body
        if (_cameraController == null || _cameraController.CurrentBody != _trackedBody)
        {
            InvalidateCachedVectors();
            SetAllVisible(false);
            return;
        }

        // No main camera
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                InvalidateCachedVectors();
                SetAllVisible(false);
                return;
            }
        }

        DrawVectorsLate();
        UpdateLabelsLate();
    }

    /// <summary>
    /// Clears cached per-frame vector state so LateUpdate will not draw stale labels.
    /// </summary>
    private void InvalidateCachedVectors()
    {
        _canDrawCached = false;
        _hasVelCached = false;
        _hasRadialCached = false;
        _hasNormalCached = false;
    }

    private void EnsureLineRenderers()
    {
        if (_lineRoot == null)
        {
            _lineRoot = new GameObject("VectorLines").transform;
            _lineRoot.SetParent(transform, false);
            _lineRoot.gameObject.layer = gameObject.layer;
        }

        if (!velocityLine)
            velocityLine = CreateLine("VelocityVector", velocityColor, _lineRoot);

        if (!radialLine)
            radialLine = CreateLine("RadialVector", radialColor, _lineRoot);

        if (!normalLine)
            normalLine = CreateLine("NormalVector", normalColor, _lineRoot);
    }

    /// <summary>
    /// Computes vectors and updates line geometry after physics interpolation and camera movement.
    /// Caches data so lines and labels share the same render-time position.
    /// </summary>
    private void DrawVectorsLate()
    {
        if (_trackedBody == null)
        {
            InvalidateCachedVectors();
            return;
        }

        EnsureLineRenderers();

        Vector3 pos = _trackedBody.RenderPosition;
        Vector3 vel = _trackedBody.velocity;

        _posCached = pos;

        float distance = Vector3.Distance(_mainCamera.transform.position, pos);
        float scaledLength = Mathf.Clamp(vectorLength * (distance * 0.05f), 2f, 50f);
        _scaledLengthCached = scaledLength;

        Vector3 center = Vector3.zero;
        bool haveCentral = false;

        if (_bodyService != null && _bodyService.CentralBody != null)
        {
            center = _bodyService.CentralBody.RenderPosition;
            haveCentral = true;
        }

        _hasVelCached = false;
        _hasRadialCached = false;
        _hasNormalCached = false;

        // velocity
        if (showVelocity && velocityLine != null && vel.sqrMagnitude > EPS)
        {
            Vector3 dir = vel.normalized;
            _velDirCached = dir;
            _hasVelCached = true;

            _twoPoints[0] = pos;
            _twoPoints[1] = pos + dir * scaledLength;
            velocityLine.UpdateLine(_twoPoints);
            velocityLine.SetVisibility(true);
        }
        else
        {
            velocityLine?.SetVisibility(false);
        }

        // radial
        if (showRadial && radialLine != null && haveCentral)
        {
            Vector3 r = pos - center;
            if (r.sqrMagnitude > EPS)
            {
                Vector3 dir = r.normalized;
                _radialDirCached = dir;
                _hasRadialCached = true;

                _twoPoints[0] = pos;
                _twoPoints[1] = pos + dir * scaledLength;
                radialLine.UpdateLine(_twoPoints);
                radialLine.SetVisibility(true);
            }
            else
            {
                radialLine.SetVisibility(false);
            }
        }
        else
        {
            radialLine?.SetVisibility(false);
        }

        // normal (right-hand-rule orbit normal)
        if (showNormal && normalLine != null && haveCentral && vel.sqrMagnitude > EPS)
        {
            Vector3 r = pos - center;
            Vector3 h = Vector3.Cross(r, vel);   // r × v

            Vector3 normalDir;
            if (h.sqrMagnitude > H_MIN * H_MIN)
            {
                // ALWAYS +h = right-hand-rule orbit normal
                normalDir = -h.normalized;
            }
            else
            {
                // Degenerate fallback if orbit normal is tiny or undefined
                Vector3 rHat = SafeNorm(r, Vector3.up);
                BuildTangentFrame(rHat, out _, out var nFallback);
                normalDir = nFallback;
            }

            _normalDirCached = normalDir;
            _hasNormalCached = true;

            _twoPoints[0] = pos;
            _twoPoints[1] = pos + normalDir * scaledLength;
            normalLine.UpdateLine(_twoPoints);
            normalLine.SetVisibility(true);
        }
        else
        {
            normalLine?.SetVisibility(false);
        }

        _canDrawCached = true;
    }

    /// <summary>
    /// Updates label positions/orientations from the same cached vectors used for the lines.
    /// </summary>
    private void UpdateLabelsLate()
    {
        if (!_canDrawCached)
            return;

        // velocity label
        if (velocityLabel3D != null)
        {
            if (_hasVelCached)
                velocityLabel3D.UpdateLabel(_posCached, _velDirCached, _scaledLengthCached, true);
            else
                velocityLabel3D.UpdateLabel(_posCached, Vector3.zero, 0f, false);
        }

        // radial label
        if (radialLabel3D != null)
        {
            if (_hasRadialCached)
                radialLabel3D.UpdateLabel(_posCached, _radialDirCached, _scaledLengthCached, true);
            else
                radialLabel3D.UpdateLabel(_posCached, Vector3.zero, 0f, false);
        }

        // normal label
        if (normalLabel3D != null)
        {
            if (_hasNormalCached)
                normalLabel3D.UpdateLabel(_posCached, _normalDirCached, _scaledLengthCached, true);
            else
                normalLabel3D.UpdateLabel(_posCached, Vector3.zero, 0f, false);
        }
    }

    public void SetVelocityEnabled(bool enabled) => showVelocity = enabled;
    public void SetRadialEnabled(bool enabled) => showRadial = enabled;
    public void SetNormalEnabled(bool enabled) => showNormal = enabled;

    /// <summary>Toggles all vectors from a UI button.</summary>
    public void ToggleFromUI() => ToggleAllVectors();

    /// <summary>Toggles the global vector visibility flag.</summary>
    public void ToggleAllVectors()
    {
        showVectors = !showVectors;
        if (!showVectors)
        {
            InvalidateCachedVectors();
            SetAllVisible(false);
        }
    }

    /// <summary>Clears all line data from the renderers.</summary>
    public void ClearAll()
    {
        velocityLine?.Clear();
        radialLine?.Clear();
        normalLine?.Clear();
    }

    private ProceduralLineRenderer CreateLine(string name, Color color, Transform parent)
    {
        var go = new GameObject(name) { layer = gameObject.layer };
        go.transform.SetParent(parent, false);

        var lr = go.AddComponent<ProceduralLineRenderer>();
        lr.SetLineColor("#" + ColorUtility.ToHtmlStringRGB(color));
        lr.SetLineWidth(0.1f);

        return lr;
    }

    private void SetAllVisible(bool visible)
    {
        velocityLine?.SetVisibility(visible);
        radialLine?.SetVisibility(visible);
        normalLine?.SetVisibility(visible);

        if (!visible)
        {
            velocityLabel3D?.HideImmediate();
            radialLabel3D?.HideImmediate();
            normalLabel3D?.HideImmediate();
        }
    }

    private static Vector3 SafeNorm(Vector3 v, Vector3 fallback)
    {
        float m = v.magnitude;
        if (m > EPS) return v / m;
        return (fallback.sqrMagnitude > 0f) ? fallback.normalized : Vector3.forward;
    }

    private static void BuildTangentFrame(Vector3 rHat, out Vector3 tHat, out Vector3 nFallback)
    {
        Vector3 refUp = Mathf.Abs(Vector3.Dot(rHat, Vector3.up)) < 0.9f
            ? Vector3.up
            : Vector3.forward;

        tHat = Vector3.Cross(refUp, rHat);
        if (tHat.sqrMagnitude < EPS)
        {
            refUp = Vector3.right;
            tHat = Vector3.Cross(refUp, rHat);
        }
        tHat.Normalize();

        nFallback = Vector3.Cross(rHat, tHat);
        nFallback.Normalize();
    }

    private void HandleTrackedBodyChanged(NBody newBody)
    {
        _trackedBody = newBody;
    }
}
