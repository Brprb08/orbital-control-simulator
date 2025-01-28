using UnityEngine;
using System;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralLineRenderer : MonoBehaviour
{
    private Mesh lineMesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    [Header("Line Settings")]
    public float lineWidth = 0.1f;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        lineMesh = new Mesh { name = "LineMesh" };
        meshFilter.mesh = lineMesh;

        if (!meshRenderer.sharedMaterial)
        {
            meshRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        }
    }

    /// <summary>
    /// Dynamically sets the line color by updating the material's color.
    /// </summary>
    public void SetLineColor(string hexColor)
    {
        if (ColorUtility.TryParseHtmlString(hexColor, out Color color))
        {
            meshRenderer.material.color = color;
        }
        else
        {
            Debug.LogWarning($"Invalid hex color string: {hexColor}");
        }
    }

    /// <summary>
    /// Dynamically sets the line width (currently not used in mesh generation, see notes below).
    /// </summary>
    public void SetLineWidth(float width)
    {
        lineWidth = width;
        // NOTE: Because this uses MeshTopology.Lines, 
        // changing lineWidth won't make thick lines automatically. 
        // You'd need a custom approach to create quads or geometry for thicker lines. 
        // This method is here for convenience if you later implement a thicker line solution.
    }

    /// <summary>
    /// Clears the line mesh.
    /// </summary>
    public void Clear()
    {
        lineMesh.Clear();
    }

    /// <summary>
    /// Updates (or creates) the line mesh from an array of points.
    /// Each point is connected sequentially in a line-strip style.
    /// </summary>
    public void UpdateLine(Vector3[] points)
    {
        // Edge case: if invalid or too few points, clear mesh
        if (points == null || points.Length < 2)
        {
            Clear();
            return;
        }

        // Safety clamp to avoid massive arrays
        int maxPoints = Math.Min(points.Length, 30000);

        // Convert world positions to local positions so the mesh
        // will move/rotate with the parent object
        for (int i = 0; i < maxPoints; i++)
        {
            points[i] = transform.InverseTransformPoint(points[i]);
        }

        // Prepare arrays
        Vector3[] vertices = new Vector3[maxPoints];
        int[] indices = new int[(maxPoints - 1) * 2];

        // Assign vertices
        for (int i = 0; i < maxPoints; i++)
        {
            vertices[i] = points[i];
        }

        // Build line indices (pairwise from point i to i+1)
        for (int i = 0; i < maxPoints - 1; i++)
        {
            indices[i * 2] = i;
            indices[i * 2 + 1] = i + 1;
        }

        // Update mesh
        lineMesh.Clear();
        lineMesh.vertices = vertices;
        lineMesh.SetIndices(indices, MeshTopology.Lines, 0);
    }

    public void SetVisibility(bool isVisible)
    {
        if (meshRenderer != null)
        {
            meshRenderer.enabled = isVisible;
        }
    }
}