using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// "How it works" guide shown after the landing loader: three swipeable pages
/// (Search → Preview → Place in AR), each with an animated illustration drawn in code in the cyan
/// neon theme (NeonArt), title, short text, page dots, NEXT / GET STARTED and Skip.
/// Shown once every time the app is started (not when coming back to the home screen from a model).
/// </summary>
public class OnboardingGuide : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    static bool shownThisRun;

    static readonly (string title, string body)[] Pages =
    {
        ("Search any 3D model", "Type what you want to see — a sofa, a sports car, a dragon. Thousands of free models, ready to drop in."),
        ("Preview it in 3D", "Spin it, zoom in and check every detail before you bring it into your room."),
        ("Place it in your space", "Point at the floor and tap PLACE MODEL. Move it with the joystick, pinch to resize, then snap a photo."),
    };

    private RectTransform root, pagesRoot;
    private RectTransform[] pages;
    private Image[] dots;
    private TextMeshProUGUI buttonLabel, typed;
    private CanvasGroup group;
    private float position, dragStart;
    private int target;
    private bool dragging, closing;
    private float typeTimer;
    private int wordIndex, charCount;
    static readonly string[] Words = { "sofa", "sports car", "dragon", "indoor plant" };

    public static void ShowIfNeeded()
    {
        if (shownThisRun) return;
        shownThisRun = true;
        var canvas = UIKit.CreateCanvas(null, "OnboardingGuide", 90); // just under the loader, which fades over it
        canvas.gameObject.AddComponent<OnboardingGuide>().Build((RectTransform)canvas.transform);
        SafeAreaFitter.Apply(canvas);
    }

    // ------------------------------------------------------------------ build

    private void Build(RectTransform canvasRect)
    {
        root = canvasRect;
        group = root.gameObject.AddComponent<CanvasGroup>();

        var bg = UIKit.Rect("Backdrop", root);
        UIKit.Stretch(bg, Vector2.zero, Vector2.zero);
        bg.gameObject.AddComponent<Image>().color = SafeAreaFitter.BackdropColor; // swipe surface + blocks the screen below

        pagesRoot = UIKit.Rect("Pages", root);
        UIKit.Stretch(pagesRoot, Vector2.zero, Vector2.zero);
        pages = new RectTransform[Pages.Length];
        for (int i = 0; i < Pages.Length; i++)
        {
            var page = UIKit.Rect("Page" + (i + 1), pagesRoot);
            UIKit.Stretch(page, Vector2.zero, Vector2.zero);
            page.gameObject.AddComponent<CanvasGroup>();
            pages[i] = page;

            var art = UIKit.Rect("Art", page);
            art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f);
            art.anchoredPosition = new Vector2(0f, 330f);
            art.sizeDelta = new Vector2(1080f, 1150f); // full width: the pictures have soft edges
            // AI artwork if present (Resources/RealityDropUI/Onboarding/Guide1..3), else the code-drawn art
            var picture = Resources.Load<Texture2D>($"RealityDropUI/Onboarding/Guide{i + 1}");
            if (picture != null) BuildPicture(art, picture);
            else if (i == 0) BuildSearchArt(art);
            else if (i == 1) BuildPreviewArt(art);
            else BuildPlaceArt(art);

            var title = UIKit.Text("Title", page, UIFonts.Bold, 76f, UIKit.TextMain, TextAlignmentOptions.Center);
            title.text = Pages[i].title;
            Band(title.rectTransform, -390f, 110f, 80f);

            var body = UIKit.Text("Body", page, UIFonts.Regular, 44f, UIKit.TextMuted, TextAlignmentOptions.Top);
            body.text = Pages[i].body;
            body.lineSpacing = 8f;
            Band(body.rectTransform, -560f, 200f, 110f);
        }

        // Page dots
        var dotRow = UIKit.Rect("Dots", root);
        dotRow.anchorMin = dotRow.anchorMax = new Vector2(0.5f, 0.5f);
        dotRow.anchoredPosition = new Vector2(0f, -760f);
        dots = new Image[Pages.Length];
        for (int i = 0; i < Pages.Length; i++)
        {
            var d = UIKit.Rect("Dot", dotRow);
            d.anchorMin = d.anchorMax = new Vector2(0.5f, 0.5f);
            d.sizeDelta = new Vector2(22f, 22f);
            dots[i] = d.gameObject.AddComponent<Image>();
            dots[i].raycastTarget = false;
            d.gameObject.AddComponent<UIRoundedCorners>().SetRadius(11f);
        }

        // NEXT / GET STARTED
        var btn = UIKit.PrimaryButton(root, "Next", null, "NEXT", Next);
        var br = (RectTransform)btn.transform;
        br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f);
        br.anchoredPosition = new Vector2(0f, -930f);
        br.sizeDelta = new Vector2(820f, 160f);
        buttonLabel = btn.GetComponentInChildren<TextMeshProUGUI>();

        // Skip
        var skip = UIKit.Rect("Skip", root);
        skip.anchorMin = skip.anchorMax = new Vector2(1f, 1f);
        skip.pivot = new Vector2(1f, 1f);
        skip.anchoredPosition = new Vector2(-50f, -60f);
        skip.sizeDelta = new Vector2(220f, 110f);
        var skipHit = skip.gameObject.AddComponent<Image>();
        skipHit.color = new Color(0f, 0f, 0f, 0f);
        skip.gameObject.AddComponent<Button>().onClick.AddListener(Close);
        var st = UIKit.Text("Label", skip, UIFonts.SemiBold, 44f, UIKit.TextMuted, TextAlignmentOptions.MidlineRight);
        st.text = "Skip";
        UIKit.Stretch(st.rectTransform, Vector2.zero, Vector2.zero);

        Layout(0f);
    }

    private static void Band(RectTransform rt, float y, float height, float margin)
    {
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y + height * 0.5f);
        rt.sizeDelta = new Vector2(-margin * 2f, height);
    }

    // ------------------------------------------------------------------ illustrations

    private readonly System.Collections.Generic.List<Texture2D> pictures = new System.Collections.Generic.List<Texture2D>();

    /// <summary>An AI picture fitted inside the art area (keeps its aspect), floating gently. The pictures
    /// have feathered (transparent) edges, so they melt into the dark background — no frame.</summary>
    private void BuildPicture(RectTransform art, Texture2D tex)
    {
        pictures.Add(tex);
        var frame = UIKit.Rect("Picture", art);
        UIKit.Stretch(frame, Vector2.zero, Vector2.zero);
        var fit = frame.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = (float)tex.width / tex.height;
        var img = frame.gameObject.AddComponent<RawImage>();
        img.texture = tex;
        img.raycastTarget = false;
        frame.gameObject.AddComponent<Floater>();
    }

    /// <summary>Search pill with a word being typed, above a 2×2 grid of model cards with spinning cubes.</summary>
    private void BuildSearchArt(RectTransform art)
    {
        var pill = UIKit.Rect("SearchPill", art);
        pill.anchorMin = pill.anchorMax = new Vector2(0.5f, 0.5f);
        pill.anchoredPosition = new Vector2(0f, 400f);
        pill.sizeDelta = new Vector2(780f, 130f);
        UIKit.Surface(pill.gameObject, UIKit.GlassFill, 65f, UIKit.WithAlpha(NeonArt.Cyan, 0.8f), 3f);
        var icon = UIKit.Icon(pill, UIIcons.Search, NeonArt.Cyan, 64f);
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        icon.rectTransform.anchoredPosition = new Vector2(75f, 0f);
        typed = UIKit.Text("Typed", pill, UIFonts.SemiBold, 48f, UIKit.TextMain, TextAlignmentOptions.MidlineLeft);
        UIKit.Stretch(typed.rectTransform, new Vector2(130f, 0f), new Vector2(-40f, 0f));

        float cw = 330f, gap = 40f;
        for (int i = 0; i < 4; i++)
        {
            float x = (i % 2 == 0 ? -1f : 1f) * (cw + gap) * 0.5f;
            float y = 120f - (i / 2) * (cw + gap) - cw * 0.5f + 40f;
            bool hot = i == 0;
            if (hot) NeonArt.Glow(art, new Vector2(x, y), cw * 1.5f, 0.16f);
            var card = UIKit.Rect("Card", art);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = new Vector2(x, y);
            card.sizeDelta = new Vector2(cw, cw);
            UIKit.Surface(card.gameObject, UIKit.GlassFill, 36f, UIKit.WithAlpha(NeonArt.Cyan, hot ? 0.9f : 0.3f), hot ? 3.5f : 2f);
            var cube = NeonArt.Cube(card, new Vector2(0f, 30f), 190f, hot ? 5f : 4f);
            cube.speed = 30f + i * 12f;
            cube.yaw = i * 37f;
            if (!hot) cube.color = UIKit.WithAlpha(NeonArt.Cyan, 0.55f);
            NeonArt.Bar(card, new Vector2(-40f, -115f), new Vector2(180f, 16f), new Color(1f, 1f, 1f, hot ? 0.5f : 0.18f));
            NeonArt.Bar(card, new Vector2(-75f, -142f), new Vector2(110f, 12f), new Color(1f, 1f, 1f, 0.12f));
        }
    }

    /// <summary>Big cube turning on a turntable ring, with rotate / zoom chips.</summary>
    private void BuildPreviewArt(RectTransform art)
    {
        NeonArt.Glow(art, new Vector2(0f, 120f), 900f, 0.10f);
        NeonArt.FloorRing(art, new Vector2(0f, -150f), 760f, 1f);
        NeonArt.FloorRing(art, new Vector2(0f, -150f), 540f, 0.35f);
        var cube = NeonArt.Cube(art, new Vector2(0f, 150f), 560f, 9f);
        cube.speed = 38f;
        cube.bobAmount = 10f;

        var spinL = UIKit.Icon(art, UIIcons.Reset, UIKit.WithAlpha(NeonArt.Cyan, 0.75f), 110f);
        spinL.rectTransform.anchoredPosition = new Vector2(-380f, 170f);
        var spinR = UIKit.Icon(art, UIIcons.Spin, UIKit.WithAlpha(NeonArt.Cyan, 0.75f), 110f);
        spinR.rectTransform.anchoredPosition = new Vector2(380f, 170f);

        Chip(art, new Vector2(-190f, -330f), "DRAG TO SPIN");
        Chip(art, new Vector2(190f, -330f), "PINCH TO ZOOM");
    }

    /// <summary>A phone showing the AR view: viewfinder brackets, a cube dropping onto a scanned floor ring,
    /// and the joystick + shutter controls along the bottom.</summary>
    private void BuildPlaceArt(RectTransform art)
    {
        var phone = UIKit.Rect("Phone", art);
        phone.anchorMin = phone.anchorMax = new Vector2(0.5f, 0.5f);
        phone.anchoredPosition = new Vector2(0f, 60f);
        phone.sizeDelta = new Vector2(560f, 980f);
        NeonArt.Glow(art, new Vector2(0f, 60f), 1000f, 0.07f);
        UIKit.Surface(phone.gameObject, new Color(0.03f, 0.06f, 0.09f, 1f), 70f, UIKit.WithAlpha(NeonArt.Cyan, 0.85f), 5f);
        NeonArt.Bar(phone, new Vector2(0f, 455f), new Vector2(120f, 14f), UIKit.WithAlpha(NeonArt.Cyan, 0.5f)); // speaker

        NeonArt.Brackets(phone, new Vector2(0f, 60f), new Vector2(420f, 520f), 70f, 7f, UIKit.WithAlpha(NeonArt.Cyan, 0.9f));
        NeonArt.ScanPulse(phone, new Vector2(0f, -110f), 420f, 0f);
        NeonArt.ScanPulse(phone, new Vector2(0f, -110f), 420f, 1.1f);
        NeonArt.FloorRing(phone, new Vector2(0f, -110f), 340f, 1f);
        var cube = NeonArt.Cube(phone, new Vector2(0f, 60f), 300f, 6f);
        cube.speed = 25f;
        cube.bobAmount = 14f;

        // Joystick + shutter, like the real AR screen
        var stick = UIKit.Rect("Stick", phone);
        stick.anchorMin = stick.anchorMax = new Vector2(0.5f, 0.5f);
        stick.anchoredPosition = new Vector2(-120f, -360f);
        stick.sizeDelta = new Vector2(130f, 130f);
        UIKit.Surface(stick.gameObject, new Color(0.05f, 0.09f, 0.12f, 0.9f), 65f, UIKit.WithAlpha(NeonArt.Cyan, 0.6f), 3f);
        var knob = NeonArt.Bar(stick, Vector2.zero, new Vector2(58f, 58f), NeonArt.Cyan);
        knob.gameObject.AddComponent<KnobWiggle>();

        var shutter = UIKit.Rect("Shutter", phone);
        shutter.anchorMin = shutter.anchorMax = new Vector2(0.5f, 0.5f);
        shutter.anchoredPosition = new Vector2(70f, -360f);
        shutter.sizeDelta = new Vector2(110f, 110f);
        UIKit.Surface(shutter.gameObject, new Color(0f, 0f, 0f, 0.3f), 55f, Color.white, 6f);
        NeonArt.Bar(shutter, Vector2.zero, new Vector2(80f, 80f), Color.white);
    }

    private static void Chip(RectTransform parent, Vector2 pos, string label)
    {
        var chip = UIKit.Rect("Chip", parent);
        chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 0.5f);
        chip.anchoredPosition = pos;
        chip.sizeDelta = new Vector2(340f, 86f);
        UIKit.Surface(chip.gameObject, UIKit.GlassFill, 43f, UIKit.WithAlpha(NeonArt.Cyan, 0.5f), 2f);
        var t = UIKit.Text("Label", chip, UIFonts.DisplaySemiBold, 36f, NeonArt.Cyan, TextAlignmentOptions.Center);
        t.text = label;
        t.characterSpacing = 6f;
        UIKit.Stretch(t.rectTransform, Vector2.zero, Vector2.zero);
    }

    // ------------------------------------------------------------------ paging

    void OnDestroy()
    {
        foreach (var t in pictures) if (t != null) Resources.UnloadAsset(t);
    }

    private void Next()
    {
        if (target >= Pages.Length - 1) { Close(); return; }
        target++;
    }

    private void Close()
    {
        if (closing) return;
        closing = true;
    }

    public void OnBeginDrag(PointerEventData e) { dragging = true; dragStart = position; }

    public void OnDrag(PointerEventData e)
    {
        float delta = (e.position.x - e.pressPosition.x) / Mathf.Max(1f, Screen.width); // in pages
        position = Mathf.Clamp(dragStart - delta * 1.1f, -0.25f, Pages.Length - 0.75f);
    }

    public void OnEndDrag(PointerEventData e)
    {
        dragging = false;
        float moved = position - dragStart;
        if (moved > 0.15f) target = Mathf.Min(Mathf.RoundToInt(dragStart) + 1, Pages.Length - 1);
        else if (moved < -0.15f) target = Mathf.Max(Mathf.RoundToInt(dragStart) - 1, 0);
        else target = Mathf.RoundToInt(dragStart);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (Input.GetKeyDown(KeyCode.Escape)) // Android back: previous page, or skip on the first
        {
            if (target > 0) target--;
            else Close();
        }

        if (!dragging) position = Mathf.Lerp(position, target, 1f - Mathf.Exp(-dt * 10f));
        Layout(dt);

        // Typing animation in the search pill
        if (typed != null)
        {
            typeTimer += dt;
            string word = Words[wordIndex];
            if (charCount < word.Length && typeTimer > 0.11f) { typeTimer = 0f; charCount++; }
            else if (charCount >= word.Length && typeTimer > 1.4f) { typeTimer = 0f; charCount = 0; wordIndex = (wordIndex + 1) % Words.Length; }
            bool caret = Mathf.Repeat(Time.unscaledTime, 0.9f) < 0.5f;
            typed.text = word.Substring(0, charCount) + (caret ? "<color=#3EF2FF>|</color>" : " ");
        }

        if (closing)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, 0f, dt * 3f);
            group.blocksRaycasts = false;
            if (group.alpha <= 0f) Destroy(gameObject);
        }
    }

    private void Layout(float dt)
    {
        float width = root.rect.width;
        int current = Mathf.Clamp(Mathf.RoundToInt(position), 0, Pages.Length - 1);
        for (int i = 0; i < pages.Length; i++)
        {
            float offset = i - position;
            pages[i].anchoredPosition = new Vector2(offset * width, 0f);
            pages[i].GetComponent<CanvasGroup>().alpha = Mathf.Clamp01(1f - Mathf.Abs(offset) * 0.8f);
            pages[i].gameObject.SetActive(Mathf.Abs(offset) < 1.2f); // off-screen pages don't animate
        }
        for (int i = 0; i < dots.Length; i++)
        {
            bool on = i == current;
            var rt = dots[i].rectTransform;
            float w = Mathf.Lerp(rt.sizeDelta.x, on ? 64f : 22f, dt > 0f ? 1f - Mathf.Exp(-dt * 12f) : 1f);
            rt.sizeDelta = new Vector2(w, 22f);
            dots[i].color = on ? NeonArt.Cyan : new Color(1f, 1f, 1f, 0.25f);
        }
        float x = -(dots.Length - 1) * 0.5f * 50f - 10f;
        for (int i = 0; i < dots.Length; i++)
        {
            var rt = dots[i].rectTransform;
            rt.anchoredPosition = new Vector2(x + rt.sizeDelta.x * 0.5f, 0f);
            x += rt.sizeDelta.x + 28f;
        }
        if (buttonLabel != null) buttonLabel.text = target >= Pages.Length - 1 ? "GET STARTED" : "NEXT";
    }
}

/// <summary>Slow up/down float for the guide pictures.</summary>
public class Floater : MonoBehaviour
{
    void Update() => ((RectTransform)transform).localPosition = new Vector3(0f, 12f * Mathf.Sin(Time.unscaledTime * 1.3f), 0f);
}

/// <summary>Moves the joystick knob in a slow circle in the guide illustration.</summary>
public class KnobWiggle : MonoBehaviour
{
    void Update()
    {
        float t = Time.unscaledTime * 1.6f;
        ((RectTransform)transform).anchoredPosition = new Vector2(Mathf.Cos(t), Mathf.Sin(t * 0.8f)) * 30f;
    }
}
