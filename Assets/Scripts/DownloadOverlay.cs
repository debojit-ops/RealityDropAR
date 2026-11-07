using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Full-screen download progress screen shown after a model is tapped:
///   dimmed background → glass panel with neon border containing the model thumbnail, title, author,
///   a neon progress bar with glow, a big live percentage, "x MB of y MB", the current step
///   (Preparing → Downloading → Unpacking → Opening) and a Cancel button.
/// Built entirely in code (no prefab), styled like the result cards. Progress animates smoothly.
/// </summary>
public class DownloadOverlay : MonoBehaviour
{
    static readonly Color DimColor     = new Color(0.01f, 0.02f, 0.04f, 0.88f);
    static readonly Color PanelColor   = new Color(0.055f, 0.075f, 0.10f, 0.97f);
    static readonly Color NeonColor    = new Color(0.30f, 0.88f, 1f, 1f);
    static readonly Color TrackColor   = new Color(1f, 1f, 1f, 0.08f);
    static readonly Color TitleColor   = new Color(0.96f, 0.97f, 0.98f, 1f);
    static readonly Color MutedColor   = new Color(0.58f, 0.66f, 0.74f, 1f);
    static readonly Color SkeletonColor= new Color(0.14f, 0.18f, 0.22f, 1f);

    private CanvasGroup group;
    private RectTransform panel;
    private RawImage thumbnail;
    private TextMeshProUGUI titleText, authorText, stageText, percentText, sizeText;
    private RectTransform fill;
    private Image fillImage, fillGlow;
    private RectTransform track;
    private Button cancelButton;
    private Action onCancel;

    private float targetProgress;   // -1 = unknown size
    private float shownProgress;
    private bool visible;
    private float visibleAmount;

    private const float PanelWidth = 1500f; // canvas units (canvas is 1920 wide)

    // ------------------------------------------------------------------ public API

    public static DownloadOverlay Create(Canvas rootCanvas)
    {
        var go = new GameObject("DownloadOverlay", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(rootCanvas.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var overlay = go.AddComponent<DownloadOverlay>();
        overlay.Build(rootCanvas);
        go.SetActive(false);
        return overlay;
    }

    public void Show(string title, string author, Texture thumbnailTexture, Action cancelAction)
    {
        onCancel = cancelAction;
        titleText.text = string.IsNullOrEmpty(title) ? "3D model" : title;
        authorText.text = string.IsNullOrEmpty(author) ? string.Empty : author;
        authorText.gameObject.SetActive(!string.IsNullOrEmpty(author));

        thumbnail.texture = thumbnailTexture;
        thumbnail.color = thumbnailTexture != null ? Color.white : SkeletonColor;
        // Lay the panel out first: on the very first Show the picture area is still the default
        // 100×100 square, so cropping now would stretch the image until the hi-res one arrived.
        gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        CoverCrop(thumbnail);
        lastThumbSize = thumbnail.rectTransform.rect.size;

        targetProgress = 0f;
        shownProgress = 0f;
        SetStage("Preparing download…");
        sizeText.text = string.Empty;
        percentText.text = "0%";
        cancelButton.gameObject.SetActive(true);

        transform.SetAsLastSibling();
        gameObject.SetActive(true);
        visible = true;
    }

    public void SetStage(string text) => stageText.text = text;

    /// <summary>Swaps in a sharper thumbnail once it has loaded. The overlay owns and destroys it.</summary>
    public void SetHiResThumbnail(Texture texture)
    {
        if (texture == null) return;
        if (!visible) { Destroy(texture); return; }
        ReleaseHiRes();
        hiResTexture = texture;
        thumbnail.texture = texture;
        thumbnail.color = Color.white;
        CoverCrop(thumbnail);
    }

    private Texture hiResTexture;

    private void ReleaseHiRes()
    {
        if (hiResTexture == null) return;
        if (thumbnail != null && thumbnail.texture == hiResTexture) thumbnail.texture = null;
        Destroy(hiResTexture);
        hiResTexture = null;
    }

    void OnDestroy() => ReleaseHiRes();

    /// <summary>progress 0..1, or -1 when the total size is unknown.</summary>
    public void SetProgress(float progress, ulong downloadedBytes, ulong totalBytes)
    {
        targetProgress = progress;
        sizeText.text = totalBytes > 0
            ? $"{ToMB(downloadedBytes)} of {ToMB(totalBytes)}"
            : (downloadedBytes > 0 ? $"{ToMB(downloadedBytes)} downloaded" : string.Empty);
    }

    /// <summary>Final step: download finished, the preview scene is opening.</summary>
    public void SetOpening()
    {
        targetProgress = 1f;
        SetStage("Opening preview…");
        cancelButton.gameObject.SetActive(false);
    }

    public void Hide()
    {
        visible = false;
        onCancel = null;
        ReleaseHiRes();
    }

    // ------------------------------------------------------------------ build

    private void Build(Canvas canvas)
    {
        UIRoundedCorners.EnableCanvasChannels(canvas);

        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        // Dim background (also blocks taps on the list behind)
        var dim = gameObject.AddComponent<Image>();
        dim.color = DimColor;
        dim.raycastTarget = true;

        // Panel
        panel = NewRect("Panel", transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(PanelWidth, 0f);
        var panelImg = panel.gameObject.AddComponent<Image>();
        panelImg.color = PanelColor;
        var panelShape = panel.gameObject.AddComponent<UIRoundedCorners>();
        panelShape.SetBorderColor(NeonColor);
        panelShape.SetRadius(64f);
        panelShape.SetBorder(3f);

        // Soft neon glow around the panel (behind it)
        var glow = NewRect("PanelGlow", panel);
        glow.anchorMin = Vector2.zero;
        glow.anchorMax = Vector2.one;
        glow.offsetMin = new Vector2(-40f, -40f);
        glow.offsetMax = new Vector2(40f, 40f);
        glow.SetAsFirstSibling();
        var glowImg = glow.gameObject.AddComponent<Image>();
        glowImg.color = new Color(NeonColor.r, NeonColor.g, NeonColor.b, 0.35f);
        glowImg.raycastTarget = false;
        var glowShape = glow.gameObject.AddComponent<UIRoundedCorners>();
        glowShape.SetRadius(104f);
        glowShape.SetSoftness(55f);
        glow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        // The panel graphic is drawn before its children, so move the glow out to sit behind the panel
        glow.SetParent(transform, true);
        glow.SetSiblingIndex(panel.GetSiblingIndex());
        glow.anchorMin = glow.anchorMax = new Vector2(0.5f, 0.5f);
        glow.pivot = new Vector2(0.5f, 0.5f);
        panelGlow = glow;

        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(36, 36, 36, 56);
        layout.spacing = 26f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Thumbnail (16:9, rounded, cover-cropped)
        var thumbRect = NewRect("Thumbnail", panel);
        thumbnail = thumbRect.gameObject.AddComponent<RawImage>();
        thumbnail.raycastTarget = false;
        var thumbShape = thumbRect.gameObject.AddComponent<UIRoundedCorners>();
        thumbShape.SetBorderColor(new Color(1f, 1f, 1f, 0.16f)); // thin inner outline separates bright images from the neon
        thumbShape.SetRadius(36f);
        thumbShape.SetBorder(2.5f);
        var thumbLE = thumbRect.gameObject.AddComponent<LayoutElement>();
        thumbLE.preferredHeight = thumbLE.minHeight = (PanelWidth - 72f) * 9f / 16f;

        // Text block with side margins
        var textBlock = NewRect("Info", panel);
        var textLayout = textBlock.gameObject.AddComponent<VerticalLayoutGroup>();
        textLayout.padding = new RectOffset(28, 28, 10, 0);
        textLayout.spacing = 8f;
        textLayout.childControlWidth = true;
        textLayout.childControlHeight = true;
        textLayout.childForceExpandWidth = true;
        textLayout.childForceExpandHeight = false;

        titleText = NewText("Title", textBlock, UIFonts.Bold, 76f, TitleColor, FontStyles.Normal, TextAlignmentOptions.Left);
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        titleText.overflowMode = TextOverflowModes.Ellipsis;
        authorText = NewText("Author", textBlock, UIFonts.Regular, 56f, MutedColor, FontStyles.Normal, TextAlignmentOptions.Left);
        authorText.textWrappingMode = TextWrappingModes.NoWrap;
        authorText.overflowMode = TextOverflowModes.Ellipsis;

        // Status row: stage (left) + big percentage (right)
        var row = NewRect("StatusRow", panel);
        var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(28, 28, 24, 0);
        rowLayout.childAlignment = TextAnchor.LowerLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        stageText = NewText("Stage", row, UIFonts.SemiBold, 60f, TitleColor, FontStyles.Normal, TextAlignmentOptions.BottomLeft);
        stageText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        percentText = NewText("Percent", row, UIFonts.Display, 150f, NeonColor, FontStyles.Normal, TextAlignmentOptions.BottomRight);
        percentText.textWrappingMode = TextWrappingModes.NoWrap;

        // Progress bar: track + glow + fill
        var barHolder = NewRect("ProgressBar", panel);
        var barLE = barHolder.gameObject.AddComponent<LayoutElement>();
        barLE.preferredHeight = barLE.minHeight = 30f;

        track = NewRect("Track", barHolder);
        track.anchorMin = new Vector2(0f, 0f);
        track.anchorMax = new Vector2(1f, 1f);
        track.offsetMin = new Vector2(28f, 0f);
        track.offsetMax = new Vector2(-28f, 0f);
        var trackImg = track.gameObject.AddComponent<Image>();
        trackImg.color = TrackColor;
        trackImg.raycastTarget = false;
        track.gameObject.AddComponent<UIRoundedCorners>().SetRadius(9999f);

        var fillGlowRect = NewRect("FillGlow", track);
        fillGlowRect.anchorMin = new Vector2(0f, 0.5f);
        fillGlowRect.anchorMax = new Vector2(0f, 0.5f);
        fillGlowRect.pivot = new Vector2(0f, 0.5f);
        fillGlow = fillGlowRect.gameObject.AddComponent<Image>();
        fillGlow.color = new Color(NeonColor.r, NeonColor.g, NeonColor.b, 0.5f);
        fillGlow.raycastTarget = false;
        var fgShape = fillGlowRect.gameObject.AddComponent<UIRoundedCorners>();
        fgShape.SetRadius(9999f);
        fgShape.SetSoftness(26f);

        fill = NewRect("Fill", track);
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(0f, 1f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.color = Color.white;
        fillImage.raycastTarget = false;
        fill.gameObject.AddComponent<UIRoundedCorners>().SetRadius(9999f);
        fill.gameObject.AddComponent<UIGradient>().Set(new Color(0.65f, 0.97f, 1f, 1f), new Color(0.15f, 0.65f, 0.98f, 1f));

        // Size text under the bar
        var sizeHolder = NewRect("SizeRow", panel);
        var sizeLayout = sizeHolder.gameObject.AddComponent<HorizontalLayoutGroup>();
        sizeLayout.padding = new RectOffset(28, 28, 0, 0);
        sizeLayout.childControlWidth = true;
        sizeLayout.childControlHeight = true;
        sizeLayout.childForceExpandWidth = true;
        sizeText = NewText("Size", sizeHolder, UIFonts.Regular, 52f, MutedColor, FontStyles.Normal, TextAlignmentOptions.Left);

        // Cancel button (full width, iOS-style secondary button)
        var cancelRect = NewRect("Cancel", panel);
        var cancelLE = cancelRect.gameObject.AddComponent<LayoutElement>();
        cancelLE.preferredHeight = cancelLE.minHeight = 170f;
        var cancelInner = NewRect("Button", cancelRect);
        cancelInner.anchorMin = new Vector2(0f, 0f);
        cancelInner.anchorMax = new Vector2(1f, 1f);
        cancelInner.offsetMin = new Vector2(28f, 0f);
        cancelInner.offsetMax = new Vector2(-28f, -20f);
        var cancelImg = cancelInner.gameObject.AddComponent<Image>();
        cancelImg.color = new Color(1f, 1f, 1f, 0.06f);
        var cancelShape = cancelInner.gameObject.AddComponent<UIRoundedCorners>();
        cancelShape.SetBorderColor(new Color(1f, 1f, 1f, 0.25f));
        cancelShape.SetRadius(40f);
        cancelShape.SetBorder(2.5f);
        var cancelLabel = NewText("Label", cancelInner, UIFonts.SemiBold, 58f, TitleColor, FontStyles.Normal, TextAlignmentOptions.Center);
        var labelRect = cancelLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        cancelLabel.text = "Cancel";
        cancelButton = cancelInner.gameObject.AddComponent<Button>();
        cancelButton.targetGraphic = cancelImg;
        cancelButton.onClick.AddListener(() => onCancel?.Invoke());
        cancelRect.gameObject.SetActive(true);
    }

    private RectTransform panelGlow;

    // ------------------------------------------------------------------ animation

    private Vector2 lastThumbSize;

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // Keep the picture cropped correctly if its area changes size (layout, rotation)
        if (thumbnail != null)
        {
            Vector2 size = thumbnail.rectTransform.rect.size;
            if (size != lastThumbSize) { lastThumbSize = size; CoverCrop(thumbnail); }
        }

        // Fade + pop the panel in / out
        visibleAmount = Mathf.MoveTowards(visibleAmount, visible ? 1f : 0f, dt * (visible ? 4f : 6f));
        group.alpha = Smooth(visibleAmount);
        group.blocksRaycasts = visible;
        float scale = Mathf.Lerp(0.92f, 1f, EaseOutBack(visibleAmount));
        panel.localScale = Vector3.one * scale;
        if (panelGlow != null)
        {
            panelGlow.localScale = panel.localScale;
            panelGlow.sizeDelta = panel.rect.size + new Vector2(80f, 80f);
            panelGlow.anchoredPosition = panel.anchoredPosition;
        }
        if (!visible && visibleAmount <= 0f)
        {
            gameObject.SetActive(false);
            return;
        }

        float width = track.rect.width;
        float height = track.rect.height;
        float now = Time.unscaledTime;

        if (targetProgress >= 0f)
        {
            // Smoothly chase the real progress
            shownProgress = Mathf.Lerp(shownProgress, targetProgress, 1f - Mathf.Exp(-dt * 10f));
            if (Mathf.Abs(shownProgress - targetProgress) < 0.001f) shownProgress = targetProgress;
            float w = Mathf.Max(height, width * shownProgress); // never thinner than a dot
            fill.anchoredPosition = Vector2.zero;
            fill.sizeDelta = new Vector2(shownProgress <= 0f ? 0f : w, 0f);
            percentText.text = Mathf.RoundToInt(shownProgress * 100f) + "%";
        }
        else
        {
            // Unknown size: a glowing segment sweeps back and forth
            float seg = width * 0.28f;
            float t = 0.5f + 0.5f * Mathf.Sin(now * 2.4f);
            fill.sizeDelta = new Vector2(seg, 0f);
            fill.anchoredPosition = new Vector2((width - seg) * t, 0f);
            percentText.text = "…";
        }

        // Glow follows the fill and gently breathes
        var glowRect = fillGlow.rectTransform;
        glowRect.anchoredPosition = fill.anchoredPosition + new Vector2(-18f, 0f);
        glowRect.sizeDelta = new Vector2(fill.sizeDelta.x + 36f, height + 40f);
        float breathe = 0.42f + 0.15f * Mathf.Sin(now * 3f);
        fillGlow.color = new Color(NeonColor.r, NeonColor.g, NeonColor.b, fill.sizeDelta.x > 0f ? breathe : 0f);
    }

    // ------------------------------------------------------------------ helpers

    private static string ToMB(ulong bytes)
    {
        double mb = bytes / (1024.0 * 1024.0);
        return mb >= 10 ? $"{mb:0} MB" : $"{mb:0.0} MB";
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.4f, c3 = c1 + 1f;
        float x = t - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    private static void CoverCrop(RawImage img)
    {
        var tex = img.texture;
        Vector2 size = img.rectTransform.rect.size;
        if (size.x <= 0f || size.y <= 0f) size = new Vector2(16f, 9f);
        if (tex == null || tex.height == 0) { img.uvRect = new Rect(0, 0, 1, 1); return; }
        float texAspect = (float)tex.width / tex.height;
        float areaAspect = size.x / size.y;
        if (texAspect > areaAspect)
        {
            float u = areaAspect / texAspect;
            img.uvRect = new Rect((1f - u) * 0.5f, 0f, u, 1f);
        }
        else
        {
            float v = texAspect / areaAspect;
            img.uvRect = new Rect(0f, (1f - v) * 0.5f, 1f, v);
        }
    }

    private static RectTransform NewRect(string objName, Transform parent)
    {
        var go = new GameObject(objName, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static TextMeshProUGUI NewText(string objName, Transform parent, TMP_FontAsset font, float size,
                                           Color color, FontStyles style, TextAlignmentOptions align)
    {
        var rt = NewRect(objName, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        UIFonts.Apply(t, font);
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }
}
