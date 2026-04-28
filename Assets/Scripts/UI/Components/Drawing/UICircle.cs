using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class UICircle : Graphic
{
    [SerializeField, Range(8, 128)]
    private int _segments = 48;

    [SerializeField, Range(0.01f, 0.5f)]
    private float _radiusFactor = 0.075f; // 0.5 = touches edges of rect

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * _radiusFactor;

        // center vertex
        vh.AddVert(center, color, Vector2.zero);

        for (int i = 0; i <= _segments; i++)
        {
            float angle = (i / (float)_segments) * Mathf.PI * 2f;
            Vector2 pos = center + new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle)
            ) * radius;

            vh.AddVert(pos, color, Vector2.zero);

            if (i > 0)
            {
                vh.AddTriangle(0, i, i + 1);
            }
        }
    }
}
