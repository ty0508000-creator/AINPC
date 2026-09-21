using UnityEngine;
using UnityEngine.UI;

/// <summary>Small vector emblems; no external textures or font glyphs required.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public class RpgSkillEmblem : MaskableGraphic
{
    public int Branch;
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (Branch == 0)
        {
            Stroke(vh, new Vector2(-0.32f, -0.32f), new Vector2(0.30f, 0.34f), 0.10f);
            Stroke(vh, new Vector2(-0.28f, -0.05f), new Vector2(-0.06f, -0.28f), 0.055f);
            Stroke(vh, new Vector2(0.16f, 0.34f), new Vector2(0.30f, 0.34f), 0.035f);
        }
        else if (Branch == 1)
        {
            Vector2[] points = { new Vector2(-0.32f, 0.27f), new Vector2(0, 0.37f), new Vector2(0.32f, 0.27f),
                new Vector2(0.25f, -0.16f), new Vector2(0, -0.37f), new Vector2(-0.25f, -0.16f) };
            for (int i = 0; i < points.Length; i++) Stroke(vh, points[i], points[(i + 1) % points.Length], 0.045f);
            Stroke(vh, new Vector2(0, -0.2f), new Vector2(0, 0.2f), 0.04f);
        }
        else
        {
            for (int i = 0; i < 6; i++)
            {
                float angle = Mathf.PI * i / 3;
                Vector2 tip = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 0.37f;
                Vector2 left = new Vector2(Mathf.Sin(angle - 0.5f), Mathf.Cos(angle - 0.5f)) * 0.18f;
                Vector2 right = new Vector2(Mathf.Sin(angle + 0.5f), Mathf.Cos(angle + 0.5f)) * 0.18f;
                Stroke(vh, left, tip, 0.035f); Stroke(vh, tip, right, 0.035f);
            }
        }
    }
    void Stroke(VertexHelper vh, Vector2 a, Vector2 b, float width)
    {
        Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * width / 2;
        Vector2[] points = { a - normal, a + normal, b + normal, b - normal };
        int start = vh.currentVertCount; Rect rect = rectTransform.rect;
        foreach (Vector2 p in points)
        {
            var vertex = UIVertex.simpleVert;
            vertex.position = new Vector3(rect.center.x + p.x * rect.width, rect.center.y + p.y * rect.height, 0);
            vertex.color = color; vh.AddVert(vertex);
        }
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }
}
