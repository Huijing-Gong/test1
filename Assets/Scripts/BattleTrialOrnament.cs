using UnityEngine;
using UnityEngine.UI;

// Small, resolution-independent menu motifs. No textures or additional lights.
// All geometry is decorative and deliberately excluded from UI raycasts.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattleTrialOrnament : MaskableGraphic
{
    public BattleTrialOrnament() { useLegacyMeshGeneration = false; }
    public enum Motif { Frame, Rule, Sun, Leaf, Technology, Core, Pill, Diamond, Wash, Orbit, Chevron, HollowDiamond }
    public Motif shape;
    public Color accent = new Color32(202, 176, 111, 255);
    public float stroke = 1.4f;
    Vector2 size;
    VertexHelper mesh;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); mesh = vh; size = rectTransform.rect.size;
        if (size.x <= 0 || size.y <= 0) return;
        switch (shape)
        {
            case Motif.Frame:
                CutRect(3, 3, size.x - 6, size.y - 6, 13, color, false);
                CutRect(8, 8, size.x - 16, size.y - 16, 10, WithAlpha(color, .28f), false);
                Diamond(new Vector2(size.x * .5f, 3), 4, accent);
                break;
            case Motif.Rule:
                Line(new Vector2(0, size.y * .5f), new Vector2(size.x * .46f, size.y * .5f), color);
                Line(new Vector2(size.x * .54f, size.y * .5f), new Vector2(size.x, size.y * .5f), color);
                Diamond(size * .5f, 6, accent);
                Diamond(size * .5f, 2.8f, new Color32(246, 235, 207, 255));
                break;
            case Motif.Pill:
                CutRect(1, 1, size.x - 2, size.y - 2, 9, color, true);
                CutRect(1, 1, size.x - 2, size.y - 2, 9, accent, false);
                break;
            case Motif.Diamond: Diamond(size * .5f, Mathf.Min(size.x, size.y) * .5f, color); break;
            case Motif.Wash:
                // Fade the text scrim out smoothly towards the scenery.
                for (int i = 0; i < 24; i++)
                {
                    float a = i / 24f, b = (i + 1) / 24f;
                    Color ca = WithAlpha(color, 1f - a * a), cb = WithAlpha(color, 1f - b * b);
                    Quad(new Vector2(a * size.x, 0), new Vector2(b * size.x, 0),
                        new Vector2(b * size.x, size.y), new Vector2(a * size.x, size.y), ca, cb, cb, ca);
                }
                break;
            case Motif.Sun: Sun(); break;
            case Motif.Leaf: Leaf(); break;
            case Motif.Technology: Technology(); break;
            case Motif.Core: Core(); break;
            case Motif.Orbit: Circle(size * .5f, size.x * .48f, size.y * .40f, 64, color); break;
            case Motif.Chevron:
                Line(At(.2f, .1f), At(.75f, .5f), color, 4);
                Line(At(.75f, .5f), At(.2f, .9f), color, 4);
                break;
            case Motif.HollowDiamond:
                float r = Mathf.Min(size.x, size.y) * .5f;
                Diamond(size * .5f, r, color);
                Diamond(size * .5f, r * .45f, new Color32(246, 235, 207, 255));
                break;
        }
    }
    static Color WithAlpha(Color c, float factor) { c.a *= factor; return c; }
    void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
    {
        int n = mesh.currentVertCount;
        mesh.AddVert(Position(a), ca, Vector2.zero);
        mesh.AddVert(Position(b), cb, Vector2.zero);
        mesh.AddVert(Position(c), cc, Vector2.zero);
        mesh.AddVert(Position(d), cd, Vector2.zero);
        mesh.AddTriangle(n, n + 1, n + 2); mesh.AddTriangle(n + 2, n + 3, n);
    }
    void Line(Vector2 a, Vector2 b, Color c, float width = 0)
    {
        Vector2 delta = b - a;
        if (delta.sqrMagnitude < .001f) return;
        Vector2 n = new Vector2(-delta.y, delta.x).normalized * ((width > 0 ? width : stroke) * .5f);
        Quad(a - n, b - n, b + n, a + n, c, c, c, c);
    }
    void Diamond(Vector2 p, float r, Color c)
    { Quad(p + Vector2.left * r, p + Vector2.up * r, p + Vector2.right * r, p + Vector2.down * r, c, c, c, c); }
    void Poly(Vector2[] points, Color c, bool fill = false)
    {
        if (!fill) { for (int i = 0; i < points.Length; i++) Line(points[i], points[(i + 1) % points.Length], c); return; }
        Vector2 center = Vector2.zero; foreach (var p in points) center += p; center /= points.Length;
        int n = mesh.currentVertCount;
        mesh.AddVert(Position(center), c, Vector2.zero);
        foreach (var p in points) mesh.AddVert(Position(p), c, Vector2.zero);
        for (int i = 0; i < points.Length; i++) mesh.AddTriangle(n, n + 1 + i, n + 1 + (i + 1) % points.Length);
    }
    Vector3 Position(Vector2 point) => new Vector3(point.x + rectTransform.rect.xMin,
        -point.y + rectTransform.rect.yMax, 0);
    void CutRect(float x, float y, float w, float h, float cut, Color c, bool fill)
    {
        Poly(new[] { new Vector2(x + cut, y), new Vector2(x + w - cut, y), new Vector2(x + w, y + cut),
            new Vector2(x + w, y + h - cut), new Vector2(x + w - cut, y + h), new Vector2(x + cut, y + h),
            new Vector2(x, y + h - cut), new Vector2(x, y + cut) }, c, fill);
    }
    void Circle(Vector2 p, float rx, float ry, int count, Color c)
    {
        Vector2 previous = p + Vector2.right * rx;
        for (int i = 1; i <= count; i++)
        {
            float angle = i * Mathf.PI * 2 / count;
            Vector2 next = p + new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry);
            Line(previous, next, c); previous = next;
        }
    }
    Vector2 At(float x, float y) => new Vector2(x * size.x, y * size.y);
    void Sun()
    {
        var p = size * .5f; float r = Mathf.Min(size.x, size.y);
        Circle(p, r * .26f, r * .26f, 32, color);
        Circle(p, r * .15f, r * .15f, 24, WithAlpha(color, .6f));
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var n = new Vector2(-d.y, d.x);
            Poly(new[] { p + d * r * .31f - n * r * .045f, p + d * r * .47f, p + d * r * .31f + n * r * .045f }, color);
        }
    }
    void Leaf()
    {
        Poly(new[] { At(.5f, .05f), At(.73f, .34f), At(.64f, .62f), At(.5f, .82f), At(.36f, .62f), At(.27f, .34f) }, color);
        Poly(new[] { At(.12f, .38f), At(.4f, .52f), At(.46f, .82f), At(.23f, .67f) }, color);
        Poly(new[] { At(.88f, .38f), At(.6f, .52f), At(.54f, .82f), At(.77f, .67f) }, color);
        Line(At(.5f, .20f), At(.5f, .94f), color);
        Line(At(.21f, .5f), At(.5f, .86f), color); Line(At(.79f, .5f), At(.5f, .86f), color);
    }
    void Technology()
    {
        Poly(new[] { At(.5f, .06f), At(.96f, .86f), At(.04f, .86f) }, color);
        Poly(new[] { At(.5f, .24f), At(.76f, .73f), At(.24f, .73f) }, color);
        Circle(At(.5f, .56f), size.x * .12f, size.y * .12f, 24, color);
        Line(At(.5f, .06f), At(.5f, .24f), color);
        Line(At(.04f, .86f), At(.24f, .73f), color); Line(At(.96f, .86f), At(.76f, .73f), color);
        Diamond(At(.5f, .56f), 4, accent);
    }
    void Core()
    {
        float r = Mathf.Min(size.x, size.y); var center = size * .5f;
        Circle(center, r * .43f, r * .43f, 48, color);
        Circle(center, r * .34f, r * .34f, 40, WithAlpha(color, .65f));
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4; var p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * .43f;
            Diamond(p, r * .055f, color);
        }
        Poly(new[] { At(.5f, .17f), At(.67f, .4f), At(.61f, .68f), At(.5f, .81f), At(.39f, .68f), At(.33f, .4f) }, WithAlpha(accent, .22f), true);
        Poly(new[] { At(.5f, .17f), At(.67f, .4f), At(.61f, .68f), At(.5f, .81f), At(.39f, .68f), At(.33f, .4f) }, accent);
        Line(At(.5f, .25f), At(.5f, .74f), accent, 2); Line(At(.38f, .4f), At(.62f, .4f), accent);
    }
}
