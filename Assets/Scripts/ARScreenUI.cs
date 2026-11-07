using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// AR screen HUD in the app's neon glass style (replaces the old AR canvas and debug buttons):
///   top: back · model title · re-place
///   center-bottom: coaching pill that always says what to do next
///     (preparing → move phone → tap to place → drag/pinch/twist; "tracking lost" when needed)
///   bottom: PLACE MODEL button once the glowing ring sits on a surface
///   size chip while pinching ("120%")
/// Reads everything from ModelPlacement; added automatically by ARSceneInitializer.
/// </summary>
public class ARScreenUI : MonoBehaviour
{
    const float K = UIKit.K;

    private ModelPlacement placement;
    private ARSceneInitializer initializer;

    private CanvasGroup coachGroup, placeGroup, resetGroup, sizeGroup, liteGroup, modeGroup;
    private TextMeshProUGUI modeLabel;
    private TextMeshProUGUI coachText, sizeText;
    private RawImage coachIcon;
    private string coachMessage;
    private float coachSwap = 1f;          // 0..1 crossfade when the message changes
    private float placedTime = -1f;
    private bool userInteracted;

    public static void Ensure()
    {
        if (FindFirstObjectByType<ARScreenUI>() != null) return;
        new GameObject("ARScreenUI").AddComponent<ARScreenUI>();
    }

    void Start()
    {
        placement = FindFirstObjectByType<ModelPlacement>();
        initializer = FindFirstObjectByType<ARSceneInitializer>();
        if (placement != null)
        {
            placement.ModelTitle = PlayerPrefs.GetString("LastModelTitle", "");
            placement.OnPlaced += () => { placedTime = Time.unscaledTime; userInteracted = false; };
            placement.OnUserManipulated += () => userInteracted = true;
        }
        Build();
    }

    // ------------------------------------------------------------------ build

    private void Build()
    {
        var canvas = UIKit.CreateCanvas(transform, "ARUI");
        UIKit.HideOtherCanvases(canvas);
        var root = (RectTransform)canvas.transform;

        // Top bar
        var bar = UIKit.Rect("TopBar", root);
        bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, 44f * K);
        bar.anchoredPosition = new Vector2(0f, -44f * K);

        var back = UIKit.IconButton(bar, "Back", UIIcons.Back, 44f, OnBack);
        Place((RectTransform)back.transform, new Vector2(0f, 0.5f), new Vector2(42f * K, 0f));

        var resetBtn = UIKit.IconButton(bar, "Replace", UIIcons.Reset, 44f, OnReplace);
        var resetRt = (RectTransform)resetBtn.transform;
        Place(resetRt, new Vector2(1f, 0.5f), new Vector2(-42f * K, 0f));
        resetGroup = resetRt.gameObject.AddComponent<CanvasGroup>();
        resetGroup.alpha = 0f;

        string title = PlayerPrefs.GetString("LastModelTitle", "");
        if (!string.IsNullOrEmpty(title))
        {
            var t = UIKit.Text("Title", bar, UIFonts.SemiBold, 16f * K, UIKit.TextMain, TextAlignmentOptions.Center);
            t.text = title;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            var tr = t.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
            tr.sizeDelta = new Vector2(220f * K, 24f * K);
            // Readability over the camera feed
            t.fontMaterial.EnableKeyword("UNDERLAY_ON");
            t.fontMaterial.SetFloat("_UnderlaySoftness", 0.6f);
            t.fontMaterial.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.7f));
        }

        // Size chip (while pinching)
        var chip = UIKit.Rect("SizeChip", root);
        chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 1f);
        chip.pivot = new Vector2(0.5f, 1f);
        chip.sizeDelta = new Vector2(110f * K, 34f * K);
        chip.anchoredPosition = new Vector2(0f, -104f * K);
        UIKit.Surface(chip.gameObject, UIKit.GlassFill, 9999f, UIKit.WithAlpha(UIKit.Neon, 0.5f), 1.2f * K);
        chip.GetComponent<Image>().raycastTarget = false;
        sizeGroup = chip.gameObject.AddComponent<CanvasGroup>();
        sizeGroup.alpha = 0f;
        sizeText = UIKit.Text("Text", chip, UIFonts.Display, 18f * K, UIKit.Neon, TextAlignmentOptions.Center);
        UIKit.Stretch(sizeText.rectTransform, Vector2.zero, Vector2.zero);
        sizeText.characterSpacing = 4f;

        // MANUAL ⇄ AR MODE switch (always available once the model is ready)
        var mode = UIKit.Rect("ModeToggle", root);
        mode.anchorMin = mode.anchorMax = new Vector2(0.5f, 1f);
        mode.pivot = new Vector2(0.5f, 1f);
        mode.anchoredPosition = new Vector2(0f, -150f * K);
        UIKit.Surface(mode.gameObject, UIKit.GlassFill, 9999f, UIKit.WithAlpha(UIKit.Neon, 0.6f), 1.2f * K);
        var modeLayout = mode.gameObject.AddComponent<HorizontalLayoutGroup>();
        modeLayout.padding = new RectOffset((int)(18 * K), (int)(18 * K), (int)(8 * K), (int)(8 * K));
        modeLayout.childAlignment = TextAnchor.MiddleCenter;
        modeLayout.childControlWidth = modeLayout.childControlHeight = true;
        modeLayout.childForceExpandWidth = modeLayout.childForceExpandHeight = false;
        var modeFit = mode.gameObject.AddComponent<ContentSizeFitter>();
        modeFit.horizontalFit = modeFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        modeLabel = UIKit.Text("Label", mode, UIFonts.Display, 17f * K, UIKit.Neon, TextAlignmentOptions.Center);
        modeLabel.text = "MANUAL";
        modeLabel.characterSpacing = 6f;
        modeLabel.textWrappingMode = TextWrappingModes.NoWrap;
        var modeBtn = mode.gameObject.AddComponent<Button>();
        modeBtn.transition = Selectable.Transition.None;
        modeBtn.onClick.AddListener(OnToggleMode);
        UIPress.Add(mode);
        modeGroup = mode.gameObject.AddComponent<CanvasGroup>();
        modeGroup.alpha = 0f;

        // PLACE MODEL
        var place = UIKit.PrimaryButton(root, "PlaceModel", UIIcons.ARCube, "PLACE MODEL", OnPlace);
        var placeRt = (RectTransform)place.transform;
        placeRt.anchorMin = new Vector2(0f, 0f); placeRt.anchorMax = new Vector2(1f, 0f);
        placeRt.pivot = new Vector2(0.5f, 0f);
        placeRt.offsetMin = new Vector2(30f * K, 40f * K);
        placeRt.offsetMax = new Vector2(-30f * K, (40f + 58f) * K);
        placeGroup = placeRt.gameObject.AddComponent<CanvasGroup>();
        placeGroup.alpha = 0f;

        // USE CAMERA MODE (only when ARCore can't track on this phone)
        var lite = UIKit.PrimaryButton(root, "LiteMode", UIIcons.ARCube, "PLACE MANUALLY", OnLiteMode);
        var liteRt = (RectTransform)lite.transform;
        liteRt.anchorMin = placeRt.anchorMin; liteRt.anchorMax = placeRt.anchorMax;
        liteRt.pivot = placeRt.pivot;
        liteRt.offsetMin = placeRt.offsetMin; liteRt.offsetMax = placeRt.offsetMax;
        liteGroup = liteRt.gameObject.AddComponent<CanvasGroup>();
        liteGroup.alpha = 0f;

        // Coaching pill
        var coach = UIKit.Rect("Coach", root);
        coach.anchorMin = coach.anchorMax = new Vector2(0.5f, 0f);
        coach.pivot = new Vector2(0.5f, 0f);
        coach.anchoredPosition = new Vector2(0f, 122f * K);
        UIKit.Surface(coach.gameObject, new Color(0.04f, 0.06f, 0.085f, 0.82f), 9999f, UIKit.WithAlpha(UIKit.Neon, 0.35f), 1.2f * K);
        coach.GetComponent<Image>().raycastTarget = false;
        coachGroup = coach.gameObject.AddComponent<CanvasGroup>();
        coachGroup.blocksRaycasts = false;
        var hl = coach.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset((int)(16 * K), (int)(18 * K), (int)(11 * K), (int)(11 * K));
        hl.spacing = 10f * K;
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = hl.childForceExpandHeight = false;
        var fit = coach.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        coachIcon = UIKit.Icon(coach, UIIcons.Spin, UIKit.Neon, 20f * K);
        var ile = coachIcon.gameObject.AddComponent<LayoutElement>();
        ile.preferredWidth = ile.preferredHeight = 20f * K;
        coachText = UIKit.Text("Text", coach, UIFonts.SemiBold, 15f * K, UIKit.TextMain, TextAlignmentOptions.Left);
        coachText.textWrappingMode = TextWrappingModes.NoWrap;

        // Play / pause + clip picker for animated models, once the model is placed
        AnimationControls.Create(root, () => placement != null ? placement.Model : null, rt =>
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 40f * K);
        }, listOpensUp: true, visible: () => placement != null && placement.IsPlaced);

        SafeAreaFitter.Apply(canvas, addBackdrop: false);
    }


    /// <summary>Why AR isn't tracking yet, in plain words (null when there is nothing specific to say).</summary>
    private static string TrackingHint()
    {
        if (ARSession.state == ARSessionState.Unsupported)
            return "This phone doesn't support AR";
        if (ARSession.state == ARSessionState.NeedsInstall || ARSession.state == ARSessionState.Installing)
            return "Installing Google Play Services for AR…";
        switch (ARSession.notTrackingReason)
        {
            case NotTrackingReason.InsufficientLight: return "Too dark: turn on a light";
            case NotTrackingReason.InsufficientFeatures: return "Point at a floor with some texture (a rug or wood)";
            case NotTrackingReason.ExcessiveMotion: return "Move your phone more slowly";
            case NotTrackingReason.CameraUnavailable: return "Camera unavailable: close other camera apps";
            case NotTrackingReason.Unsupported: return "AR tracking isn't supported on this phone";
            default: return null;
        }
    }
    private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
    }

    // ------------------------------------------------------------------ actions

    private void OnBack() => SceneManager.LoadScene("PreviewScene_new");

    /// <summary>MANUAL ⇄ AR: manual places the model in front of you and lets you move it freely.</summary>
    private void OnToggleMode()
    {
        if (placement == null) return;
        if (placement.LiteMode) { placement.ExitLiteMode(); placedTime = -1f; }
        else OnLiteMode();
    }

    private void OnLiteMode()
    {
        if (placement == null) return;
        placement.EnterLiteMode();
        placedTime = Time.unscaledTime;
        userInteracted = false;
    }

    private void OnPlace()
    {
        if (placement != null) placement.PlaceAtReticle();
    }

    private void OnReplace()
    {
        if (placement != null) placement.ResetPlacement();
        placedTime = -1f;
    }

    // ------------------------------------------------------------------ state → HUD

    void Update()
    {
        if (placement == null) return;
        float dt = Time.unscaledDeltaTime;
        var state = placement.CurrentState;

        // What to tell the user right now
        string icon = UIIcons.Spin;
        string message;
        bool showCoach = true;
        bool modelReady = initializer == null || initializer.IsModelReady;

        if (initializer != null && initializer.HasError)
        {
            message = "Couldn't load the model. Go back and try again.";
            icon = UIIcons.Info;
        }
        else if (!modelReady)
            message = "Preparing your model…";
        else if (placement.LiteMode)
        {
            // Lite mode: explain the limits once, then get out of the way
            message = "Manual: drag to move · 2 fingers up/down to raise · pinch · twist";
            icon = UIIcons.Info;
            showCoach = !userInteracted && placedTime >= 0f && Time.unscaledTime - placedTime < 8f;
        }
        else if (placement.TrackingFailed)
        {
            message = "AR can't find surfaces here: tap PLACE MANUALLY";
            icon = UIIcons.Info;
        }
        else if (placement.IsTrackingLost)
        {
            message = state == ModelPlacement.PlacementState.Placed
                ? "Tracking lost: move your phone slowly"
                : "Move your phone slowly to restart AR";
            icon = UIIcons.Info;
        }
        else switch (state)
        {
            case ModelPlacement.PlacementState.Initializing:
                message = TrackingHint() ?? "Move your phone slowly to start AR";
                break;
            case ModelPlacement.PlacementState.Scanning:
                message = "Move your phone slowly over a floor or table";
                break;
            case ModelPlacement.PlacementState.Ready:
                message = "Aim the glowing ring, then tap PLACE MODEL";
                icon = UIIcons.ARCube;
                break;
            default: // Placed
                message = "Drag to move · Pinch to resize · Twist to rotate";
                icon = UIIcons.Spin;
                // Hide the tip once the user has used it, or after a while
                showCoach = !userInteracted && placedTime >= 0f && Time.unscaledTime - placedTime < 6f;
                break;
        }

        // Crossfade the coaching text when it changes
        if (message != coachMessage)
        {
            if (coachSwap >= 1f || coachMessage == null)
            {
                coachMessage = message;
                coachText.text = message;
                coachIcon.texture = UIIcons.Get(icon);
                coachSwap = coachMessage == null ? 1f : 0f;
            }
        }
        coachSwap = Mathf.MoveTowards(coachSwap, 1f, dt * 4f);
        UIKit.Fade(coachGroup, showCoach, dt);
        coachGroup.alpha *= Mathf.Lerp(0.25f, 1f, coachSwap);
        coachGroup.blocksRaycasts = false;

        // Spinning icon while waiting
        bool waiting = !modelReady || state == ModelPlacement.PlacementState.Initializing || state == ModelPlacement.PlacementState.Scanning;
        coachIcon.rectTransform.localRotation = waiting
            ? Quaternion.Euler(0f, 0f, -Time.unscaledTime * 120f)
            : Quaternion.identity;

        UIKit.Fade(placeGroup, !placement.LiteMode && state == ModelPlacement.PlacementState.Ready && !placement.IsTrackingLost, dt);
        UIKit.Fade(liteGroup, placement.TrackingFailed && !placement.LiteMode && modelReady, dt);
        UIKit.Fade(modeGroup, modelReady && (initializer == null || !initializer.HasError), dt);
        if (modeLabel != null) modeLabel.text = placement.LiteMode ? "AR MODE" : "MANUAL";
        UIKit.Fade(resetGroup, state == ModelPlacement.PlacementState.Placed, dt);

        bool pinching = placement.IsPinching;
        if (pinching) sizeText.text = Mathf.RoundToInt(placement.ScaleFactor * 100f) + "%";
        UIKit.Fade(sizeGroup, pinching, dt, pinching ? 10f : 2.5f);
        sizeGroup.blocksRaycasts = false;
    }
}
