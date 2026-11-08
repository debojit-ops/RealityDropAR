using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Code-drawn neon illustrations in the app's cyan theme (used by the loading screen and the guide):
/// a spinning wireframe cube (the RealityDrop cube), a glowing floor ring, an expanding scan pulse
/// and AR corner brackets. Everything is plain uGUI, so it stays sharp at any resolution.
/// </summary>
public static class NeonArt
{
    public static readonly Color Cyan = new Color(0.24f, 0.95f, 1f, 1f);

    /// <summary>Wireframe cube that turns slowly; returns the component so callers can tweak it.</summary>
    public static WireCube Cube(Transform parent, Vector2 pos, float size, float thickness = 7f)
    {
        var rt = UIKit.Rect("WireCube", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(size, size);
        var cube = rt.gameObject.AddComponent<WireCube>();
        cube.Build(size * 0.5f, thickness);
        return cube;
    }

    /// <summary>Flat glowing ellipse lying on the "floor" (a ring seen in perspective).</summary>
    public static RawImage FloorRing(Transform parent, Vector2 pos, float width, float alpha = 1f)
    {
        var rt = UIKit.Rect("FloorRing", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(width, width * 0.32f);
        var img = rt.gameObject.AddComponent<RawImage>();
        img.texture = RingTexture();
        img.color = UIKit.WithAlpha(Cyan, alpha);
        img.raycastTarget = false;
        return img;
    }

    /// <summary>A floor ring that keeps expanding and fading out (scanning feel).</summary>
    public static void ScanPulse(Transform parent, Vector2 pos, float width, float delay = 0f)
    {
        var ring = FloorRing(parent, pos, width, 0f);
        var pulse = ring.gameObject.AddComponent<RingPulse>();
        pulse.delay = delay;
    }

    /// <summary>Four L-shaped corners framing a rect (the "AR viewfinder" look).</summary>
    public static void Brackets(Transform parent, Vector2 center, Vector2 size, float arm, float thickness, Color color)
    {
        for (int i = 0; i < 4; i++)
        {
            float sx = (i % 2 == 0) ? -1f : 1f, sy = (i < 2) ? 1f : -1f;
            Vector2 corner = center + new Vector2(sx * size.x * 0.5f, sy * size.y * 0.5f);
            Bar(parent, corner + new Vector2(-sx * arm * 0.5f, -sy * thickness * 0.5f), new Vector2(arm, thickness), color);
            Bar(parent, corner + new Vector2(-sx * thickness * 0.5f, -sy * arm * 0.5f), new Vector2(thickness, arm), color);
        }
    }

    public static Image Bar(Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        var rt = UIKit.Rect("Bar", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        rt.gameObject.AddComponent<UIRoundedCorners>().SetRadius(Mathf.Min(size.x, size.y) * 0.5f);
        return img;
    }

    /// <summary>Soft round glow (radial falloff) — put behind things to make them "emit" light.</summary>
    public static Image Glow(Transform parent, Vector2 pos, float diameter, float alpha)
    {
        var rt = UIKit.Rect("Glow", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(diameter, diameter);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = UIKit.WithAlpha(Cyan, alpha);
        img.raycastTarget = false;
        var s = rt.gameObject.AddComponent<UIRoundedCorners>();
        s.SetRadius(diameter * 0.5f);
        s.SetSoftness(diameter * 0.4f);
        return img;
    }

    private static Texture2D ringTex;
    /// <summary>Anti-aliased neon ring with an outer/inner glow and a faint inner fill.</summary>
    public static Texture2D RingTexture()
    {
        if (ringTex != null) return ringTex;
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        float c = (n - 1) * 0.5f, radius = n * 0.40f, core = n * 0.012f;
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            float d = Mathf.Abs(r - radius);
            float line = Mathf.Clamp01(core - d + 1f);                 // sharp core
            float glow = Mathf.Exp(-d * d / (2f * (n * 0.035f) * (n * 0.035f))) * 0.45f;
            float fill = r < radius ? 0.07f : 0f;
            float a = Mathf.Clamp01(Mathf.Max(line, glow) + fill);
            px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * a)); // tinted by the RawImage color
        }
        tex.SetPixels32(px);
        tex.Apply();
        return ringTex = tex;
    }
}

/// <summary>A cube drawn as 12 glowing lines, projected in perspective and turned every frame.</summary>
public class WireCube : MonoBehaviour
{
    public float speed = 40f;        // degrees per second around the vertical axis
    public float tilt = 24f;         // looking down onto the cube
    public float yaw = 30f;
    public float bobAmount = 0f;     // up/down float in px
    public Color color = NeonArt.Cyan;

    private float half;
    private RectTransform[] cores, glows;
    private Image[] coreImgs, glowImgs;
    private static readonly int[,] Edges =
    {
        {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7}
    };
    private readonly Vector3[] corners = new Vector3[8];
    private readonly Vector2[] projected = new Vector2[8];

    public void Build(float halfSize, float thickness)
    {
        half = halfSize * 0.62f; // leave room for the corners when it turns
        cores = new RectTransform[12]; glows = new RectTransform[12];
        coreImgs = new Image[12]; glowImgs = new Image[12];
        for (int i = 0; i < 12; i++)
        {
            glows[i] = Line(out glowImgs[i], thickness * 3.2f, true);
            cores[i] = Line(out coreImgs[i], thickness, false);
        }
        int k = 0;
        for (int y = -1; y <= 1; y += 2)
        {
            corners[k++] = new Vector3(-1, y, -1); corners[k++] = new Vector3(1, y, -1);
            corners[k++] = new Vector3(1, y, 1);   corners[k++] = new Vector3(-1, y, 1);
        }
        Redraw();
    }

    private RectTransform Line(out Image img, float thickness, bool glow)
    {
        var rt = UIKit.Rect(glow ? "Glow" : "Edge", transform);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(10f, thickness);
        img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        var s = rt.gameObject.AddComponent<UIRoundedCorners>();
        s.SetRadius(thickness * 0.5f);
        if (glow) s.SetSoftness(thickness * 0.45f);
        return rt;
    }

    void Update()
    {
        yaw += speed * Time.unscaledDeltaTime;
        Redraw();
    }

    private void Redraw()
    {
        if (cores == null) return;
        var rot = Quaternion.Euler(tilt, 0f, 0f) * Quaternion.Euler(0f, yaw, 0f);
        float f = half * 6f;
        float bob = bobAmount * Mathf.Sin(Time.unscaledTime * 2.2f);
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = rot * (corners[i] * half);
            float s = f / (f + p.z);
            projected[i] = new Vector2(p.x * s, p.y * s + bob);
        }
        for (int e = 0; e < 12; e++)
        {
            Vector2 a = projected[Edges[e, 0]], b = projected[Edges[e, 1]];
            Vector2 d = b - a;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Place(cores[e], a, b, d.magnitude, ang);
            Place(glows[e], a, b, d.magnitude, ang);
            coreImgs[e].color = Color.Lerp(color, Color.white, 0.35f);
            glowImgs[e].color = UIKit.WithAlpha(color, 0.35f);
        }
    }

    private static void Place(RectTransform rt, Vector2 a, Vector2 b, float length, float angle)
    {
        rt.anchoredPosition = (a + b) * 0.5f;
        rt.sizeDelta = new Vector2(length + rt.sizeDelta.y, rt.sizeDelta.y); // rounded ends overlap at corners
        rt.localRotation = Quaternion.Euler(0f, 0f, angle);
    }
}

/// <summary>Expands a ring from 60 % to 140 % while fading it out, forever.</summary>
public class RingPulse : MonoBehaviour
{
    public float delay, period = 2.2f;
    private Vector2 baseSize;
    private RawImage img;

    void Awake() { img = GetComponent<RawImage>(); baseSize = ((RectTransform)transform).sizeDelta; }

    void Update()
    {
        float t = Mathf.Repeat(Time.unscaledTime - delay, period) / period;
        ((RectTransform)transform).sizeDelta = baseSize * Mathf.Lerp(0.6f, 1.4f, t);
        img.color = UIKit.WithAlpha(NeonArt.Cyan, 0.55f * (1f - t) * Mathf.Clamp01(t * 6f));
    }
}
