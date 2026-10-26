using UnityEngine;
using UnityEngine.UI;

// Painted nine-slice sprites for large torn-paper faces; the tiny native-mesh
// fallback remains useful for chips. Neither path needs screen-sized bitmaps.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattleTrialPaperGraphic : Image
{
    public Texture2D grain;
    public Color outline = new Color32(22, 61, 53, 255);
    public float border = 5;
    public bool torn = true;
    public bool insetRim;
    public BattleTrialPaperGraphic() { useLegacyMeshGeneration = false; }
    public override Texture mainTexture => sprite ? sprite.texture : grain ? grain : Texture2D.whiteTexture;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        if (sprite) { base.OnPopulateMesh(vh); return; }
        vh.Clear();
        Vector2 size = rectTransform.rect.size;
        if (size.x <= 0 || size.y <= 0) return;
        Color edge = outline; edge.a *= color.a;
        Polygon(vh, Outline(size, 0), edge);
        Polygon(vh, Outline(size, border), color);
        if (insetRim)
        {
            var path = Outline(size, border + 5);
            Color rim = new Color(1, .96f, .78f, .45f * color.a);
            for (int i = 0; i < path.Length; i++) Line(vh, path[i], path[(i + 1) % path.Length], rim, 1.2f);
        }
    }
    Vector2[] Outline(Vector2 size, float margin)
    {
        float w = Mathf.Max(1, size.x - margin * 2), h = Mathf.Max(1, size.y - margin * 2);
        float cut = Mathf.Min(18, h * .28f);
        Vector2[] p;
        if (torn)
            p = new[] { new Vector2(cut, 3), new Vector2(w * .29f, 0), new Vector2(w * .30f, 3),
                new Vector2(w * .54f, 1), new Vector2(w * .55f, 4), new Vector2(w - cut, 2),
                new Vector2(w - 1, cut), new Vector2(w - 3, h * .45f), new Vector2(w, h * .46f),
                new Vector2(w - 2, h - cut), new Vector2(w - cut, h - 2), new Vector2(w * .67f, h),
                new Vector2(w * .66f, h - 3), new Vector2(w * .37f, h - 1), new Vector2(w * .36f, h - 4),
                new Vector2(cut, h - 2), new Vector2(2, h - cut), new Vector2(4, h * .6f),
                new Vector2(0, h * .59f), new Vector2(1, cut) };
        else p = new[] { new Vector2(cut, 0), new Vector2(w - cut, 0), new Vector2(w, cut),
            new Vector2(w, h - cut), new Vector2(w - cut, h), new Vector2(cut, h), new Vector2(0, h - cut), new Vector2(0, cut) };
        for (int i = 0; i < p.Length; i++) p[i] += Vector2.one * margin;
        return p;
    }
    Vector3 Position(Vector2 p)
    {
        var r = rectTransform.rect;
        return new Vector3(r.xMin + p.x, r.yMax - p.y, 0);
    }
    void Vertex(VertexHelper vh, Vector2 p, Color c)
    { vh.AddVert(Position(p), c, p / 256f); }
    void Polygon(VertexHelper vh, Vector2[] p, Color c)
    {
        int start = vh.currentVertCount;
        Vector2 center = Vector2.zero; foreach (var point in p) center += point; center /= p.Length;
        Vertex(vh, center, c);
        foreach (var point in p) Vertex(vh, point, c);
        for (int i = 0; i < p.Length; i++) vh.AddTriangle(start, start + i + 1, start + (i + 1) % p.Length + 1);
    }
    void Line(VertexHelper vh, Vector2 a, Vector2 b, Color c, float width)
    {
        Vector2 delta = b - a; if (delta.sqrMagnitude < .001f) return;
        Vector2 n = new Vector2(-delta.y, delta.x).normalized * width * .5f;
        int start = vh.currentVertCount;
        Vertex(vh, a - n, c); Vertex(vh, b - n, c); Vertex(vh, b + n, c); Vertex(vh, a + n, c);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start + 2, start + 3, start);
    }
}
