using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;

/// <summary>
/// Mesh-based polyline renderer that builds a camera-facing quad strip.
/// Points are passed in world space via UpdateLine, but the mesh is built
/// in local space so this object can be parented/scaled normally.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralLineRenderer : MonoBehaviour
{
    [Header("Line Settings")]
    [Tooltip("Base width in world units near the camera.")]
    [Min(0.0001f)]
    public float lineWidth = 0.003f;

    [Tooltip("If true, width scales with distance so the line stays readable when you zoom out.")]
    public bool approximateScreenSpaceWidth = true;

    [Tooltip("Safety limit for how many input points we will accept.")]
    public int maxPoints = 300000;

    [Header("Material")]
    [Tooltip("Optional material override. If left empty, a Sprites/Default material is created.")]
    [SerializeField] private Material lineMaterial;

    [Header("Appearance")]
    [Range(0f, 1f)]
    public float defaultAlpha = 0.45f;

    [Header("Smoothing")]
    [Tooltip("If true, long segments are subdivided once in UpdateLine (not every frame).")]
    public bool enableSmoothing = true;

    [Tooltip("Desired maximum world-space length per segment after smoothing.")]
    [Min(0.01f)]
    public float targetSegmentLength = 1.5f;

    [Tooltip("Hard cap on total smoothed points (for safety).")]
    public int maxSmoothedPoints = 200_000;

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
    private Vector2[] uvs;
    private int[] indices;
    private float[] segmentLengths;

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
            var mat = new Material(Shader.Find("Sprites/Default"));
            if (!mat.shader)
                Debug.LogWarning("[ProceduralLineRenderer] Sprites/Default shader not found.");
            meshRenderer.sharedMaterial = mat;
        }

        ConfigureMaterial(meshRenderer.sharedMaterial);

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
        }
        else
        {
            Debug.LogWarning($"[ProceduralLineRenderer] Invalid hex color string: {hexColor}");
        }
    }

    public void SetLineWidth(float width)
    {
        lineWidth = Mathf.Max(0.0001f, width);
    }

    public void Clear()
    {
        if (lineMesh != null)
            lineMesh.Clear();

        worldPoints = null;
        smoothedWorldPoints = null;
    }

    /// <summary>
    /// Update the line points in world space. Smoothing (if enabled) happens here once,
    /// not every frame.
    /// </summary>
    public void UpdateLine(Vector3[] points)
    {
        if (points == null || points.Length < 2)
        {
            Clear();
            return;
        }

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
            int smoothCount = BuildSmoothedPoints(worldPoints, baseCount);

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
    }

    /// <summary>
    /// Subdivides long segments into smaller ones (once per UpdateLine).
    /// Uses a reusable List to avoid GC.
    /// Returns the smoothed point count in the smoothingBuffer.
    /// </summary>
    private int BuildSmoothedPoints(Vector3[] src, int count)
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

        for (int i = 1; i < count; i++)
        {
            Vector3 a = src[i - 1];
            Vector3 b = src[i];
            float segLen = Vector3.Distance(a, b);

            int steps = Mathf.Max(1, Mathf.FloorToInt(segLen / targetSegmentLength));
            steps = Mathf.Clamp(steps, 1, maxSubdivisionsPerSegment);

            int extra = steps;
            if (totalCount + extra > maxSmoothedPoints)
            {
                smoothingBuffer.Add(b);
                break;
            }

            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                Vector3 p = Vector3.Lerp(a, b, t);
                smoothingBuffer.Add(p);
                totalCount++;
            }
        }

        return smoothingBuffer.Count;
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

        var cam = Camera.main;
        if (!cam) return;

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

        int vertCount = count * 2;
        int triCount = (count - 1) * 2;
        int indexCount = triCount * 3;

        if (vertices == null || vertices.Length != vertCount)
            vertices = new Vector3[vertCount];

        if (uvs == null || uvs.Length != vertCount)
            uvs = new Vector2[vertCount];

        if (indices == null || indices.Length != indexCount)
            indices = new int[indexCount];

        if (segmentLengths == null || segmentLengths.Length != count)
            segmentLengths = new float[count];

        Vector3 camPos = cam.transform.position;

        float totalLength = 0f;
        segmentLengths[0] = 0f;
        for (int i = 1; i < count; i++)
        {
            float segLen = Vector3.Distance(smoothedWorldPoints[i - 1], smoothedWorldPoints[i]);
            totalLength += segLen;
            segmentLengths[i] = totalLength;
        }

        float invTotalLength = totalLength > 1e-5f ? 1f / totalLength : 0f;

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

            Vector3 viewDir = camPos - pWorld;
            if (viewDir.sqrMagnitude < 1e-8f)
                viewDir = -cam.transform.forward;
            viewDir.Normalize();

            Vector3 side = Vector3.Cross(dir, viewDir);
            if (side.sqrMagnitude < 1e-6f)
            {
                Vector3 alt = Mathf.Abs(dir.y) < 0.99f ? Vector3.up : Vector3.right;
                side = Vector3.Cross(dir, alt);
            }
            if (side.sqrMagnitude < 1e-8f)
                side = Vector3.right;
            side.Normalize();

            float width = lineWidth;
            if (approximateScreenSpaceWidth)
            {
                float dist = Vector3.Distance(camPos, pWorld);
                width = Mathf.Max(0.0001f, lineWidth * dist * 0.04f);
            }

            Vector3 offsetWorld = side * (width * 0.5f);

            int v0 = i * 2;
            int v1 = v0 + 1;

            vertices[v0] = transform.InverseTransformPoint(pWorld - offsetWorld);
            vertices[v1] = transform.InverseTransformPoint(pWorld + offsetWorld);

            float t = segmentLengths[i] * invTotalLength;
            uvs[v0] = new Vector2(0f, t);
            uvs[v1] = new Vector2(1f, t);
        }

        int idx = 0;
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

        lineMesh.Clear();
        lineMesh.vertices = vertices;
        lineMesh.uv = uvs;
        lineMesh.triangles = indices;
        lineMesh.RecalculateBounds();
    }
}
