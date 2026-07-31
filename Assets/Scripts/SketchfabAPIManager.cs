using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using Newtonsoft.Json.Linq;

public class SketchfabAPIManager : MonoBehaviour
{
    private const string TokenPrefKey = "SketchfabApiToken";
    private const string DefaultToken = "74a53107840d4c52870087a8022d4a3c";

    [Header("UI Hierarchy")]
    public GameObject thumbnailPrefab;
    public Transform resultsParent;

    private string apiToken;
    private string nextPageUrl = null;
    private bool isFetchingPage = false;

    void Awake()
    {
        apiToken = PlayerPrefs.GetString(TokenPrefKey, DefaultToken);
    }

    /// <summary>Call from a settings UI input field to store the token once.</summary>
    public void SetApiToken(string token)
    {
        apiToken = token.Trim();
        PlayerPrefs.SetString(TokenPrefKey, apiToken);
        PlayerPrefs.Save();
        Debug.Log("[SketchfabAPIManager] API token saved.");
    }

    public string GetApiToken() => apiToken;

    /// <summary>Start a fresh search query. Clears existing results and resets pagination.</summary>
    public void SearchModels(string query)
    {
        if (string.IsNullOrEmpty(apiToken))
        {
            Debug.LogError("[SketchfabAPIManager] Cannot search: API token is not set.");
            return;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            Debug.LogWarning("[SketchfabAPIManager] Empty search query.");
            return;
        }

        nextPageUrl = null;
        ClearResults();
        
        string initialUrl = "https://api.sketchfab.com/v3/search?type=models&q=" + UnityWebRequest.EscapeURL(query);
        StartCoroutine(FetchPageCoroutine(initialUrl, isNewSearch: true));
    }

    /// <summary>Called by Endless Scrolling (SearchUI) when scrolling near viewport bottom.</summary>
    public void FetchNextPage()
    {
        if (isFetchingPage) return;

        if (string.IsNullOrEmpty(nextPageUrl))
        {
            Debug.Log("[SketchfabAPIManager] No more pages available.");
            return;
        }

        Debug.Log($"[SketchfabAPIManager] Fetching next page: {nextPageUrl}");
        StartCoroutine(FetchPageCoroutine(nextPageUrl, isNewSearch: false));
    }

    private IEnumerator FetchPageCoroutine(string url, bool isNewSearch)
    {
        isFetchingPage = true;

        UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Authorization", "Token " + apiToken);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[SketchfabAPIManager] Page request failed ({request.responseCode}): {request.error}");
        }
        else
        {
            ParseResults(request.downloadHandler.text, isNewSearch);
        }

        isFetchingPage = false;
    }

    private void ParseResults(string json, bool isNewSearch)
    {
        JObject data = JObject.Parse(json);
        
        // Extract next page cursor URL if available
        nextPageUrl = data["next"]?.ToString() ?? data["cursors"]?["next"]?.ToString();

        JArray results = (JArray)data["results"];

        if (isNewSearch)
        {
            ClearResults();
        }

        if (results == null || results.Count == 0)
        {
            Debug.Log("[SketchfabAPIManager] No models found.");
            return;
        }

        foreach (var model in results)
        {
            string name = model.Value<string>("name") ?? "Untitled";
            string uid  = model.Value<string>("uid")  ?? string.Empty;

            string thumbnailUrl = null;
            var images = (JArray)model["thumbnails"]?["images"];
            if (images != null && images.Count > 0)
            {
                int bestIndex = 0, bestWidth = -1;
                for (int i = 0; i < images.Count; i++)
                {
                    int w = images[i].Value<int?>("width") ?? -1;
                    if (w > bestWidth) { bestWidth = w; bestIndex = i; }
                }
                thumbnailUrl = images[bestIndex].Value<string>("url");
                if (string.IsNullOrEmpty(thumbnailUrl))
                    thumbnailUrl = images.Last.Value<string>("url");
            }

            GameObject item = Instantiate(thumbnailPrefab, resultsParent);

            var title = item.GetComponentInChildren<TextMeshProUGUI>(true);
            if (title) title.text = name;

            var img = item.GetComponentInChildren<RawImage>(true);
            if (!string.IsNullOrEmpty(thumbnailUrl) && img)
                StartCoroutine(LoadThumbnail(thumbnailUrl, img));

            var btn = item.GetComponent<Button>();
            if (btn != null)
            {
                string capturedUid = uid;
                btn.onClick.AddListener(() => OnModelSelected(capturedUid));
            }
        }

        Debug.Log($"[SketchfabAPIManager] Loaded {results.Count} models. Next page available: {!string.IsNullOrEmpty(nextPageUrl)}");
    }

    private IEnumerator LoadThumbnail(string url, RawImage image)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (image != null)
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(request);
                    image.texture = texture;
                }
            }
            else
            {
                Debug.LogWarning("[SketchfabAPIManager] Thumbnail load failed: " + request.error);
            }
        }
    }

    private void ClearResults()
    {
        if (resultsParent == null) return;

        foreach (Transform child in resultsParent)
        {
            var rawImg = child.GetComponentInChildren<RawImage>(true);
            if (rawImg != null && rawImg.texture != null)
            {
                // Explicitly free GPU texture memory to prevent OOM on mobile
                Destroy(rawImg.texture);
                rawImg.texture = null;
            }
            Destroy(child.gameObject);
        }
    }

    private void OnModelSelected(string uid)
    {
        StartCoroutine(FetchDownloadLink(uid));
    }

    private IEnumerator FetchDownloadLink(string uid)
    {
        string url = $"https://api.sketchfab.com/v3/models/{uid}/download";

        UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Authorization", "Token " + apiToken);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[SketchfabAPIManager] Download link fetch failed (" + request.responseCode + "): " + request.error);
            yield break;
        }

        var data   = JObject.Parse(request.downloadHandler.text);
        string glbUrl = data["glb"]?["url"]?.ToString();

        if (string.IsNullOrEmpty(glbUrl))
        {
            Debug.LogError("[SketchfabAPIManager] No GLB URL in response for model " + uid);
            yield break;
        }

        string saveFolder = Path.Combine(Application.persistentDataPath, "Models");
        StartCoroutine(ModelDownloader.DownloadAndExtractGLB(glbUrl, saveFolder, (glbPath) =>
        {
            if (!string.IsNullOrEmpty(glbPath))
            {
                // New model selected — clear the cached instance so AR re-loads fresh
                ModelLoader.ClearCache();
                PlayerPrefs.SetString("LastModelPath", glbPath);
                PlayerPrefs.Save();
                UnityEngine.SceneManagement.SceneManager.LoadScene("PreviewScene_new");
            }
        }));
    }
}
