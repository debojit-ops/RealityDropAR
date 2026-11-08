using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// "Explore by category" row under the recommendations slideshow: a horizontally scrolling strip of tiles
/// (Furniture, Sofa, Bed, Cars, Indoor plants, Electronics, …). Each tile shows a real thumbnail — the top
/// free, family-safe model for that category — and tapping it searches that category.
/// Thumbnails URLs are cached for 7 days (PlayerPrefs "CategoryThumbsV1"); images load politely through
/// ThumbnailLoader (on-screen tiles first, paused during model downloads). Built by RecommendationsPanel.
/// </summary>
public class CategoryRow : MonoBehaviour
{
    /// <summary>Label → search query. Edit freely; order = order on screen.</summary>
    public static readonly (string label, string query)[] Categories =
    {
        ("Furniture", "furniture"),
        ("Sofa", "sofa"),
        ("Bed", "bed"),
        ("Chair", "chair"),
        ("Table", "table"),
        ("Lamp", "lamp"),
        ("Indoor plants", "indoor plant"),
        ("Home decor", "home decor"),
        ("Kitchen", "kitchen"),
        ("Cars", "car"),
        ("Bikes", "motorcycle"),
        ("Electronics", "electronics"),
        ("Phones", "smartphone"),
        ("Gaming", "gaming console"),
        ("Shoes", "sneakers"),
        ("Watches", "watch"),
        ("Animals", "animal"),
        ("Dinosaurs", "dinosaur"),
        ("Food", "food"),
        ("Buildings", "house"),
    };

    public const float Height = 720f;    // space the row takes at the bottom of the panel (panel units)

    const float TileW = 420f, TileH = 520f, ImageH = 402f, Gap = 36f, SidePad = 80f; // bigger tiles (was 340×420)
    const string CacheKey = "CategoryThumbsV1";
    const double CacheDays = 7;
    const int ThumbWidth = 360;

    static readonly Color Neon     = new Color(0.30f, 0.88f, 1f, 1f);
    static readonly Color CardFill = new Color(0.055f, 0.078f, 0.106f, 1f);
    static readonly Color TextMain = new Color(0.957f, 0.965f, 0.976f, 1f);

    private class Tile { public string Query; public RawImage Image; public RawImage Icon; public Texture2D Texture; public Vector2 LastSize; public Tile Source; }

    // Infinite auto-scroll: the tiles are laid out 3 times in a row; the strip drifts slowly left and
    // jumps back by one set width when it passes the middle copy (looks seamless, since copies are identical).
    const int Copies = 3;
    const float AutoSpeed = 55f;       // panel units per second
    const float ResumeDelay = 2.5f;    // seconds after the user lets go before drifting again
    private RectTransform content;
    private ScrollRect strip;
    private float setWidth, speed, lastTouch = -100f;
    private bool userDragging;

    [System.Serializable] private class ThumbCache { public long savedAt; public List<string> queries = new List<string>(); public List<string> urls = new List<string>(); }

    private readonly List<Tile> tiles = new List<Tile>();
    private SketchfabAPIManager api;
    private TMP_InputField searchBar;
    private RectTransform viewport;
    private readonly List<Tile> sources = new List<Tile>(); // the middle copy: loads the pictures
    private System.Func<bool> shouldPause;
    private System.Func<bool> canStart;

    public static CategoryRow Create(RectTransform panel, SketchfabAPIManager api, TMP_InputField searchBar,
                                     System.Func<bool> shouldPause, System.Func<bool> canStart)
    {
        var rt = NewRect("Categories", panel);
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.offsetMin = new Vector2(0f, 40f);
        rt.offsetMax = new Vector2(0f, 40f + Height);
        var row = rt.gameObject.AddComponent<CategoryRow>();
        row.api = api;
        row.searchBar = searchBar;
        row.shouldPause = shouldPause;
        row.canStart = canStart;
        row.Build(rt);
        row.StartCoroutine(row.LoadThumbnails());
        return row;
    }

    private void Build(RectTransform rt)
    {
        // Heading with the same accent bar look as the rest of the home screen
        var bar = NewRect("Accent", rt);
        bar.anchorMin = bar.anchorMax = new Vector2(0f, 1f);
        bar.pivot = new Vector2(0f, 1f);
        bar.anchoredPosition = new Vector2(SidePad, -28f);
        bar.sizeDelta = new Vector2(12f, 64f);
        var barImg = bar.gameObject.AddComponent<Image>();
        barImg.color = Neon;
        barImg.raycastTarget = false;
        bar.gameObject.AddComponent<UIRoundedCorners>().SetRadius(6f);

        var heading = NewText("Heading", rt, UIFonts.Bold, 70f, TextMain, TextAlignmentOptions.MidlineLeft);
        heading.text = "Explore by category";
        var hr = heading.rectTransform;
        hr.anchorMin = new Vector2(0f, 1f); hr.anchorMax = new Vector2(1f, 1f);
        hr.pivot = new Vector2(0.5f, 1f);
        hr.offsetMin = new Vector2(SidePad + 36f, -120f); hr.offsetMax = new Vector2(-SidePad, 0f);

        // Horizontal scroll strip
        viewport = NewRect("Viewport", rt);
        viewport.anchorMin = new Vector2(0f, 0f); viewport.anchorMax = new Vector2(1f, 0f);
        viewport.pivot = new Vector2(0.5f, 0f);
        viewport.offsetMin = new Vector2(0f, 20f); viewport.offsetMax = new Vector2(0f, 20f + TileH + 20f);
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(0f, 0f, 0f, 0f);                // catches drags between tiles
        viewport.gameObject.AddComponent<RectMask2D>();

        content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 0f); content.anchorMax = new Vector2(0f, 1f);
        content.pivot = new Vector2(0f, 0.5f);
        setWidth = Categories.Length * (TileW + Gap);
        content.sizeDelta = new Vector2(SidePad + setWidth * Copies, 0f);
        content.anchoredPosition = Vector2.zero;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Unrestricted; // wrapping keeps it in range
        scroll.inertia = true;
        scroll.decelerationRate = 0.12f;
        scroll.scrollSensitivity = 30f;

        strip = scroll;
        var relay = viewport.gameObject.AddComponent<CategoryDragRelay>();
        relay.OnBegin = () => { userDragging = true; lastTouch = Time.unscaledTime; speed = 0f; };
        relay.OnEnd = () => { userDragging = false; lastTouch = Time.unscaledTime; };

        for (int c = 0; c < Copies; c++)
            for (int i = 0; i < Categories.Length; i++)
            {
                var tile = AddTile(content, i, SidePad + (c * Categories.Length + i) * (TileW + Gap));
                if (c == 1) sources.Add(tile);
            }
        for (int c = 0; c < Copies; c++)
            for (int i = 0; i < Categories.Length; i++)
                if (c != 1) tiles[c * Categories.Length + i].Source = sources[i];
        content.anchoredPosition = new Vector2(-setWidth, 0f); // start on the middle copy
    }

    private Tile AddTile(RectTransform content, int index, float x)
    {
        var (label, query) = Categories[index];
        var t = new Tile { Query = query };

        var rt = NewRect("Tile " + label, content);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta = new Vector2(TileW, TileH);
        UIKit.Surface(rt.gameObject, CardFill, 40f, new Color(Neon.r, Neon.g, Neon.b, 0.35f), 2.5f);

        var imgRt = NewRect("Image", rt);
        imgRt.anchorMin = new Vector2(0f, 1f); imgRt.anchorMax = new Vector2(1f, 1f);
        imgRt.pivot = new Vector2(0.5f, 1f);
        imgRt.offsetMin = new Vector2(8f, -8f - ImageH); imgRt.offsetMax = new Vector2(-8f, -8f);
        t.Image = imgRt.gameObject.AddComponent<RawImage>();
        t.Image.color = new Color(0.11f, 0.14f, 0.18f, 1f);
        t.Image.raycastTarget = false;
        imgRt.gameObject.AddComponent<UIRoundedCorners>().SetRadius(32f);

        // Placeholder cube icon until the picture arrives
        t.Icon = UIKit.Icon(imgRt, UIIcons.ARCube, new Color(Neon.r, Neon.g, Neon.b, 0.35f), 130f);

        var text = NewText("Label", rt, UIFonts.SemiBold, 50f, TextMain, TextAlignmentOptions.Center);
        text.text = label;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        var tr = text.rectTransform;
        tr.anchorMin = new Vector2(0f, 0f); tr.anchorMax = new Vector2(1f, 0f);
        tr.pivot = new Vector2(0.5f, 0f);
        tr.offsetMin = new Vector2(14f, 6f); tr.offsetMax = new Vector2(-14f, TileH - ImageH - 10f);

        var btn = rt.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => Search(t.Query));
        UIPress.Add(rt);

        tiles.Add(t);
        return t;
    }

    private void Search(string query)
    {
        if (api == null) return;
        if (searchBar != null) searchBar.text = query;
        var scroll = GetComponentInParent<RecommendationsPanel>()?.GetComponentInParent<ScrollRect>();
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        api.SearchModels(query); // results grid replaces the home screen
    }

    // ------------------------------------------------------------------ thumbnails

    private IEnumerator LoadThumbnails()
    {
        // Recommendations first: they're what the loading screen waits for
        while (canStart != null && !canStart()) yield return null;

        var urls = new string[Categories.Length];
        if (TryLoadCache(urls))
        {
            for (int i = 0; i < urls.Length; i++) ShowThumb(i, urls[i]);
            yield break;
        }

        int next = 0, running = 0;
        while (next < Categories.Length || running > 0)
        {
            while (shouldPause != null && shouldPause()) yield return null;
            while (running < 3 && next < Categories.Length)
            {
                int index = next++;
                running++;
                StartCoroutine(FetchThumb(index, url => { urls[index] = url; ShowThumb(index, url); running--; }));
            }
            yield return null;
        }

        var cache = new ThumbCache { savedAt = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        for (int i = 0; i < Categories.Length; i++) { cache.queries.Add(Categories[i].query); cache.urls.Add(urls[i] ?? ""); }
        PlayerPrefs.SetString(CacheKey, JsonUtility.ToJson(cache));
        PlayerPrefs.Save();
    }

    private bool TryLoadCache(string[] urls)
    {
        string json = PlayerPrefs.GetString(CacheKey, "");
        if (string.IsNullOrEmpty(json)) return false;
        ThumbCache cache;
        try { cache = JsonUtility.FromJson<ThumbCache>(json); } catch { return false; }
        if (cache == null || cache.queries == null) return false;
        double ageDays = (System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() - cache.savedAt) / 86400.0;
        if (ageDays < 0 || ageDays > CacheDays) return false;
        int found = 0;
        for (int i = 0; i < Categories.Length; i++)
        {
            int j = cache.queries.IndexOf(Categories[i].query);   // list edited since? missing ones refetch next week
            if (j >= 0 && j < cache.urls.Count && !string.IsNullOrEmpty(cache.urls[j])) { urls[i] = cache.urls[j]; found++; }
        }
        return found >= Categories.Length / 2;
    }

    private void ShowThumb(int index, string url)
    {
        if (string.IsNullOrEmpty(url) || index >= sources.Count) return;
        var t = sources[index];
        ThumbnailLoader.Load(url, t.Image, viewport, tex => { t.Texture = tex; t.LastSize = Vector2.zero; });
    }

    /// <summary>Thumbnail URL of the top free, downloadable, family-safe model for a category (or null).</summary>
    private IEnumerator FetchThumb(int index, System.Action<string> done)
    {
        string url = "https://api.sketchfab.com/v3/search?type=models&downloadable=true&count=12&q=" +
                     UnityWebRequest.EscapeURL(Categories[index].query);
        string json = null;
        using (var req = UnityWebRequest.Get(url))
        {
            string token = api != null ? api.GetApiToken() : null;
            if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", "Token " + token);
            req.timeout = 15;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success) json = req.downloadHandler.text;
        }
        string thumb = null;
        if (json != null)
        {
            try
            {
                foreach (var model in (JArray)JObject.Parse(json)["results"])
                {
                    if (model.Value<bool?>("isDownloadable") == false) continue;
                    if (!ContentFilter.IsSafe(model)) continue;
                    thumb = PickImage((JArray)model["thumbnails"]?["images"], ThumbWidth);
                    if (!string.IsNullOrEmpty(thumb)) break;
                }
            }
            catch { thumb = null; }
        }
        done(thumb);
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

    // ------------------------------------------------------------------ per-frame

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);

        // Slow drift to the left; pauses while the finger is on the strip (and a moment after)
        if (strip != null && content != null && setWidth > 0f)
        {
            bool touching = userDragging || Input.touchCount > 0 && RectTransformUtility.RectangleContainsScreenPoint(viewport, Input.GetTouch(0).position, null);
            if (touching) lastTouch = Time.unscaledTime;
            bool coasting = strip.velocity.sqrMagnitude > 25f;
            float targetSpeed = (!touching && !coasting && Time.unscaledTime - lastTouch > ResumeDelay) ? AutoSpeed : 0f;
            speed = Mathf.MoveTowards(speed, targetSpeed, AutoSpeed * dt); // ease in over ~1 s
            Vector2 pos = content.anchoredPosition;
            if (speed > 0f) pos.x -= speed * dt;
            if (!userDragging)
            {
                while (pos.x > -setWidth + 0.5f * (TileW + Gap)) pos.x -= setWidth;   // dragged/flung right
                while (pos.x < -2f * setWidth) pos.x += setWidth;                    // drifted/flung left
            }
            content.anchoredPosition = pos;
        }

        foreach (var t in tiles)
        {
            if (t.Texture == null && t.Source != null && t.Source.Texture != null)
            {
                t.Texture = t.Source.Texture;   // copies show the same picture (owned by the source)
                t.Image.texture = t.Texture;
                t.LastSize = Vector2.zero;
            }
            if (t.Texture == null)
            {
                t.Image.color = Color.Lerp(new Color(0.10f, 0.13f, 0.17f, 1f), new Color(0.15f, 0.19f, 0.24f, 1f), pulse);
                continue;
            }
            if (t.Icon != null) { Destroy(t.Icon.gameObject); t.Icon = null; }
            t.Image.color = Color.Lerp(t.Image.color, Color.white, 1f - Mathf.Exp(-6f * dt));
            Vector2 size = t.Image.rectTransform.rect.size;
            if (size != t.LastSize) { t.LastSize = size; CoverCrop(t.Image, size); }
        }
    }

    void OnDestroy()
    {
        foreach (var t in sources) if (t.Texture != null) Destroy(t.Texture); // copies only borrow them
    }

    // ------------------------------------------------------------------ helpers

    private static void CoverCrop(RawImage img, Vector2 size)
    {
        var tex = img.texture;
        if (tex == null || size.x <= 0f || size.y <= 0f || tex.height == 0) return;
        float texAspect = (float)tex.width / tex.height, areaAspect = size.x / size.y;
        if (texAspect > areaAspect) { float u = areaAspect / texAspect; img.uvRect = new Rect((1f - u) * 0.5f, 0f, u, 1f); }
        else { float v = texAspect / areaAspect; img.uvRect = new Rect(0f, (1f - v) * 0.5f, 1f, v); }
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
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
}

/// <summary>Tells the category strip when the user starts/stops dragging it (sits next to its ScrollRect).</summary>
public class CategoryDragRelay : MonoBehaviour, UnityEngine.EventSystems.IBeginDragHandler, UnityEngine.EventSystems.IEndDragHandler
{
    public System.Action OnBegin, OnEnd;
    public void OnBeginDrag(UnityEngine.EventSystems.PointerEventData e) => OnBegin?.Invoke();
    public void OnEndDrag(UnityEngine.EventSystems.PointerEventData e) => OnEnd?.Invoke();
}
