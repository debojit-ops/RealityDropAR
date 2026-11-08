using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Search filters (kept for the whole app session, applied to every text search and the Animated list).
/// All map to real Sketchfab /v3/search parameters.
/// </summary>
public static class SearchFilters
{
    public static readonly (string label, string value)[] Sorts =
    {
        ("Relevance", null), ("Most liked", "-likeCount"), ("Most viewed", "-viewCount"), ("Newest", "-publishedAt"),
    };

    /// <summary>Sketchfab category slugs.</summary>
    public static readonly (string label, string slug)[] Categories =
    {
        ("All", null),
        ("Furniture & home", "furniture-home"),
        ("Cars & vehicles", "cars-vehicles"),
        ("Characters", "characters-creatures"),
        ("Animals", "animals-pets"),
        ("Nature & plants", "nature-plants"),
        ("Electronics", "electronics-gadgets"),
        ("Food & drink", "food-drink"),
        ("Architecture", "architecture"),
        ("Science & tech", "science-technology"),
        ("Fashion", "fashion-style"),
        ("Sports", "sports-fitness"),
        ("Music", "music"),
    };

    public const int LightFaceLimit = 50000; // "Light models": loads fast, runs smoothly on phones

    public class State
    {
        public int Sort, Category;
        public bool Animated, StaffPicks, Light;
        public State Clone() => (State)MemberwiseClone();
        public int ActiveCount => (Sort != 0 ? 1 : 0) + (Category != 0 ? 1 : 0) + (Animated ? 1 : 0) + (StaffPicks ? 1 : 0) + (Light ? 1 : 0);
    }

    public static State Current { get; private set; } = new State();
    public static int ActiveCount => Current.ActiveCount;

    public static void Set(State s) => Current = s ?? new State();

    /// <summary>Extra URL parameters ("&sort_by=…&categories=…"), empty when no filter is set.</summary>
    public static string ToQuery()
    {
        var s = Current;
        string q = "";
        if (Sorts[s.Sort].value != null) q += "&sort_by=" + Sorts[s.Sort].value;
        if (Categories[s.Category].slug != null) q += "&categories=" + Categories[s.Category].slug;
        if (s.Animated) q += "&animated=true";
        if (s.StaffPicks) q += "&staffpicked=true";
        if (s.Light) q += "&max_face_count=" + LightFaceLimit;
        return q;
    }
}

/// <summary>
/// Bottom sheet for the search filters: Sort by · Category · Show only, with Reset and SHOW RESULTS.
/// Edits a copy; nothing changes until SHOW RESULTS. Own overlay canvas (1080×2340 reference, UIKit units).
/// </summary>
public class FilterSheet : MonoBehaviour
{
    const float K = UIKit.K;
    static readonly Color SheetFill  = new Color(0.035f, 0.05f, 0.07f, 0.98f);
    static readonly Color ChipFill   = new Color(1f, 1f, 1f, 0.04f);
    static readonly Color ChipBorder = new Color(1f, 1f, 1f, 0.14f);

    private static FilterSheet instance;
    private System.Action onApply;
    private SearchFilters.State draft;
    private CanvasGroup dimGroup;
    private RectTransform sheet;
    private float shown;      // 0 hidden → 1 open
    private bool open;
    private readonly List<Chip> sortChips = new List<Chip>(), categoryChips = new List<Chip>();
    private Chip animChip, staffChip, lightChip;

    private class Chip
    {
        public RectTransform Rect; public Image Bg; public UIRoundedCorners Shape; public TextMeshProUGUI Text;
        public void Set(bool on)
        {
            Bg.color = on ? new Color(0.24f, 0.95f, 1f, 0.16f) : ChipFill;
            Shape.SetBorderColor(on ? new Color(0.24f, 0.95f, 1f, 0.85f) : ChipBorder);
            Text.color = on ? NeonArt.Cyan : UIKit.TextMain;
        }
    }

    /// <summary>Opens the sheet; <paramref name="apply"/> runs after SHOW RESULTS (filters already saved).</summary>
    public static void Open(System.Action apply)
    {
        if (instance == null)
        {
            var canvas = UIKit.CreateCanvas(null, "FilterSheet", 60);
            instance = canvas.gameObject.AddComponent<FilterSheet>();
            instance.Build((RectTransform)canvas.transform);
        }
        instance.onApply = apply;
        instance.draft = SearchFilters.Current.Clone();
        instance.Sync();
        instance.open = true;
        instance.gameObject.SetActive(true);
    }

    // ------------------------------------------------------------------ build

    private void Build(RectTransform root)
    {
        // Dim backdrop: tap outside the sheet to close
        var dim = UIKit.Rect("Dim", root);
        UIKit.Stretch(dim, Vector2.zero, Vector2.zero);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        dim.gameObject.AddComponent<Button>().onClick.AddListener(Close);
        dimGroup = dim.gameObject.AddComponent<CanvasGroup>();

        sheet = UIKit.Rect("Sheet", root);
        sheet.anchorMin = new Vector2(0f, 0f); sheet.anchorMax = new Vector2(1f, 0f);
        sheet.pivot = new Vector2(0.5f, 0f);
        var bg = sheet.gameObject.AddComponent<Image>();
        bg.color = SheetFill;
        var shape = sheet.gameObject.AddComponent<UIRoundedCorners>();
        shape.SetBorderColor(new Color(0.24f, 0.95f, 1f, 0.25f));
        shape.SetRadii(28f * K, 28f * K, 0f, 0f);
        shape.SetBorder(1.2f * K);

        float y = -14f * K; // cursor from the sheet top (negative = down)
        float side = 22f * K;

        // Grab handle
        var handle = NeonArt.Bar(sheet, Vector2.zero, new Vector2(40f * K, 5f * K), new Color(1f, 1f, 1f, 0.25f));
        TopCenter(handle.rectTransform, y);
        y -= 26f * K;

        // Header: Filters · Reset
        var title = UIKit.Text("Title", sheet, UIFonts.Bold, 24f * K, UIKit.TextMain, TextAlignmentOptions.MidlineLeft);
        title.text = "Filters";
        Row(title.rectTransform, y, 34f * K, side, side + 120f * K);
        var reset = UIKit.Text("Reset", sheet, UIFonts.SemiBold, 16f * K, NeonArt.Cyan, TextAlignmentOptions.MidlineRight);
        reset.text = "Reset";
        reset.raycastTarget = true;
        Row(reset.rectTransform, y, 34f * K, side + 200f * K, side);
        reset.gameObject.AddComponent<Button>().onClick.AddListener(() => { draft = new SearchFilters.State(); Sync(); });
        y -= 34f * K + 18f * K;

        y = Section("Sort by", y, side);
        y = Chips(SearchFilters.Sorts.Length, i => SearchFilters.Sorts[i].label, sortChips, i => { draft.Sort = i; Sync(); }, y, side);

        y = Section("Category", y, side);
        y = Chips(SearchFilters.Categories.Length, i => SearchFilters.Categories[i].label, categoryChips, i => { draft.Category = i; Sync(); }, y, side);

        y = Section("Show only", y, side);
        var toggles = new List<Chip>();
        string[] toggleLabels = { "Animated", "Staff picks", "Light models" };
        y = Chips(3, i => toggleLabels[i], toggles, i =>
        {
            if (i == 0) draft.Animated = !draft.Animated;
            else if (i == 1) draft.StaffPicks = !draft.StaffPicks;
            else draft.Light = !draft.Light;
            Sync();
        }, y, side);
        animChip = toggles[0]; staffChip = toggles[1]; lightChip = toggles[2];
        var note = UIKit.Text("Note", sheet, UIFonts.Regular, 12.5f * K, UIKit.TextMuted, TextAlignmentOptions.TopLeft);
        note.text = "Light models load faster and run smoother in AR.";
        Row(note.rectTransform, y + 4f * K, 20f * K, side, side);
        y -= 24f * K + 14f * K;

        // SHOW RESULTS
        var apply = UIKit.PrimaryButton(sheet, "Apply", null, "SHOW RESULTS", Apply);
        var ar = (RectTransform)apply.transform;
        ar.anchorMin = new Vector2(0f, 1f); ar.anchorMax = new Vector2(1f, 1f);
        ar.pivot = new Vector2(0.5f, 1f);
        ar.offsetMin = new Vector2(side, y - 54f * K); ar.offsetMax = new Vector2(-side, y);
        y -= 54f * K + 30f * K;

        sheet.sizeDelta = new Vector2(0f, -y);
        sheet.anchoredPosition = new Vector2(0f, -sheet.sizeDelta.y);
        dimGroup.alpha = 0f;
    }

    private float Section(string label, float y, float side)
    {
        var t = UIKit.Text(label, sheet, UIFonts.SemiBold, 13f * K, UIKit.TextMuted, TextAlignmentOptions.MidlineLeft);
        t.text = label.ToUpperInvariant();
        t.characterSpacing = 8f;
        Row(t.rectTransform, y, 22f * K, side, side);
        return y - 22f * K - 10f * K;
    }

    /// <summary>Wrapping row of pill chips; returns the cursor below them.</summary>
    private float Chips(int count, System.Func<int, string> label, List<Chip> into, System.Action<int> onTap, float y, float side)
    {
        float h = 38f * K, gap = 10f * K, pad = 16f * K, maxW = 1080f - side * 2f;
        float x = 0f;
        for (int i = 0; i < count; i++)
        {
            var rt = UIKit.Rect("Chip", sheet);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            var c = new Chip { Rect = rt };
            c.Bg = rt.gameObject.AddComponent<Image>();
            c.Shape = rt.gameObject.AddComponent<UIRoundedCorners>();
            c.Shape.SetRadius(h * 0.5f);
            c.Shape.SetBorder(1.2f * K);
            c.Text = UIKit.Text("Label", rt, UIFonts.SemiBold, 14.5f * K, UIKit.TextMain, TextAlignmentOptions.Center);
            c.Text.text = label(i);
            c.Text.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Stretch(c.Text.rectTransform, Vector2.zero, Vector2.zero);
            float w = c.Text.GetPreferredValues(label(i)).x + pad * 2f;
            if (x > 0f && x + w > maxW) { x = 0f; y -= h + gap; }
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(side + x, y);
            x += w + gap;
            int index = i;
            rt.gameObject.AddComponent<Button>().onClick.AddListener(() => onTap(index));
            UIPress.Add(rt);
            into.Add(c);
        }
        return y - h - 22f * K;
    }

    private static void Row(RectTransform rt, float y, float height, float left, float right)
    {
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(left, y - height); rt.offsetMax = new Vector2(-right, y);
    }

    private static void TopCenter(RectTransform rt, float y)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
    }

    // ------------------------------------------------------------------ state

    private void Sync()
    {
        for (int i = 0; i < sortChips.Count; i++) sortChips[i].Set(draft.Sort == i);
        for (int i = 0; i < categoryChips.Count; i++) categoryChips[i].Set(draft.Category == i);
        animChip?.Set(draft.Animated);
        staffChip?.Set(draft.StaffPicks);
        lightChip?.Set(draft.Light);
    }

    private void Apply()
    {
        SearchFilters.Set(draft.Clone());
        Close();
        onApply?.Invoke();
    }

    private void Close() => open = false;

    void Update()
    {
        if (open && Input.GetKeyDown(KeyCode.Escape)) { Close(); backFrame = Time.frameCount; } // Back closes the sheet first
        float dt = Time.unscaledDeltaTime;
        shown = Mathf.MoveTowards(shown, open ? 1f : 0f, dt * 4f);
        float e = open ? 1f - Mathf.Pow(1f - shown, 3f) : shown * shown;   // ease out when opening
        sheet.anchoredPosition = new Vector2(0f, -sheet.sizeDelta.y * (1f - e));
        dimGroup.alpha = e;
        dimGroup.blocksRaycasts = open;
        if (!open && shown <= 0f) gameObject.SetActive(false);
    }

    public static bool IsOpen => instance != null && instance.open;

    private static int backFrame = -1;
    /// <summary>True while the sheet is open or it was closed by Back this frame (so Back doesn't also leave the results).</summary>
    public static bool ConsumedBack => IsOpen || backFrame == Time.frameCount;
}
