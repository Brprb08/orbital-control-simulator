using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;

/// <summary>
/// Mesh-based polyline renderer that builds a static tube/ribbon or camera-facing quad strip.
/// Points are passed in world space via UpdateLine, but the mesh is built
/// in local space so this object can be parented/scaled normally.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[DefaultExecutionOrder(200)]
public class ProceduralLineRenderer : MonoBehaviour
{
    public enum LineFacingMode
    {
        GpuBillboard,
        StaticTube,
        StableRibbon,
        CameraBillboard
    }

    [Header("Line Settings")]
    [Tooltip("GpuBillboard keeps screen-space thickness in the shader without CPU rebuilds on camera movement.")]
    public LineFacingMode facingMode = LineFacingMode.GpuBillboard;

    [Tooltip("Base width in world units near the camera.")]
    [Min(0.0001f)]
    public float lineWidth = 0.003f;

    [Tooltip("If true, width scales with distance so the line stays readable when you zoom out.")]
    public bool approximateScreenSpaceWidth = true;

    [Tooltip("GpuBillboard width in screen pixels.")]
    [Min(0.1f)]
    public float screenSpaceLineWidthPixels = 3.5f;

    [Tooltip("Number of sides for StaticTube mode.")]
    [Range(3, 12)]
    public int tubeSides = 6;

    [Tooltip("StaticTube width as a fraction of the trajectory bounds. Keeps large orbits visible without camera rebuilds.")]
    [Min(0f)]
    public float staticTubeRelativeWidth = 0.001f;

    [Tooltip("Minimum world-space width for StaticTube mode.")]
    [Min(0.0001f)]
    public float staticTubeMinWidth = 0.25f;

    [Tooltip("Maximum world-space width for StaticTube mode.")]
    [Min(0.0001f)]
    public float staticTubeMaxWidth = 20f;

    [Tooltip("Safety limit for how many input points we will accept.")]
    public int maxPoints = 300000;

    [Header("Material")]
    [Tooltip("Optional material override. If left empty, a Sprites/Default material is created.")]
    [SerializeField] private Material lineMaterial;

    [Header("Appearance")]
    [Range(0f, 1f)]
    public float defaultAlpha = 0.78f;

    [Header("Smoothing")]
    [Tooltip("If true, long segments are subdivided once in UpdateLine (not every frame).")]
    public bool enableSmoothing = true;

    [Tooltip("If true, visual-only Catmull-Rom interpolation is used between sampled points. Original samples are preserved.")]
    public bool enableCurveInterpolation = true;

    [Tooltip("Desired maximum world-space length per segment after smoothing.")]
    [Min(0.01f)]
    public float targetSegmentLength = 1.5f;

    [Tooltip("Max allowed visual deviation from the original segment before falling back to linear interpolation. 0 disables the guard.")]
    [Min(0f)]
    public float maxCurveDeviation = 2f;

    [Tooltip("Hard cap on total smoothed points (for safety).")]
    public int maxSmoothedPoints = 200_000;

    [Tooltip("Preferred cap for display points after smoothing. Keeps very large trajectories from becoming huge meshes.")]
    [Min(256)]
    public int targetRenderedPointBudget = 24_000;

    [Tooltip("Max number of subdivisions per segment.")]
    [Range(1, 16)]
    public int maxSubdivisionsPerSegment = 8;

    [Tooltip("If input point count exceeds this, smoothing is skipped to keep it fast.")]
    public int smoothingInputSoftLimit = 30_000;

    private Mesh lineMesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    // Original world-space centerline points from the sim
    private Vector3[] worldPoints;

    // Smoothed world-space points actually used to build the mesh
    private Vector3[] smoothedWorldPoints;

    // Cached mesh buffers (to avoid per-frame allocations)
    private Vector3[] vertices;
    private Vector3[] normals;
    private Vector2[] uvs;
    private Vector4[] tangents;
    private int[] indices;
    private float[] segmentLengths;
    private bool meshDirty;
    private bool smoothClosedLoop;
    private Vector3 lastCameraPosition;
    private Quaternion lastCameraRotation;
    private Vector3 lastTransformPosition;
    private Quaternion lastTransformRotation;
    private Vector3 lastTransformScale;
    private bool hasBuiltMesh;

    // Reusable list for smoothing to avoid per-UpdateLine allocs
    private readonly List<Vector3> smoothingBuffer = new List<Vector3>(1024);

    public bool HasPoints => smoothedWorldPoints != null && smoothedWorldPoints.Length > 1;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        if (lineMesh == null)
        {
            lineMesh = new Mesh
            {
                name = "OrbitLineMesh",
                indexFormat = IndexFormat.UInt32
            };
            lineMesh.MarkDynamic();
        }

        meshFilter.sharedMesh = lineMesh;

        if (lineMaterial != null)
        {
            meshRenderer.sharedMaterial = lineMaterial;
        }
        else if (!meshRenderer.sharedMaterial)
        {
            Shader shader = Shader.Find("Custom/ProceduralScreenSpaceLine");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
                if (facingMode == LineFacingMode.GpuBillboard)
                    facingMode = LineFacingMode.StaticTube;
            }

            var mat = new Material(shader);
            if (!mat.shader)
                Debug.LogWarning("[ProceduralLineRenderer] No compatible line shader found.");
            meshRenderer.sharedMaterial = mat;
        }

        ConfigureMaterial(meshRenderer.sharedMaterial);
        ApplyMaterialWidth();

        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    private void ConfigureMaterial(Material mat)
    {
        if (!mat) return;

        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.SetInt("_Cull", (int)CullMode.Off);
        mat.renderQueue = (int)RenderQueue.Transparent;

        if (mat.HasProperty("_LinePixelWidth"))
            mat.SetFloat("_LinePixelWidth", Mathf.Max(0.1f, screenSpaceLineWidthPixels));
    }

    /// <summary>
    /// Set line color via hex string (#RRGGBB or #RRGGBBAA).
    /// </summary>
    public void SetLineColor(string hexColor)
    {
        if (!meshRenderer) return;

        if (ColorUtility.TryParseHtmlString(hexColor, out var color))
        {
            color.a = defaultAlpha;
            meshRenderer.material.color = color;
            ApplyMaterialWidth();
        }
        else
        {
            Debug.LogWarning($"[ProceduralLineRenderer] Invalid hex color string: {hexColor}");
        }
    }

    public void SetLineWidth(float width)
    {
        lineWidth = Mathf.Max(0.0001f, width);

        ApplyMaterialWidth();
        meshDirty = true;
    }

    public void SetScreenSpaceLineWidth(float widthPixels)
    {
        screenSpaceLineWidthPixels = Mathf.Max(0.1f, widthPixels);
        ApplyMaterialWidth();
    }

    private void ApplyMaterialWidth()
    {
        if (meshRenderer == null || meshRenderer.sharedMaterial == null)
            return;

        if (meshRenderer.sharedMaterial.HasProperty("_LinePixelWidth"))
            meshRenderer.sharedMaterial.SetFloat("_LinePixelWidth", Mathf.Max(0.1f, screenSpaceLineWidthPixels));
    }

    public void Clear()
    {
        if (lineMesh != null)
            lineMesh.Clear();

        worldPoints = null;
        smoothedWorldPoints = null;
        meshDirty = false;
        hasBuiltMesh = false;
    }

    public Vector3[] GetWorldPointsCopy()
    {
        if (worldPoints == null || worldPoints.Length < 2)
            return Array.Empty<Vector3>();

        var copy = new Vector3[worldPoints.Length];
        Array.Copy(worldPoints, copy, worldPoints.Length);
        return copy;
    }

    /// <summary>
    /// Update the line points in world space. Smoothing (if enabled) happens here once,
    /// not every frame.
    /// </summary>
    public void UpdateLine(Vector3[] points)
    {
        UpdateLine(points, smoothClosedLoop: false);
    }

    public void UpdateLine(Vector3[] points, bool smoothClosedLoop)
    {
        if (points == null || points.Length < 2)
        {
            Clear();
            return;
        }

        this.smoothClosedLoop = smoothClosedLoop && IsClosedLoop(points, Mathf.Min(points.Length, maxPoints));

        int baseCount = Mathf.Min(points.Length, maxPoints);
        if (baseCount < 2)
        {
            Clear();
            return;
        }

        if (worldPoints == null || worldPoints.Length != baseCount)
            worldPoints = new Vector3[baseCount];

        Array.Copy(points, worldPoints, baseCount);

        bool doSmoothing = enableSmoothing && baseCount <= smoothingInputSoftLimit;

        if (doSmoothing)
        {
            int smoothCount = BuildSmoothedPoints(worldPoints, baseCount, this.smoothClosedLoop);

            if (smoothedWorldPoints == null || smoothedWorldPoints.Length != smoothCount)
                smoothedWorldPoints = new Vector3[smoothCount];

            for (int i = 0; i < smoothCount; i++)
                smoothedWorldPoints[i] = smoothingBuffer[i];
        }
        else
        {
            if (smoothedWorldPoints == null || smoothedWorldPoints.Length != baseCount)
                smoothedWorldPoints = new Vector3[baseCount];

            Array.Copy(worldPoints, smoothedWorldPoints, baseCount);
        }

        meshDirty = true;
    }

    /// <summary>
    /// Subdivides long segments into smaller ones (once per UpdateLine).
    /// Uses a reusable List to avoid GC.
    /// Returns the smoothed point count in the smoothingBuffer.
    /// </summary>
    private int BuildSmoothedPoints(Vector3[] src, int count, bool closedLoop)
    {
        smoothingBuffer.Clear();

        if (count < 2 || targetSegmentLength <= 0f)
        {
            for (int i = 0; i < count; i++)
                smoothingBuffer.Add(src[i]);
            return smoothingBuffer.Count;
        }

        smoothingBuffer.Add(src[0]);
        int totalCount = 1;
        int pointBudget = Mathf.Max(count, Mathf.Min(maxSmoothedPoints, targetRenderedPointBudget));
        float segmentBudgetScale = Mathf.Clamp01((pointBudget - count) / Mathf.Max(1f, count * (maxSubdivisionsPerSegment - 1f)));

        bool useCurveInterpolation = enableCurveInterpolation && count >= 4;

        for (int i = 1; i < count; i++)
        {
            Vector3 p1 = src[i - 1];
            Vector3 p2 = src[i];
            float segLen = Vector3.Distance(p1, p2);

            int segmentMaxSubdivisions = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, maxSubdivisionsPerSegment, segmentBudgetScale)));
            int steps = Mathf.Max(1, Mathf.FloorToInt(segLen / targetSegmentLength));
            steps = Mathf.Clamp(steps, 1, segmentMaxSubdivisions);

            int extra = steps;
            if (totalCount + extra > pointBudget)
            {
                if (totalCount < pointBudget)
                    smoothingBuffer.Add(p2);
                break;
            }

            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                Vector3 p = Vector3.Lerp(p1, p2, t);

                if (useCurveInterpolation && s < steps)
                {
                    Vector3 p0 = GetCurvePoint(src, count, i - 2, closedLoop);
                    Vector3 p3 = GetCurvePoint(src, count, i + 1, closedLoop);
                    Vector3 curved = CatmullRom(p0, p1, p2, p3, t);

                    if (maxCurveDeviation <= 0f ||
                        DistanceFromSegment(curved, p1, p2) <= maxCurveDeviation)
                    {
                        p = curved;
                    }
                }

                smoothingBuffer.Add(p);
                totalCount++;
            }
        }

        return smoothingBuffer.Count;
    }

    private static bool IsClosedLoop(Vector3[] points, int count)
    {
        return count >= 4 && (points[count - 1] - points[0]).sqrMagnitude <= 1e-6f;
    }

    private static Vector3 GetCurvePoint(Vector3[] src, int count, int index, bool closedLoop)
    {
        if (!closedLoop)
            return src[Mathf.Clamp(index, 0, count - 1)];

        int uniqueCount = count - 1;
        int wrapped = ((index % uniqueCount) + uniqueCount) % uniqueCount;
        return src[wrapped];
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    private static float DistanceFromSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float denom = Vector3.Dot(ab, ab);
        if (denom < 1e-8f)
            return Vector3.Distance(point, a);

        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / denom);
        return Vector3.Distance(point, a + ab * t);
    }

    public void SetVisibility(bool isVisible)
    {
        if (meshRenderer != null)
            meshRenderer.enabled = isVisible;
    }

    void LateUpdate()
    {
        if (!HasPoints)
            return;

        if (meshRenderer != null && !meshRenderer.enabled)
            return;

        Camera cam = null;
        if (facingMode == LineFacingMode.CameraBillboard)
        {
            cam = Camera.main;
            if (!cam) return;

            if (!meshDirty && hasBuiltMesh && !HasCameraOrTransformChanged(cam))
                return;
        }
        else if (!meshDirty && hasBuiltMesh && !HasTransformChanged())
        {
            return;
        }

        RebuildMesh(cam);
    }

    private void RebuildMesh(Camera cam)
    {
        int count = smoothedWorldPoints.Length;
        if (count < 2)
        {
            Clear();
            return;
        }

        int sides = facingMode == LineFacingMode.StaticTube ? Mathf.Clamp(tubeSides, 3, 12) : 2;
        int vertCount = count * sides;
        int indexCount = facingMode == LineFacingMode.StaticTube
            ? (count - 1) * sides * 6
            : (count - 1) * 6;

        if (vertices == null || vertices.Length != vertCount)
            vertices = new Vector3[vertCount];

        if (facingMode == LineFacingMode.GpuBillboard && (normals == null || normals.Length != vertCount))
            normals = new Vector3[vertCount];

        if (uvs == null || uvs.Length != vertCount)
            uvs = new Vector2[vertCount];

        if (facingMode == LineFacingMode.GpuBillboard && (tangents == null || tangents.Length != vertCount))
            tangents = new Vector4[vertCount];

        if (indices == null || indices.Length != indexCount)
            indices = new int[indexCount];

        if (segmentLengths == null || segmentLengths.Length != count)
            segmentLengths = new float[count];

        Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
        Vector3 stableNormal = facingMode != LineFacingMode.CameraBillboard
            ? ComputeStableNormal(smoothedWorldPoints)
            : Vector3.up;

        float totalLength = 0f;
        segmentLengths[0] = 0f;
        for (int i = 1; i < count; i++)
        {
            float segLen = Vector3.Distance(smoothedWorldPoints[i - 1], smoothedWorldPoints[i]);
            totalLength += segLen;
            segmentLengths[i] = totalLength;
        }

        float invTotalLength = totalLength > 1e-5f ? 1f / totalLength : 0f;

        Bounds localMeshBounds = new Bounds();
        bool hasLocalMeshBounds = false;

        bool closedLoop = smoothClosedLoop && IsClosedLoop(smoothedWorldPoints, count);

        if (facingMode == LineFacingMode.GpuBillboard)
            BuildGpuBillboardVertices(count, invTotalLength, closedLoop, ref localMeshBounds, ref hasLocalMeshBounds);
        else if (facingMode == LineFacingMode.StaticTube)
            BuildTubeVertices(count, sides, stableNormal, ResolveStaticTubeWidth(), invTotalLength, ref localMeshBounds, ref hasLocalMeshBounds);
        else
            BuildRibbonVertices(count, cam, camPos, stableNormal, totalLength, invTotalLength, ref localMeshBounds, ref hasLocalMeshBounds);

        int idx = 0;
        if (facingMode == LineFacingMode.StaticTube)
        {
            for (int i = 0; i < count - 1; i++)
            {
                int ring0 = i * sides;
                int ring1 = (i + 1) * sides;

                for (int s = 0; s < sides; s++)
                {
                    int next = (s + 1) % sides;

                    indices[idx++] = ring0 + s;
                    indices[idx++] = ring1 + s;
                    indices[idx++] = ring0 + next;

                    indices[idx++] = ring0 + next;
                    indices[idx++] = ring1 + s;
                    indices[idx++] = ring1 + next;
                }
            }
        }
        else
        {
            for (int i = 0; i < count - 1; i++)
            {
                int v0 = i * 2;
                int v1 = v0 + 1;
                int v2 = (i + 1) * 2;
                int v3 = v2 + 1;

                indices[idx++] = v0;
                indices[idx++] = v2;
                indices[idx++] = v1;

                indices[idx++] = v2;
                indices[idx++] = v3;
                indices[idx++] = v1;
            }
        }

        lineMesh.Clear();
        lineMesh.vertices = vertices;
        if (facingMode == LineFacingMode.GpuBillboard)
        {
            lineMesh.normals = normals;
            lineMesh.tangents = tangents;
        }
        lineMesh.uv = uvs;
        lineMesh.triangles = indices;
        lineMesh.bounds = localMeshBounds;

        if (cam != null)
        {
            lastCameraPosition = cam.transform.position;
            lastCameraRotation = cam.transform.rotation;
        }

        lastTransformPosition = transform.position;
        lastTransformRotation = transform.rotation;
        lastTransformScale = transform.lossyScale;
        meshDirty = false;
        hasBuiltMesh = true;
    }

    private void BuildGpuBillboardVertices(
        int count,
        float invTotalLength,
        bool closedLoop,
        ref Bounds localMeshBounds,
        ref bool hasLocalMeshBounds)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 pWorld = smoothedWorldPoints[i];
            Vector3 previousWorld = GetLineNeighbor(smoothedWorldPoints, count, i - 1, closedLoop, pWorld);
            Vector3 nextWorld = GetLineNeighbor(smoothedWorldPoints, count, i + 1, closedLoop, pWorld);

            Vector3 pLocal = transform.InverseTransformPoint(pWorld);
            Vector3 previousLocal = transform.InverseTransformPoint(previousWorld);
            Vector3 nextLocal = transform.InverseTransformPoint(nextWorld);

            int v0 = i * 2;
            int v1 = v0 + 1;
            float t = segmentLengths[i] * invTotalLength;

            vertices[v0] = pLocal;
            vertices[v1] = pLocal;
            normals[v0] = previousLocal;
            normals[v1] = previousLocal;
            tangents[v0] = new Vector4(nextLocal.x, nextLocal.y, nextLocal.z, -1f);
            tangents[v1] = new Vector4(nextLocal.x, nextLocal.y, nextLocal.z, 1f);
            uvs[v0] = new Vector2(0f, t);
            uvs[v1] = new Vector2(1f, t);

            if (!hasLocalMeshBounds)
            {
                localMeshBounds = new Bounds(pLocal, Vector3.zero);
                hasLocalMeshBounds = true;
            }

            localMeshBounds.Encapsulate(pLocal);
        }

        float localPadding = Mathf.Max(lineWidth, staticTubeMaxWidth);
        localMeshBounds.Expand(localPadding);
    }

    private static Vector3 GetLineNeighbor(Vector3[] points, int count, int index, bool closedLoop, Vector3 fallback)
    {
        if (!closedLoop)
            return index >= 0 && index < count ? points[index] : fallback;

        int uniqueCount = count - 1;
        int wrapped = ((index % uniqueCount) + uniqueCount) % uniqueCount;
        return points[wrapped];
    }

    private void BuildRibbonVertices(
        int count,
        Camera cam,
        Vector3 camPos,
        Vector3 stableNormal,
        float totalLength,
        float invTotalLength,
        ref Bounds localMeshBounds,
        ref bool hasLocalMeshBounds)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 pWorld = smoothedWorldPoints[i];

            Vector3 dir;
            if (i == 0)
                dir = smoothedWorldPoints[1] - pWorld;
            else if (i == count - 1)
                dir = pWorld - smoothedWorldPoints[i - 1];
            else
                dir = (smoothedWorldPoints[i + 1] - smoothedWorldPoints[i - 1]) * 0.5f;

            if (dir.sqrMagnitude < 1e-8f)
                dir = Vector3.forward;
            dir.Normalize();

            Vector3 side;
            if (facingMode == LineFacingMode.CameraBillboard)
            {
                Vector3 viewDir = camPos - pWorld;
                if (viewDir.sqrMagnitude < 1e-8f)
                    viewDir = -cam.transform.forward;
                viewDir.Normalize();

                side = Vector3.Cross(dir, viewDir);
            }

            else
            {
                side = Vector3.Cross(stableNormal, dir);
            }

            if (side.sqrMagnitude < 1e-6f)
                side = ComputeFallbackSide(dir);

            side.Normalize();

            float width = lineWidth;
            if (approximateScreenSpaceWidth && facingMode == LineFacingMode.CameraBillboard)
            {
                float dist = Vector3.Distance(camPos, pWorld);
                width = Mathf.Max(0.0001f, lineWidth * dist * 0.04f);
            }

            Vector3 offsetWorld = side * (width * 0.5f);

            int v0 = i * 2;
            int v1 = v0 + 1;

            vertices[v0] = transform.InverseTransformPoint(pWorld - offsetWorld);
            vertices[v1] = transform.InverseTransformPoint(pWorld + offsetWorld);

            if (!hasLocalMeshBounds)
            {
                localMeshBounds = new Bounds(vertices[v0], Vector3.zero);
                hasLocalMeshBounds = true;
            }

            localMeshBounds.Encapsulate(vertices[v0]);
            localMeshBounds.Encapsulate(vertices[v1]);

            float t = segmentLengths[i] * invTotalLength;
            uvs[v0] = new Vector2(0f, t);
            uvs[v1] = new Vector2(1f, t);
        }
    }

    private void BuildTubeVertices(
        int count,
        int sides,
        Vector3 stableNormal,
        float tubeWidth,
        float invTotalLength,
        ref Bounds localMeshBounds,
        ref bool hasLocalMeshBounds)
    {
        Vector3 previousSide = Vector3.zero;
        float radius = tubeWidth * 0.5f;

        for (int i = 0; i < count; i++)
        {
            Vector3 pWorld = smoothedWorldPoints[i];

            Vector3 dir;
            if (i == 0)
                dir = smoothedWorldPoints[1] - pWorld;
            else if (i == count - 1)
                dir = pWorld - smoothedWorldPoints[i - 1];
            else
                dir = (smoothedWorldPoints[i + 1] - smoothedWorldPoints[i - 1]) * 0.5f;

            if (dir.sqrMagnitude < 1e-8f)
                dir = Vector3.forward;
            dir.Normalize();

            Vector3 side = i == 0
                ? Vector3.Cross(stableNormal, dir)
                : previousSide - Vector3.Dot(previousSide, dir) * dir;

            if (side.sqrMagnitude < 1e-6f)
                side = ComputeFallbackSide(dir);

            side.Normalize();
            previousSide = side;

            Vector3 up = Vector3.Cross(dir, side);
            if (up.sqrMagnitude < 1e-6f)
                up = stableNormal;
            up.Normalize();

            float v = segmentLengths[i] * invTotalLength;
            int ringStart = i * sides;

            for (int s = 0; s < sides; s++)
            {
                float angle = (Mathf.PI * 2f * s) / sides;
                Vector3 radial = (Mathf.Cos(angle) * side + Mathf.Sin(angle) * up) * radius;
                Vector3 localVertex = transform.InverseTransformPoint(pWorld + radial);
                int vertexIndex = ringStart + s;

                vertices[vertexIndex] = localVertex;
                uvs[vertexIndex] = new Vector2(s / (float)sides, v);

                if (!hasLocalMeshBounds)
                {
                    localMeshBounds = new Bounds(localVertex, Vector3.zero);
                    hasLocalMeshBounds = true;
                }

                localMeshBounds.Encapsulate(localVertex);
            }
        }
    }

    private float ResolveStaticTubeWidth()
    {
        float width = lineWidth;

        if (staticTubeRelativeWidth > 0f && smoothedWorldPoints != null && smoothedWorldPoints.Length > 1)
        {
            Bounds bounds = new Bounds(smoothedWorldPoints[0], Vector3.zero);
            for (int i = 1; i < smoothedWorldPoints.Length; i++)
                bounds.Encapsulate(smoothedWorldPoints[i]);

            width = Mathf.Max(width, bounds.extents.magnitude * staticTubeRelativeWidth);
        }

        float minWidth = Mathf.Max(0.0001f, staticTubeMinWidth);
        float maxWidth = Mathf.Max(minWidth, staticTubeMaxWidth);
        return Mathf.Clamp(width, minWidth, maxWidth);
    }

    private static Vector3 ComputeStableNormal(Vector3[] points)
    {
        if (points == null || points.Length < 3)
            return Vector3.up;

        Vector3 origin = points[0];
        Vector3 normal = Vector3.zero;

        for (int i = 1; i < points.Length - 1; i++)
        {
            Vector3 a = points[i] - origin;
            Vector3 b = points[i + 1] - origin;
            Vector3 cross = Vector3.Cross(a, b);

            if (cross.sqrMagnitude > 1e-10f)
                normal += cross.normalized;
        }

        if (normal.sqrMagnitude < 1e-8f)
        {
            Vector3 dir = points[points.Length - 1] - points[0];
            return ComputeFallbackSide(dir);
        }

        return normal.normalized;
    }

    private static Vector3 ComputeFallbackSide(Vector3 dir)
    {
        Vector3 alt = Mathf.Abs(dir.y) < 0.99f ? Vector3.up : Vector3.right;
        Vector3 side = Vector3.Cross(dir, alt);

        if (side.sqrMagnitude < 1e-8f)
            side = Vector3.right;

        return side.normalized;
    }

    private bool HasCameraOrTransformChanged(Camera cam)
    {
        Transform camTransform = cam.transform;
        if ((camTransform.position - lastCameraPosition).sqrMagnitude > 1e-6f)
            return true;

        if (Quaternion.Angle(camTransform.rotation, lastCameraRotation) > 0.01f)
            return true;

        if ((transform.position - lastTransformPosition).sqrMagnitude > 1e-8f)
            return true;

        if (Quaternion.Angle(transform.rotation, lastTransformRotation) > 0.01f)
            return true;

        if ((transform.lossyScale - lastTransformScale).sqrMagnitude > 1e-8f)
            return true;

        return false;
    }

    private bool HasTransformChanged()
    {
        if ((transform.position - lastTransformPosition).sqrMagnitude > 1e-8f)
            return true;

        if (Quaternion.Angle(transform.rotation, lastTransformRotation) > 0.01f)
            return true;

        if ((transform.lossyScale - lastTransformScale).sqrMagnitude > 1e-8f)
            return true;

        return false;
    }

}
