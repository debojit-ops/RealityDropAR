using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Networking;

public static class ModelDownloader
{
    /// <summary>Abort if no bytes arrive for this long (connection stalled).</summary>
    public const float StallTimeoutSeconds = 20f;

    /// <summary>
    /// Downloads a model (zip or glb), extracts if needed, and returns the .glb path (null on failure).
    /// onProgress(progress 0..1 or -1 if size unknown, bytesDownloaded, totalBytes or 0) is called every frame.
    /// onStage("downloading" / "unpacking") reports the current step.
    /// If isCancelled() returns true the request is aborted and onComplete is NOT called.
    /// </summary>
    public static IEnumerator DownloadAndExtractGLB(
        string downloadUrl,
        string saveFolder,
        Action<string> onComplete,
        Action<float, ulong, ulong> onProgress = null,
        Func<bool> isCancelled = null,
        Action<string> onStage = null)
    {
        if (!Directory.Exists(saveFolder))
            Directory.CreateDirectory(saveFolder);

        // --- Step 1: Clean the URL and figure out extension ---
        string cleanUrl = downloadUrl.Split('?')[0]; // strip query params
        string extension = Path.GetExtension(cleanUrl).ToLower();
        if (string.IsNullOrEmpty(extension))
            extension = ".glb"; // fallback if missing

        string localPath = Path.Combine(saveFolder, "downloadedFile" + extension);

        // --- Step 2: Download file, reporting progress every frame ---
        onStage?.Invoke("downloading");
        using (UnityWebRequest request = UnityWebRequest.Get(downloadUrl))
        {
            request.downloadHandler = new DownloadHandlerFile(localPath);
            var operation = request.SendWebRequest();
            ulong totalBytes = 0;
            ulong lastBytes = 0;
            float lastProgressTime = Time.realtimeSinceStartup;

            while (!operation.isDone)
            {
                if (isCancelled != null && isCancelled())
                {
                    request.Abort();
                    Debug.Log("[ModelDownloader] Download cancelled by user.");
                    yield break;
                }

                if (totalBytes == 0)
                    ulong.TryParse(request.GetResponseHeader("Content-Length"), out totalBytes);

                ulong downloaded = request.downloadedBytes;

                // Stall guard: no new bytes for 20 s → give up with a clear error instead of hanging
                if (downloaded != lastBytes) { lastBytes = downloaded; lastProgressTime = Time.realtimeSinceStartup; }
                else if (Time.realtimeSinceStartup - lastProgressTime > StallTimeoutSeconds)
                {
                    request.Abort();
                    Debug.LogError($"[ModelDownloader] Download stalled (no data for {StallTimeoutSeconds:0}s at {downloaded} bytes).");
                    onComplete?.Invoke(null);
                    yield break;
                }

                float progress = totalBytes > 0 ? Mathf.Clamp01((float)((double)downloaded / totalBytes)) : -1f;
                onProgress?.Invoke(progress, downloaded, totalBytes);
                yield return null;
            }

            if (isCancelled != null && isCancelled()) yield break;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("❌ Download failed: " + request.error);
                onComplete?.Invoke(null);
                yield break;
            }

            onProgress?.Invoke(1f, request.downloadedBytes, totalBytes > 0 ? totalBytes : request.downloadedBytes);
        }

        // --- Step 3: Log debug info ---
        FileInfo fi = new FileInfo(localPath);
        Debug.Log($"✅ Download complete: {localPath} ({fi.Length} bytes)");

        // --- Step 4: Handle file type ---
        if (extension == ".zip")
        {
            onStage?.Invoke("unpacking");
            yield return null; // let the UI show the "unpacking" step before the blocking extract

            string extractPath = Path.Combine(saveFolder, "Extracted");
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);

            try
            {
                ZipFile.ExtractToDirectory(localPath, extractPath);
            }
            catch (Exception ex)
            {
                Debug.LogError("❌ Failed to unzip file: " + ex.Message);
                onComplete?.Invoke(null);
                yield break;
            }

            // Look for .glb inside
            string[] glbFiles = Directory.GetFiles(extractPath, "*.glb", SearchOption.AllDirectories);
            if (glbFiles.Length > 0)
            {
                Debug.Log("🎯 Found GLB inside zip: " + glbFiles[0]);
                onComplete?.Invoke(glbFiles[0]);
                yield break;
            }
            else
            {
                Debug.LogError("❌ No .glb file found in zip archive.");
                onComplete?.Invoke(null);
                yield break;
            }
        }
        else if (extension == ".glb")
        {
            Debug.Log("🎯 Direct GLB downloaded: " + localPath);
            onComplete?.Invoke(localPath);
            yield break;
        }
        else
        {
            Debug.LogWarning("⚠ Unknown file type downloaded: " + extension);
            onComplete?.Invoke(null);
            yield break;
        }
    }
}
