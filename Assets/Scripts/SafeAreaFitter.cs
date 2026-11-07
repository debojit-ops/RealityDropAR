using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps a screen's UI clear of notches, punch-hole cameras and rounded corners.
/// The scene layout is absolute (positioned from the screen center), so instead of re-anchoring every
/// element, everything is moved into one container that is uniformly scaled and centered inside
/// Screen.safeArea. A dark backdrop fills the thin margins. Does nothing on screens without cutouts
/// (including the normal Game view; use the Device Simulator to preview notches).
/// </summary>
public class SafeAreaFitter : MonoBehaviour
{
    public static readonly Color BackdropColor = new Color(0.02f, 0.035f, 0.055f, 1f);

    private RectTransform canvasRect;
    private RectTransform container;
    private Rect lastSafeArea;
    private Vector2Int lastScreen;

    /// <summary>Wraps all current children of the root canvas into a safe-area container (once).</summary>
    /// <param name="addBackdrop">False for screens with 3D behind the UI (the margins stay see-through).</param>
    public static void Apply(Canvas canvas, bool addBackdrop = true)
    {
        if (canvas == null) return;
        canvas = canvas.rootCanvas;
        if (canvas.GetComponent<SafeAreaFitter>() != null) return;
        canvas.gameObject.AddComponent<SafeAreaFitter>().Setup(canvas, addBackdrop);
    }

    private void Setup(Canvas canvas, bool addBackdrop)
    {
        canvasRect = (RectTransform)canvas.transform;

        // Collect existing children before adding our own objects
        int count = canvasRect.childCount;
        var children = new Transform[count];
        for (int i = 0; i < count; i++) children[i] = canvasRect.GetChild(i);

        RectTransform backdrop = null;
        if (addBackdrop)
        {
            var backdropGO = new GameObject("SafeAreaBackdrop", typeof(RectTransform), typeof(Image));
            backdrop = (RectTransform)backdropGO.transform;
            backdrop.SetParent(canvasRect, false);
            Stretch(backdrop);
            var img = backdropGO.GetComponent<Image>();
            img.color = BackdropColor;
            img.raycastTarget = false;
        }

        container = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
        container.SetParent(canvasRect, false);
        Stretch(container);

        // The container has exactly the canvas rect, so children keep identical layout when moved
        foreach (var child in children)
            child.SetParent(container, false);

        if (backdrop != null) backdrop.SetAsFirstSibling();
        Fit();
    }

    void Update()
    {
        if (Screen.safeArea != lastSafeArea || Screen.width != lastScreen.x || Screen.height != lastScreen.y)
            Fit();
    }

    private void Fit()
    {
        lastSafeArea = Screen.safeArea;
        lastScreen = new Vector2Int(Screen.width, Screen.height);
        if (container == null || Screen.width <= 0 || Screen.height <= 0) return;

        Rect safe = Screen.safeArea;
        float scale = Mathf.Min(safe.width / Screen.width, safe.height / Screen.height);
        scale = Mathf.Clamp(scale, 0.5f, 1f);

        // Offset of the safe-area center from the screen center, converted to canvas units
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 canvasPerPixel = new Vector2(canvasRect.rect.width / Screen.width, canvasRect.rect.height / Screen.height);
        Vector2 offset = Vector2.Scale(safe.center - screenCenter, canvasPerPixel);

        container.localScale = new Vector3(scale, scale, 1f);
        container.anchoredPosition = offset;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
