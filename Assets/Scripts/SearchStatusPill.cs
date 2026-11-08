using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small pill over the placeholder cards while a search waits for its first results:
/// starts with "Searching…", then rotates through playful words ("Cooking…", "Brewing…").
/// Built by RecommendationsPanel; watches SketchfabAPIManager.IsSearching.
/// </summary>
public class SearchStatusPill : MonoBehaviour
{
    static readonly string[] Words =
    {
        "Cooking", "Brewing", "Summoning", "Sculpting", "Conjuring", "Polishing",
        "Unboxing", "Rendering", "Hunting", "Forging", "Tinkering", "Assembling",
    };

    const float WordSeconds = 1.1f, FadeSeconds = 0.14f;

    private SketchfabAPIManager api;
    private CanvasGroup group;
    private TextMeshProUGUI label;
    private bool wasSearching;
    private float wordTimer;
    private int lastWord = -1;

    public static void Create(Transform parent, SketchfabAPIManager api)
    {
        var rt = UIKit.Rect("SearchStatus", parent);
        rt.SetAsLastSibling();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

        var image = rt.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        UIKit.Surface(rt.gameObject, new Color(0.06f, 0.085f, 0.11f, 0.96f), 9999f, UIKit.WithAlpha(UIKit.Neon, 0.35f), 2f);

        var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(70, 70, 34, 34);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fit = rt.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var pill = rt.gameObject.AddComponent<SearchStatusPill>();
        pill.api = api;
        pill.group = rt.gameObject.AddComponent<CanvasGroup>();
        pill.group.alpha = 0f;
        pill.group.blocksRaycasts = pill.group.interactable = false;
        pill.label = UIKit.Text("Label", rt, UIFonts.SemiBold, 58f, UIKit.TextMain, TextAlignmentOptions.Center);
        pill.label.textWrappingMode = TextWrappingModes.NoWrap;
    }

    void Update()
    {
        if (api == null) return;
        float dt = Time.unscaledDeltaTime;
        bool searching = api.IsSearching;

        if (searching && !wasSearching)
        {
            label.text = "Searching…";
            wordTimer = 0f;
        }
        wasSearching = searching;

        group.alpha = Mathf.MoveTowards(group.alpha, searching ? 1f : 0f, dt * 6f);
        if (!searching) return;

        // Fade each word out, swap it while invisible, fade the next one in
        float before = wordTimer;
        wordTimer += dt;
        if (before < WordSeconds && wordTimer >= WordSeconds) label.text = NextWord();
        if (wordTimer >= WordSeconds + FadeSeconds) wordTimer -= WordSeconds + FadeSeconds;

        float fadeOut = Mathf.Clamp01((WordSeconds - wordTimer) / FadeSeconds);
        float fadeIn = Mathf.Clamp01(wordTimer >= WordSeconds ? (wordTimer - WordSeconds) / FadeSeconds : 1f);
        label.alpha = wordTimer >= WordSeconds ? fadeIn : fadeOut;
    }

    private string NextWord()
    {
        int next = Random.Range(0, Words.Length - 1);
        if (next >= lastWord && lastWord >= 0) next++;  // never the same word twice in a row
        lastWord = next;
        return Words[next] + "…";
    }
}
