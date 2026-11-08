using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// "Recommended for you" slideshow on the main search screen (shown while no search results are visible).
///
/// Shows real models, one big card at a time — swipe, or let it auto-advance; tap a card (or VIEW) to open it.
/// What gets recommended:
///   1. "Because you searched …" — a free model for each of the user's last 2 searches
///   2. "Anime pick" — a free model for popular anime characters
///      (Naruto, Gojo and Itachi always, plus a few others rotated on each launch)
/// Added by SearchUI.Start. Edit the character list in <see cref="AnimeCharacters"/>.
/// </summary>
public class RecommendationsPanel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    /// <summary>Display name → search query. The first three are always included.</summary>
    public static readonly (string label, string query)[] AnimeCharacters =
    {
        ("Naruto Uzumaki", "naruto"),
        ("Satoru Gojo", "gojo satoru"),
        ("Itachi Uchiha", "itachi"),
        ("Sasuke Uchiha", "sasuke"),
        ("Kakashi Hatake", "kakashi"),
        ("Ryomen Sukuna", "sukuna"),
        ("Monkey D. Luffy", "luffy"),
        ("Roronoa Zoro", "zoro"),
        ("Goku", "goku"),
        ("Vegeta", "vegeta"),
        ("Tanjiro Kamado", "tanjiro"),
        ("Nezuko Kamado", "nezuko"),
        ("Levi Ackerman", "levi ackerman"),
        ("Saitama", "saitama"),
        ("Killua Zoldyck", "killua"),
        ("Ichigo Kurosaki", "ichigo"),
        ("Pikachu", "pikachu"),
    };

    const int AlwaysIncluded = 3;   // Naruto, Gojo, Itachi
    const int RotatingExtras = 4;   // random others per launch
    const int MaxRecentPicks = 2;
    const string RecentKey = "RecentSearches";
    const float AutoAdvanceSeconds = 4.5f;

    static readonly Color Neon     = new Color(0.30f, 0.88f, 1f, 1f);
    static readonly Color Ink      = new Color(0.012f, 0.063f, 0.098f, 1f);
    static readonly Color CardFill = new Color(0.055f, 0.078f, 0.106f, 1f);
    static readonly Color TextMain = new Color(0.957f, 0.965f, 0.976f, 1f);
    static readonly Color TextSoft = new Color(0.72f, 0.76f, 0.81f, 1f);

    private class Slide
    {
        public string Uid, Caption, Title, ModelName, Author, HiResUrl, Query;
        public long SizeBytes;
        public int Likes;
        public bool Animated;
        public RectTransform Rect;
        public CanvasGroup Group;
        public RawImage Image;
        public Texture2D Texture;
        public Vector2 LastSize;
    }

    private SketchfabAPIManager api;
    private TMP_InputField searchBar;
    private Transform resultsContent;
    private CanvasGroup group;
    private RectTransform slideArea, dotsRow;
    private TextMeshProUGUI statusText;
    private readonly List<Slide> slides = new List<Slide>();
    private readonly List<Image> dots = new List<Image>();
    private readonly HashSet<string> usedUids = new HashSet<string>();

    private float position;        // current (animated) slide index
    private int target;            // slide we are moving to
    private bool dragging;
    private float dragStartPosition;
    private float lastInteraction;
    private bool loadingDone;

    // Navigation
    private CanvasGroup navGroup;           // ‹ › arrows on the slideshow
    private CanvasGroup backGroup;          // "‹ Recommended" over the search results
    private ResultsGridLayout gridLayout;
    private ScrollRect resultsScroll;
    const float BackBarHeight = 170f;       // space reserved above the first row of results

    // ------------------------------------------------------------------ public API

    /// <summary>True once the first slide's picture is on screen (or loading finished with nothing to show).</summary>
    public bool IsReady => slides.Count > 0 ? slides[0].Texture != null : loadingDone;

    public static RecommendationsPanel Create(ScrollRect results, SketchfabAPIManager api, TMP_InputField searchBar)
    {
        if (results == null || api == null) return null;
        var existing = results.GetComponentInChildren<RecommendationsPanel>(true);
        if (existing != null) return existing;

        var go = new GameObject("Recommendations", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(results.transform, false);
        rt.SetAsLastSibling();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var panel = go.AddComponent<RecommendationsPanel>();
        panel.api = api;
        panel.searchBar = searchBar;
        panel.resultsContent = api.resultsParent != null ? api.resultsParent : results.content;
        panel.resultsScroll = results;
        panel.gridLayout = results.GetComponentInChildren<ResultsGridLayout>(true);
        panel.Build(rt);
        panel.BuildBackButton(results);
        SearchStatusPill.Create(results.transform, api);
        panel.StartCoroutine(panel.LoadRecommendations());
        return panel;
    }

    /// <summary>Remembers a search so the slideshow can recommend "Because you searched …".</summary>
    public static void RememberSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        query = query.Trim();
        var list = LoadRecents();
        list.RemoveAll(s => string.Equals(s, query, System.StringComparison.OrdinalIgnoreCase));
        list.Insert(0, query);
        if (list.Count > 5) list.RemoveRange(5, list.Count - 5);
        PlayerPrefs.SetString(RecentKey, string.Join("\n", list));
        PlayerPrefs.Save();
    }

    private static List<string> LoadRecents()
    {
        var list = new List<string>();
        foreach (var s in PlayerPrefs.GetString(RecentKey, "").Split('\n'))
            if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
        return list;
    }

    // ------------------------------------------------------------------ build

    private void Build(RectTransform rt)
    {
        UIRoundedCorners.EnableCanvasChannels(GetComponentInParent<Canvas>());
        group = gameObject.AddComponent<CanvasGroup>();

        // Transparent hit area so swipes anywhere on the panel move the slideshow
        var hit = gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);

        // Top: quick actions (Favourites · Animated · Recents · Surprise)
        quickRow = NewRect("QuickActions", rt);
        quickRow.anchorMin = new Vector2(0f, 1f); quickRow.anchorMax = new Vector2(1f, 1f);
        quickRow.pivot = new Vector2(0.5f, 1f);
        quickRow.offsetMin = new Vector2(0f, -HeadingTop - QuickRowHeight); quickRow.offsetMax = new Vector2(0f, -HeadingTop);
        var ql = quickRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        ql.padding = new RectOffset(80, 80, 0, 0);
        ql.spacing = 30f;
        ql.childAlignment = TextAnchor.MiddleCenter;
        ql.childControlWidth = ql.childControlHeight = true;
        ql.childForceExpandWidth = ql.childForceExpandHeight = true;   // 4 equal tiles across
        BuildQuickActions(quickRow);

        // Heading in the same style as "Explore by category": cyan accent bar + bold title.
        // Its row is moved down in Update so it always sits right above the cards (no empty gap).
        headingRow = NewRect("HeadingRow", rt);
        headingRow.anchorMin = new Vector2(0f, 1f); headingRow.anchorMax = new Vector2(1f, 1f);
        headingRow.pivot = new Vector2(0.5f, 1f);
        headingRow.sizeDelta = new Vector2(0f, 120f);
        headingRow.anchoredPosition = new Vector2(0f, -HeadingY);

        var bar = NewRect("Accent", headingRow);
        bar.anchorMin = bar.anchorMax = new Vector2(0f, 1f);
        bar.pivot = new Vector2(0f, 1f);
        bar.anchoredPosition = new Vector2(80f, -28f);
        bar.sizeDelta = new Vector2(12f, 64f);
        var barImg = bar.gameObject.AddComponent<Image>();
        barImg.color = Neon;
        barImg.raycastTarget = false;
        bar.gameObject.AddComponent<UIRoundedCorners>().SetRadius(6f);

        var heading = NewText("Heading", headingRow, UIFonts.Bold, 70f, TextMain, TextAlignmentOptions.MidlineLeft);
        heading.text = "Recommended for you";
        var hr = heading.rectTransform;
        hr.anchorMin = Vector2.zero; hr.anchorMax = Vector2.one;
        hr.offsetMin = new Vector2(116f, 0f); hr.offsetMax = new Vector2(-80f, 0f);

        slideArea = NewRect("Slides", rt);
        slideArea.anchorMin = Vector2.zero; slideArea.anchorMax = Vector2.one;
        slideArea.offsetMin = new Vector2(0f, 190f + CategoryRow.Height); // "Explore by category" sits below
        slideArea.offsetMax = new Vector2(0f, -HeadingY - 140f);
        // Clip the cards to the panel: the peeking neighbours must never cross the neon frame
        var mask = slideArea.gameObject.AddComponent<RectMask2D>();
        mask.padding = new Vector4(SideClip, 0f, SideClip, 0f);
        mask.softness = new Vector2Int(24, 0);

        statusText = NewText("Status", slideArea, UIFonts.SemiBold, 56f, TextSoft, TextAlignmentOptions.Center);
        statusText.text = "Finding models for you…";
        Stretch(statusText.rectTransform);

        // ‹ › arrows on either side of the current card
        var nav = NewRect("Arrows", rt);
        nav.anchorMin = slideArea.anchorMin; nav.anchorMax = slideArea.anchorMax;
        nav.offsetMin = slideArea.offsetMin; nav.offsetMax = slideArea.offsetMax;
        navGroup = nav.gameObject.AddComponent<CanvasGroup>();
        navGroup.alpha = 0f;
        ArrowButton(nav, "Prev", UIIcons.Back, new Vector2(0f, 0.5f), new Vector2(40f, 0f), () => Step(-1));
        ArrowButton(nav, "Next", UIIcons.Next, new Vector2(1f, 0.5f), new Vector2(-40f, 0f), () => Step(+1));

        dotsRow = NewRect("Dots", rt);
        dotsRow.anchorMin = dotsRow.anchorMax = new Vector2(0.5f, 0f);
        dotsRow.pivot = new Vector2(0.5f, 0f);
        dotsRow.anchoredPosition = new Vector2(0f, 100f + CategoryRow.Height);
        var hl = dotsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 18f;
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = hl.childForceExpandHeight = false;
        var fit = dotsRow.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // "Explore by category" strip under the slideshow (loads after the recommendations)
        CategoryRow.Create(rt, api, searchBar, () => ShouldPause, () => loadingDone);
    }

    private Slide AddSlide(string uid, string caption, string title, string modelName, string author, string hiResUrl, string query, long sizeBytes = 0, int likes = 0, bool animated = false)
    {
        var s = new Slide { Likes = likes, Animated = animated, Uid = uid, Caption = caption, Title = title, ModelName = modelName, Author = author, HiResUrl = hiResUrl, Query = query, SizeBytes = sizeBytes };

        s.Rect = NewRect("Slide " + title, slideArea);
        s.Rect.anchorMin = s.Rect.anchorMax = new Vector2(0.5f, 0.5f);
        s.Group = s.Rect.gameObject.AddComponent<CanvasGroup>();
        s.Group.alpha = 0f;
        Surface(s.Rect.gameObject, CardFill, 72f, new Color(Neon.r, Neon.g, Neon.b, 0.4f), 3f);

        // Thumbnail (cover-cropped, rounded) with a pulsing placeholder until it loads
        var imgRt = NewRect("Image", s.Rect);
        imgRt.anchorMin = Vector2.zero; imgRt.anchorMax = Vector2.one;
        imgRt.offsetMin = new Vector2(9f, 9f); imgRt.offsetMax = new Vector2(-9f, -9f);
        s.Image = imgRt.gameObject.AddComponent<RawImage>();
        s.Image.color = new Color(0.12f, 0.15f, 0.19f, 1f);
        s.Image.raycastTarget = false;
        imgRt.gameObject.AddComponent<UIRoundedCorners>().SetRadius(64f);

        // Dark fade at the bottom so the text is readable over any image
        var scrim = NewRect("Scrim", s.Rect);
        scrim.anchorMin = new Vector2(0f, 0f); scrim.anchorMax = new Vector2(1f, 0.58f);
        scrim.offsetMin = new Vector2(9f, 9f); scrim.offsetMax = new Vector2(-9f, 0f);
        var scrimImg = scrim.gameObject.AddComponent<Image>();
        scrimImg.color = Color.white;
        scrimImg.raycastTarget = false;
        scrim.gameObject.AddComponent<UIRoundedCorners>().SetRadii(0f, 0f, 64f, 64f);
        scrim.gameObject.AddComponent<UIGradient>().Set(new Color(0.02f, 0.03f, 0.05f, 0f), new Color(0.02f, 0.03f, 0.05f, 0.94f));

        // Caption pill · title · model name and author
        var captionRt = NewRect("Caption", s.Rect);
        captionRt.anchorMin = captionRt.anchorMax = new Vector2(0f, 0f);
        captionRt.pivot = new Vector2(0f, 0f);
        captionRt.anchoredPosition = new Vector2(64f, 360f);
        Surface(captionRt.gameObject, new Color(0.02f, 0.05f, 0.08f, 0.85f), 9999f, new Color(Neon.r, Neon.g, Neon.b, 0.6f), 2.5f);
        var cl = captionRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        cl.padding = new RectOffset(30, 30, 10, 10);
        cl.childControlWidth = cl.childControlHeight = true;
        cl.childForceExpandWidth = cl.childForceExpandHeight = false;
        var cf = captionRt.gameObject.AddComponent<ContentSizeFitter>();
        cf.horizontalFit = cf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var capText = NewText("Text", captionRt, UIFonts.Display, 42f, Neon, TextAlignmentOptions.Center);
        capText.text = s.Caption;
        capText.characterSpacing = 10f;
        capText.textWrappingMode = TextWrappingModes.NoWrap;

        var titleText = NewText("Title", s.Rect, UIFonts.Bold, 100f, TextMain, TextAlignmentOptions.BottomLeft);
        titleText.text = s.Title;
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        titleText.overflowMode = TextOverflowModes.Ellipsis;
        var tr = titleText.rectTransform;
        tr.anchorMin = new Vector2(0f, 0f); tr.anchorMax = new Vector2(1f, 0f);
        tr.pivot = new Vector2(0f, 0f);
        tr.offsetMin = new Vector2(64f, 200f); tr.offsetMax = new Vector2(-64f, 340f);

        var sub = NewText("Model", s.Rect, UIFonts.Regular, 52f, TextSoft, TextAlignmentOptions.TopLeft);
        sub.text = string.IsNullOrEmpty(s.Author) ? "Free 3D model" : "by " + s.Author;
        sub.textWrappingMode = TextWrappingModes.NoWrap;
        sub.overflowMode = TextOverflowModes.Ellipsis;
        var sr = sub.rectTransform;
        sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(1f, 0f);
        sr.pivot = new Vector2(0f, 0f);
        sr.offsetMin = new Vector2(64f, 70f); sr.offsetMax = new Vector2(-520f, 190f);

        // VIEW button
        var view = UIKit.PrimaryButton(s.Rect, "View", UIIcons.ARCube, "VIEW", () => Open(s));
        var vr = (RectTransform)view.transform;
        vr.anchorMin = vr.anchorMax = new Vector2(1f, 0f);
        vr.pivot = new Vector2(1f, 0f);
        vr.sizeDelta = new Vector2(400f, 150f);
        vr.anchoredPosition = new Vector2(-56f, 60f);

        // ♥ save to favourites (top-right of the card)
        var heart = HeartButton.Create(s.Rect, 140f);
        var hrt = (RectTransform)heart.transform;
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(1f, 1f);
        hrt.anchoredPosition = new Vector2(-40f, -40f);
        heart.Bind(new Favourites.Item { uid = uid, name = modelName, author = author, thumb = hiResUrl, hiRes = hiResUrl, size = sizeBytes, likes = likes, animated = animated });

        // Tap anywhere on the card also opens it
        var btn = s.Rect.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => { if (!dragging) ShowAllLike(s); }); // tap card → all models for that name

        slides.Add(s);
        AddDot();
        statusText.gameObject.SetActive(false);
        return s;
    }

    private void AddDot()
    {
        var rt = NewRect("Dot", dotsRow);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 22f;
        le.preferredHeight = 22f;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.3f);
        img.raycastTarget = false;
        rt.gameObject.AddComponent<UIRoundedCorners>().SetRadius(9999f);
        dots.Add(img);
    }

    // ------------------------------------------------------------------ data

    // ------------------------------------------------------------------ loading (cached, parallel, polite)

    [System.Serializable] private class CachedSlide { public string uid, caption, title, author, image, query; public long size; public int likes; public bool animated; }
    [System.Serializable] private class SlideCache { public long savedAt; public string recentsKey; public List<CachedSlide> slides = new List<CachedSlide>(); }

    const string CacheKey = "RecommendationsCacheV4"; // V4: slides remember file size, likes, animated
    const double CacheHours = 24;
    const int ParallelFetches = 3;     // searches at once (was 1 after another)
    const int ResultsPerQuery = 12;    // enough to skip models already used by another slide (was 24)
    const int SlideImageWidth = 640;   // sharp on the card, ~1/3 the data of 1024 px

    /// <summary>Background loading waits while a model downloads or search results are on screen.</summary>
    private bool ShouldPause => NetworkPriority.BackgroundPaused ||
                                (resultsContent != null && resultsContent.childCount > 0);

    private IEnumerator LoadRecommendations()
    {
        yield return null; // let the API manager read its token

        var recents = LoadRecents();
        var recentPicks = recents.GetRange(0, Mathf.Min(MaxRecentPicks, recents.Count));
        string recentsKey = string.Join("|", recentPicks);

        // 1) Saved recommendations from the last 24 h: slideshow appears instantly, no network
        if (TryLoadCache(recentsKey, out var cached))
        {
            foreach (var c in cached.slides) ShowSlide(c);
            loadingDone = true;
            Debug.Log($"[Recommendations] {cached.slides.Count} slides from cache.");
            yield break;
        }

        // 2) Otherwise fetch: recent searches first, then anime picks (3 searches in parallel)
        var picks = new List<(string caption, string title, string query)>();
        foreach (var r in recentPicks) picks.Add(("BECAUSE YOU SEARCHED", Capitalize(r), r));
        var extras = new List<int>();
        for (int i = AlwaysIncluded; i < AnimeCharacters.Length; i++) extras.Add(i);
        for (int i = extras.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (extras[i], extras[j]) = (extras[j], extras[i]); }
        for (int i = 0; i < AlwaysIncluded && i < AnimeCharacters.Length; i++)
            picks.Add(("ANIME PICK", AnimeCharacters[i].label, AnimeCharacters[i].query));
        for (int i = 0; i < RotatingExtras && i < extras.Count; i++)
            picks.Add(("ANIME PICK", AnimeCharacters[extras[i]].label, AnimeCharacters[extras[i]].query));

        var found = new CachedSlide[picks.Count];
        int next = 0, running = 0;
        float started = Time.realtimeSinceStartup;
        while (next < picks.Count || running > 0)
        {
            while (ShouldPause) yield return null;
            while (running < ParallelFetches && next < picks.Count)
            {
                int index = next++;
                running++;
                StartCoroutine(FetchTopModel(picks[index], slide =>
                {
                    found[index] = slide;
                    if (slide != null) ShowSlide(slide);
                }, () => running--));
            }
            yield return null;
        }
        Debug.Log($"[Recommendations] Fetched {picks.Count} picks in {Time.realtimeSinceStartup - started:0.0}s.");

        // 3) Save in pick order for next time
        var cache = new SlideCache { savedAt = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(), recentsKey = recentsKey };
        foreach (var s in found) if (s != null) cache.slides.Add(s);
        if (cache.slides.Count > 0)
        {
            PlayerPrefs.SetString(CacheKey, JsonUtility.ToJson(cache));
            PlayerPrefs.Save();
        }

        loadingDone = true;
        if (slides.Count == 0) statusText.text = "Search above to find 3D models.";
    }

    private static bool TryLoadCache(string recentsKey, out SlideCache cache)
    {
        cache = null;
        string json = PlayerPrefs.GetString(CacheKey, "");
        if (string.IsNullOrEmpty(json)) return false;
        try { cache = JsonUtility.FromJson<SlideCache>(json); } catch { return false; }
        if (cache == null || cache.slides == null || cache.slides.Count == 0) return false;
        double ageHours = (System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() - cache.savedAt) / 3600.0;
        // New searches since then: refresh so "Because you searched" stays current
        return ageHours >= 0 && ageHours < CacheHours && cache.recentsKey == recentsKey;
    }

    private void ShowSlide(CachedSlide c)
    {
        if (c == null || slides.Exists(s => s.Uid == c.uid)) return;
        usedUids.Add(c.uid);
        var slide = AddSlide(c.uid, c.caption, c.title, c.title, c.author, c.image, c.query, c.size, c.likes, c.animated);
        // Same polite loader as the results grid (max 4 at a time, pauses during downloads)
        ThumbnailLoader.Load(c.image, slide.Image, null, tex => { slide.Texture = tex; slide.LastSize = Vector2.zero; });
    }

    /// <summary>Best matching free, downloadable model for a query (one slide, or null).</summary>
    private IEnumerator FetchTopModel((string caption, string title, string query) pick, System.Action<CachedSlide> onResult, System.Action onDone)
    {
        yield return FetchTopModelInner(pick, onResult);
        onDone?.Invoke();
    }

    private IEnumerator FetchTopModelInner((string caption, string title, string query) pick, System.Action<CachedSlide> onResult)
    {
        // Default (relevance) order: sorting anime results by likes surfaces fan-service models.
        string url = $"https://api.sketchfab.com/v3/search?type=models&downloadable=true&count={ResultsPerQuery}&q=" + UnityWebRequest.EscapeURL(pick.query);
        string json = null;
        using (var req = UnityWebRequest.Get(url))
        {
            string token = api != null ? api.GetApiToken() : null;
            if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", "Token " + token);
            req.timeout = 15;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success) json = req.downloadHandler.text;
        }
        if (json == null) { onResult(null); yield break; }

        JArray results;
        try { results = (JArray)JObject.Parse(json)["results"]; }
        catch { onResult(null); yield break; }
        if (results == null) { onResult(null); yield break; }

        foreach (var model in results)
        {
            string uid = model.Value<string>("uid");
            if (string.IsNullOrEmpty(uid) || usedUids.Contains(uid)) continue;
            if (model.Value<bool?>("isDownloadable") == false) continue;
            string img = PickImage((JArray)model["thumbnails"]?["images"], SlideImageWidth);
            if (string.IsNullOrEmpty(img)) continue;

            usedUids.Add(uid); // reserve now so parallel fetches never pick the same model
            string author = model["user"]?.Value<string>("displayName");
            if (string.IsNullOrWhiteSpace(author)) author = model["user"]?.Value<string>("username");
            onResult(new CachedSlide
            {
                uid = uid,
                caption = (pick.caption + " · " + pick.title).ToUpperInvariant(),
                title = model.Value<string>("name") ?? "3D model", // real model name; caption says why
                author = author,
                image = img,
                query = pick.query,
                size = ((model["archives"] as JObject)?["glb"] as JObject)?.Value<long?>("size") ?? 0,
                likes = model.Value<int?>("likeCount") ?? 0,
                animated = (model.Value<int?>("animationCount") ?? 0) > 0
            });
            yield break;
        }
        onResult(null);
    }

    // ------------------------------------------------------------------ navigation

    private void ArrowButton(RectTransform parent, string name, string icon, Vector2 anchor, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(anchor.x, 0.5f);
        rt.sizeDelta = new Vector2(150f, 150f);
        rt.anchoredPosition = pos;
        Surface(rt.gameObject, new Color(0.04f, 0.06f, 0.085f, 0.85f), 9999f, new Color(Neon.r, Neon.g, Neon.b, 0.6f), 3f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(onClick);
        UIPress.Add(rt);
        var iconRt = NewRect("Icon", rt);
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.sizeDelta = new Vector2(70f, 70f);
        var img = iconRt.gameObject.AddComponent<RawImage>();
        img.texture = UIIcons.Get(icon);
        img.color = Neon;
        img.raycastTarget = false;
    }

    // ------------------------------------------------------------------ quick actions

    static readonly Color TileFill = new Color(0.06f, 0.08f, 0.105f, 1f);
    private TextMeshProUGUI favCountText;
    private GameObject favCountBadge;
    private string lastSurprise;

    /// <summary>Extra fun searches for "Surprise" (on top of every category).</summary>
    static readonly string[] SurpriseExtras =
    {
        "robot", "spaceship", "dragon", "castle", "astronaut", "guitar", "treasure chest", "mushroom",
        "sports car", "dinosaur", "cat", "owl", "lighthouse", "rocket", "sneakers", "vintage camera",
    };

    private void BuildQuickActions(RectTransform row)
    {
        QuickTile(row, "Favourites", Favourites.HeartFilled, () => RunQuick(() => api.ShowFavourites()), withCount: true);
        QuickTile(row, "Animated", UIIcons.Get(UIIcons.Animated), () => RunQuick(() => api.SearchAnimated()));
        QuickTile(row, "Recents", UIIcons.Get(UIIcons.Recent), () => RunQuick(() => api.ShowRecents()));
        QuickTile(row, "Surprise", UIIcons.Get(UIIcons.Dice), Surprise, spinIcon: true);
        RefreshSavedCount();
        Favourites.Changed += RefreshSavedCount;
    }

    /// <summary>Glass tile: glowing round icon badge on top, label underneath (optional count bubble).</summary>
    private void QuickTile(RectTransform row, string label, Texture icon, UnityEngine.Events.UnityAction onClick,
                           bool withCount = false, bool spinIcon = false)
    {
        var rt = NewRect("Quick " + label, row);
        rt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        // Calm, flat style: dark card, hairline border, tinted icon circle (no glow)
        Surface(rt.gameObject, TileFill, 40f, new Color(1f, 1f, 1f, 0.08f), 2f);

        const float badge = 116f;

        var circle = NewRect("Badge", rt);
        circle.anchorMin = circle.anchorMax = new Vector2(0.5f, 1f);
        circle.pivot = new Vector2(0.5f, 1f);
        circle.sizeDelta = new Vector2(badge, badge);
        circle.anchoredPosition = new Vector2(0f, -26f);
        Surface(circle.gameObject, new Color(Neon.r, Neon.g, Neon.b, 0.10f), 9999f, Neon, 0f);
        circle.GetComponent<Image>().raycastTarget = false;

        var iconRt = NewRect("Icon", circle);
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.sizeDelta = new Vector2(58f, 58f);
        var img = iconRt.gameObject.AddComponent<RawImage>();
        img.texture = icon;
        img.color = Neon;
        img.raycastTarget = false;

        var text = NewText("Label", rt, UIFonts.SemiBold, 40f, new Color(0.88f, 0.91f, 0.94f, 1f), TextAlignmentOptions.Center);
        text.text = label;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        var tr = text.rectTransform;
        tr.anchorMin = new Vector2(0f, 0f); tr.anchorMax = new Vector2(1f, 0f);
        tr.pivot = new Vector2(0.5f, 0f);
        tr.offsetMin = new Vector2(10f, 18f); tr.offsetMax = new Vector2(-10f, 78f);

        if (withCount)
        {
            // Small cyan bubble with the number of saved models, on the badge's top-right
            var bubble = NewRect("Count", circle);
            bubble.anchorMin = bubble.anchorMax = new Vector2(1f, 1f);
            bubble.pivot = new Vector2(0.5f, 0.5f);
            bubble.anchoredPosition = new Vector2(-14f, -14f);
            Surface(bubble.gameObject, Neon, 9999f, TileFill, 4f);
            bubble.GetComponent<Image>().raycastTarget = false;
            var hl = bubble.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(14, 14, 2, 2);
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = hl.childForceExpandHeight = false;
            var fit = bubble.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            favCountText = NewText("N", bubble, UIFonts.Display, 34f, Ink, TextAlignmentOptions.Center);
            favCountText.textWrappingMode = TextWrappingModes.NoWrap;
            var le = favCountText.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 26f;
            favCountBadge = bubble.gameObject;
        }

        var btn = rt.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(onClick);
        if (spinIcon) btn.onClick.AddListener(() => StartCoroutine(SpinOnce(iconRt)));
        UIPress.Add(rt);
    }

    /// <summary>Clears the search bar, scrolls to the top and runs a quick-action list.</summary>
    private void RunQuick(System.Action action)
    {
        lastInteraction = Time.unscaledTime;
        if (api == null) return;
        if (searchBar != null) searchBar.text = string.Empty;
        if (resultsScroll != null) resultsScroll.verticalNormalizedPosition = 1f;
        action();
    }

    /// <summary>"Surprise": searches a random category or fun topic (never the same twice in a row).</summary>
    private void Surprise()
    {
        lastInteraction = Time.unscaledTime;
        if (api == null) return;
        var pool = new List<string>(SurpriseExtras);
        foreach (var c in CategoryRow.Categories) pool.Add(c.query);
        pool.Remove(lastSurprise);
        string q = pool[Random.Range(0, pool.Count)];
        lastSurprise = q;
        StartCoroutine(SearchAfterSpin(q));
    }

    private IEnumerator SearchAfterSpin(string query)
    {
        yield return new WaitForSecondsRealtime(0.35f); // let the dice roll first
        if (searchBar != null) searchBar.text = query;
        if (resultsScroll != null) resultsScroll.verticalNormalizedPosition = 1f;
        api.SearchModels(query);
    }

    private static IEnumerator SpinOnce(RectTransform icon)
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.45f)
        {
            float e = 1f - Mathf.Pow(1f - t, 3f);
            icon.localRotation = Quaternion.Euler(0f, 0f, -360f * e);
            icon.localScale = Vector3.one * (1f + 0.2f * Mathf.Sin(t * Mathf.PI));
            yield return null;
        }
        icon.localRotation = Quaternion.identity;
        icon.localScale = Vector3.one;
    }

    private void RefreshSavedCount()
    {
        if (favCountText == null) return;
        int n = Favourites.Count;
        favCountText.text = n > 99 ? "99+" : n.ToString();
        favCountBadge.SetActive(n > 0);
    }

    /// <summary>Previous / next card (wraps around).</summary>
    private void Step(int dir)
    {
        if (slides.Count == 0) return;
        target += dir; // endless: indices wrap in the layout
        lastInteraction = Time.unscaledTime;
    }

    // ------------------------------------------------------------------ results bar

    static readonly Color BarFill = new Color(0.06f, 0.085f, 0.11f, 0.96f);
    static readonly Color BarBorder = new Color(1f, 1f, 1f, 0.12f);
    private TextMeshProUGUI resultsTitle, filterCountText;
    private GameObject filterPill, filterCountBubble;
    private UIRoundedCorners filterShape;
    private string shownTitleFor;
    private int shownFilterCount = -1;

    /// <summary>Bar above the search results: ‹ Home · what you're looking at · Filters (with active count).</summary>
    private void BuildBackButton(ScrollRect results)
    {
        var bar = NewRect("ResultsBar", results.transform);
        bar.SetAsLastSibling();
        bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.offsetMin = new Vector2(64f, -36f - 124f); bar.offsetMax = new Vector2(-64f, -36f);

        // ‹ Home
        var home = BarPill(bar, "Home", UIIcons.Back, "Home", new Vector2(0f, 0.5f), out _);
        home.GetComponent<Button>().onClick.AddListener(GoHome);

        // Filters (right)
        var filters = BarPill(bar, "Filters", UIIcons.Filter, "Filters", new Vector2(1f, 0.5f), out var filtersRow);
        filterPill = filters.gameObject;
        filterShape = filters.GetComponent<UIRoundedCorners>();
        filters.GetComponent<Button>().onClick.AddListener(() =>
        {
            if (api != null) FilterSheet.Open(() =>
            {
                if (resultsScroll != null) resultsScroll.verticalNormalizedPosition = 1f;
                api.ApplyFilters();
            });
        });
        var bubble = NewRect("Count", filtersRow);
        Surface(bubble.gameObject, Neon, 9999f, Neon, 0f);
        bubble.GetComponent<Image>().raycastTarget = false;
        var bl = bubble.gameObject.AddComponent<HorizontalLayoutGroup>();
        bl.padding = new RectOffset(14, 14, 0, 0);
        bl.childAlignment = TextAnchor.MiddleCenter;
        bl.childControlWidth = bl.childControlHeight = true;
        bl.childForceExpandWidth = bl.childForceExpandHeight = false;
        var ble = bubble.gameObject.AddComponent<LayoutElement>();
        ble.minHeight = ble.preferredHeight = 52f;
        ble.minWidth = 52f;
        filterCountText = NewText("N", bubble, UIFonts.Display, 36f, Ink, TextAlignmentOptions.Center);
        filterCountBubble = bubble.gameObject;

        // Title in the middle: "sofa", Favourites, Recently viewed …
        resultsTitle = NewText("Title", bar, UIFonts.Bold, 52f, TextMain, TextAlignmentOptions.Center);
        resultsTitle.textWrappingMode = TextWrappingModes.NoWrap;
        resultsTitle.overflowMode = TextOverflowModes.Ellipsis;
        var tr = resultsTitle.rectTransform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(330f, 0f); tr.offsetMax = new Vector2(-400f, 0f);

        backGroup = bar.gameObject.AddComponent<CanvasGroup>();
        backGroup.alpha = 0f;
        backGroup.blocksRaycasts = false;
    }

    /// <summary>Glass pill with an icon and a label (auto width), anchored to one side of the bar.</summary>
    private RectTransform BarPill(RectTransform bar, string name, string icon, string label, Vector2 anchor, out RectTransform row)
    {
        var rt = NewRect(name, bar);
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        Surface(rt.gameObject, BarFill, 9999f, BarBorder, 2f);
        var hl = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(34, 42, 30, 30);
        hl.spacing = 16f;
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = hl.childForceExpandHeight = false;
        var fit = rt.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var iconRt = NewRect("Icon", rt);
        var le = iconRt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = 52f;
        var img = iconRt.gameObject.AddComponent<RawImage>();
        img.texture = UIIcons.Get(icon);
        img.color = Neon;
        img.raycastTarget = false;

        var t = NewText("Text", rt, UIFonts.SemiBold, 48f, TextMain, TextAlignmentOptions.MidlineLeft);
        t.text = label;
        t.textWrappingMode = TextWrappingModes.NoWrap;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        UIPress.Add(rt);
        row = rt;
        return rt;
    }

    /// <summary>Keeps the bar's title and the Filters pill (visibility, count) up to date.</summary>
    private void UpdateResultsBar()
    {
        if (api == null || resultsTitle == null) return;
        string q = api.CurrentQuery;
        if (q != shownTitleFor)
        {
            shownTitleFor = q;
            resultsTitle.text = q == SketchfabAPIManager.SavedQuery ? "Favourites"
                              : SketchfabAPIManager.IsQuickList(q) ? q
                              : string.IsNullOrEmpty(q) ? "" : "\u201C" + q + "\u201D";
            filterPill.SetActive(api.CanFilter);
        }
        int n = SearchFilters.ActiveCount;
        if (n != shownFilterCount)
        {
            shownFilterCount = n;
            filterCountBubble.SetActive(n > 0);
            filterCountText.text = n.ToString();
            filterShape.SetBorderColor(n > 0 ? new Color(Neon.r, Neon.g, Neon.b, 0.7f) : BarBorder);
        }
    }

    /// <summary>Clears the search and brings the recommendations slideshow back.</summary>
    private void GoHome()
    {
        if (searchBar != null) searchBar.text = string.Empty;
        if (api != null) api.ResetSearch();
        if (resultsScroll != null)
        {
            resultsScroll.StopMovement();
            resultsScroll.verticalNormalizedPosition = 1f;
        }
        lastInteraction = Time.unscaledTime;
    }

    /// <summary>VIEW button: open this exact model.</summary>
    private void Open(Slide s)
    {
        lastInteraction = Time.unscaledTime;
        if (api != null) api.OpenModel(s.Uid, s.ModelName, s.Author, s.Texture, s.HiResUrl, s.SizeBytes, s.Likes, s.Animated);
    }

    /// <summary>Tap on the card: show every model for that name (e.g. all "gojo satoru" models).</summary>
    private void ShowAllLike(Slide s)
    {
        lastInteraction = Time.unscaledTime;
        if (api == null || string.IsNullOrEmpty(s.Query)) return;
        if (searchBar != null) searchBar.text = s.Query;
        var scroll = GetComponentInParent<ScrollRect>();
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        api.SearchModels(s.Query); // results grid replaces the slideshow (it hides itself)
    }

    void OnDestroy()
    {
        Favourites.Changed -= RefreshSavedCount;
        foreach (var s in slides) if (s.Texture != null) Destroy(s.Texture);
    }

    // ------------------------------------------------------------------ swipe

    public void OnBeginDrag(PointerEventData e)
    {
        if (slides.Count == 0) return;
        dragging = true;
        dragStartPosition = position;
        lastInteraction = Time.unscaledTime;
    }

    public void OnDrag(PointerEventData e)
    {
        if (!dragging) return;
        // Finger movement in the slide area's own units, so the card tracks the finger exactly
        var cam = e.pressEventCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(slideArea, e.pressPosition, cam, out Vector2 a);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(slideArea, e.position, cam, out Vector2 b);
        float delta = (b.x - a.x) / SlideStep();
        position = dragStartPosition - delta; // endless in both directions
        lastInteraction = Time.unscaledTime;
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (!dragging) return;
        float moved = position - dragStartPosition;
        int start = Mathf.RoundToInt(dragStartPosition);
        if (moved > 0.15f) target = start + 1;
        else if (moved < -0.15f) target = start - 1;
        else target = start;
        lastInteraction = Time.unscaledTime;
        // Let the Button ignore the click that ends a swipe
        StartCoroutine(EndDragNextFrame());
    }

    private IEnumerator EndDragNextFrame()
    {
        yield return null;
        dragging = false;
    }

    // ------------------------------------------------------------------ animation

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // Visible only while the results area is empty (no cards, no loading placeholders)
        bool show = resultsContent == null || resultsContent.childCount == 0;
        if (loadingDone && slides.Count == 0) show = show && statusText.gameObject.activeSelf;
        group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, dt * 5f);
        group.blocksRaycasts = show && group.alpha > 0.5f;
        group.interactable = group.blocksRaycasts;

        // "‹ Recommended" back button while search results are on screen (+ room for it above the grid)
        bool resultsShowing = resultsContent != null && resultsContent.childCount > 0;
        if (backGroup != null)
        {
            backGroup.alpha = Mathf.MoveTowards(backGroup.alpha, resultsShowing ? 1f : 0f, dt * 5f);
            backGroup.blocksRaycasts = backGroup.interactable = resultsShowing && backGroup.alpha > 0.5f;
        }
        if (gridLayout != null) gridLayout.ExtraTopPadding = resultsShowing ? BackBarHeight : 0f;
        if (resultsShowing) UpdateResultsBar();
        if (resultsShowing && Input.GetKeyDown(KeyCode.Escape) && !FilterSheet.ConsumedBack) GoHome(); // Android back button

        // Arrows only when there is somewhere to go
        if (navGroup != null)
        {
            bool nav = slides.Count > 1;
            navGroup.alpha = Mathf.MoveTowards(navGroup.alpha, nav ? 1f : 0f, dt * 5f);
            navGroup.blocksRaycasts = navGroup.interactable = nav;
        }
        // Card size; the heading row moves down by the unused height so it hugs the cards
        Rect area = slideArea.rect;
        cardW = area.width * CardWidth;
        cardH = Mathf.Min(area.height, cardW * 1.12f);
        cardSpare = Mathf.Max(0f, area.height - cardH);
        if (headingRow != null) headingRow.anchoredPosition = new Vector2(0f, -HeadingY - cardSpare);

        if (slides.Count == 0) return;

        // Auto-advance (loops back to the first slide)
        if (show && !dragging && Time.unscaledTime - lastInteraction > AutoAdvanceSeconds)
        {
            target++; // endless: keeps going forward, wrapping around
            lastInteraction = Time.unscaledTime;
        }
        if (!dragging) position = Mathf.Lerp(position, target, 1f - Mathf.Exp(-9f * dt));

        // Layout: current card centered, neighbours peek in smaller and dimmer.
        // Endless: each card's distance from the current one is wrapped around the ring of slides.
        float w = cardW, h = cardH, spare = cardSpare;
        float step = w + 48f;
        int n = slides.Count;
        for (int i = 0; i < n; i++)
        {
            var s = slides[i];
            float d = i - position;
            d -= n * Mathf.Round(d / n);
            float ad = Mathf.Abs(d);
            s.Rect.sizeDelta = new Vector2(w, h);
            s.Rect.anchoredPosition = new Vector2(d * step, -spare * 0.5f); // bottom-aligned in the area
            s.Rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.9f, Mathf.Clamp01(ad));
            float targetAlpha = Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(ad));
            s.Group.alpha = Mathf.MoveTowards(s.Group.alpha, targetAlpha, dt * 4f);

            if (s.Texture == null)
                s.Image.color = Color.Lerp(new Color(0.11f, 0.14f, 0.18f, 1f), new Color(0.17f, 0.21f, 0.26f, 1f), 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f));
            else
            {
                s.Image.color = Color.Lerp(s.Image.color, Color.white, 1f - Mathf.Exp(-6f * dt));
                Vector2 size = s.Image.rectTransform.rect.size;
                if (size != s.LastSize) { s.LastSize = size; CoverCrop(s.Image, size); }
            }
        }

        // Dots
        int current = ((Mathf.RoundToInt(position) % n) + n) % n;
        for (int i = 0; i < dots.Count; i++)
        {
            bool on = i == current;
            var le = dots[i].GetComponent<LayoutElement>();
            le.preferredWidth = Mathf.Lerp(le.preferredWidth, on ? 64f : 22f, 1f - Mathf.Exp(-12f * dt));
            dots[i].color = on ? Neon : new Color(1f, 1f, 1f, 0.3f);
        }
    }

    const float CardWidth = 0.70f; // share of the panel width (0.82 → 0.76 → 0.70)
    const float HeadingTop = 30f;        // quick-action row starts this far below the panel top
    const float QuickRowHeight = 250f;
    const float HeadingY = HeadingTop + QuickRowHeight + 30f; // heading row top when the cards fill their area
    private RectTransform quickRow, headingRow;
    const float SideClip = 36f;    // cards are cut this far inside the panel edge (clear of the frame border)
    private float cardW, cardH, cardSpare;
    private float SlideStep() => slideArea.rect.width * CardWidth + 48f;

    // ------------------------------------------------------------------ helpers

    private static void CoverCrop(RawImage img, Vector2 size)
    {
        var tex = img.texture;
        if (tex == null || size.x <= 0f || size.y <= 0f || tex.height == 0) return;
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

    private static string PickImage(JArray images, int targetWidth)
    {
        if (images == null || images.Count == 0) return null;
        int best = 0, minDiff = int.MaxValue;
        for (int i = 0; i < images.Count; i++)
        {
            int w = images[i].Value<int?>("width") ?? -1;
            if (w <= 0) continue;
            int d = Mathf.Abs(w - targetWidth);
            if (d < minDiff) { minDiff = d; best = i; }
        }
        return images[best].Value<string>("url");
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        UIFonts.Apply(t, font);
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    private static void Surface(GameObject go, Color fill, float radius, Color border, float borderWidth)
    {
        var img = go.AddComponent<Image>();
        img.color = fill;
        var shape = go.AddComponent<UIRoundedCorners>();
        shape.SetBorderColor(border);
        shape.SetRadius(radius);
        shape.SetBorder(borderWidth);
    }
}
