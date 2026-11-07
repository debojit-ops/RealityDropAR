using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// App-wide network priority. When the user opens a model, background traffic (thumbnails,
/// extra result pages, recommendations) pauses so the download link and model file get the
/// connection to themselves. Resumed when the download fails or is cancelled.
/// </summary>
public static class NetworkPriority
{
    public static bool BackgroundPaused { get; private set; }

    // A new screen starts with normal priority (e.g. coming back from the preview to search)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        BackgroundPaused = false;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => BackgroundPaused = false;
    }

    public static void PauseBackground()
    {
        if (BackgroundPaused) return;
        BackgroundPaused = true;
        Debug.Log("[NetworkPriority] Background loading paused (model download has priority).");
    }

    public static void ResumeBackground()
    {
        if (!BackgroundPaused) return;
        BackgroundPaused = false;
        Debug.Log("[NetworkPriority] Background loading resumed.");
    }
}

/// <summary>
/// Loads thumbnail images politely:
///  • at most <see cref="MaxConcurrent"/> downloads at a time (instead of 100+ at once)
///  • cards on screen (or within one screen of it) first; off-screen cards wait until scrolled near
///  • pauses — and cancels in-flight images — while a model download has priority
/// Textures for cards destroyed before loading finishes are freed immediately.
/// </summary>
public class ThumbnailLoader : MonoBehaviour
{
    public const int MaxConcurrent = 4;

    private class Item
    {
        public string Url;
        public RawImage Target;
        public RectTransform Viewport;
        public Action<Texture2D> OnLoaded;
        public UnityWebRequest Request;
    }

    private static ThumbnailLoader instance;
    private readonly List<Item> queue = new List<Item>();
    private readonly List<Item> active = new List<Item>();
    private static readonly Vector3[] corners = new Vector3[4];

    private static ThumbnailLoader Instance
    {
        get
        {
            if (instance == null) instance = new GameObject("ThumbnailLoader").AddComponent<ThumbnailLoader>();
            return instance;
        }
    }

    /// <summary>Queues an image. <paramref name="viewport"/> (optional) = the scroll area the card lives in.</summary>
    public static void Load(string url, RawImage target, RectTransform viewport = null, Action<Texture2D> onLoaded = null)
    {
        if (string.IsNullOrEmpty(url) || target == null) return;
        Instance.queue.Add(new Item { Url = url, Target = target, Viewport = viewport, OnLoaded = onLoaded });
    }

    void Update()
    {
        // Pause: cancel in-flight images and put them back in the queue
        if (NetworkPriority.BackgroundPaused)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var it = active[i];
                if (it.Request != null) { it.Request.Abort(); it.Request.Dispose(); it.Request = null; }
                active.RemoveAt(i);
                if (it.Target != null) queue.Insert(0, it);
            }
            return;
        }

        // Drop items whose card no longer exists
        queue.RemoveAll(it => it.Target == null);

        while (active.Count < MaxConcurrent)
        {
            int next = PickNext();
            if (next < 0) break;
            var item = queue[next];
            queue.RemoveAt(next);
            Begin(item);
        }

        // Finish completed requests
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var it = active[i];
            if (it.Request == null || !it.Request.isDone) continue;
            active.RemoveAt(i);

            if (it.Request.result == UnityWebRequest.Result.Success)
            {
                var tex = DownloadHandlerTexture.GetContent(it.Request);
                if (it.Target != null)
                {
                    it.Target.texture = tex;
                    it.OnLoaded?.Invoke(tex);
                }
                else Destroy(tex); // card removed while loading: free GPU memory
            }
            else if (it.Request.result != UnityWebRequest.Result.InProgress)
            {
                Debug.LogWarning("[ThumbnailLoader] Thumbnail failed: " + it.Request.error);
            }
            it.Request.Dispose();
            it.Request = null;
        }
    }

    /// <summary>Visible cards first, then the rest in order (cards far off-screen wait).</summary>
    private int PickNext()
    {
        int fallback = -1;
        for (int i = 0; i < queue.Count; i++)
        {
            var it = queue[i];
            if (it.Target == null) continue;
            if (it.Viewport == null) return i;            // not in a scroll list: load in order
            float d = DistanceFromViewport(it.Target.rectTransform, it.Viewport);
            if (d <= 0f) return i;                        // on screen now
            if (fallback < 0 && d <= 1f) fallback = i;    // within one screen of the viewport
        }
        return fallback;
    }

    /// <summary>0 = overlaps the viewport; otherwise distance in viewport heights.</summary>
    private static float DistanceFromViewport(RectTransform target, RectTransform viewport)
    {
        viewport.GetWorldCorners(corners);
        float vMin = corners[0].y, vMax = corners[1].y, vh = Mathf.Max(1f, vMax - vMin);
        target.GetWorldCorners(corners);
        float tMin = corners[0].y, tMax = corners[1].y;
        if (tMax >= vMin && tMin <= vMax) return 0f;
        return tMin > vMax ? (tMin - vMax) / vh : (vMin - tMax) / vh;
    }

    private void Begin(Item item)
    {
        item.Request = UnityWebRequestTexture.GetTexture(item.Url);
        item.Request.timeout = 20;
        item.Request.SendWebRequest();
        active.Add(item);
    }

    void OnDestroy()
    {
        foreach (var it in active) { it.Request?.Abort(); it.Request?.Dispose(); }
        active.Clear();
        queue.Clear();
        if (instance == this) instance = null;
    }
}
