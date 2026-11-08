using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Remembers the current search (query, loaded results, next page, scroll position) across scene
/// changes, so "Back" from the preview screen returns to the same feed instead of an empty one.
/// Lives in static memory only (not saved to disk) — it lasts while the app is running.
/// To keep memory low while a 3D model is open, only thumbnails near the scroll position are
/// kept; the rest are re-downloaded when the list is restored.
/// </summary>
public static class SearchSession
{
    public class Entry
    {
        public string Uid;
        public string Name;
        public string Author;
        public string ThumbnailUrl;
        public string HiResThumbnailUrl;
        public long SizeBytes;      // GLB size from Sketchfab (0 = unknown)
        public int Likes;           // Sketchfab like count (0 = none / unknown)
        public bool Animated;       // the model has at least one animation
        public Texture2D Thumbnail; // null = not loaded / released
    }

    public static string Query { get; private set; }
    public static readonly List<Entry> Results = new List<Entry>();
    public static string NextPageUrl;
    public static float ScrollPosition = 1f; // ScrollRect.verticalNormalizedPosition (1 = top)
    public static bool PendingRestore { get; private set; }

    /// <summary>How many thumbnails to keep in memory around the visible cards while away.</summary>
    public const int KeptThumbnails = 48;

    public static bool HasResults => !string.IsNullOrEmpty(Query) && Results.Count > 0;

    public static void Begin(string query)
    {
        Query = query;
        Results.Clear();
        NextPageUrl = null;
        ScrollPosition = 1f;
        PendingRestore = false;
    }

    public static Entry Add(string uid, string name, string author, string thumbUrl, string hiResUrl, long sizeBytes = 0, int likes = 0, bool animated = false)
    {
        var e = new Entry { Likes = likes, Animated = animated, Uid = uid, Name = name, Author = author, ThumbnailUrl = thumbUrl, HiResThumbnailUrl = hiResUrl, SizeBytes = sizeBytes };
        Results.Add(e);
        return e;
    }

    /// <summary>Call right before leaving the search screen.</summary>
    public static void SaveForReturn(string nextPageUrl, float scrollPosition)
    {
        NextPageUrl = nextPageUrl;
        ScrollPosition = Mathf.Clamp01(scrollPosition);
        PendingRestore = HasResults;

        // Keep only thumbnails around the current scroll position; the rest are freed on scene change
        int count = Results.Count;
        int center = Mathf.RoundToInt((1f - ScrollPosition) * Mathf.Max(0, count - 1));
        int from = Mathf.Max(0, center - KeptThumbnails / 3);
        int to = Mathf.Min(count - 1, from + KeptThumbnails - 1);
        for (int i = 0; i < count; i++)
            if (i < from || i > to) Results[i].Thumbnail = null;
    }

    /// <summary>The search screen calls this when it has rebuilt the feed.</summary>
    public static void MarkRestored() => PendingRestore = false;
}
