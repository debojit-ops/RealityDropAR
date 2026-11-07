using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Premium iOS-style search-result card, built on top of the ThumbnaiItem prefab at runtime:
///  - dark glass background with rounded corners + thin cyan border
///  - soft shadow that turns into a cyan glow while pressed / downloading
///  - inset thumbnail with matching rounded corners, cover-cropped (never stretched)
///  - pulsing skeleton while the thumbnail loads, then a fade-in
///  - "FREE" pill badge, bold title (max 2 lines) and grey "by author" line
///  - staggered pop-in when the card appears, spring scale when pressed
/// All sizes scale with the card width, so it looks the same on every phone.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ResultCardView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("Colors")]
    public Color cardColor = new Color(0.055f, 0.075f, 0.10f, 0.92f);
    public Color shadowColor = new Color(0f, 0f, 0f, 0.55f);
    public Color glowColor = new Color(0.25f, 0.85f, 1f, 0.60f);
    public Color titleColor = new Color(0.96f, 0.97f, 0.98f, 1f);
    public Color authorColor = new Color(0.66f, 0.73f, 0.80f, 1f);
    public Color skeletonColorA = new Color(0.12f, 0.15f, 0.19f, 1f);
    public Color skeletonColorB = new Color(0.19f, 0.23f, 0.28f, 1f);
    public Color badgeColor = new Color(0.02f, 0.05f, 0.08f, 0.82f);

    [Header("Proportions (fraction of card width)")]
    public float cornerRadius = 0.09f;
    public float imageInset = 0.022f;
    public float titleFontSize = 0.092f;
    public float authorFontSize = 0.072f;
    public float textSideMargin = 0.055f;
    public int titleLines = 2;

    [Header("Motion")]
    public float appearDuration = 0.45f;
    public float pressedScale = 0.95f;
    public float imageFadeDuration = 0.4f;

    private RectTransform rect;
    private Image hitArea;
    private Image glow;
    private UIRoundedCorners glowShape;
    private Image background;
    private UIRoundedCorners backgroundShape;
    private RawImage thumbnail;
    private UIRoundedCorners thumbnailShape;
    private RectTransform badge;
    private UIRoundedCorners badgeShape;
    private TextMeshProUGUI badgeText;
    private TextMeshProUGUI title;
    private TextMeshProUGUI author;
    private LayoutElement thumbnailElement, titleElement, authorElement;
    private VerticalLayoutGroup layout;
    private CanvasGroup canvasGroup;
    private ScrollRect scrollRect;

    private float lastCardWidth = -1f;
    private Vector2 lastImageSize = -Vector2.one;
    private Texture lastTexture;

    private float appearDelay;
    private float appearTime = -1f;
    private bool pressed;
    private bool busy;
    private float pressAmount;      // 0..1 smoothed
    private float imageReveal;      // 0..1 after the texture arrives
    private bool built;

    // ------------------------------------------------------------------ public API

    public string TitleText { get; private set; }
    /// <summary>~1024px thumbnail used by the download screen.</summary>
    public string HiResThumbnailUrl { get; set; }
    public string AuthorName { get; private set; }

    /// <summary>Sets the text content. Author may be null/empty.</summary>
    public void SetInfo(string titleText, string authorName)
    {
        Build();
        TitleText = titleText;
        AuthorName = authorName;
        if (title != null) title.text = titleText;
        if (author != null) author.text = string.IsNullOrEmpty(authorName) ? string.Empty : "by " + authorName;
        if (badge != null) badge.gameObject.SetActive(true);
    }

    /// <summary>Grey placeholder card shown while a search is loading.</summary>
    public void SetSkeleton()
    {
        Build();
        if (title != null) title.text = string.Empty;
        if (author != null) author.text = string.Empty;
        if (badge != null) badge.gameObject.SetActive(false);
        var button = GetComponent<Button>();
        if (button != null) button.interactable = false;
    }

    /// <summary>The RawImage that receives the downloaded thumbnail.</summary>
    public RawImage Thumbnail { get { Build(); return thumbnail; } }

    /// <summary>Delay before the pop-in animation (used to stagger a page of cards).</summary>
    public void SetAppearDelay(float delay)
    {
        appearDelay = Mathf.Max(0f, delay);
        appearTime = Time.unscaledTime + appearDelay; // AddComponent already ran OnEnable, so restart here
    }

    /// <summary>Keeps the card glowing while its model downloads.</summary>
    public void SetBusy(bool value) => busy = value;

    // ------------------------------------------------------------------ setup

    void Awake()
    {
        Build();
    }

    void OnEnable()
    {
        appearTime = Time.unscaledTime + appearDelay;
        if (canvasGroup != null) canvasGroup.alpha = 0f;
    }

    private void Build()
    {
        if (built) return;
        built = true;

        rect = (RectTransform)transform;
        thumbnail = GetComponentInChildren<RawImage>(true);
        title = GetComponentInChildren<TextMeshProUGUI>(true);
        scrollRect = GetComponentInParent<ScrollRect>();

        // Prefab AspectRatioFitters fight the grid / card layout
        foreach (var fitter in GetComponentsInChildren<AspectRatioFitter>(true))
            fitter.enabled = false;

        // Root graphic stays as an invisible hit area for the Button (raycasts still work at alpha 0)
        hitArea = GetComponent<Image>();
        if (hitArea != null)
        {
            hitArea.sprite = null;
            hitArea.color = new Color(0f, 0f, 0f, 0f);
        }

        var button = GetComponent<Button>();
        if (button != null) button.transition = Selectable.Transition.None; // we animate ourselves

        canvasGroup = GetOrAdd<CanvasGroup>(gameObject);
        canvasGroup.alpha = 0f;

        // Layers behind the content (children render after the root, so these replace the root visuals)
        glow = CreateLayer("Glow", 0, out glowShape);
        background = CreateLayer("Background", 1, out backgroundShape);
        background.color = cardColor;
        glow.color = shadowColor;

        layout = GetOrAdd<VerticalLayoutGroup>(gameObject);
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childScaleWidth = false;
        layout.childScaleHeight = false;
        layout.reverseArrangement = false;
        layout.spacing = 0f;

        if (thumbnail != null)
        {
            thumbnail.transform.SetSiblingIndex(2);
            thumbnail.color = skeletonColorA;
            thumbnail.raycastTarget = false;
            thumbnailShape = GetOrAdd<UIRoundedCorners>(thumbnail.gameObject);
            thumbnailElement = GetOrAdd<LayoutElement>(thumbnail.gameObject);
            thumbnailElement.minHeight = 0f;
            thumbnailElement.preferredHeight = 0f;
            thumbnailElement.flexibleHeight = 1f;
            thumbnailElement.flexibleWidth = 1f;
            CreateBadge();
        }

        if (title != null)
        {
            UIFonts.Apply(title, UIFonts.SemiBold);
            StyleText(title, titleColor, FontStyles.Normal, Mathf.Max(1, titleLines));
            titleElement = GetOrAdd<LayoutElement>(title.gameObject);
            titleElement.flexibleHeight = 0f;

            var authorGO = Instantiate(title.gameObject, transform);
            authorGO.name = "Author";
            author = authorGO.GetComponent<TextMeshProUGUI>();
            author.text = string.Empty;
            UIFonts.Apply(author, UIFonts.Regular);
            StyleText(author, authorColor, FontStyles.Normal, 1);
            authorElement = GetOrAdd<LayoutElement>(authorGO);
            authorElement.flexibleHeight = 0f;

            title.transform.SetAsLastSibling();
            authorGO.transform.SetAsLastSibling();
        }

        UIRoundedCorners.EnableCanvasChannels(GetComponentInParent<Canvas>());
    }

    private Image CreateLayer(string layerName, int siblingIndex, out UIRoundedCorners shape)
    {
        var go = new GameObject(layerName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.SetSiblingIndex(siblingIndex);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        shape = go.AddComponent<UIRoundedCorners>();
        return img;
    }

    private void CreateBadge()
    {
        var go = new GameObject("FreeBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        badge = (RectTransform)go.transform;
        badge.SetParent(thumbnail.transform, false);
        badge.anchorMin = badge.anchorMax = new Vector2(0f, 1f);
        badge.pivot = new Vector2(0f, 1f);
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var img = go.GetComponent<Image>();
        img.color = badgeColor;
        img.raycastTarget = false;
        badgeShape = go.AddComponent<UIRoundedCorners>();
        badgeShape.SetRadius(9999f); // pill

        var textGO = new GameObject("Label", typeof(RectTransform));
        var textRect = (RectTransform)textGO.transform;
        textRect.SetParent(badge, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        badgeText = textGO.AddComponent<TextMeshProUGUI>();
        UIFonts.Apply(badgeText, UIFonts.Display);
        badgeText.text = "FREE";
        badgeText.alignment = TextAlignmentOptions.Center;
        badgeText.characterSpacing = 10f;
        badgeText.color = new Color(0.70f, 0.97f, 1f, 1f);
        badgeText.raycastTarget = false;
        badgeText.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private static void StyleText(TextMeshProUGUI text, Color color, FontStyles style, int lines)
    {
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.maxVisibleLines = lines;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.fontStyle = style;
        text.color = color;
        text.raycastTarget = false;
        text.lineSpacing = -2f;
        // The prefab text had -11 character spacing, which squashed letters ("by3DMAN")
        text.characterSpacing = 0f;
        text.wordSpacing = 0f;
        text.fontFeatures = new System.Collections.Generic.List<UnityEngine.TextCore.OTL_FeatureTag> { UnityEngine.TextCore.OTL_FeatureTag.kern };
    }

    // ------------------------------------------------------------------ sizing

    private void ApplySizes(float w)
    {
        lastCardWidth = w;
        if (w <= 0f) return;

        float radius = w * cornerRadius;
        float inset = Mathf.Max(2f, w * imageInset);

        backgroundShape.SetRadius(radius);
        backgroundShape.SetBorder(Mathf.Max(1.5f, w * 0.004f));

        // Shadow/glow: larger than the card, soft falloff, slightly lower (light comes from above)
        float g = w * 0.07f;
        var glowRect = glow.rectTransform;
        glowRect.offsetMin = new Vector2(-g, -g * 1.25f);
        glowRect.offsetMax = new Vector2(g, g * 0.75f);
        glowShape.SetRadius(radius + g);
        glowShape.SetSoftness(g * 1.6f);

        // Nested corners: inner radius = outer radius - inset (keeps the gap even all around)
        if (thumbnailShape != null) thumbnailShape.SetRadius(Mathf.Max(0f, radius - inset));

        layout.padding = new RectOffset(Mathf.RoundToInt(inset), Mathf.RoundToInt(inset), Mathf.RoundToInt(inset), 0);

        float side = w * textSideMargin;
        if (title != null)
        {
            float size = w * titleFontSize;
            float top = w * 0.04f;
            title.fontSize = size;
            title.margin = new Vector4(side - inset, top, side - inset, 0f);
            float h = Mathf.Ceil(size * 1.22f * Mathf.Max(1, titleLines) + top);
            titleElement.minHeight = titleElement.preferredHeight = h;
        }
        if (author != null)
        {
            float size = w * authorFontSize;
            float bottom = w * 0.05f;
            author.fontSize = size;
            author.margin = new Vector4(side - inset, w * 0.008f, side - inset, bottom);
            float h = Mathf.Ceil(size * 1.3f + bottom + w * 0.008f);
            authorElement.minHeight = authorElement.preferredHeight = h;
        }
        if (badge != null)
        {
            float bh = w * 0.095f;
            float pad = w * 0.035f;
            badge.sizeDelta = new Vector2(bh * 2.4f, bh);
            badge.anchoredPosition = new Vector2(pad, -pad);
            badgeText.fontSize = bh * 0.58f;
            badgeShape.SetBorder(Mathf.Max(1f, w * 0.003f));
        }

        LayoutRebuilder.MarkLayoutForRebuild(rect);
    }

    /// <summary>Crops the texture via uvRect so it fills the image area without stretching (object-fit: cover).</summary>
    private void UpdateCoverCrop(Vector2 imageSize)
    {
        lastImageSize = imageSize;
        Texture tex = thumbnail.texture;

        if (tex == null || imageSize.x <= 0f || imageSize.y <= 0f || tex.height == 0)
        {
            thumbnail.uvRect = new Rect(0f, 0f, 1f, 1f);
            return;
        }

        float textureAspect = (float)tex.width / tex.height;
        float areaAspect = imageSize.x / imageSize.y;
        if (textureAspect > areaAspect)
        {
            float u = areaAspect / textureAspect;
            thumbnail.uvRect = new Rect((1f - u) * 0.5f, 0f, u, 1f);
        }
        else
        {
            float v = textureAspect / areaAspect;
            thumbnail.uvRect = new Rect(0f, (1f - v) * 0.5f, 1f, v);
        }
    }

    // ------------------------------------------------------------------ animation

    void LateUpdate()
    {
        float w = rect.rect.width;
        if (!Mathf.Approximately(w, lastCardWidth)) ApplySizes(w);

        float now = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;

        // Pop-in: fade + scale up with a soft overshoot
        float appear = 1f;
        if (appearTime >= 0f)
        {
            appear = Mathf.Clamp01((now - appearTime) / appearDuration);
            if (appear >= 1f) appearTime = -1f;
        }
        canvasGroup.alpha = EaseOutCubic(appear);
        float appearScale = Mathf.LerpUnclamped(0.88f, 1f, EaseOutBack(appear));

        // Cancel the press look once the finger starts scrolling the list
        if (pressed && scrollRect != null && scrollRect.velocity.sqrMagnitude > 400f) pressed = false;

        float pressTarget = pressed ? 1f : 0f;
        pressAmount = Mathf.MoveTowards(pressAmount, pressTarget, dt * (pressed ? 10f : 6f));
        float pressScale = Mathf.Lerp(1f, pressedScale, EaseOutCubic(pressAmount));
        rect.localScale = Vector3.one * (appearScale * pressScale);

        // Shadow -> cyan glow while pressed; gentle breathing glow while downloading
        float glowAmount = Mathf.Max(pressAmount, busy ? 0.55f + 0.45f * Mathf.Sin(now * 5f) : 0f);
        glow.color = Color.Lerp(shadowColor, glowColor, glowAmount);

        // Thumbnail: pulsing skeleton until the texture arrives, then fade in
        if (thumbnail != null)
        {
            if (thumbnail.texture != lastTexture)
            {
                lastTexture = thumbnail.texture;
                imageReveal = 0f;
                UpdateCoverCrop(thumbnail.rectTransform.rect.size);
            }

            Vector2 imageSize = thumbnail.rectTransform.rect.size;
            if (imageSize != lastImageSize) UpdateCoverCrop(imageSize);

            Color skeleton = Color.Lerp(skeletonColorA, skeletonColorB, 0.5f + 0.5f * Mathf.Sin(now * 3.2f));
            if (lastTexture == null)
            {
                thumbnail.color = skeleton;
            }
            else if (imageReveal < 1f)
            {
                imageReveal = Mathf.Clamp01(imageReveal + dt / imageFadeDuration);
                thumbnail.color = Color.Lerp(skeleton, Color.white, EaseOutCubic(imageReveal));
            }
        }
    }

    public void OnPointerDown(PointerEventData eventData) => pressed = true;
    public void OnPointerUp(PointerEventData eventData) => pressed = false;
    public void OnPointerExit(PointerEventData eventData) => pressed = false;

    private static float EaseOutCubic(float t) { t = 1f - t; return 1f - t * t * t; }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.4f, c3 = c1 + 1f;
        float x = t - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}
