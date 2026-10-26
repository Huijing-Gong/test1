using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Small native UGUI shapes; no large panel textures, extra shaders or lights.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattleHudShape : MaskableGraphic
{
    public enum Kind { Capsule, Card, Circle, Diamond, Star, Triangle, Pause, Clipped, Launch }
    public Kind shape;
    public Color rim = new Color32(236, 224, 178, 255);
    public float stroke = 2.5f;
    public float cornerRatio = .48f;
    public bool mirrored;
    public BattleHudShape() { useLegacyMeshGeneration = false; }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var rect = rectTransform.rect;
        if (rect.width <= 0 || rect.height <= 0) return;
        if (shape == Kind.Launch)
        {
            // A fighter being launched diagonally away from a short ledge.
            Vector2 P(float x, float y) => new Vector2(rect.xMin + rect.width*x, rect.yMin + rect.height*y);
            Bar(vh, new Rect(P(.02f,.06f), new Vector2(rect.width*.47f,rect.height*.10f)), color);
            Fill(vh, new List<Vector2> { P(.15f,.32f),P(.33f,.25f),P(.46f,.48f),P(.28f,.56f) }, color);
            Fill(vh, new List<Vector2> { P(.38f,.54f),P(.47f,.45f),P(.76f,.75f),P(.67f,.84f) }, color);
            Fill(vh, new List<Vector2> { P(.58f,.94f),P(.95f,.96f),P(.93f,.58f) }, color);
            return;
        }
        if (shape == Kind.Pause)
        {
            Bar(vh, new Rect(rect.xMin + rect.width * .22f, rect.yMin, rect.width * .16f, rect.height), color);
            Bar(vh, new Rect(rect.xMin + rect.width * .62f, rect.yMin, rect.width * .16f, rect.height), color);
            return;
        }
        if (stroke > 0) Fill(vh, Points(rect), rim);
        var inner = new Rect(rect.xMin + stroke, rect.yMin + stroke,
            Mathf.Max(0, rect.width - 2 * stroke), Mathf.Max(0, rect.height - 2 * stroke));
        Fill(vh, Points(stroke > 0 ? inner : rect), color);
    }

    List<Vector2> Points(Rect r)
    {
        var p = new List<Vector2>(40);
        if (shape == Kind.Circle)
        {
            for (int i = 0; i < 48; i++)
            {
                float a = i * Mathf.PI * 2 / 48;
                p.Add(r.center + new Vector2(Mathf.Cos(a) * r.width / 2, Mathf.Sin(a) * r.height / 2));
            }
        }
        else if (shape == Kind.Diamond || shape == Kind.Star)
        {
            int count = shape == Kind.Star ? 8 : 4;
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2 / count;
                float scale = shape == Kind.Star && i % 2 != 0 ? .32f : 1;
                p.Add(r.center + new Vector2(Mathf.Cos(a) * r.width * .5f * scale,
                    Mathf.Sin(a) * r.height * .5f * scale));
            }
        }
        else if (shape == Kind.Triangle)
        {
            p.Add(new Vector2(r.xMin, r.yMin)); p.Add(new Vector2(r.xMax, r.center.y));
            p.Add(new Vector2(r.xMin, r.yMax));
        }
        else if (shape == Kind.Clipped)
        {
            float c = Mathf.Min(r.height * .25f, r.width * .12f);
            p.Add(new Vector2(r.xMin + c, r.yMin)); p.Add(new Vector2(r.xMax - c, r.yMin));
            p.Add(new Vector2(r.xMax, r.yMin + c)); p.Add(new Vector2(r.xMax, r.yMax - c));
            p.Add(new Vector2(r.xMax - c, r.yMax)); p.Add(new Vector2(r.xMin + c, r.yMax));
            p.Add(new Vector2(r.xMin, r.yMax - c)); p.Add(new Vector2(r.xMin, r.yMin + c));
        }
        else
        {
            float radius = Mathf.Min(r.height * cornerRatio, r.width * .25f);
            Arc(p, new Vector2(r.xMin + radius, r.yMin + radius), radius, 180, 270);
            if (shape == Kind.Card)
            {
                float cut = Mathf.Min(r.height * .38f, r.width * .16f);
                p.Add(new Vector2(r.xMax - cut, r.yMin));
                p.Add(new Vector2(r.xMax, r.center.y));
                p.Add(new Vector2(r.xMax - cut, r.yMax));
            }
            else
            {
                Arc(p, new Vector2(r.xMax - radius, r.yMin + radius), radius, 270, 360);
                Arc(p, new Vector2(r.xMax - radius, r.yMax - radius), radius, 0, 90);
            }
            Arc(p, new Vector2(r.xMin + radius, r.yMax - radius), radius, 90, 180);
            if (mirrored) for (int i = 0; i < p.Count; i++) p[i] = new Vector2(2 * r.center.x - p[i].x, p[i].y);
        }
        return p;
    }
    static void Arc(List<Vector2> p, Vector2 center, float r, float from, float to)
    {
        for (int i = 0; i <= 8; i++)
        {
            float a = Mathf.Lerp(from, to, i / 8f) * Mathf.Deg2Rad;
            p.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
        }
    }
    static void Bar(VertexHelper vh, Rect r, Color c)
    {
        Fill(vh, new List<Vector2> { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin),
            new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) }, c);
    }
    static void Fill(VertexHelper vh, List<Vector2> p, Color c)
    {
        if (p.Count < 3) return;
        int start = vh.currentVertCount;
        Vector2 center = Vector2.zero;
        foreach (var v in p) center += v;
        center /= p.Count;
        vh.AddVert(center, c, Vector2.zero);
        foreach (var v in p) vh.AddVert(v, c, Vector2.zero);
        for (int i = 0; i < p.Count; i++) vh.AddTriangle(start, start + i + 1, start + (i + 1) % p.Count + 1);
    }
}
