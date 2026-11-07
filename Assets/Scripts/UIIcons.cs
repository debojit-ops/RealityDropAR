using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Crisp line icons drawn in code (no image files): each icon is a few strokes on a 24×24 grid
/// (same coordinates as SVG, y down), rasterized once with anti-aliasing into a white texture.
/// Show it with a RawImage and tint it with the RawImage color.
/// </summary>
public static class UIIcons
{
    public const string Back = "back", Info = "info", Spin = "spin", Reset = "reset",
                        Scene = "scene", Light = "light", ARCube = "arcube", Close = "close", Next = "next";

    private const int Size = 128;           // texture size (px)
    private const float Scale = Size / 24f; // grid → px
    private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

    private abstract class Prim { public abstract float Dist(Vector2 p); }

    private class Line : Prim
    {
        readonly Vector2 a, b;
        public Line(float ax, float ay, float bx, float by) { a = new Vector2(ax, ay); b = new Vector2(bx, by); }
        public override float Dist(Vector2 p)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(1e-5f, ba.sqrMagnitude));
            return (pa - ba * h).magnitude;
        }
    }

    private class Circle : Prim
    {
        readonly Vector2 c; readonly float r; readonly bool filled;
        public Circle(float cx, float cy, float radius, bool fill = false) { c = new Vector2(cx, cy); r = radius; filled = fill; }
        public override float Dist(Vector2 p)
        {
            float d = (p - c).magnitude - r;
            return filled ? Mathf.Max(0f, d) : Mathf.Abs(d);
        }
    }

    /// <summary>Arc on a y-down grid: angles in degrees, 0 = +x, positive sweep = clockwise on screen.</summary>
    private class Arc : Prim
    {
        readonly Vector2 c; readonly float r, start, sweep;
        public Arc(float cx, float cy, float radius, float startDeg, float sweepDeg) { c = new Vector2(cx, cy); r = radius; start = startDeg; sweep = sweepDeg; }
        public override float Dist(Vector2 p)
        {
            Vector2 d = p - c;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float delta = sweep >= 0 ? Mathf.Repeat(ang - start, 360f) : Mathf.Repeat(start - ang, 360f);
            if (delta <= Mathf.Abs(sweep)) return Mathf.Abs(d.magnitude - r);
            Vector2 e0 = c + Dir(start) * r;
            Vector2 e1 = c + Dir(start + sweep) * r;
            return Mathf.Min((p - e0).magnitude, (p - e1).magnitude);
        }
        static Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
    }

    private class Ellipse : Prim
    {
        readonly Vector2 c; readonly float rx, ry;
        public Ellipse(float cx, float cy, float radiusX, float radiusY) { c = new Vector2(cx, cy); rx = radiusX; ry = radiusY; }
        public override float Dist(Vector2 p)
        {
            Vector2 q = new Vector2((p.x - c.x) / rx, (p.y - c.y) / ry);
            return Mathf.Abs(q.magnitude - 1f) * Mathf.Min(rx, ry);
        }
    }

    public static Texture2D Get(string name)
    {
        if (cache.TryGetValue(name, out var tex) && tex != null) return tex;
        tex = Rasterize(Define(name), 2.0f);
        tex.name = "Icon_" + name;
        cache[name] = tex;
        return tex;
    }

    private static List<Prim> Define(string name)
    {
        var p = new List<Prim>();
        switch (name)
        {
            case Back:
                p.Add(new Line(15, 6, 9, 12)); p.Add(new Line(9, 12, 15, 18));
                break;
            case Close:
                p.Add(new Line(6, 6, 18, 18)); p.Add(new Line(18, 6, 6, 18));
                break;
            case Next:
                p.Add(new Line(9, 6, 15, 12)); p.Add(new Line(15, 12, 9, 18));
                break;            case Info:
                p.Add(new Circle(12, 12, 9)); p.Add(new Line(12, 11, 12, 17)); p.Add(new Circle(12, 7.6f, 1.1f, true));
                break;
            case Spin: // clockwise arrow
                p.Add(new Arc(12, 12, 9, 0f, 312f));
                p.Add(new Line(21, 3.5f, 21, 9)); p.Add(new Line(21, 9, 15.5f, 9));
                break;
            case Reset: // counter-clockwise arrow
                p.Add(new Arc(12, 12, 9, 180f, -312f));
                p.Add(new Line(3, 3.5f, 3, 9)); p.Add(new Line(3, 9, 8.5f, 9));
                break;
            case Scene: // globe
                p.Add(new Circle(12, 12, 9)); p.Add(new Line(3, 12, 21, 12)); p.Add(new Ellipse(12, 12, 4.2f, 9));
                break;
            case Light: // sun
                p.Add(new Circle(12, 12, 4));
                for (int i = 0; i < 8; i++)
                {
                    float a = i * 45f * Mathf.Deg2Rad;
                    Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    p.Add(new Line(12 + d.x * 7f, 12 + d.y * 7f, 12 + d.x * 9.5f, 12 + d.y * 9.5f));
                }
                break;
            case ARCube:
                p.Add(new Line(12, 2, 21, 7)); p.Add(new Line(21, 7, 21, 17)); p.Add(new Line(21, 17, 12, 22));
                p.Add(new Line(12, 22, 3, 17)); p.Add(new Line(3, 17, 3, 7)); p.Add(new Line(3, 7, 12, 2));
                p.Add(new Line(12, 22, 12, 12)); p.Add(new Line(21, 7, 12, 12)); p.Add(new Line(3, 7, 12, 12));
                break;
        }
        return p;
    }

    private static Texture2D Rasterize(List<Prim> prims, float strokeWidth)
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[Size * Size];
        float half = strokeWidth * 0.5f;
        float aa = 1f / Scale; // ~1px in grid units

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                // Texture rows go bottom→top; icon grid is y-down
                Vector2 p = new Vector2((x + 0.5f) / Scale, 24f - (y + 0.5f) / Scale);
                float d = float.MaxValue;
                foreach (var prim in prims) d = Mathf.Min(d, prim.Dist(p));
                float a = Mathf.Clamp01((half - d) / aa + 0.5f);
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }
}
