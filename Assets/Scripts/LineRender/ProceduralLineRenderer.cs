using UnityEngine;
using System;

/// <summary>
/// Handles the creation and rendering of procedural lines using a mesh-based approach.
/// Supports dynamic updates of line positions, colors, and visibility, making it good for
/// trajectory rendering or debug visualization.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralLineRenderer : MonoBehaviour
{
    private Mesh lineMesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    [Header("Line Settings")]
    public float lineWidth = 0.5f;

    private Vector3[] lastDrawnPoints;

    public bool HasPoints => lastDrawnPoints != null && lastDrawnPoints.Length > 1;

    /// <summary>
    /// Initializes the ProceduralLineRenderer by setting up the mesh and material components.
    /// Ensures a default material is applied if none exists.
    /// </summary>
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
    /// Sets the color of the line by modifying the material's color.
    /// </summary>
    /// <param name="hexColor">A valid hex color string.</param>
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
    /// Sets the width of the line. Note: this method is a placeholder—
    /// MeshTopology.Lines does not support thick lines by default.
    /// </summary>
    /// <param name="width">The desired width of the line.</param>
    public void SetLineWidth(float width)
    {
        lineWidth = width;
        // changing lineWidth won't make thick lines automatically. 
        // Need a custom approach to create quads or geometry for thicker lines. 
        // This method is a placeholder for a potential later change.
    }

    /// <summary>
    /// Clears all existing line data from the mesh, effectively hiding the line.
    /// </summary>
    public void Clear()
    {
        lineMesh.Clear();
    }

    /// <summary>
    /// Updates the line mesh with a new set of points. Each point is connected
    /// sequentially in a line-strip style using MeshTopology.Lines.
    /// </summary>
    /// <param name="points">An array of points defining the line's shape.</param>
    public void UpdateLine(Vector3[] points)
    {
        if (points == null || points.Length < 2)
        {
            Clear();
            return;
        }

        int maxPoints = Math.Min(points.Length, 30000);
        lastDrawnPoints = new Vector3[maxPoints];

        for (int i = 0; i < maxPoints; i++)
        {
            points[i] = transform.InverseTransformPoint(points[i]);
            lastDrawnPoints[i] = points[i];
        }

        Vector3[] vertices = new Vector3[maxPoints];
        int[] indices = new int[(maxPoints - 1) * 2];

        for (int i = 0; i < maxPoints; i++)
        {
            vertices[i] = points[i];
        }

        for (int i = 0; i < maxPoints - 1; i++)
        {
            indices[i * 2] = i;
            indices[i * 2 + 1] = i + 1;
        }

        lineMesh.Clear();
        lineMesh.vertices = vertices;
        lineMesh.SetIndices(indices, MeshTopology.Lines, 0);
    }

    /// <summary>
    /// Sets the visibility of the line mesh renderer.
    /// </summary>
    /// <param name="isVisible">True to make the line visible, false to hide it.</param>
    public void SetVisibility(bool isVisible)
    {
        if (meshRenderer != null)
        {
            meshRenderer.enabled = isVisible;
        }
    }
}