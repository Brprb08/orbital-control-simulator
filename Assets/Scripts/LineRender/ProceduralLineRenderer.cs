using UnityEngine;
using System;

/**
* This class handles the creation and rendering of procedural lines using a mesh-based approach.
* It supports dynamic updates of line positions, colors, and visibility, making it ideal for
* applications like trajectory rendering or debug visualization.
**/
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralLineRenderer : MonoBehaviour
{
    private Mesh lineMesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    [Header("Line Settings")]
    public float lineWidth = 0.1f;

    private Vector3[] lastDrawnPoints;

    public bool HasPoints => lastDrawnPoints != null && lastDrawnPoints.Length > 1;

    /**
    * Initializes the ProceduralLineRenderer by setting up the mesh and material components.
    * Ensures a default material is applied if none exists.
    **/
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

    /**
    * Sets the color of the line by modifying the material's color.
    * @param hexColor -  A valid hex color string (e.g., "#FF0000" for red).
    **/
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

    /**
    * Sets the width of the line. This property does not currently affect
    * the rendered line due to limitations of MeshTopology.Lines.
    * @param width  - The desired width of the line.
    **/
    public void SetLineWidth(float width)
    {
        lineWidth = width;
        // changing lineWidth won't make thick lines automatically. 
        // Need a custom approach to create quads or geometry for thicker lines. 
        // This method is a placeholder for a potential later change.
    }

    /**
    * Clears all existing line data from the mesh, effectively hiding the line.
    **/
    public void Clear()
    {
        lineMesh.Clear();
    }

    /**
    * Updates the line mesh with a new set of points. Each point is connected
    * sequentially in a line-strip style.
    * @param points  - An array of points defining the line's shape.
    **/
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

    public void SetVisibility(bool isVisible)
    {
        if (meshRenderer != null)
        {
            meshRenderer.enabled = isVisible;
        }
    }
}