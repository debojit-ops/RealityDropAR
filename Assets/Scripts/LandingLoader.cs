using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Full-screen loading screen shown every time the landing (search) screen opens, in the app's cyan
/// neon theme: the RealityDrop header logo (Resources/RealityDropUI/HeaderLogo.png, cut from bg.png),
/// a spinning wireframe cube on a glowing floor ring with scan pulses, a progress bar and status text.
/// Stays until the first recommendation picture has loaded (or restored search results are back),
/// at least <see cref="MinShow"/> seconds. Then it fades out and, once per app start, opens the guide.
/// </summary>
public class LandingLoader : MonoBehaviour
{
    const float MinShow = 2.8f;   // long enough to read the brand, even when everything is cached
    const float MaxWait = 15f;    // offline / slow network: never trap the user

    private RecommendationsPanel panel;
    private Transform resultsContent;
    private CanvasGroup group;
    private RectTransform logo, fill;
    private Image logoGlow;
    private TextMeshProUGUI status;
    private Texture2D logoTexture, artTexture;
    private RectTransform art, track;
    private RawImage artImage;

    // Where the progress UI sits on the splash image (pixel rows of the source image): the dark band
    // between the tagline (ends ~1205) and the lower light swoosh (starts ~1340).
    const float ArtPercentRow = 1243f, ArtBarRow = 1282f, ArtStatusRow = 1318f;
    private float started, progress, progressVelocity, dotTimer;
    private float readyAt = -1f, readyFrom;
    private TextMeshProUGUI percent;
    private int dots;
    private bool closing;

    private static LandingLoader instance;

    public static void Show(RecommendationsPanel panel, Transform resultsContent)
    {
        // Only one loader at a time (SampleScene has two SearchUI components; each used to open one,
        // and the first to close unloaded the shared splash texture under the second → stuck screen)
        if (instance != null)
        {
            if (instance.panel == null) instance.panel = panel;
            if (instance.resultsContent == null) instance.resultsContent = resultsContent;
            return;
        }
        var canvas = UIKit.CreateCanvas(null, "LandingLoader", 100);
        var loader = canvas.gameObject.AddComponent<LandingLoader>();
        instance = loader;
        loader.panel = panel;
        loader.resultsContent = resultsContent;
        loader.Build((RectTransform)canvas.transform);
    }

    private void Build(RectTransform root)
    {
        started = Time.realtimeSinceStartup;
        group = root.gameObject.AddComponent<CanvasGroup>();

        var bg = UIKit.Rect("Backdrop", root);
        UIKit.Stretch(bg, Vector2.zero, Vector2.zero);
        bg.gameObject.AddComponent<Image>().color = SafeAreaFitter.BackdropColor; // also blocks taps

        // Splash artwork (Resources/RealityDropUI/Onboarding/LoaderBackground): the full splash design with
        // its own logo, tagline and neon frame. Fitted to the screen (never cropped, so the frame stays whole);
        // the dark edges blend into the backdrop. Only the progress bar + status are drawn on top.
        artTexture = Resources.Load<Texture2D>("RealityDropUI/Onboarding/LoaderBackground");
        if (artTexture != null)
        {
            art = UIKit.Rect("Artwork", root);
            UIKit.Stretch(art, Vector2.zero, Vector2.zero);
            artImage = art.gameObject.AddComponent<RawImage>();
            artImage.texture = artTexture;
            artImage.raycastTarget = false;
            var fit = art.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = (float)artTexture.width / artTexture.height;
        }
        else BuildCodeLogo(root);

        // Loading animation (only without artwork): the cube floats over a floor ring while scan pulses spread out
        if (artTexture == null)
        {
            NeonArt.Glow(root, new Vector2(0f, -60f), 760f, 0.08f);
            NeonArt.ScanPulse(root, new Vector2(0f, -250f), 640f, 0f);
            NeonArt.ScanPulse(root, new Vector2(0f, -250f), 640f, 1.1f);
            NeonArt.FloorRing(root, new Vector2(0f, -250f), 520f, 0.9f);
            var cube = NeonArt.Cube(root, new Vector2(0f, -20f), 380f, 7f);
            cube.speed = 55f;
            cube.bobAmount = 18f;
        }

        BuildProgress(root);
        Tick(0f);
    }

    /// <summary>Fallback when there is no splash image: our header logo + tagline.</summary>
    private void BuildCodeLogo(RectTransform root)
    {
        logoGlow = NeonArt.Glow(root, new Vector2(0f, 640f), 900f, 0.10f);
        logo = UIKit.Rect("Logo", root);
        logo.anchorMin = logo.anchorMax = new Vector2(0.5f, 0.5f);
        logo.anchoredPosition = new Vector2(0f, 640f);
        logoTexture = Resources.Load<Texture2D>("RealityDropUI/HeaderLogo");
        var logoImg = logo.gameObject.AddComponent<RawImage>();
        logoImg.raycastTarget = false;
        if (logoTexture != null)
        {
            logoImg.texture = logoTexture;
            float w = 900f;
            logo.sizeDelta = new Vector2(w, w * logoTexture.height / logoTexture.width);
        }
        else logo.gameObject.SetActive(false);

        var tag = UIKit.Text("Tagline", root, UIFonts.SemiBold, 36f, UIKit.TextMuted, TextAlignmentOptions.Center);
        tag.text = "SEARCH  <color=#3EF2FF>•</color>  PREVIEW  <color=#3EF2FF>•</color>  PLACE IN AR";
        tag.characterSpacing = 14f;
        Place(tag.rectTransform, 470f, 60f);
    }

    private void BuildProgress(RectTransform root)
    {
        // Progress bar
        track = UIKit.Rect("Track", root);
        track.anchorMin = track.anchorMax = new Vector2(0.5f, 0.5f);
        track.anchoredPosition = new Vector2(0f, -620f);
        track.sizeDelta = new Vector2(520f, 10f);
        var ti = track.gameObject.AddComponent<Image>();
        ti.color = new Color(1f, 1f, 1f, 0.08f);
        ti.raycastTarget = false;
        track.gameObject.AddComponent<UIRoundedCorners>().SetRadius(6f);

        fill = UIKit.Rect("Fill", track);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0f, 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        var fi = fill.gameObject.AddComponent<Image>();
        fi.color = Color.white;
        fi.raycastTarget = false;
        fill.gameObject.AddComponent<UIRoundedCorners>().SetRadius(6f);
        fill.gameObject.AddComponent<UIGradient>().Set(UIKit.GradTop, UIKit.GradBottom);

        percent = UIKit.Text("Percent", root, UIFonts.Display, 44f, NeonArt.Cyan, TextAlignmentOptions.Center);
        Place(percent.rectTransform, -565f, 60f);

        status = UIKit.Text("Status", root, UIFonts.SemiBold, 40f, UIKit.TextMuted, TextAlignmentOptions.Center);
        Place(status.rectTransform, -700f, 70f);

        if (artTexture != null) { percent.fontSize = 38f; status.fontSize = 32f; }
    }

    /// <summary>With the splash image: keeps the progress UI in the image's dark band at any screen shape.</summary>
    private void PlaceOnArtwork()
    {
        if (art == null || artTexture == null) return; // texture gone: just keep the last layout
        Rect r = art.rect;
        if (r.height <= 0f) return;
        float scale = r.height / artTexture.height;
        float Y(float row) => r.height * 0.5f - row * scale;   // image row → canvas y (art is centred)
        percent.rectTransform.anchoredPosition = new Vector2(0f, Y(ArtPercentRow));
        track.anchoredPosition = new Vector2(0f, Y(ArtBarRow));
        status.rectTransform.anchoredPosition = new Vector2(0f, Y(ArtStatusRow));
        track.sizeDelta = new Vector2(Mathf.Min(520f, r.width * 0.5f), 10f);
    }

    private static void Place(RectTransform rt, float y, float height)
    {
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(-80f, height);
    }

    private bool Ready
    {
        get
        {
            if (resultsContent != null && resultsContent.childCount > 0) return true; // back from a model: results restored
            return panel == null || panel.IsReady;
        }
    }

    void Update()
    {
        // Closing must never be blocked by a problem in the visuals
        try { Tick(Time.unscaledDeltaTime); }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            if (Time.realtimeSinceStartup - started > MinShow) Destroy(gameObject);
        }
    }

    private void Tick(float dt)
    {
        float t = Time.realtimeSinceStartup - started;
        bool ready = Ready || t > MaxWait;

        // Smooth 0 → 100 %: while waiting the goal eases towards ~90 %; once ready it glides from wherever
        // the bar is to 100 % over the remaining minimum time (at least 1 s). SmoothDamp keeps the speed
        // continuous, so the bar never jumps or stalls when loading finishes.
        if (ready && readyAt < 0f) { readyAt = t; readyFrom = progress; }
        float goal;
        if (!ready) goal = 0.9f * (1f - Mathf.Exp(-t / 2.2f));
        else
        {
            float finish = Mathf.Max(1f, MinShow - readyAt);
            goal = Mathf.Lerp(readyFrom, 1f, Mathf.Clamp01((t - readyAt) / finish));
        }
        progress = Mathf.SmoothDamp(progress, goal, ref progressVelocity, 0.3f, 1.5f, Mathf.Min(dt, 0.1f));
        if (ready && goal >= 1f && progress > 0.995f) progress = 1f;
        fill.anchorMax = new Vector2(progress, 1f);
        percent.text = Mathf.FloorToInt(progress * 100f) + "%";

        float intro = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.6f));
        if (art != null && artImage != null)
        {
            artImage.color = new Color(1f, 1f, 1f, intro);   // splash fades in, no zoom (keeps the frame whole)
            PlaceOnArtwork();
        }
        if (logo != null)
        {
            // Fallback logo fades/slides in
            logo.anchoredPosition = new Vector2(0f, 640f - 40f * (1f - intro));
            logo.GetComponent<RawImage>().color = new Color(1f, 1f, 1f, intro);
            logoGlow.color = UIKit.WithAlpha(NeonArt.Cyan, 0.08f * intro);
        }

        string baseText = ready ? "Ready" : (t > 6f ? "Still loading recommendations" : "Finding models for you");
        dotTimer += dt;
        if (dotTimer > 0.4f) { dotTimer = 0f; dots = (dots + 1) % 4; }
        status.text = ready ? baseText : baseText + (dots == 0 ? "" : " " + string.Join(" ", new string('.', dots).ToCharArray()));

        if (!closing && ready && t >= MinShow && progress >= 1f)
        {
            closing = true;
            OnboardingGuide.ShowIfNeeded(); // once per app start: the guide appears under the fading loader
        }
        if (closing)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, 0f, dt * 2.5f);
            group.blocksRaycasts = false;
            if (group.alpha <= 0f) Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (logoTexture != null) Resources.UnloadAsset(logoTexture);
        if (artTexture != null) Resources.UnloadAsset(artTexture);
        Debug.Log($"[LandingLoader] Closed after {Time.realtimeSinceStartup - started:0.0}s.");
    }
}
