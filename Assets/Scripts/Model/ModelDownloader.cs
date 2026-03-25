
using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Networking;

public static class ModelDownloader
{
    /// <summary>
    /// Downloads a model (zip or glb), extracts if needed, and returns the .glb path.
    /// </summary>
    public static IEnumerator DownloadAndExtractGLB(string downloadUrl, string saveFolder, Action<string> onComplete)
    {
        if (!Directory.Exists(saveFolder))
            Directory.CreateDirectory(saveFolder);

        // --- Step 1: Clean the URL and figure out extension ---
        string cleanUrl = downloadUrl.Split('?')[0]; // strip query params
        string extension = Path.GetExtension(cleanUrl).ToLower();
        if (string.IsNullOrEmpty(extension))
            extension = ".glb"; // fallback if missing

        string localPath = Path.Combine(saveFolder, "downloadedFile" + extension);

        // --- Step 2: Download file ---
        using (UnityWebRequest request = UnityWebRequest.Get(downloadUrl))
        {
            request.downloadHandler = new DownloadHandlerFile(localPath);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("❌ Download failed: " + request.error);
                onComplete?.Invoke(null);
                yield break;
            }
        }

        // --- Step 3: Log debug info ---
        FileInfo fi = new FileInfo(localPath);
        Debug.Log($"✅ Download complete: {localPath} ({fi.Length} bytes)");

        // --- Step 4: Handle file type ---
        if (extension == ".zip")
        {
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
