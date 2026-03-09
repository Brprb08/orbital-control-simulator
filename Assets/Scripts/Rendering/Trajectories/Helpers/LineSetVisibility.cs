using UnityEngine;

public static class LineSetVisibility
{
    public static void Set(ProceduralLineRenderer line, bool visible)
    {
        if (!line) return;
#if UNITY_2021_2_OR_NEWER
        var r = line.GetComponent<Renderer>();
        if (r != null) r.forceRenderingOff = !visible;
#else
        var r = line.GetComponent<Renderer>();
        if (r != null) r.enabled = visible;
#endif
    }

    public static void Clear(params ProceduralLineRenderer[] lines)
    {
        if (lines == null) return;
        for (int i = 0; i < lines.Length; i++)
            lines[i]?.Clear();
    }

    public static void SetAll(bool visible, params ProceduralLineRenderer[] lines)
    {
        if (lines == null) return;
        for (int i = 0; i < lines.Length; i++)
            Set(lines[i], visible);
    }
}