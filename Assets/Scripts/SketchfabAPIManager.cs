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

    [Header("Loading Settings")]
    [Tooltip("How many extra pages to load automatically when searching (e.g. 4 pages = ~120 models total upfront)")]
    public int autoLoadPages = 4;
    [Tooltip("If true, it will attempt to load EVERY result Sketchfab has. WARNING: High risk of crashing mobile devices out of memory!")]
    public bool loadInfiniteNoLimit = false;

    [Header("User Messages")]
    [Tooltip("Optional text for status/error messages. If empty, a temporary toast is created on the results' canvas.")]
    public TextMeshProUGUI statusText;
    [Tooltip("Seconds a message stays visible.")]
    public float messageDuration = 3f;

    private string apiToken;
    private string currentQuery = null;
    private string nextPageUrl = null;
    private bool isFetchingPage = false;
    private bool isDownloading = false;
    private int pagesLeftToAutoLoad = 0;
    private int shownThisSearch = 0;

    private GameObject toastInstance;
    private TextMeshProUGUI toastText;
    private Coroutine hideMessageRoutine;

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

        currentQuery = query.Trim();
        nextPageUrl = null;
        pagesLeftToAutoLoad = loadInfiniteNoLimit ? 9999 : autoLoadPages;
        shownThisSearch = 0;
        ClearResults();
        
        string initialUrl = $"https://api.sketchfab.com/v3/search?type=models&downloadable=true&count=24&q={UnityWebRequest.EscapeURL(currentQuery)}";
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

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Authorization", "Token " + apiToken);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[SketchfabAPIManager] Page request failed ({request.responseCode}): {request.error}");
                if (isNewSearch)
                    ShowMessage("Couldn't reach Sketchfab. Check your internet connection and try again.");
            }
            else
            {
                ParseResults(request.downloadHandler.text, isNewSearch);
            }
        }

        isFetchingPage = false;
    }

    private void ParseResults(string json, bool isNewSearch)
    {
        JObject data = JObject.Parse(json);
        
        // Extract next page cursor URL if available
        string rawNext = data["next"]?.ToString();
        string cursorToken = data["cursors"]?["next"]?.ToString();

        if (!string.IsNullOrEmpty(rawNext) && rawNext.StartsWith("http", System.StringComparison.OrdinalIgnoreCase))
        {
            nextPageUrl = rawNext;
        }
        else if (!string.IsNullOrEmpty(cursorToken))
        {
            nextPageUrl = $"https://api.sketchfab.com/v3/search?type=models&downloadable=true&count=24&q={UnityWebRequest.EscapeURL(currentQuery ?? "")}&cursor={UnityWebRequest.EscapeURL(cursorToken)}";
        }
        else
        {
            nextPageUrl = null;
        }

        JArray results = (JArray)data["results"];

        if (isNewSearch)
        {
            ClearResults();
        }

        if (results == null || results.Count == 0)
        {
            Debug.Log("[SketchfabAPIManager] No models found.");
            if (shownThisSearch == 0)
                ShowMessage($"No free downloadable models found for \"{currentQuery}\".");
            return;
        }

        int skipped = 0;
        foreach (var model in results)
        {
            // Safety net on top of the downloadable=true query filter:
            // never show a card the user can't actually download.
            if (!IsFreeDownloadable(model))
            {
                skipped++;
                continue;
            }

            string name = model.Value<string>("name") ?? "Untitled";
            string uid  = model.Value<string>("uid")  ?? string.Empty;

            string thumbnailUrl = null;
            var images = (JArray)model["thumbnails"]?["images"];
            if (images != null && images.Count > 0)
            {
                int bestIndex = 0;
                int minDiff = int.MaxValue;
                // Target ~512px width for a good balance of quality and RAM
                // (Using the maximum size will crash the app when loading "no limit" cards)
                for (int i = 0; i < images.Count; i++)
                {
                    int w = images[i].Value<int?>("width") ?? -1;
                    if (w > 0)
                    {
                        int diff = Mathf.Abs(w - 512);
                        if (diff < minDiff)
                        {
                            minDiff = diff;
                            bestIndex = i;
                        }
                    }
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
            shownThisSearch++;
        }

        Debug.Log($"[SketchfabAPIManager] Loaded {results.Count - skipped} models ({skipped} skipped as not downloadable). Next page available: {!string.IsNullOrEmpty(nextPageUrl)}");

        bool hasNextPage = !string.IsNullOrEmpty(nextPageUrl);

        // Automatically load the next page if we have auto-load remaining
        if (pagesLeftToAutoLoad > 0 && hasNextPage)
        {
            pagesLeftToAutoLoad--;
            FetchNextPage();
        }
        else if (shownThisSearch == 0 && !hasNextPage)
        {
            ShowMessage($"No free downloadable models found for \"{currentQuery}\".");
        }
    }

    /// <summary>
    /// True if the search result is flagged as downloadable. Missing flag = trust the query filter.
    /// </summary>
    private static bool IsFreeDownloadable(JToken model)
    {
        bool? downloadable = model.Value<bool?>("isDownloadable");
        return downloadable != false;
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
        // Ignore extra taps while a model is already downloading
        if (isDownloading) return;
        StartCoroutine(FetchDownloadLink(uid));
    }

    private IEnumerator FetchDownloadLink(string uid)
    {
        isDownloading = true;
        ShowMessage("Downloading model...", autoHide: false);

        string url = $"https://api.sketchfab.com/v3/models/{uid}/download";
        string glbUrl = null;

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Authorization", "Token " + apiToken);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[SketchfabAPIManager] Download link fetch failed (" + request.responseCode + "): " + request.error);
                FailDownload(request.responseCode == 401 || request.responseCode == 403 || request.responseCode == 404
                    ? "This model isn't available for download. Please try another one."
                    : "Couldn't reach Sketchfab. Check your internet connection and try again.");
                yield break;
            }

            try
            {
                var data = JObject.Parse(request.downloadHandler.text);
                glbUrl = data["glb"]?["url"]?.ToString();
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[SketchfabAPIManager] Could not parse download response: " + ex.Message);
            }
        }

        if (string.IsNullOrEmpty(glbUrl))
        {
            Debug.LogError("[SketchfabAPIManager] No GLB URL in response for model " + uid);
            FailDownload("This model isn't available in a supported format. Please try another one.");
            yield break;
        }

        string saveFolder = Path.Combine(Application.persistentDataPath, "Models");
        yield return ModelDownloader.DownloadAndExtractGLB(glbUrl, saveFolder, (glbPath) =>
        {
            if (string.IsNullOrEmpty(glbPath))
            {
                FailDownload("Download failed. Please try again or pick another model.");
                return;
            }

            // New model selected — clear the cached instance so AR re-loads fresh
            ModelLoader.ClearCache();
            PlayerPrefs.SetString("LastModelPath", glbPath);
            PlayerPrefs.Save();
            isDownloading = false;
            UnityEngine.SceneManagement.SceneManager.LoadScene("PreviewScene_new");
        });
    }

    private void FailDownload(string userMessage)
    {
        isDownloading = false;
        ShowMessage(userMessage);
    }

    #region User Messages

    /// <summary>Shows a short message to the user (status text if assigned, otherwise a toast).</summary>
    private void ShowMessage(string message, bool autoHide = true)
    {
        TextMeshProUGUI target = statusText != null ? statusText : GetOrCreateToast();
        if (target == null)
        {
            Debug.Log("[SketchfabAPIManager] " + message);
            return;
        }

        target.text = message;
        target.gameObject.SetActive(true);
        if (statusText == null && toastInstance != null)
        {
            toastInstance.SetActive(true);
            toastInstance.transform.SetAsLastSibling();
        }

        if (hideMessageRoutine != null) StopCoroutine(hideMessageRoutine);
        hideMessageRoutine = autoHide ? StartCoroutine(HideMessageAfterDelay()) : null;
    }

    private IEnumerator HideMessageAfterDelay()
    {
        yield return new WaitForSeconds(messageDuration);
        if (statusText != null) statusText.text = string.Empty;
        if (toastInstance != null) toastInstance.SetActive(false);
        hideMessageRoutine = null;
    }

    private TextMeshProUGUI GetOrCreateToast()
    {
        if (toastText != null) return toastText;

        Canvas canvas = resultsParent != null ? resultsParent.GetComponentInParent<Canvas>() : null;
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return null;
        canvas = canvas.rootCanvas;

        toastInstance = new GameObject("MessageToast", typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)toastInstance.transform;
        rect.SetParent(canvas.transform, false);
        rect.anchorMin = new Vector2(0.05f, 0.04f);
        rect.anchorMax = new Vector2(0.95f, 0.04f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(0f, 140f);

        var bg = toastInstance.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.8f);
        bg.raycastTarget = false;

        var textGO = new GameObject("Text", typeof(RectTransform));
        var textRect = (RectTransform)textGO.transform;
        textRect.SetParent(rect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(24f, 12f);
        textRect.offsetMax = new Vector2(-24f, -12f);

        toastText = textGO.AddComponent<TextMeshProUGUI>();
        toastText.alignment = TextAlignmentOptions.Center;
        toastText.enableAutoSizing = true;
        toastText.fontSizeMin = 18f;
        toastText.fontSizeMax = 40f;
        toastText.color = Color.white;
        toastText.raycastTarget = false;

        toastInstance.SetActive(false);
        return toastText;
    }

    #endregion
}
