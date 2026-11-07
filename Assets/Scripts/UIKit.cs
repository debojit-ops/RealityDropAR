using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shared building blocks for the app's code-built screens (neon glass style).
/// Units: screens use a 1080×2340 reference canvas and design in 390pt → multiply pt by UIKit.K.
/// </summary>
public static class UIKit
{
    public const float K = 1080f / 390f;

    public static readonly Color Neon       = new Color(0.30f, 0.88f, 1f, 1f);
    public static readonly Color GlassFill  = new Color(0.055f, 0.078f, 0.106f, 0.88f);
    public static readonly Color TextMain   = new Color(0.957f, 0.965f, 0.976f, 1f);
    public static readonly Color TextMuted  = new Color(0.66f, 0.70f, 0.75f, 1f);
    public static readonly Color ButtonText = new Color(0.012f, 0.063f, 0.098f, 1f);
    public static readonly Color GradTop    = new Color(0.56f, 0.95f, 1f, 1f);
    public static readonly Color GradBottom = new Color(0.11f, 0.61f, 0.94f, 1f);

    public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    /// <summary>Overlay canvas with the app's scaler settings.</summary>
    public static Canvas CreateCanvas(Transform parent, string name, int sortingOrder = 10)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 2340f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        UIRoundedCorners.EnableCanvasChannels(canvas);
        return canvas;
    }

    /// <summary>Deactivates every other root canvas in the scene (replaces old UI).</summary>
    public static void HideOtherCanvases(Canvas keep)
    {
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (c.isRootCanvas && c != keep) c.gameObject.SetActive(false);
    }

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    public static void Surface(GameObject go, Color fill, float radius, Color border, float borderWidth)
    {
        var img = go.GetComponent<Image>();
        if (img == null) img = go.AddComponent<Image>();
        img.color = fill;
        var shape = go.GetComponent<UIRoundedCorners>();
        if (shape == null) shape = go.AddComponent<UIRoundedCorners>();
        if (borderWidth > 0f) shape.SetBorderColor(border);
        shape.SetRadius(radius);
        shape.SetBorder(borderWidth);
    }

    public static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
    {
        var rt = Rect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        UIFonts.Apply(t, font);
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    public static RawImage Icon(Transform parent, string icon, Color color, float size)
    {
        var rt = Rect("Icon", parent);
        rt.sizeDelta = new Vector2(size, size);
        var img = rt.gameObject.AddComponent<RawImage>();
        img.texture = UIIcons.Get(icon);
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>Square glass button with a neon outline and a centered icon.</summary>
    public static Button IconButton(Transform parent, string name, string icon, float sizePt, UnityEngine.Events.UnityAction onClick)
    {
        var rt = Rect(name, parent);
        rt.sizeDelta = new Vector2(sizePt * K, sizePt * K);
        Surface(rt.gameObject, new Color(0.055f, 0.078f, 0.106f, 0.85f), 14f * K, WithAlpha(Neon, 0.55f), 1.5f * K);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(onClick);
        var ic = Icon(rt, icon, Neon, sizePt * 0.45f * K);
        ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        UIPress.Add(rt);
        return btn;
    }

    /// <summary>Big cyan gradient call-to-action with an icon and label (like VIEW IN AR).</summary>
    public static Button PrimaryButton(Transform parent, string name, string icon, string label, UnityEngine.Events.UnityAction onClick)
    {
        var rt = Rect(name, parent);
        var glow = Rect("Glow", rt);
        glow.anchorMin = Vector2.zero; glow.anchorMax = Vector2.one;
        glow.offsetMin = new Vector2(-12f, -12f) * K; glow.offsetMax = new Vector2(12f, 12f) * K;
        var glowImg = glow.gameObject.AddComponent<Image>();
        glowImg.color = WithAlpha(Neon, 0.45f);
        glowImg.raycastTarget = false;
        var gs = glow.gameObject.AddComponent<UIRoundedCorners>();
        gs.SetRadius(30f * K);
        gs.SetSoftness(16f * K);

        var face = Rect("Face", rt);
        face.anchorMin = Vector2.zero; face.anchorMax = Vector2.one;
        face.offsetMin = face.offsetMax = Vector2.zero;
        var img = face.gameObject.AddComponent<Image>();
        img.color = Color.white;
        face.gameObject.AddComponent<UIRoundedCorners>().SetRadius(18f * K);
        face.gameObject.AddComponent<UIGradient>().Set(GradTop, GradBottom);

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(onClick);
        UIPress.Add(rt);

        var content = Rect("Content", face);
        content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
        var hl = content.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 10f * K;
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = hl.childForceExpandHeight = false;
        var fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        if (!string.IsNullOrEmpty(icon))
        {
            var ic = Icon(content, icon, ButtonText, 22f * K);
            var le = ic.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.preferredHeight = 22f * K;
        }
        var t = Text("Label", content, UIFonts.Display, 22f * K, ButtonText, TextAlignmentOptions.Center);
        t.text = label;
        t.characterSpacing = 8f;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return btn;
    }

    public static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    public static void Fade(CanvasGroup g, bool show, float dt, float speed = 5f)
    {
        if (g == null) return;
        g.alpha = Mathf.MoveTowards(g.alpha, show ? 1f : 0f, dt * speed);
        g.blocksRaycasts = show && g.alpha > 0.5f;
        g.interactable = g.blocksRaycasts;
    }
}
