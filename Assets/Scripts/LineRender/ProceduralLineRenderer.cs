using UnityEngine;

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

        // Ensure there's a MeshRenderer with a material
        var mr = GetComponent<MeshRenderer>();
        if (!mr.sharedMaterial)
        {
            mr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        }
    }

    public void UpdateLine(Vector3[] points)
    {
        if (points == null || points.Length < 2)
        {
            lineMesh.Clear();
            return;
        }
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = transform.InverseTransformPoint(points[i]); // Convert to local space
        }
        Vector3[] vertices = new Vector3[points.Length];
        int[] indices = new int[(points.Length - 1) * 2];



        // Use points directly
        for (int i = 0; i < points.Length; i++)
        {
            vertices[i] = points[i]; // Use the exact point
        }

        // Line strip (connect each point)
        for (int i = 0; i < points.Length - 1; i++)
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