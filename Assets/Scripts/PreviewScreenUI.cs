using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Preview screen, design "A · Neon Showroom" (see the design canvas):
///   top bar (back · title/author · info) → FREE / size badges → model on a neon turntable
///   → gesture hint → glass dock with Auto-spin · Reset · Scene · Light and a big VIEW IN AR button.
/// Includes a loading state, a failure state and an info card. Built entirely in code and
/// replaces the scene's old UI canvas at runtime. Added automatically by ModelPreviewManager.
/// </summary>
public class PreviewScreenUI : MonoBehaviour
{
    // Palette (matches the search screen)
    static readonly Color Neon       = new Color(0.30f, 0.88f, 1f, 1f);
    static readonly Color Glass      = new Color(0.055f, 0.078f, 0.106f, 0.88f);
    static readonly Color DockColor  = new Color(0.047f, 0.071f, 0.098f, 0.94f);
    static readonly Color TextMain   = new Color(0.957f, 0.965f, 0.976f, 1f);
    static readonly Color TextMuted  = new Color(0.66f, 0.70f, 0.75f, 1f);
    static readonly Color ToolText   = new Color(0.86f, 0.89f, 0.92f, 1f);
    static readonly Color ButtonText = new Color(0.012f, 0.063f, 0.098f, 1f);

    // Design was drawn at 390pt wide; canvas reference is 1080 wide → 1pt = 2.77 units
    const float K = 1080f / 390f;

    private ModelPreviewManager manager;
    private PreviewControls orbit;
    private PreviewEnvironment environment;
    private Canvas canvas;

    private TextMeshProUGUI sceneLabel, lightLabel;
    private Image spinBg; private UIRoundedCorners spinShape; private RawImage spinIcon; private TextMeshProUGUI spinLabel;
    private CanvasGroup hintGroup, loadingGroup, infoGroup, failGroup;
    private RectTransform loadingBarFill;
    private Button arButton; private CanvasGroup arGroup;
    private bool hintHidden, infoOpen, ready, failed;

    private readonly Dictionary<RectTransform, float> pressAmounts = new Dictionary<RectTransform, float>();
    private readonly HashSet<RectTransform> pressed = new HashSet<RectTransform>();

    // ------------------------------------------------------------------ setup

    public static void Ensure()
    {
        if (FindFirstObjectByType<PreviewScreenUI>() != null) return;
        new GameObject("PreviewScreenUI").AddComponent<PreviewScreenUI>();
    }

    void Start()
    {
        manager = FindFirstObjectByType<ModelPreviewManager>();
        orbit = FindFirstObjectByType<PreviewControls>();
        Camera cam = manager != null && manager.previewCamera != null ? manager.previewCamera : Camera.main;

        environment = gameObject.AddComponent<PreviewEnvironment>();
        environment.Init(cam, manager != null ? manager.modelParent : null);

        HideOldUI();
        BuildUI();

        if (orbit != null) orbit.OnUserInteracted += HideHint;
        if (manager != null)
        {
            manager.OnModelReady += HandleReady;
            manager.OnModelFailed += HandleFailed;
            if (manager.IsReady) HandleReady(manager.ModelBounds);
            else if (manager.HasFailed) HandleFailed();
            // Design default: turntable spins until the user touches the model
            manager.SetAutoRotate(true);
        }
        RefreshTools();
    }

    void OnDestroy()
    {
        if (orbit != null) orbit.OnUserInteracted -= HideHint;
        if (manager != null)
        {
            manager.OnModelReady -= HandleReady;
            manager.OnModelFailed -= HandleFailed;
        }
    }

    private void HideOldUI()
    {
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (c.isRootCanvas && c.gameObject != gameObject) c.gameObject.SetActive(false);
    }

    private void HandleReady(Bounds bounds)
    {
        ready = true;
        environment.FitTurntable(bounds);
    }

    private void HandleFailed()
    {
        failed = true;
    }

    // ------------------------------------------------------------------ build

    private void BuildUI()
    {
        var canvasGO = new GameObject("PreviewUI", typeof(RectTransform));
        canvasGO.transform.SetParent(transform, false);
        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 2340f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();
        UIRoundedCorners.EnableCanvasChannels(canvas);
        var root = (RectTransform)canvasGO.transform;

        string title = PlayerPrefs.GetString("LastModelTitle", "3D model");
        string author = PlayerPrefs.GetString("LastModelAuthor", "");
        string sizeText = FormatSize(PlayerPrefs.GetString("LastModelSizeBytes", ""));

        BuildTopBar(root, title, author);
        BuildBadges(root, sizeText);

        // Play / pause + clip picker for animated models (hidden when the model has no animation)
        AnimationControls.Create(root, () => manager != null ? manager.LoadedModel : null, rt =>
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(16f * K, -150f * K);
        }, listOpensUp: false);
        BuildHint(root);
        BuildDock(root);
        BuildLoading(root);
        BuildFailure(root);
        BuildInfoCard(root, title, author, sizeText);

        SafeAreaFitter.Apply(canvas, addBackdrop: false);
    }

    private void BuildTopBar(RectTransform root, string title, string author)
    {
        var bar = NewRect("TopBar", root);
        bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, 44f * K);
        bar.anchoredPosition = new Vector2(0f, -44f * K);

        var back = IconButton(bar, "Back", UIIcons.Back, new Vector2(0f, 0.5f), new Vector2(20f * K + 22f * K, 0f), OnBack);
        var info = IconButton(bar, "Info", UIIcons.Info, new Vector2(1f, 0.5f), new Vector2(-20f * K - 22f * K, 0f), ToggleInfo);

        // ♥ save to favourites, left of the info button
        string uid = PlayerPrefs.GetString("LastModelUid", "");
        if (!string.IsNullOrEmpty(uid))
        {
            var heart = HeartButton.Create(bar, 44f * K);
            var hrt = (RectTransform)heart.transform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(1f, 0.5f);
            hrt.anchoredPosition = new Vector2(-20f * K - 44f * K - 10f * K - 22f * K, 0f);
            string thumb = PlayerPrefs.GetString("LastModelThumb", "");
            long.TryParse(PlayerPrefs.GetString("LastModelSizeBytes", ""), out long size);
            heart.Bind(new Favourites.Item { uid = uid, name = title, author = author, thumb = thumb, hiRes = thumb, size = size, likes = PlayerPrefs.GetInt("LastModelLikes", 0), animated = PlayerPrefs.GetInt("LastModelAnimated", 0) == 1 });
        }

        var titleText = NewText("Title", bar, UIFonts.SemiBold, 17f * K, TextMain, TextAlignmentOptions.Center);
        titleText.text = title;
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        titleText.overflowMode = TextOverflowModes.Ellipsis;
        var tr = titleText.rectTransform;
        tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
        tr.sizeDelta = new Vector2(160f * K, 22f * K); // room for back + ♥ + info
        tr.anchoredPosition = new Vector2(0f, string.IsNullOrEmpty(author) ? 0f : 9f * K);

        if (!string.IsNullOrEmpty(author))
        {
            var authorText = NewText("Author", bar, UIFonts.Regular, 13f * K, TextMuted, TextAlignmentOptions.Center);
            authorText.text = "by " + author;
            authorText.textWrappingMode = TextWrappingModes.NoWrap;
            authorText.overflowMode = TextOverflowModes.Ellipsis;
            var ar = authorText.rectTransform;
            ar.anchorMin = ar.anchorMax = new Vector2(0.5f, 0.5f);
            ar.sizeDelta = new Vector2(160f * K, 18f * K);
            ar.anchoredPosition = new Vector2(0f, -12f * K);
        }
    }

    private void BuildBadges(RectTransform root, string sizeText)
    {
        var row = NewRect("Badges", root);
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.anchoredPosition = new Vector2(0f, -112f * K);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f * K;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fit = row.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Pill(row, "FREE", UIFonts.Display, 13f * K, new Color(0.60f, 0.93f, 1f, 1f),
             new Color(0.02f, 0.047f, 0.078f, 0.75f), new Color(Neon.r, Neon.g, Neon.b, 0.45f), 1.2f * K);
        Pill(row, "Sketchfab", UIFonts.Regular, 12f * K, new Color(0.79f, 0.82f, 0.86f, 1f),
             new Color(1f, 1f, 1f, 0.07f), Color.clear, 0f);
        if (!string.IsNullOrEmpty(sizeText))
            Pill(row, sizeText, UIFonts.Regular, 12f * K, new Color(0.79f, 0.82f, 0.86f, 1f),
                 new Color(1f, 1f, 1f, 0.07f), Color.clear, 0f);
    }

    private void BuildHint(RectTransform root)
    {
        var chip = NewRect("GestureHint", root);
        chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 0f);
        chip.pivot = new Vector2(0.5f, 0f);
        chip.anchoredPosition = new Vector2(0f, 212f * K);
        hintGroup = chip.gameObject.AddComponent<CanvasGroup>();
        hintGroup.blocksRaycasts = false;
        var bg = chip.gameObject.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.06f);
        bg.raycastTarget = false;
        chip.gameObject.AddComponent<UIRoundedCorners>().SetRadius(9999f);
        var layout = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset((int)(14 * K), (int)(14 * K), (int)(7 * K), (int)(7 * K));
        layout.spacing = 8f * K;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fit = chip.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var icon = Icon(chip, UIIcons.Spin, TextMuted, 16f * K);
        var le = icon.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = 16f * K;
        var text = NewText("Text", chip, UIFonts.Regular, 13f * K, TextMuted, TextAlignmentOptions.Center);
        text.text = "Drag to rotate · Pinch to zoom";
        text.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private void BuildDock(RectTransform root)
    {
        var dock = NewRect("Dock", root);
        dock.anchorMin = new Vector2(0f, 0f); dock.anchorMax = new Vector2(1f, 0f);
        dock.pivot = new Vector2(0.5f, 0f);
        dock.offsetMin = new Vector2(16f * K, 28f * K);
        dock.offsetMax = new Vector2(-16f * K, (28f + 164f) * K);
        Surface(dock.gameObject, DockColor, 26f * K, new Color(Neon.r, Neon.g, Neon.b, 0.35f), 1.5f * K);

        // Tool row
        var tools = NewRect("Tools", dock);
        tools.anchorMin = new Vector2(0f, 1f); tools.anchorMax = new Vector2(1f, 1f);
        tools.pivot = new Vector2(0.5f, 1f);
        tools.offsetMin = new Vector2(14f * K, -(14f + 64f) * K);
        tools.offsetMax = new Vector2(-14f * K, -14f * K);
        var row = tools.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 8f * K;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = true;

        ToolButton(tools, UIIcons.Spin, "Auto-spin", ToggleSpin, out spinBg, out spinShape, out spinIcon, out spinLabel);
        ToolButton(tools, UIIcons.Reset, "Reset", OnReset, out _, out _, out _, out _);
        ToolButton(tools, UIIcons.Scene, PreviewEnvironment.SceneNames[(int)environment.CurrentScene], OnScene, out _, out _, out _, out sceneLabel);
        ToolButton(tools, UIIcons.Light, PreviewEnvironment.LightNames[(int)environment.CurrentLight], OnLight, out _, out _, out _, out lightLabel);

        // VIEW IN AR
        var arRect = NewRect("ViewInAR", dock);
        arRect.anchorMin = new Vector2(0f, 0f); arRect.anchorMax = new Vector2(1f, 0f);
        arRect.pivot = new Vector2(0.5f, 0f);
        arRect.offsetMin = new Vector2(14f * K, 14f * K);
        arRect.offsetMax = new Vector2(-14f * K, (14f + 58f) * K);

        var glow = NewRect("Glow", dock);
        glow.anchorMin = arRect.anchorMin; glow.anchorMax = arRect.anchorMax; glow.pivot = arRect.pivot;
        glow.offsetMin = arRect.offsetMin - new Vector2(12f, 12f) * K;
        glow.offsetMax = arRect.offsetMax + new Vector2(12f, 12f) * K;
        glow.SetSiblingIndex(arRect.GetSiblingIndex());
        var glowImg = glow.gameObject.AddComponent<Image>();
        glowImg.color = new Color(Neon.r, Neon.g, Neon.b, 0.45f);
        glowImg.raycastTarget = false;
        var glowShape = glow.gameObject.AddComponent<UIRoundedCorners>();
        glowShape.SetRadius(30f * K);
        glowShape.SetSoftness(16f * K);

        var arImg = arRect.gameObject.AddComponent<Image>();
        arImg.color = Color.white;
        arRect.gameObject.AddComponent<UIRoundedCorners>().SetRadius(18f * K);
        arRect.gameObject.AddComponent<UIGradient>().Set(new Color(0.56f, 0.95f, 1f, 1f), new Color(0.11f, 0.61f, 0.94f, 1f));
        arButton = arRect.gameObject.AddComponent<Button>();
        arButton.transition = Selectable.Transition.None;
        arButton.onClick.AddListener(OnViewInAR);
        arGroup = arRect.gameObject.AddComponent<CanvasGroup>();
        AddPress(arRect);

        var content = NewRect("Content", arRect);
        content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
        var cl = content.gameObject.AddComponent<HorizontalLayoutGroup>();
        cl.spacing = 10f * K;
        cl.childAlignment = TextAnchor.MiddleCenter;
        cl.childControlWidth = cl.childControlHeight = true;
        cl.childForceExpandWidth = cl.childForceExpandHeight = false;
        var cf = content.gameObject.AddComponent<ContentSizeFitter>();
        cf.horizontalFit = cf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var cube = Icon(content, UIIcons.ARCube, ButtonText, 22f * K);
        var cle = cube.gameObject.AddComponent<LayoutElement>();
        cle.preferredWidth = cle.preferredHeight = 22f * K;
        var label = NewText("Label", content, UIFonts.Display, 22f * K, ButtonText, TextAlignmentOptions.Center);
        label.text = "VIEW IN AR";
        label.characterSpacing = 8f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private void BuildLoading(RectTransform root)
    {
        var box = NewRect("Loading", root);
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.55f);
        box.sizeDelta = new Vector2(240f * K, 60f * K);
        loadingGroup = box.gameObject.AddComponent<CanvasGroup>();
        loadingGroup.blocksRaycasts = false;

        var text = NewText("Text", box, UIFonts.SemiBold, 16f * K, TextMain, TextAlignmentOptions.Center);
        text.text = "Loading model…";
        Stretch(text.rectTransform, new Vector2(0f, 20f * K), Vector2.zero);

        var track = NewRect("Track", box);
        track.anchorMin = track.anchorMax = new Vector2(0.5f, 0f);
        track.pivot = new Vector2(0.5f, 0f);
        track.sizeDelta = new Vector2(180f * K, 6f * K);
        track.anchoredPosition = new Vector2(0f, 4f * K);
        var trackImg = track.gameObject.AddComponent<Image>();
        trackImg.color = new Color(1f, 1f, 1f, 0.10f);
        trackImg.raycastTarget = false;
        track.gameObject.AddComponent<UIRoundedCorners>().SetRadius(9999f);

        loadingBarFill = NewRect("Fill", track);
        loadingBarFill.anchorMin = new Vector2(0f, 0f); loadingBarFill.anchorMax = new Vector2(0f, 1f);
        loadingBarFill.pivot = new Vector2(0f, 0.5f);
        loadingBarFill.sizeDelta = new Vector2(60f * K, 0f);
        var fillImg = loadingBarFill.gameObject.AddComponent<Image>();
        fillImg.color = Neon;
        fillImg.raycastTarget = false;
        loadingBarFill.gameObject.AddComponent<UIRoundedCorners>().SetRadius(9999f);
    }

    private void BuildFailure(RectTransform root)
    {
        var box = NewRect("Failed", root);
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.55f);
        box.sizeDelta = new Vector2(300f * K, 70f * K);
        failGroup = box.gameObject.AddComponent<CanvasGroup>();
        failGroup.alpha = 0f;
        failGroup.blocksRaycasts = false;

        var t1 = NewText("Title", box, UIFonts.SemiBold, 17f * K, TextMain, TextAlignmentOptions.Center);
        t1.text = "Couldn't open this model";
        Stretch(t1.rectTransform, new Vector2(0f, 30f * K), Vector2.zero);
        var t2 = NewText("Sub", box, UIFonts.Regular, 14f * K, TextMuted, TextAlignmentOptions.Center);
        t2.text = "Go back and try another one.";
        Stretch(t2.rectTransform, Vector2.zero, new Vector2(0f, -36f * K));
    }

    private void BuildInfoCard(RectTransform root, string title, string author, string sizeText)
    {
        var card = NewRect("InfoCard", root);
        card.anchorMin = new Vector2(0f, 1f); card.anchorMax = new Vector2(1f, 1f);
        card.pivot = new Vector2(0.5f, 1f);
        card.anchoredPosition = new Vector2(0f, -100f * K);
        card.offsetMin = new Vector2(16f * K, card.offsetMin.y);
        card.offsetMax = new Vector2(-16f * K, card.offsetMax.y);
        Surface(card.gameObject, new Color(0.055f, 0.078f, 0.106f, 0.97f), 22f * K, new Color(Neon.r, Neon.g, Neon.b, 0.45f), 1.5f * K);
        infoGroup = card.gameObject.AddComponent<CanvasGroup>();
        infoGroup.alpha = 0f;
        infoGroup.blocksRaycasts = false;

        var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset((int)(18 * K), (int)(18 * K), (int)(16 * K), (int)(18 * K));
        layout.spacing = 6f * K;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fit = card.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var t = NewText("Title", card, UIFonts.Bold, 18f * K, TextMain, TextAlignmentOptions.TopLeft);
        t.text = title;
        if (!string.IsNullOrEmpty(author))
        {
            var a = NewText("Author", card, UIFonts.Regular, 14f * K, TextMuted, TextAlignmentOptions.TopLeft);
            a.text = "by " + author;
        }
        var details = NewText("Details", card, UIFonts.Regular, 13f * K, ToolText, TextAlignmentOptions.TopLeft);
        details.text = "Free download from Sketchfab" + (string.IsNullOrEmpty(sizeText) ? "" : " · " + sizeText) +
                       "\nCreative Commons model: please credit the author when you share it.";
        details.lineSpacing = 6f;
    }

    // ------------------------------------------------------------------ actions

    private void OnBack() => SceneManager.LoadScene("SampleScene");

    private void ToggleInfo() => infoOpen = !infoOpen;

    private void ToggleSpin()
    {
        if (manager != null) manager.SetAutoRotate(!manager.GetAutoRotate());
        RefreshTools();
    }

    private void OnReset()
    {
        if (manager != null) manager.ResetView();
        else if (orbit != null) orbit.ResetView();
    }

    private void OnScene()
    {
        var mode = environment.NextScene();
        if (sceneLabel != null) sceneLabel.text = PreviewEnvironment.SceneNames[(int)mode];
    }

    private void OnLight()
    {
        var mode = environment.NextLight();
        if (lightLabel != null) lightLabel.text = PreviewEnvironment.LightNames[(int)mode];
    }

    private void OnViewInAR()
    {
        if (!ready) return;
        var legacy = FindFirstObjectByType<PreviewUIManager>(FindObjectsInactive.Include);
        if (legacy != null)
        {
            if (legacy.previewManager == null) legacy.previewManager = manager;
            legacy.OnViewInAR();
            return;
        }
        // Fallback: same hand-off as PreviewUIManager
        string path = PlayerPrefs.GetString("LastModelPath", "");
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
        if (WebARLauncher.TryOpen(path, PlayerPrefs.GetString("LastModelTitle", "3D model"))) return; // xr8-webar
        if (ARModelBridge.Instance == null) new GameObject("ARModelBridge").AddComponent<ARModelBridge>();
        ARModelBridge.Instance.SetModel(manager != null ? manager.LoadedGltf : null, path);
        SceneManager.LoadScene("ARScene");
    }

    private void HideHint() => hintHidden = true;

    private void RefreshTools()
    {
        bool on = manager != null && manager.GetAutoRotate();
        if (spinBg != null) spinBg.color = on ? new Color(Neon.r, Neon.g, Neon.b, 0.14f) : new Color(1f, 1f, 1f, 0.04f);
        if (spinShape != null)
        {
            spinShape.SetBorderColor(on ? Neon : new Color(1f, 1f, 1f, 0.10f));
            spinShape.SetBorder((on ? 1.5f : 1f) * K);
        }
        if (spinIcon != null) spinIcon.color = on ? Neon : ToolText;
        if (spinLabel != null) spinLabel.color = on ? Neon : ToolText;
    }

    // ------------------------------------------------------------------ animation

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float now = Time.unscaledTime;

        Fade(loadingGroup, !ready && !failed, dt);
        Fade(failGroup, failed, dt);
        Fade(hintGroup, ready && !hintHidden, dt);
        Fade(infoGroup, infoOpen, dt);
        if (infoGroup != null) infoGroup.blocksRaycasts = infoOpen;

        if (loadingBarFill != null && !ready)
        {
            float w = ((RectTransform)loadingBarFill.parent).rect.width;
            float seg = loadingBarFill.rect.width;
            float t = 0.5f + 0.5f * Mathf.Sin(now * 2.6f);
            loadingBarFill.anchoredPosition = new Vector2((w - seg) * t, 0f);
        }

        if (arGroup != null)
        {
            arGroup.alpha = Mathf.MoveTowards(arGroup.alpha, ready ? 1f : 0.45f, dt * 4f);
            arButton.interactable = ready;
        }

        // Press springs
        var keys = new List<RectTransform>(pressAmounts.Keys);
        foreach (var rt in keys)
        {
            if (rt == null) continue;
            bool isDown = pressed.Contains(rt);
            float a = Mathf.MoveTowards(pressAmounts[rt], isDown ? 1f : 0f, dt * (isDown ? 12f : 7f));
            pressAmounts[rt] = a;
            rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.95f, a * a * (3f - 2f * a));
        }
    }

    private static void Fade(CanvasGroup g, bool show, float dt)
    {
        if (g == null) return;
        g.alpha = Mathf.MoveTowards(g.alpha, show ? 1f : 0f, dt * 5f);
    }

    // ------------------------------------------------------------------ widgets

    private RectTransform IconButton(RectTransform parent, string objName, string icon, Vector2 anchor, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        var rt = NewRect(objName, parent);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.sizeDelta = new Vector2(44f * K, 44f * K);
        rt.anchoredPosition = pos;
        Surface(rt.gameObject, new Color(0.055f, 0.078f, 0.106f, 0.85f), 14f * K, new Color(Neon.r, Neon.g, Neon.b, 0.55f), 1.5f * K);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(onClick);
        var ic = Icon(rt, icon, Neon, 20f * K);
        ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        AddPress(rt);
        return rt;
    }

    private void ToolButton(RectTransform parent, string icon, string label, UnityEngine.Events.UnityAction onClick,
                            out Image bg, out UIRoundedCorners shape, out RawImage iconImg, out TextMeshProUGUI text)
    {
        var rt = NewRect(label + "Tool", parent);
        bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.04f);
        shape = rt.gameObject.AddComponent<UIRoundedCorners>();
        shape.SetBorderColor(new Color(1f, 1f, 1f, 0.10f));
        shape.SetRadius(16f * K);
        shape.SetBorder(1f * K);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(onClick);
        AddPress(rt);

        iconImg = Icon(rt, icon, ToolText, 20f * K);
        iconImg.rectTransform.anchorMin = iconImg.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        iconImg.rectTransform.anchoredPosition = new Vector2(0f, 9f * K);

        text = NewText("Label", rt, UIFonts.SemiBold, 11f * K, ToolText, TextAlignmentOptions.Center);
        text.text = label;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        var tr = text.rectTransform;
        tr.anchorMin = new Vector2(0f, 0.5f); tr.anchorMax = new Vector2(1f, 0.5f);
        tr.sizeDelta = new Vector2(0f, 16f * K);
        tr.anchoredPosition = new Vector2(0f, -14f * K);
    }

    private void Pill(RectTransform parent, string label, TMP_FontAsset font, float size, Color textColor,
                      Color fill, Color border, float borderWidth)
    {
        var rt = NewRect(label + "Pill", parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = fill;
        img.raycastTarget = false;
        var shape = rt.gameObject.AddComponent<UIRoundedCorners>();
        if (borderWidth > 0f) shape.SetBorderColor(border);
        shape.SetRadius(9999f);
        shape.SetBorder(borderWidth);
        var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset((int)(12 * K), (int)(12 * K), (int)(4 * K), (int)(4 * K));
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var t = NewText("Text", rt, font, size, textColor, TextAlignmentOptions.Center);
        t.text = label;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        if (font == UIFonts.Display) t.characterSpacing = 10f;
    }

    private void AddPress(RectTransform rt)
    {
        pressAmounts[rt] = 0f;
        var relay = rt.gameObject.AddComponent<UIPressRelay>();
        relay.onChange = down => { if (down) pressed.Add(rt); else pressed.Remove(rt); };
    }

    private static void Surface(GameObject go, Color fill, float radius, Color border, float borderWidth)
    {
        var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        img.color = fill;
        var shape = go.GetComponent<UIRoundedCorners>() ?? go.AddComponent<UIRoundedCorners>();
        shape.SetBorderColor(border);
        shape.SetRadius(radius);
        shape.SetBorder(borderWidth);
    }

    private static RawImage Icon(RectTransform parent, string name, Color color, float size)
    {
        var rt = NewRect("Icon", parent);
        rt.sizeDelta = new Vector2(size, size);
        var img = rt.gameObject.AddComponent<RawImage>();
        img.texture = UIIcons.Get(name);
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static TextMeshProUGUI NewText(string objName, Transform parent, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
    {
        var rt = NewRect(objName, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        UIFonts.Apply(t, font);
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    private static RectTransform NewRect(string objName, Transform parent)
    {
        var go = new GameObject(objName, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private static string FormatSize(string bytesText)
    {
        if (!ulong.TryParse(bytesText, out ulong bytes) || bytes == 0) return null;
        double mb = bytes / (1024.0 * 1024.0);
        return mb >= 10 ? $"{mb:0} MB" : $"{mb:0.0} MB";
    }
}
