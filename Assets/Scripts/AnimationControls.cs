using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Play/pause + clip picker for animated models (glTFast imports glTF animations as legacy
/// <see cref="Animation"/> clips on the model). Used on the preview and AR screens.
///   • no clips  → hidden
///   • 1 clip    → [▶/❚❚  Clip name]
///   • 2+ clips  → [▶/❚❚] [Clip name ▾] → list of clips (tap to switch, smooth crossfade)
/// Clips loop. The first clip starts automatically when the model appears.
/// The model is looked up through a callback because it loads after the screen is built.
/// </summary>
public class AnimationControls : MonoBehaviour
{
    const float K = UIKit.K;

    private Func<GameObject> getModel;
    private Func<bool> isVisible;
    private bool listOpensUp;

    private Animation anim;
    private readonly List<string> clips = new List<string>();
    private string currentClip;
    private bool paused;

    private CanvasGroup group;
    private RawImage playIcon;
    private TextMeshProUGUI clipLabel;
    private RawImage chevron;
    private RectTransform list;
    private CanvasGroup listGroup;
    private bool listOpen;
    private float findTimer;

    /// <summary>
    /// Creates the control. <paramref name="place"/> positions the root rect; <paramref name="visible"/>
    /// (optional) lets the screen hide it (e.g. AR before the model is placed).
    /// </summary>
    public static AnimationControls Create(RectTransform parent, Func<GameObject> model, Action<RectTransform> place,
                                           bool listOpensUp, Func<bool> visible = null)
    {
        var rt = UIKit.Rect("AnimationControls", parent);
        place?.Invoke(rt);
        var c = rt.gameObject.AddComponent<AnimationControls>();
        c.getModel = model;
        c.isVisible = visible;
        c.listOpensUp = listOpensUp;
        c.Build(rt);
        return c;
    }

    // ------------------------------------------------------------------ build

    private void Build(RectTransform root)
    {
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        var row = root.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 8f * K;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        var fit = root.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ▶ / ❚❚ round button
        var play = UIKit.Rect("PlayPause", root);
        var ple = play.gameObject.AddComponent<LayoutElement>();
        ple.preferredWidth = ple.preferredHeight = 44f * K;
        UIKit.Surface(play.gameObject, UIKit.GlassFill, 9999f, UIKit.WithAlpha(UIKit.Neon, 0.6f), 1.5f * K);
        var pb = play.gameObject.AddComponent<Button>();
        pb.transition = Selectable.Transition.None;
        pb.onClick.AddListener(TogglePlay);
        UIPress.Add(play);
        playIcon = UIKit.Icon(play, UIIcons.Pause, UIKit.Neon, 20f * K);
        playIcon.rectTransform.anchorMin = playIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);

        // Clip name pill (opens the list when there are several clips)
        var pill = UIKit.Rect("Clip", root);
        UIKit.Surface(pill.gameObject, UIKit.GlassFill, 9999f, UIKit.WithAlpha(UIKit.Neon, 0.35f), 1.2f * K);
        var pl = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
        pl.padding = new RectOffset((int)(16 * K), (int)(14 * K), 0, 0);
        pl.spacing = 6f * K;
        pl.childAlignment = TextAnchor.MiddleLeft;
        pl.childControlWidth = pl.childControlHeight = true;
        pl.childForceExpandWidth = pl.childForceExpandHeight = false;
        var pillLE = pill.gameObject.AddComponent<LayoutElement>();
        pillLE.preferredHeight = 44f * K;
        var cb = pill.gameObject.AddComponent<Button>();
        cb.transition = Selectable.Transition.None;
        cb.onClick.AddListener(() => { if (clips.Count > 1) SetListOpen(!listOpen); else TogglePlay(); });
        UIPress.Add(pill);

        clipLabel = UIKit.Text("Name", pill, UIFonts.SemiBold, 14f * K, UIKit.TextMain, TextAlignmentOptions.MidlineLeft);
        clipLabel.textWrappingMode = TextWrappingModes.NoWrap;
        clipLabel.overflowMode = TextOverflowModes.Ellipsis;
        var nameLE = clipLabel.gameObject.AddComponent<LayoutElement>();
        nameLE.preferredWidth = 150f * K;

        chevron = UIKit.Icon(pill, UIIcons.ChevronDown, UIKit.Neon, 16f * K);
        var chLE = chevron.gameObject.AddComponent<LayoutElement>();
        chLE.preferredWidth = chLE.preferredHeight = 16f * K;

        // Clip list (built when the model's clips are known)
        list = UIKit.Rect("ClipList", root);
        list.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        list.anchorMin = list.anchorMax = listOpensUp ? new Vector2(0f, 1f) : new Vector2(0f, 0f);
        list.pivot = listOpensUp ? new Vector2(0f, 0f) : new Vector2(0f, 1f);
        list.anchoredPosition = new Vector2(52f * K, listOpensUp ? 8f * K : -8f * K);
        list.sizeDelta = new Vector2(220f * K, 0f);
        UIKit.Surface(list.gameObject, new Color(0.04f, 0.06f, 0.085f, 0.96f), 18f * K, UIKit.WithAlpha(UIKit.Neon, 0.45f), 1.2f * K);
        var vl = list.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset((int)(6 * K), (int)(6 * K), (int)(6 * K), (int)(6 * K));
        vl.spacing = 2f * K;
        vl.childControlWidth = vl.childControlHeight = true;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;
        var lf = list.gameObject.AddComponent<ContentSizeFitter>();
        lf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        listGroup = list.gameObject.AddComponent<CanvasGroup>();
        listGroup.alpha = 0f;
        listGroup.blocksRaycasts = false;
    }

    private void BuildList()
    {
        foreach (Transform child in list) Destroy(child.gameObject);
        for (int i = 0; i < clips.Count; i++)
        {
            string clipName = clips[i];
            var item = UIKit.Rect("Item " + clipName, list);
            var img = item.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            item.gameObject.AddComponent<UIRoundedCorners>().SetRadius(12f * K);
            var le = item.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 40f * K;
            var t = UIKit.Text("Text", item, UIFonts.Regular, 14f * K, UIKit.TextMain, TextAlignmentOptions.MidlineLeft);
            t.text = Pretty(clipName, i);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            UIKit.Stretch(t.rectTransform, new Vector2(12f * K, 0f), new Vector2(-12f * K, 0f));
            var b = item.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { PlayClip(clipName); SetListOpen(false); });
        }
    }

    // ------------------------------------------------------------------ animation

    private void TryFindAnimation()
    {
        var model = getModel != null ? getModel() : null;
        if (model != null && !model.activeInHierarchy) return; // AR: wait until placed (inactive models can't play)
        var found = model != null ? model.GetComponentInChildren<Animation>(true) : null;
        if (found == anim) return;

        anim = found;
        clips.Clear();
        currentClip = null;
        if (anim != null)
        {
            foreach (AnimationState state in anim)
            {
                state.wrapMode = WrapMode.Loop;
                clips.Add(state.name);
            }
            anim.playAutomatically = false;
        }
        chevron.gameObject.SetActive(clips.Count > 1);
        BuildList();
        if (clips.Count > 0) PlayClip(clips[0]); // start the first clip right away
        Debug.Log($"[AnimationControls] {clips.Count} animation clip(s) found.");
    }

    private void PlayClip(string clipName)
    {
        if (anim == null || string.IsNullOrEmpty(clipName)) return;
        currentClip = clipName;
        paused = false;
        foreach (AnimationState s in anim) s.speed = 1f;
        anim.CrossFade(clipName, 0.25f);
        Refresh();
    }

    private void TogglePlay()
    {
        if (anim == null || clips.Count == 0) return;
        if (currentClip == null) { PlayClip(clips[0]); return; }
        paused = !paused;
        foreach (AnimationState s in anim) s.speed = paused ? 0f : 1f;
        if (!paused && !anim.isPlaying) anim.Play(currentClip);
        Refresh();
    }

    private void Refresh()
    {
        playIcon.texture = UIIcons.Get(paused ? UIIcons.Play : UIIcons.Pause);
        int index = currentClip != null ? clips.IndexOf(currentClip) : -1;
        clipLabel.text = currentClip != null ? Pretty(currentClip, index) : "Animation";
        // Highlight the playing clip in the list
        for (int i = 0; i < list.childCount; i++)
        {
            var img = list.GetChild(i).GetComponent<Image>();
            if (img != null) img.color = i == index ? UIKit.WithAlpha(UIKit.Neon, 0.18f) : new Color(1f, 1f, 1f, 0f);
        }
    }

    private void SetListOpen(bool open)
    {
        listOpen = open;
        chevron.rectTransform.localRotation = Quaternion.Euler(0f, 0f, open == listOpensUp ? 180f : 0f);
        if (open) list.SetAsLastSibling();
    }

    private static string Pretty(string clipName, int index)
    {
        if (string.IsNullOrWhiteSpace(clipName) || clipName.StartsWith("animation_", StringComparison.OrdinalIgnoreCase))
            return "Animation " + (index + 1);
        return clipName.Replace('_', ' ');
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        findTimer -= dt;
        if (findTimer <= 0f) { findTimer = 0.5f; TryFindAnimation(); }

        bool show = clips.Count > 0 && (isVisible == null || isVisible());
        group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, dt * 5f);
        group.blocksRaycasts = group.interactable = show && group.alpha > 0.5f;
        if (!show && listOpen) SetListOpen(false);

        listGroup.alpha = Mathf.MoveTowards(listGroup.alpha, listOpen ? 1f : 0f, dt * 8f);
        listGroup.blocksRaycasts = listGroup.interactable = listOpen;
    }
}
