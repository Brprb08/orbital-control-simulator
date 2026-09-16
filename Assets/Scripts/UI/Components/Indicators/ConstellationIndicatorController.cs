using UnityEngine;

[DefaultExecutionOrder(100)]
public sealed class ConstellationIndicatorController : MonoBehaviour
{
    private const int MaxInstancesPerBatch = 1023;

    [Header("Appearance")]
    [SerializeField] private Color memberColor = new(0.92f, 0.24f, 1f, 1f);
    [Tooltip("Diamond diameter in screen pixels, independent of orbit altitude and zoom.")]
    [SerializeField, Min(1f)] private float markerSizePixels = 16f;

    private readonly Matrix4x4[] memberMatrices = new Matrix4x4[MaxInstancesPerBatch];

    private ConstellationRegistry registry;
    private CameraController cameraController;
    private Camera mainCamera;
    private ConstellationRecord activeConstellation;

    private Mesh markerMesh;
    private Material memberMaterial;

    public void Initialize(SimContext ctx)
    {
        registry = ctx.ConstellationRegistry;
        cameraController = ctx.CameraController;
        mainCamera = Camera.main;

        EnsureRenderResources();

        if (registry != null)
            registry.Changed += HandleConstellationsChanged;

        if (cameraController != null)
        {
            cameraController.OnTrackedBodyChanged += HandleTrackedBodyChanged;
            cameraController.OnModeChanged += HandleCameraModeChanged;
        }

        RefreshActiveSelection();
    }

    private void OnDestroy()
    {
        if (registry != null)
            registry.Changed -= HandleConstellationsChanged;

        if (cameraController != null)
        {
            cameraController.OnTrackedBodyChanged -= HandleTrackedBodyChanged;
            cameraController.OnModeChanged -= HandleCameraModeChanged;
        }

        DestroyRenderResources();
    }

    private void LateUpdate()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (activeConstellation == null || mainCamera == null)
            return;

        EnsureRenderResources();
        DrawActiveConstellationMarkers();
    }

    private void HandleConstellationsChanged()
    {
        RefreshActiveSelection();
    }

    private void HandleTrackedBodyChanged(NBody body)
    {
        RefreshActiveSelection();
    }

    private void HandleCameraModeChanged(CameraMode mode)
    {
        RefreshActiveSelection();
    }

    private void RefreshActiveSelection()
    {
        CameraVisibilityPolicy.TryGetSelectedConstellation(cameraController, registry, out activeConstellation, out _);
    }

    private void DrawActiveConstellationMarkers()
    {
        int memberCount = 0;
        NBody tracked = CameraVisibilityPolicy.SelectedBody(cameraController);
        var planes = activeConstellation.Planes;

        for (int p = 0; p < planes.Count; p++)
        {
            ConstellationPlaneRecord plane = planes[p];
            if (plane == null)
                continue;

            var members = plane.Members;
            for (int m = 0; m < members.Count; m++)
            {
                NBody body = members[m];
                // The tracked craft already has the red target indicator.
                if (body == null || body == tracked)
                    continue;

                Vector3 center = body.RenderPosition;
                float depth = Vector3.Dot(center - mainCamera.transform.position, mainCamera.transform.forward);
                if (depth <= mainCamera.nearClipPlane)
                    continue;

                Matrix4x4 matrix = BuildMarkerMatrix(center, depth);
                AddInstance(memberMatrices, ref memberCount, matrix, memberMaterial);
            }
        }

        FlushInstances(memberMatrices, memberCount, memberMaterial);
    }

    private void AddInstance(Matrix4x4[] matrices, ref int count, Matrix4x4 matrix, Material material)
    {
        matrices[count++] = matrix;

        if (count >= MaxInstancesPerBatch)
        {
            FlushInstances(matrices, count, material);
            count = 0;
        }
    }

    private void FlushInstances(Matrix4x4[] matrices, int count, Material material)
    {
        if (count <= 0 || markerMesh == null || material == null)
            return;

        Graphics.DrawMeshInstanced(
            markerMesh,
            0,
            material,
            matrices,
            count,
            null,
            UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows: false,
            layer: gameObject.layer,
            camera: mainCamera
        );
    }

    private Matrix4x4 BuildMarkerMatrix(Vector3 center, float depth)
    {
        // The diamond spans two mesh units. Camera depth keeps the apparent
        // size consistent even near the edges of the screen.
        float halfViewHeight = mainCamera.orthographic
            ? mainCamera.orthographicSize
            : depth * Mathf.Tan(mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float size = Mathf.Max(1f, markerSizePixels) * halfViewHeight / Mathf.Max(1, mainCamera.pixelHeight);
        return Matrix4x4.TRS(center, mainCamera.transform.rotation, Vector3.one * size);
    }

    private void EnsureRenderResources()
    {
        if (markerMesh == null)
            markerMesh = CreateDiamondMesh();

        if (memberMaterial == null)
            memberMaterial = CreateMaterial(memberColor);
    }

    private static Mesh CreateDiamondMesh()
    {
        var mesh = new Mesh { name = "ConstellationIndicatorDiamond" };
        mesh.vertices = new[]
        {
            new Vector3(0f, 1f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, -1f, 0f),
            new Vector3(-1f, 0f, 0f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Custom/ConstellationIndicator");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        var material = new Material(shader)
        {
            enableInstancing = true,
            renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent
        };

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        else
            material.color = color;

        return material;
    }

    private void DestroyRenderResources()
    {
        if (markerMesh != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(markerMesh);
            else
                Destroy(markerMesh);
#else
            Destroy(markerMesh);
#endif
            markerMesh = null;
        }

        DestroyMaterial(ref memberMaterial);
    }

    private void DestroyMaterial(ref Material material)
    {
        if (material == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            DestroyImmediate(material);
        else
            Destroy(material);
#else
        Destroy(material);
#endif
        material = null;
    }
}
