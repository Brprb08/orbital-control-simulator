using UnityEngine;
using System;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralLineRenderer : MonoBehaviour
{
    private Mesh lineMesh;
    private MeshFilter meshFilter;

    public float lineWidth = 0.1f;

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

    public void UpdateLine(Vector3[] points)
    {
        Debug.LogError("HERE");
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

    public void Clear()
    {
        lineMesh.Clear();
    }
}