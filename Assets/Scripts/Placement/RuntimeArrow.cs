using UnityEngine;

/// <summary>
/// Simple runtime 3D arrow (cylinder shaft + cone head), aligned along +Y in local space.
/// Call Show(start, end[, thickness, headLen, headRadius]) while you want it visible.
/// </summary>
public class RuntimeArrow : MonoBehaviour
{
    [Header("Defaults")]
    [SerializeField] private float defaultHeadLength = 2f;
    [SerializeField] private float defaultThickness = 0.10f;
    [SerializeField] private float defaultHeadRadius = 0.15f;
    [SerializeField] private Color color = Color.cyan;

    private Transform shaftTf;
    private Transform headTf;

    private Material mat;
    private Mesh shaftMesh; // unit cylinder: radius 0.5, height 1, y=0..1, +Y
    private Mesh headMesh;  // unit cone: base radius 0.5 (y=0), tip at y=1, +Y
    private bool initialized;

    private void Awake()
    {
        if (!initialized) Init();
    }

    private void OnDestroy()
    {
        if (Application.isPlaying)
        {
            if (shaftMesh) Destroy(shaftMesh);
            if (headMesh) Destroy(headMesh);
            if (mat) Destroy(mat);
        }
        else
        {
            if (shaftMesh) DestroyImmediate(shaftMesh);
            if (headMesh) DestroyImmediate(headMesh);
            if (mat) DestroyImmediate(mat);
        }
    }

    /// <summary>
    /// Builds meshes and shared material on first use.
    /// </summary>
    private void Init()
    {
        initialized = true;

        // material (URP Lit if present, else Standard)
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        mat = new Material(sh);
        mat.doubleSidedGI = true;
        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        SetColor(color);

        // meshes
        shaftMesh = MakeUnitCylinder(24);
        headMesh = MakeUnitCone(24);

        // shaft
        var shaftGo = new GameObject("Arrow_Shaft");
        shaftGo.transform.SetParent(transform, false);
        shaftTf = shaftGo.transform;
        var shaftMr = shaftGo.AddComponent<MeshRenderer>();
        var shaftMf = shaftGo.AddComponent<MeshFilter>();
        shaftMf.sharedMesh = shaftMesh;
        shaftMr.sharedMaterial = mat;

        // head
        var headGo = new GameObject("Arrow_Head");
        headGo.transform.SetParent(transform, false);
        headTf = headGo.transform;
        var headMr = headGo.AddComponent<MeshRenderer>();
        var headMf = headGo.AddComponent<MeshFilter>();
        headMf.sharedMesh = headMesh;
        headMr.sharedMaterial = mat;

        Hide();
    }

    /// <summary>
    /// Updates arrow color on the shared material.
    /// </summary>
    public void SetColor(Color c)
    {
        color = c;
        if (mat == null) return;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.2f);
    }

    /// <summary>
    /// Show/update arrow from world start→end.
    /// Thickness/head parameters fall back to defaults if negative.
    /// </summary>
    public void Show(Vector3 start, Vector3 end, float thickness = -1f, float headLen = -1f, float headRadius = -1f)
    {
        if (!initialized) Init();

        thickness = (thickness > 0f) ? thickness : defaultThickness;
        headLen = (headLen > 0f) ? headLen : defaultHeadLength;
        headRadius = (headRadius > 0f) ? headRadius : defaultHeadRadius;

        Vector3 dir = end - start;
        float totalLen = dir.magnitude;
        if (totalLen < 1e-5f)
        {
            Hide();
            return;
        }

        Vector3 fwd = dir / totalLen;

        // keep the head from swallowing the whole arrow
        headLen = Mathf.Min(headLen, totalLen * 0.5f);
        float shaftLen = Mathf.Max(0f, totalLen - headLen);

        Quaternion rot = Quaternion.FromToRotation(Vector3.up, fwd);

        // shaft
        bool showShaft = shaftLen > 1e-4f;
        shaftTf.gameObject.SetActive(showShaft);
        if (showShaft)
        {
            shaftTf.position = start;
            shaftTf.rotation = rot;
            shaftTf.localScale = new Vector3(thickness, shaftLen, thickness);
        }

        // head
        headTf.gameObject.SetActive(true);
        headTf.position = start + fwd * shaftLen;
        headTf.rotation = rot;
        headTf.localScale = new Vector3(headRadius, headLen, headRadius);

        gameObject.SetActive(true);
    }

    /// <summary>
    /// Completely hides the arrow.
    /// </summary>
    public void Hide()
    {
        if (!initialized) Init();
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Unit cylinder: base y=0, top y=1, radius 0.5, aligned +Y.
    /// </summary>
    private Mesh MakeUnitCylinder(int sides)
    {
        sides = Mathf.Clamp(sides, 3, 128);

        int ring = sides + 1;
        int vCount = ring * 2 + 2; // bottom ring + top ring + 2 centers

        var vertices = new Vector3[vCount];
        var normals = new Vector3[vCount];
        var uvs = new Vector2[vCount];

        float r = 0.5f;

        // side rings
        for (int i = 0; i <= sides; i++)
        {
            float t = i / (float)sides;
            float a = t * Mathf.PI * 2f;
            float x = Mathf.Cos(a) * r;
            float z = Mathf.Sin(a) * r;

            vertices[i] = new Vector3(x, 0f, z);
            vertices[i + ring] = new Vector3(x, 1f, z);

            Vector3 n = new Vector3(x, 0f, z).normalized;
            normals[i] = n;
            normals[i + ring] = n;

            uvs[i] = new Vector2(t, 0f);
            uvs[i + ring] = new Vector2(t, 1f);
        }

        int bottomCenter = vCount - 2;
        int topCenter = vCount - 1;

        vertices[bottomCenter] = new Vector3(0f, 0f, 0f);
        vertices[topCenter] = new Vector3(0f, 1f, 0f);
        normals[bottomCenter] = Vector3.down;
        normals[topCenter] = Vector3.up;
        uvs[bottomCenter] = new Vector2(0.5f, 0.5f);
        uvs[topCenter] = new Vector2(0.5f, 0.5f);

        var tris = new System.Collections.Generic.List<int>();

        // side quads
        for (int i = 0; i < sides; i++)
        {
            int i0 = i;
            int i1 = i + 1;
            int i2 = i + ring;
            int i3 = i + 1 + ring;

            tris.Add(i0); tris.Add(i2); tris.Add(i1);
            tris.Add(i1); tris.Add(i2); tris.Add(i3);
        }

        // bottom cap
        for (int i = 0; i < sides; i++)
        {
            int i0 = i;
            int i1 = i + 1;
            tris.Add(bottomCenter); tris.Add(i1); tris.Add(i0);
        }

        // top cap
        for (int i = 0; i < sides; i++)
        {
            int i0 = i + ring;
            int i1 = i + 1 + ring;
            tris.Add(topCenter); tris.Add(i0); tris.Add(i1);
        }

        var m = new Mesh
        {
            name = "UnitCylinder_Y0to1"
        };
        m.SetVertices(vertices);
        m.SetNormals(normals);
        m.SetUVs(0, uvs);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }

    /// <summary>
    /// Unit cone: base at y=0 (radius 0.5), tip at y=1, aligned +Y.
    /// </summary>
    private Mesh MakeUnitCone(int sides)
    {
        sides = Mathf.Clamp(sides, 3, 128);

        int ring = sides + 1;

        var vertices = new System.Collections.Generic.List<Vector3>(ring + 2);
        var normals = new System.Collections.Generic.List<Vector3>(ring + 2);
        var uvs = new System.Collections.Generic.List<Vector2>(ring + 2);

        float r = 0.5f;

        // base ring
        for (int i = 0; i <= sides; i++)
        {
            float t = i / (float)sides;
            float a = t * Mathf.PI * 2f;
            float x = Mathf.Cos(a) * r;
            float z = Mathf.Sin(a) * r;

            vertices.Add(new Vector3(x, 0f, z));
            // slightly up-tilted normal
            Vector3 n = new Vector3(x, r, z).normalized;
            normals.Add(n);
            uvs.Add(new Vector2(t, 0f));
        }

        // tip
        int tipIndex = vertices.Count;
        vertices.Add(new Vector3(0f, 1f, 0f));
        normals.Add(Vector3.up);
        uvs.Add(new Vector2(0.5f, 1f));

        // base center
        int baseCenter = vertices.Count;
        vertices.Add(new Vector3(0f, 0f, 0f));
        normals.Add(Vector3.down);
        uvs.Add(new Vector2(0.5f, 0.5f));

        var tris = new System.Collections.Generic.List<int>();

        // sides
        for (int i = 0; i < sides; i++)
        {
            int i0 = i;
            int i1 = i + 1;
            tris.Add(i0); tris.Add(tipIndex); tris.Add(i1);
        }

        // base cap
        for (int i = 0; i < sides; i++)
        {
            int i0 = i;
            int i1 = i + 1;
            tris.Add(baseCenter); tris.Add(i1); tris.Add(i0);
        }

        var m = new Mesh
        {
            name = "UnitCone_Y0to1"
        };
        m.SetVertices(vertices);
        m.SetNormals(normals);
        m.SetUVs(0, uvs);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }
}
