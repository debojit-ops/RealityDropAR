using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Restyles the scene's search field + button at runtime (every scene using SearchUI gets it):
///  - rounded-rectangle dark glass field with a neon cyan border (tight glow that hugs the edge),
///    magnifier icon, clean placeholder, cyan caret/selection and a clear (×) button
///  - matching rounded-rectangle button: dark glass, neon border, cyan "Search" label; fills with
///    a cyan tint and springs when pressed
///  - the neon brightens slightly while typing
///  - both aligned to the card edges of the results grid
/// </summary>
public class SearchBarStyler : MonoBehaviour
{
    // Palette (matches ResultCardView)
    static readonly Color FieldColor       = new Color(0.055f, 0.075f, 0.10f, 1f);
    static readonly Color NeonColor        = new Color(0.30f, 0.88f, 1f, 1f);
    static readonly Color TextColor        = new Color(0.96f, 0.97f, 0.98f, 1f);
    static readonly Color PlaceholderColor = new Color(0.52f, 0.60f, 0.68f, 1f);
    static readonly Color ButtonPressedFill= new Color(0.10f, 0.32f, 0.42f, 1f);

    [Tooltip("Corner radius as a fraction of the row height (0.5 = pill).")]
    public float cornerRadius = 0.24f;
    [Tooltip("Neon border width in canvas units.")]
    public float borderWidth = 3f;
    [Tooltip("How far the neon glow spreads outside the border, as a fraction of the row height.")]
    public float glowSpread = 0.09f;
    [Range(0f, 1f)] public float glowStrength = 0.55f;
    public string placeholderText = "Search 3D models";

    [Header("Fit inside the background search frame (canvas units)")]
    public bool fitToFrame = true;
    [Tooltip("Inner size of the neon search frame in the background image.")]
    public Vector2 frameSize = new Vector2(1690f, 185f);
    public float frameCenterX = 0f;
    [Tooltip("Gap between the frame and the field/button.")]
    public float frameGap = 24f;
    [Tooltip("Gap between the field and the button.")]
    public float innerGap = 32f;

    private TMP_InputField input;
    private Image fieldGlow;
    private Image buttonGlow;
    private Image buttonFill;
    private RectTransform buttonRect;
    private GameObject clearButton;
    private float focusAmount;
    private float pressAmount;
    private bool buttonPressed;

    /// <summary>Applies the style once. alignTo (optional) = the results ScrollRect to match widths with.</summary>
    public static void Apply(TMP_InputField field, Button button, RectTransform alignTo)
    {
        if (field == null || field.GetComponent<SearchBarStyler>() != null) return;
        var styler = field.gameObject.AddComponent<SearchBarStyler>();
        styler.Build(field, button, alignTo);
    }

    // ------------------------------------------------------------------ build

    private void Build(TMP_InputField field, Button button, RectTransform alignTo)
    {
        input = field;
        var fieldRect = (RectTransform)field.transform;
        var parent = fieldRect.parent as RectTransform;
        if (parent == null) return;
        UIRoundedCorners.EnableCanvasChannels(field.GetComponentInParent<Canvas>());

        // --- Row geometry, in the parent's (canvas) units
        Rect fieldBox = RectInParent(fieldRect, parent);
        float left = fieldBox.xMin;
        float right = button != null ? RectInParent((RectTransform)button.transform, parent).xMax : fieldBox.xMax;
        if (alignTo != null && alignTo.parent == parent)
        {
            // Line up with the card edges (results area minus the grid's edge padding)
            Rect grid = RectInParent(alignTo, parent);
            var gridLayout = alignTo.GetComponentInChildren<ResultsGridLayout>(true);
            float inset = gridLayout != null ? gridLayout.GetEdgePadding(grid.width) : 0f;
            left = grid.xMin + inset;
            right = grid.xMax - inset;
        }
        float height = fieldBox.height;
        float centerY = fieldBox.center.y;
        float gap = height * 0.12f;

        if (fitToFrame)
        {
            // Fit inside the neon search frame drawn in the background, with breathing room on every side
            float cx = parent.rect.center.x + frameCenterX;
            left = cx - frameSize.x * 0.5f + frameGap;
            right = cx + frameSize.x * 0.5f - frameGap;
            height = frameSize.y - frameGap * 2f;
            gap = innerGap;
        }
        float buttonWidth = button != null ? height * 2.3f : 0f;
        float fieldWidth = (right - left) - (button != null ? buttonWidth + gap : 0f);
        float radius = height * cornerRadius;

        PlaceInParent(fieldRect, parent, new Vector2(left + fieldWidth * 0.5f, centerY), new Vector2(fieldWidth, height));
        fieldGlow = CreateNeonGlow(fieldRect, parent, "SearchFieldGlow", radius);
        StyleField(field, fieldRect, radius);

        if (button != null)
        {
            buttonRect = (RectTransform)button.transform;
            buttonRect.localScale = Vector3.one;
            PlaceInParent(buttonRect, parent, new Vector2(right - buttonWidth * 0.5f, centerY), new Vector2(buttonWidth, height));
            buttonGlow = CreateNeonGlow(buttonRect, parent, "SearchButtonGlow", radius);
            StyleButton(button, field, radius);
        }
    }

    /// <summary>Dark rounded-rect background with a bright neon border (radius/border given in canvas units).</summary>
    private void StyleSurface(Image img, RectTransform rt, float radiusCanvas)
    {
        float toLocal = 1f / Mathf.Max(0.0001f, rt.localScale.y);
        img.sprite = null;
        img.type = Image.Type.Simple;
        img.color = FieldColor;
        var shape = GetOrAdd<UIRoundedCorners>(img.gameObject);
        shape.SetBorderColor(NeonColor);
        shape.SetRadius(radiusCanvas * toLocal);
        shape.SetBorder(borderWidth * toLocal);
    }

    private void StyleField(TMP_InputField field, RectTransform fieldRect, float radiusCanvas)
    {
        // Sizes below are in the field's local units (the field may be scaled in the scene)
        float h = fieldRect.rect.height;

        var bg = field.GetComponent<Image>();
        if (bg != null) StyleSurface(bg, fieldRect, radiusCanvas);

        BuildMagnifier(fieldRect, new Vector2(h * 0.58f, 0f), h * 0.40f);

        // Text area between the icon and the clear button
        if (field.textViewport != null)
        {
            var area = field.textViewport;
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(h * 1.05f, 0f);
            area.offsetMax = new Vector2(-h * 0.95f, 0f);
        }

        float fontSize = h * 0.48f;
        field.fontAsset = UIFonts.Regular;
        field.pointSize = fontSize;
        if (field.textComponent != null)
        {
            var t = field.textComponent;
            UIFonts.Apply(t, UIFonts.Regular);
            t.color = TextColor;
            t.fontStyle = FontStyles.Normal;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.fontSize = fontSize;
        }
        if (field.placeholder is TextMeshProUGUI ph)
        {
            UIFonts.Apply(ph, UIFonts.Regular);
            ph.text = placeholderText;
            ph.color = PlaceholderColor;
            ph.fontStyle = FontStyles.Normal;
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            ph.fontSize = fontSize;
        }

        field.customCaretColor = true;
        field.caretColor = NeonColor;
        field.caretWidth = 1; // field is scaled up in the scene, so 1 already renders as a bold caret
        field.caretBlinkRate = 0.9f;
        field.selectionColor = new Color(NeonColor.r, NeonColor.g, NeonColor.b, 0.35f);
        field.transition = Selectable.Transition.None;

        BuildClearButton(field, fieldRect, h);
    }

    private void BuildMagnifier(RectTransform parent, Vector2 centerFromLeft, float size)
    {
        var icon = NewRect("SearchIcon", parent);
        icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
        icon.pivot = new Vector2(0.5f, 0.5f);
        icon.sizeDelta = new Vector2(size, size);
        icon.anchoredPosition = centerFromLeft;

        float stroke = size * 0.15f;
        float ring = size * 0.72f;
        Vector2 ringCenter = new Vector2(-size * 0.10f, size * 0.10f);

        // Ring = neon circle with a field-colored circle on top
        var outer = NewImage("Ring", icon, NeonColor);
        outer.rectTransform.sizeDelta = new Vector2(ring, ring);
        outer.rectTransform.anchoredPosition = ringCenter;
        GetOrAdd<UIRoundedCorners>(outer.gameObject).SetRadius(9999f);

        var inner = NewImage("RingHole", icon, FieldColor);
        inner.rectTransform.sizeDelta = new Vector2(ring - stroke * 2f, ring - stroke * 2f);
        inner.rectTransform.anchoredPosition = ringCenter;
        GetOrAdd<UIRoundedCorners>(inner.gameObject).SetRadius(9999f);

        // Handle, rotated 45° from the ring towards the bottom-right
        var handle = NewImage("Handle", icon, NeonColor);
        float handleLength = size * 0.42f;
        handle.rectTransform.sizeDelta = new Vector2(handleLength, stroke);
        float offset = ring * 0.5f + handleLength * 0.5f - stroke * 0.6f;
        handle.rectTransform.anchoredPosition = ringCenter + new Vector2(offset, -offset) * 0.7071f;
        handle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
        GetOrAdd<UIRoundedCorners>(handle.gameObject).SetRadius(9999f);
    }

    private void BuildClearButton(TMP_InputField field, RectTransform fieldRect, float h)
    {
        float size = h * 0.48f;
        var rt = NewRect("ClearButton", fieldRect);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = new Vector2(-h * 0.55f, 0f);

        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.14f);
        GetOrAdd<UIRoundedCorners>(rt.gameObject).SetRadius(9999f);

        var label = NewRect("X", rt);
        label.anchorMin = Vector2.zero;
        label.anchorMax = Vector2.one;
        label.offsetMin = label.offsetMax = Vector2.zero;
        var x = label.gameObject.AddComponent<TextMeshProUGUI>();
        UIFonts.Apply(x, UIFonts.Regular);
        x.text = "×"; // ×
        x.fontSize = size * 0.85f;
        x.alignment = TextAlignmentOptions.Center;
        x.color = new Color(1f, 1f, 1f, 0.85f);
        x.raycastTarget = false;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() =>
        {
            field.text = string.Empty;
            field.ActivateInputField();
        });

        clearButton = rt.gameObject;
        clearButton.SetActive(!string.IsNullOrEmpty(field.text));
        field.onValueChanged.AddListener(v => { if (clearButton != null) clearButton.SetActive(!string.IsNullOrEmpty(v)); });
    }

    private void StyleButton(Button button, TMP_InputField field, float radiusCanvas)
    {
        float h = buttonRect.rect.height;

        buttonFill = button.GetComponent<Image>();
        if (buttonFill != null)
        {
            StyleSurface(buttonFill, buttonRect, radiusCanvas);
            var gradient = buttonFill.GetComponent<UIGradient>();
            if (gradient != null) Destroy(gradient);
        }
        button.transition = Selectable.Transition.None;

        // Hide any old label children so only ours shows
        foreach (var old in button.GetComponentsInChildren<TMP_Text>(true))
            if (old.gameObject != button.gameObject) old.gameObject.SetActive(false);

        var labelRect = NewRect("Label", buttonRect);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        UIFonts.Apply(label, UIFonts.Display);
        label.text = "SEARCH";
        label.fontSize = h * 0.54f;
        label.characterSpacing = 6f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = NeonColor;
        label.raycastTarget = false;

        var relay = GetOrAdd<UIPressRelay>(button.gameObject);
        relay.onChange = pressed => buttonPressed = pressed;
    }

    // ------------------------------------------------------------------ animation

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // Neon brightens a little while typing
        bool focused = input != null && input.isFocused;
        focusAmount = Mathf.MoveTowards(focusAmount, focused ? 1f : 0f, dt * 5f);
        if (fieldGlow != null)
            fieldGlow.color = WithAlpha(NeonColor, glowStrength * Mathf.Lerp(1f, 1.5f, Smooth(focusAmount)));

        // Button: cyan fill + slight shrink while pressed
        pressAmount = Mathf.MoveTowards(pressAmount, buttonPressed ? 1f : 0f, dt * (buttonPressed ? 12f : 6f));
        float p = Smooth(pressAmount);
        if (buttonRect != null) buttonRect.localScale = Vector3.one * Mathf.Lerp(1f, 0.95f, p);
        if (buttonFill != null) buttonFill.color = Color.Lerp(FieldColor, ButtonPressedFill, p);
        if (buttonGlow != null) buttonGlow.color = WithAlpha(NeonColor, glowStrength * Mathf.Lerp(1f, 1.6f, p));
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
    private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, Mathf.Clamp01(a));

    // ------------------------------------------------------------------ helpers

    /// <summary>Thin neon glow that hugs the target's border (a sibling drawn just behind it).</summary>
    private Image CreateNeonGlow(RectTransform target, RectTransform parent, string glowName, float radiusCanvas)
    {
        Rect box = RectInParent(target, parent);
        float spread = box.height * glowSpread;

        var rt = NewRect(glowName, parent);
        rt.SetSiblingIndex(target.GetSiblingIndex());
        PlaceInParent(rt, parent, box.center, box.size + Vector2.one * spread * 2f);

        var img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        img.color = WithAlpha(NeonColor, glowStrength);
        var shape = img.gameObject.AddComponent<UIRoundedCorners>();
        shape.SetRadius(radiusCanvas + spread); // concentric with the border
        shape.SetSoftness(spread * 1.25f);
        return img;
    }

    private static Rect RectInParent(RectTransform rt, RectTransform parent)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector2 min = parent.InverseTransformPoint(corners[0]);
        Vector2 max = parent.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }

    /// <summary>Positions rt (child of parent) so its center/size in parent units match, keeping its scale.</summary>
    private static void PlaceInParent(RectTransform rt, RectTransform parent, Vector2 center, Vector2 size)
    {
        Vector3 s = rt.localScale;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size.x / Mathf.Max(0.0001f, s.x), size.y / Mathf.Max(0.0001f, s.y));
        rt.anchoredPosition = center - parent.rect.center;
    }

    private static RectTransform NewRect(string objName, Transform parent)
    {
        var go = new GameObject(objName, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static Image NewImage(string objName, RectTransform parent, Color color)
    {
        var rt = NewRect(objName, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}
