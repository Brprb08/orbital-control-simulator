using UnityEngine;
using System;

/**
* A component for rendering lines procedurally using a `Mesh`. 
* This class allows for dynamically updating a line based on an array of points 
* and supports customization of line width.
**/
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralLineRenderer : MonoBehaviour
{
    private Mesh lineMesh;
    private MeshFilter meshFilter;

    public float lineWidth = 0.1f;

    /**
    * Initializes the `Mesh` and assigns it to the `MeshFilter` component.
    **/
    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        lineMesh = new Mesh { name = "LineMesh" };
        meshFilter.mesh = lineMesh;

        var mr = GetComponent<MeshRenderer>();
        if (!mr.sharedMaterial)
        {
            mr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        }
    }

    /**
    * Updates the line based on an array of points. Each point defines a vertex
    * in the line, and the method connects them sequentially.
    *
    * @param points An array of points that define the line. Must contain at least 2 points.
    *               If fewer points are provided, the line will be cleared.
    **/
    public void UpdateLine(Vector3[] points)
    {
        if (points == null || points.Length < 2)
        {
            lineMesh.Clear();
            return;
        }

        int maxPoints = Math.Min(points.Length, 30000);
        for (int i = 0; i < maxPoints; i++)
        {
            points[i] = transform.InverseTransformPoint(points[i]);
        }

        Vector3[] vertices = new Vector3[maxPoints];
        int[] indices = new int[(maxPoints - 1) * 2];

        for (int i = 0; i < maxPoints; i++)
        {
            vertices[i] = points[i];
        }

        // Line strip (connect each point)
        for (int i = 0; i < maxPoints - 1; i++)
        {
            indices[i * 2] = i;
            indices[i * 2 + 1] = i + 1;
        }
        lineMesh.Clear();
        lineMesh.vertices = vertices;
        lineMesh.SetIndices(indices, MeshTopology.Lines, 0);
    }

    /**
    * Clears the line by resetting the underlying `Mesh`.
    * This removes all previously rendered line data.
    **/
    public void Clear()
    {
        lineMesh.Clear();
    }
}