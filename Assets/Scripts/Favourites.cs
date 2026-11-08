using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Saved (liked) models, stored on the phone in PlayerPrefs "FavouritesV1" (newest first).
/// Hearts on result cards, slideshow cards and the preview screen toggle them; the "Saved" pill on the
/// home screen lists them in the results grid (SketchfabAPIManager.ShowFavourites).
/// </summary>
public static class Favourites
{
    [Serializable]
    public class Item
    {
        public string uid, name, author, thumb, hiRes;
        public long savedAt;
    }

    [Serializable] private class Store { public List<Item> items = new List<Item>(); }

    const string Key = "FavouritesV1";
    const int MaxItems = 200;
    private static List<Item> items;

    /// <summary>Raised after any add/remove (hearts on screen re-sync themselves).</summary>
    public static event Action Changed;

    public static IReadOnlyList<Item> All { get { Load(); return items; } }
    public static int Count { get { Load(); return items.Count; } }

    public static bool Contains(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return false;
        Load();
        return items.Exists(i => i.uid == uid);
    }

    /// <summary>Adds or removes; returns true if it is now saved.</summary>
    public static bool Toggle(Item item)
    {
        if (item == null || string.IsNullOrEmpty(item.uid)) return false;
        Load();
        int index = items.FindIndex(i => i.uid == item.uid);
        bool saved;
        if (index >= 0) { items.RemoveAt(index); saved = false; }
        else
        {
            item.savedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            items.Insert(0, item);
            if (items.Count > MaxItems) items.RemoveRange(MaxItems, items.Count - MaxItems);
            saved = true;
        }
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(new Store { items = items }));
        PlayerPrefs.Save();
        Debug.Log($"[Favourites] {(saved ? "Saved" : "Removed")} '{item.name}' ({items.Count} saved).");
        Changed?.Invoke();
        return saved;
    }

    private static void Load()
    {
        if (items != null) return;
        items = new List<Item>();
        string json = PlayerPrefs.GetString(Key, "");
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var store = JsonUtility.FromJson<Store>(json);
            if (store?.items != null) items = store.items;
        }
        catch { /* corrupt → start empty */ }
    }

    // ------------------------------------------------------------------ heart icons

    private static Texture2D outline, filled;
    public static Texture2D HeartOutline => outline != null ? outline : (outline = MakeHeart(false));
    public static Texture2D HeartFilled  => filled  != null ? filled  : (filled  = MakeHeart(true));

    /// <summary>Anti-aliased heart from the classic implicit curve (x²+y²−1)³ − x²y³ ≤ 0, 4×4 supersampled.</summary>
    private static Texture2D MakeHeart(bool fill)
    {
        const int n = 128, ss = 4;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        bool Inside(float x, float y, float scale)
        {
            x /= scale; y = (y - 0.1f) / scale + 0.1f;           // shrink around the heart's middle
            float a = x * x + y * y - 1f;
            return a * a * a - x * x * y * y * y <= 0f;
        }
        for (int py = 0; py < n; py++)
        for (int pxl = 0; pxl < n; pxl++)
        {
            int hits = 0;
            for (int sy = 0; sy < ss; sy++)
            for (int sx = 0; sx < ss; sx++)
            {
                float x = ((pxl + (sx + 0.5f) / ss) / n - 0.5f) * 2.7f;
                float y = ((py + (sy + 0.5f) / ss) / n - 0.5f) * 2.7f + 0.15f;
                bool inOuter = Inside(x, y, 1f);
                if (fill ? inOuter : inOuter && !Inside(x, y, 0.74f)) hits++;
            }
            px[py * n + pxl] = new Color32(255, 255, 255, (byte)(255 * hits / (ss * ss)));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}

/// <summary>
/// Models the user opened recently (newest first, max 30), stored in PlayerPrefs "RecentModelsV1".
/// Shown by the "Recents" quick action (SketchfabAPIManager.ShowRecents).
/// </summary>
public static class RecentModels
{
    [Serializable] private class Store { public List<Favourites.Item> items = new List<Favourites.Item>(); }
    const string Key = "RecentModelsV1";
    const int MaxItems = 30;
    private static List<Favourites.Item> items;

    public static IReadOnlyList<Favourites.Item> All { get { Load(); return items; } }

    public static void Add(Favourites.Item item)
    {
        if (item == null || string.IsNullOrEmpty(item.uid)) return;
        Load();
        items.RemoveAll(i => i.uid == item.uid);   // opened again: moves to the top
        item.savedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        items.Insert(0, item);
        if (items.Count > MaxItems) items.RemoveRange(MaxItems, items.Count - MaxItems);
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(new Store { items = items }));
        PlayerPrefs.Save();
    }

    private static void Load()
    {
        if (items != null) return;
        items = new List<Favourites.Item>();
        string json = PlayerPrefs.GetString(Key, "");
        if (string.IsNullOrEmpty(json)) return;
        try { var st = JsonUtility.FromJson<Store>(json); if (st?.items != null) items = st.items; } catch { }
    }
}

/// <summary>
/// Round glass heart button. Tap = save / unsave the model; pops when liked; stays in sync with every
/// other heart for the same model. Create with <see cref="Create"/>; set the model with <see cref="Bind"/>.
/// </summary>
public class HeartButton : MonoBehaviour
{
    static readonly Color Cyan = new Color(0.24f, 0.95f, 1f, 1f);

    private Favourites.Item item;
    private RawImage icon;
    private Image bg;
    private UIRoundedCorners bgShape;
    private bool saved;
    private float pop = 1f;

    public static HeartButton Create(Transform parent, float size)
    {
        var rt = UIKit.Rect("Heart", parent);
        rt.sizeDelta = new Vector2(size, size);
        var h = rt.gameObject.AddComponent<HeartButton>();
        h.bg = rt.gameObject.AddComponent<Image>();
        h.bg.color = new Color(0.02f, 0.05f, 0.08f, 0.78f);
        h.bgShape = rt.gameObject.AddComponent<UIRoundedCorners>();
        h.bgShape.SetBorderColor(new Color(Cyan.r, Cyan.g, Cyan.b, 0.55f));
        h.bgShape.SetRadius(9999f);
        h.bgShape.SetBorder(Mathf.Max(1.5f, size * 0.025f));
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        var ic = UIKit.Rect("Icon", rt);
        ic.anchorMin = new Vector2(0.22f, 0.22f); ic.anchorMax = new Vector2(0.78f, 0.80f);
        ic.offsetMin = ic.offsetMax = Vector2.zero;
        h.icon = ic.gameObject.AddComponent<RawImage>();
        h.icon.raycastTarget = false;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(h.OnTap);
        h.Sync();
        return h;
    }

    public void Bind(Favourites.Item model)
    {
        item = model;
        Sync();
    }

    /// <summary>Resize (cards change size with the screen).</summary>
    public void SetSize(float size)
    {
        ((RectTransform)transform).sizeDelta = new Vector2(size, size);
        bgShape.SetBorder(Mathf.Max(1.5f, size * 0.025f));
    }

    void OnEnable() { Favourites.Changed += Sync; Sync(); }
    void OnDisable() => Favourites.Changed -= Sync;

    private void OnTap()
    {
        if (item == null) return;
        saved = Favourites.Toggle(item);
        if (saved) pop = 0f;
    }

    private void Sync()
    {
        saved = item != null && Favourites.Contains(item.uid);
        if (icon == null) return;
        icon.texture = saved ? Favourites.HeartFilled : Favourites.HeartOutline;
        icon.color = saved ? Cyan : Color.white;
        if (bgShape != null) bgShape.SetBorderColor(new Color(Cyan.r, Cyan.g, Cyan.b, saved ? 0.9f : 0.55f));
    }

    void Update()
    {
        if (pop >= 1f) return;
        pop = Mathf.Min(1f, pop + Time.unscaledDeltaTime / 0.35f);
        float s = 1f + 0.35f * Mathf.Sin(pop * Mathf.PI) * (1f - pop * 0.5f); // quick bounce
        icon.rectTransform.localScale = Vector3.one * s;
    }
}
