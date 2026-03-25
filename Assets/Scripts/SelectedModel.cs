// SelectedModel.cs
// Keeps track of the user's current model selection across scenes.
// Safe to use from Search → Preview → AR without needing a GameObject.

using System.IO;

public static class SelectedModel
{
    // ---- Legacy field (kept to avoid breaking your current scripts) ----
    // You already use this in a few places, so we keep it exactly as-is.
    public static string ModelPath;  // local file path to .glb/.gltf (if downloaded)

    // ---- Extra metadata (optional but handy) ----
    public static string Uid;           // Sketchfab UID
    public static string Name;          // Model name (for UI labels)
    public static string ThumbnailUrl;  // Best thumbnail URL
    public static string RemoteUrl;     // Direct .glb/.zip URL from Sketchfab

    // Quick status helper
    public static bool HasLocalFile =>
        !string.IsNullOrEmpty(ModelPath) && File.Exists(ModelPath);

    // ---- Helper setters (use wherever convenient) ----
    public static void SetFromSearch(string uid, string name, string thumbUrl)
    {
        Uid = uid;
        Name = name;
        ThumbnailUrl = thumbUrl;
    }

    public static void SetRemote(string remoteUrl)
    {
        RemoteUrl = remoteUrl;
    }

    public static void SetLocal(string localPath)
    {
        ModelPath = localPath;
    }

    public static void Clear()
    {
        Uid = null;
        Name = null;
        ThumbnailUrl = null;
        RemoteUrl = null;
        ModelPath = null;
    }
}
