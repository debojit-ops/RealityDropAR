using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Turns the search-results Content object into a responsive N-column grid (default 2 = the "2x2" layout).
/// Cell width is computed from the scroll viewport width, so it fits any phone screen.
/// Added automatically by SketchfabAPIManager to its resultsParent; it replaces any conflicting
/// layout setup on that object (VerticalLayoutGroup, fixed LayoutElement height, odd scale).
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ResultsGridLayout : MonoBehaviour
{
    [Tooltip("Number of columns.")]
    public int columns = 2;
    [Tooltip("Card height = card width * this (1.3 = square thumbnail + 2-line title + author line).")]
    public float heightToWidthRatio = 1.42f;
    [Tooltip("Gap between cards and around the edges, as a fraction of the list width.")]
    public float spacingToWidth = 0.045f;
    [Tooltip("Left/right/top padding as a multiple of the gap between cards (1 = same as the gap).")]
    public float edgePaddingToSpacing = 1f;
    [Tooltip("Hide the scrollbar (iOS style) and let the cards use its space.")]
    public bool hideScrollbar = true;

    /// <summary>Extra space above the first row (e.g. for the "back to recommendations" button).</summary>
    public float ExtraTopPadding
    {
        get => extraTopPadding;
        set { if (Mathf.Approximately(value, extraTopPadding)) return; extraTopPadding = value; lastWidth = -1f; }
    }
    private float extraTopPadding;

    private GridLayoutGroup grid;
    private RectTransform rect;
    private RectTransform viewport;
    private float lastWidth = -1f;

    void Awake()
    {
        Setup();
    }

    /// <summary>Adds the component to the results container if it isn't there yet.</summary>
    public static ResultsGridLayout Ensure(Transform container)
    {
        if (container == null) return null;
        var layout = container.GetComponent<ResultsGridLayout>();
        if (layout == null) layout = container.gameObject.AddComponent<ResultsGridLayout>();
        return layout;
    }

    private void Setup()
    {
        if (grid != null) return;

        rect = (RectTransform)transform;

        // Only one LayoutGroup is allowed per object, so the old vertical list must go first
        var vertical = GetComponent<HorizontalOrVerticalLayoutGroup>();
        if (vertical != null) DestroyImmediate(vertical);

        // A fixed preferred height on Content stops the list from growing as pages load
        var element = GetComponent<LayoutElement>();
        if (element != null) element.enabled = false;

        rect.localScale = Vector3.one;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, rect.sizeDelta.y);

        grid = GetComponent<GridLayoutGroup>();
        if (grid == null) grid = gameObject.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Mathf.Max(1, columns);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperCenter;

        var fitter = GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = GetComponentInParent<ScrollRect>();
        if (scroll != null)
        {
            ConfigureScroll(scroll);
            viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
        }

        UIRoundedCorners.EnableCanvasChannels(GetComponentInParent<Canvas>());

        UpdateCellSize();
    }

    /// <summary>iOS-like scrolling: no visible scrollbar, rubber-band bounce, smooth momentum.</summary>
    private void ConfigureScroll(ScrollRect scroll)
    {
        scroll.horizontal = false;
        scroll.vertical = true;
        if (scroll.content == null) scroll.content = rect;

        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.elasticity = 0.12f;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.scrollSensitivity = 30f;

        if (hideScrollbar)
        {
            // The scrollbar was also shrinking the viewport (AutoHideAndExpandViewport); give that space back
            if (scroll.verticalScrollbar != null) scroll.verticalScrollbar.gameObject.SetActive(false);
            if (scroll.horizontalScrollbar != null) scroll.horizontalScrollbar.gameObject.SetActive(false);
            scroll.verticalScrollbar = null;
            scroll.horizontalScrollbar = null;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            if (scroll.viewport != null && scroll.viewport != (RectTransform)scroll.transform)
            {
                scroll.viewport.anchorMin = Vector2.zero;
                scroll.viewport.anchorMax = Vector2.one;
                scroll.viewport.offsetMin = Vector2.zero;
                scroll.viewport.offsetMax = Vector2.zero;
            }
        }
    }

    void LateUpdate()
    {
        // Re-fit when the screen/viewport size changes (rotation, different device in the Game view)
        float width = GetAvailableWidth();
        if (!Mathf.Approximately(width, lastWidth))
            UpdateCellSize();
    }

    private float GetAvailableWidth()
    {
        if (viewport != null && viewport.rect.width > 0f) return viewport.rect.width;
        return rect != null ? rect.rect.width : 0f;
    }

    /// <summary>Left/right padding the grid uses for a list of the given width (used to align other UI with the cards).</summary>
    public float GetEdgePadding(float listWidth)
    {
        float spacing = Mathf.Round(listWidth * spacingToWidth);
        return Mathf.RoundToInt(spacing * edgePaddingToSpacing);
    }

    private void UpdateCellSize()
    {
        float width = GetAvailableWidth();
        lastWidth = width;
        if (width <= 0f || grid == null) return;

        int cols = Mathf.Max(1, columns);
        float spacing = Mathf.Round(width * spacingToWidth);
        int pad = Mathf.RoundToInt(GetEdgePadding(width)); // breathing room between cards and the frame
        // Extra bottom padding so the last row can scroll clear of the bottom edge / toast
        grid.padding = new RectOffset(pad, pad, pad + Mathf.RoundToInt(extraTopPadding), pad * 3);
        grid.spacing = new Vector2(spacing, spacing * 1.15f);

        float cellWidth = (width - pad * 2f - spacing * (cols - 1)) / cols;
        cellWidth = Mathf.Max(10f, Mathf.Floor(cellWidth));
        grid.cellSize = new Vector2(cellWidth, Mathf.Floor(cellWidth * heightToWidthRatio));

        LayoutRebuilder.MarkLayoutForRebuild(rect);
    }
}
