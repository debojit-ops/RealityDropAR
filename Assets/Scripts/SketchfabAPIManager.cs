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
    [Tooltip("Hard cap on cards per search to protect mobile memory (each card holds a ~512px thumbnail). Applies even with loadInfiniteNoLimit.")]
    public int maxTotalResults = 240;
    [Tooltip("Placeholder cards shown while a search is loading.")]
    public int skeletonCount = 6;

    // Sketchfab's maximum page size for /v3/search
    private const int PageSize = 24;

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
    private int searchId = 0;

    private GameObject toastInstance;
    private TextMeshProUGUI toastText;
    private Coroutine hideMessageRoutine;
    private ResultCardView busyCard;
    private DownloadOverlay downloadOverlay;
    private int downloadToken = 0;
    private ulong lastDownloadBytes;

    void Awake()
    {
        apiToken = PlayerPrefs.GetString(TokenPrefKey, DefaultToken);

        // Results are always shown as a responsive 2-column grid
        ResultsGridLayout.Ensure(resultsParent);
    }

    void Start()
    {
        // Coming back from the preview screen: rebuild the same feed, query and scroll position
        if (SearchSession.PendingRestore && SearchSession.HasResults)
            StartCoroutine(RestoreSession());
    }

    private IEnumerator RestoreSession()
    {
        if (SearchSession.Query == SavedQuery) { ShowFavourites(); yield break; } // rebuilt fresh (likes may have changed)
        if (SearchSession.Query == RecentQuery) { ShowRecents(); yield break; }
        SearchSession.MarkRestored();
        currentQuery = SearchSession.Query;
        currentFilter = currentQuery == AnimatedQuery ? AnimatedFilter : null;
        nextPageUrl = SearchSession.NextPageUrl;
        pagesLeftToAutoLoad = 0; // more pages load as the user scrolls
        shownThisSearch = 0;
        searchId++;

        var searchUI = FindFirstObjectByType<SearchUI>();
        if (searchUI != null && searchUI.searchBar != null)
            searchUI.searchBar.text = IsQuickList(currentQuery) ? string.Empty : currentQuery; // also shows the clear (×) button

        ClearResults();
        int index = 0;
        foreach (var entry in SearchSession.Results)
        {
            SpawnResultCard(entry, Mathf.Min(index, 12)); // only the first rows animate in
            index++;
        }

        // Wait for the grid to size the cards, then jump back to where the user was
        yield return null;
        yield return null;
        Canvas.ForceUpdateCanvases();
        var scroll = resultsParent != null ? resultsParent.GetComponentInParent<ScrollRect>() : null;
        if (scroll != null)
        {
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = SearchSession.ScrollPosition;
        }
    }

    /// <summary>Creates one result card for a remembered/loaded result.</summary>
    private void SpawnResultCard(SearchSession.Entry entry, int appearIndex)
    {
        ResultCardView card = CreateCard();
        card.SetInfo(entry.Name, entry.Author);
        card.HiResThumbnailUrl = entry.HiResThumbnailUrl;
        card.SetAppearDelay(appearIndex * 0.035f); // staggered pop-in
        card.SetFavourite(new Favourites.Item
        {
            uid = entry.Uid, name = entry.Name, author = entry.Author,
            thumb = entry.ThumbnailUrl, hiRes = entry.HiResThumbnailUrl
        });

        var img = card.Thumbnail;
        if (img != null)
        {
            if (entry.Thumbnail != null) img.texture = entry.Thumbnail;
            else if (!string.IsNullOrEmpty(entry.ThumbnailUrl))
                // Visible cards first, max 4 at a time, paused during model downloads
                ThumbnailLoader.Load(entry.ThumbnailUrl, img, ResultsViewport(), tex => entry.Thumbnail = tex);
        }

        var btn = card.GetComponent<Button>();
        if (btn != null)
        {
            string capturedUid = entry.Uid, capturedThumb = entry.ThumbnailUrl;
            btn.onClick.AddListener(() => OnModelSelected(capturedUid, card, capturedThumb));
        }
        shownThisSearch++;
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

        StartSearch(query.Trim(), null, remember: true);
    }

    public const string AnimatedQuery = "Animated models", RecentQuery = "Recently viewed";
    const string AnimatedFilter = "animated=true&staffpicked=true";
    private string currentFilter;   // extra search parameters instead of a text query (quick actions)
    private string lastThumbUrl;

    /// <summary>True for the quick-action lists (their label is not typed into the search bar).</summary>
    public static bool IsQuickList(string query) => query == SavedQuery || query == AnimatedQuery || query == RecentQuery;

    public string CurrentQuery => currentQuery;

    /// <summary>Filters apply to text searches and the Animated list (not to Favourites / Recents).</summary>
    public bool CanFilter => !string.IsNullOrEmpty(currentQuery) && currentQuery != SavedQuery && currentQuery != RecentQuery;

    /// <summary>Runs the current search again with the filters from the filter sheet.</summary>
    public void ApplyFilters()
    {
        if (!CanFilter || string.IsNullOrEmpty(apiToken)) return;
        if (currentQuery == AnimatedQuery) SearchAnimated();
        else StartSearch(currentQuery, null, remember: false);
    }

    private string NoResultsMessage() => SearchFilters.ActiveCount > 0
        ? $"No models match these filters for \"{currentQuery}\". Try removing some."
        : $"No free downloadable models found for \"{currentQuery}\".";

    /// <summary>"Animated" quick action: Sketchfab staff-picked models that have animations.</summary>
    public void SearchAnimated()
    {
        if (string.IsNullOrEmpty(apiToken)) return;
        StartSearch(AnimatedQuery, AnimatedFilter, remember: false);
    }

    private string SearchUrl(string cursor = null)
    {
        string url = $"https://api.sketchfab.com/v3/search?type=models&downloadable=true&count={PageSize}&" +
                     (currentFilter ?? "q=" + UnityWebRequest.EscapeURL(currentQuery ?? ""));
        url += SearchFilters.ToQuery(); // sort / category / animated / staff picks / light models
        return cursor == null ? url : url + "&cursor=" + UnityWebRequest.EscapeURL(cursor);
    }

    private void StartSearch(string label, string filter, bool remember)
    {
        currentQuery = label;
        currentFilter = filter;
        nextPageUrl = null;
        // Load ONE extra page up front (more arrive as the user scrolls) — 5 pages at once flooded the network
        pagesLeftToAutoLoad = Mathf.Min(loadInfiniteNoLimit ? 9999 : autoLoadPages, MaxAutoLoadPages);
        shownThisSearch = 0;
        searchId++; // responses from older searches are ignored
        SearchSession.Begin(currentQuery);
        if (remember) RecommendationsPanel.RememberSearch(currentQuery); // personalizes the recommendations slideshow
        ClearResults();
        SpawnSkeletons(); // shimmering placeholder cards until the first page arrives

        string initialUrl = SearchUrl();
        StartCoroutine(FetchPageCoroutine(initialUrl, isNewSearch: true, searchId));
    }

    /// <summary>Called by Endless Scrolling (SearchUI) when scrolling near viewport bottom.</summary>
    public void FetchNextPage()
    {
        if (isFetchingPage || NetworkPriority.BackgroundPaused) return;

        if (string.IsNullOrEmpty(nextPageUrl))
        {
            Debug.Log("[SketchfabAPIManager] No more pages available.");
            return;
        }

        if (shownThisSearch >= maxTotalResults)
        {
            Debug.Log($"[SketchfabAPIManager] Reached maxTotalResults ({maxTotalResults}); not loading more.");
            return;
        }

        Debug.Log($"[SketchfabAPIManager] Fetching next page: {nextPageUrl}");
        StartCoroutine(FetchPageCoroutine(nextPageUrl, isNewSearch: false, searchId));
    }

    private IEnumerator FetchPageCoroutine(string url, bool isNewSearch, int requestSearchId)
    {
        isFetchingPage = true;
        string json = null;

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Authorization", "Token " + apiToken);

            yield return request.SendWebRequest();

            // A newer search started while this was loading: drop the result and leave the flag to the newer request
            if (requestSearchId != searchId) yield break;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[SketchfabAPIManager] Page request failed ({request.responseCode}): {request.error}");
                if (isNewSearch)
                {
                    ClearResults(); // remove skeleton cards
                    ShowMessage("Couldn't reach Sketchfab. Check your internet connection and try again.");
                }
            }
            else
            {
                json = request.downloadHandler.text;
            }
        }

        // Must be cleared BEFORE parsing: ParseResults chains FetchNextPage() for auto-loading,
        // which returns immediately while isFetchingPage is still true (this was why only 24 results ever appeared).
        isFetchingPage = false;

        if (json != null)
            ParseResults(json, isNewSearch);
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
            nextPageUrl = SearchUrl(cursorToken);
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
                ShowMessage(NoResultsMessage());
            return;
        }

        int skipped = 0;
        int indexInPage = 0;
        foreach (var model in results)
        {
            if (shownThisSearch >= maxTotalResults) break;

            // Safety net on top of the downloadable=true query filter:
            // never show a card the user can't actually download.
            if (!IsFreeDownloadable(model))
            {
                skipped++;
                continue;
            }

            string name = model.Value<string>("name") ?? "Untitled";
            string uid  = model.Value<string>("uid")  ?? string.Empty;
            string authorName = model["user"]?.Value<string>("displayName");
            if (string.IsNullOrWhiteSpace(authorName)) authorName = model["user"]?.Value<string>("username");

            var images = (JArray)model["thumbnails"]?["images"];
            // ~512px for the list (quality vs RAM: full size crashes phones with many cards),
            // ~1024px only for the big image on the download screen
            string thumbnailUrl = PickThumbnail(images, 512);
            string hiResThumbnailUrl = PickThumbnail(images, 1024);

            // Remember it (for Back from preview) and show a premium card; see ResultCardView
            var entry = SearchSession.Add(uid, name, authorName, thumbnailUrl, hiResThumbnailUrl);
            SpawnResultCard(entry, indexInPage);
            indexInPage++;
        }

        Debug.Log($"[SketchfabAPIManager] Loaded {results.Count - skipped} models ({skipped} skipped as not downloadable). Next page available: {!string.IsNullOrEmpty(nextPageUrl)}");

        bool hasNextPage = !string.IsNullOrEmpty(nextPageUrl) && shownThisSearch < maxTotalResults;

        // Automatically load the next page if we have auto-load remaining
        if (pagesLeftToAutoLoad > 0 && hasNextPage)
        {
            pagesLeftToAutoLoad--;
            FetchNextPage();
        }
        else if (shownThisSearch == 0 && !hasNextPage)
        {
            ShowMessage(NoResultsMessage());
        }
    }

    /// <summary>URL of the thumbnail whose width is closest to targetWidth (null if none).</summary>
    private static string PickThumbnail(JArray images, int targetWidth)
    {
        if (images == null || images.Count == 0) return null;
        int bestIndex = 0;
        int minDiff = int.MaxValue;
        for (int i = 0; i < images.Count; i++)
        {
            int w = images[i].Value<int?>("width") ?? -1;
            if (w <= 0) continue;
            int diff = Mathf.Abs(w - targetWidth);
            if (diff < minDiff)
            {
                minDiff = diff;
                bestIndex = i;
            }
        }
        string url = images[bestIndex].Value<string>("url");
        return string.IsNullOrEmpty(url) ? images.Last.Value<string>("url") : url;
    }

    /// <summary>Loads the sharper thumbnail for the download screen and hands it over (overlay owns it).</summary>
    private IEnumerator LoadOverlayThumbnail(string url, int token)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;
            Texture2D texture = DownloadHandlerTexture.GetContent(request);
            if (token == downloadToken && downloadOverlay != null)
                downloadOverlay.SetHiResThumbnail(texture);
            else
                Destroy(texture);
        }
    }

    private ResultCardView CreateCard()
    {
        GameObject item = Instantiate(thumbnailPrefab, resultsParent);
        var card = item.GetComponent<ResultCardView>();
        if (card == null) card = item.AddComponent<ResultCardView>();
        return card;
    }

    private void SpawnSkeletons()
    {
        if (thumbnailPrefab == null || resultsParent == null) return;
        for (int i = 0; i < skeletonCount; i++)
        {
            var card = CreateCard();
            card.SetSkeleton();
            card.SetAppearDelay(i * 0.04f);
        }
    }

    /// <summary>
    /// True if the search result is flagged as downloadable. Missing flag = trust the query filter.
    /// </summary>
    private static bool IsFreeDownloadable(JToken model)
    {
        bool? downloadable = model.Value<bool?>("isDownloadable");
        // Also never show adult / age-restricted models (see ContentFilter)
        return downloadable != false && ContentFilter.IsSafe(model);
    }

    private IEnumerator LoadThumbnail(string url, RawImage image, SearchSession.Entry entry = null)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                if (image != null)
                {
                    image.texture = texture;
                    if (entry != null) entry.Thumbnail = texture; // reused when coming back from preview
                }
                else
                    Destroy(texture); // card was removed (new search) while loading: don't leak GPU memory
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

    /// <summary>Clears the current search and its remembered feed (used by "back to recommendations").</summary>
    public void ResetSearch()
    {
        searchId++;             // ignore any page still loading
        currentQuery = null;
        nextPageUrl = null;
        shownThisSearch = 0;
        SearchSession.Begin(null);
        ClearResults();
    }

    public const string SavedQuery = "Saved models";

    /// <summary>Shows the user's saved (♥) models in the results grid. Back from a preview returns here.</summary>
    public void ShowFavourites() =>
        ShowList(SavedQuery, Favourites.All, "No saved models yet. Tap the heart on any model to save it.");

    /// <summary>"Recents" quick action: models the user opened lately, newest first.</summary>
    public void ShowRecents() =>
        ShowList(RecentQuery, RecentModels.All, "Nothing here yet. Models you open will show up here.");

    private void ShowList(string label, IReadOnlyList<Favourites.Item> items, string emptyMessage)
    {
        searchId++;             // ignore any page still loading
        currentQuery = label;
        currentFilter = null;
        nextPageUrl = null;
        pagesLeftToAutoLoad = 0;
        shownThisSearch = 0;
        SearchSession.Begin(currentQuery);
        ClearResults();
        int index = 0;
        foreach (var f in items)
        {
            var entry = SearchSession.Add(f.uid, f.name, f.author, f.thumb, string.IsNullOrEmpty(f.hiRes) ? f.thumb : f.hiRes);
            SpawnResultCard(entry, Mathf.Min(index, 12));
            index++;
        }
        if (index == 0) ShowMessage(emptyMessage);
    }

    private void OnModelSelected(string uid, ResultCardView card, string thumbUrl = null)
    {
        lastThumbUrl = thumbUrl;
        if (isDownloading) return; // ignore extra taps while a model is downloading
        busyCard = card;
        OpenModel(uid,
                  card != null ? card.TitleText : null,
                  card != null ? card.AuthorName : null,
                  card != null && card.Thumbnail != null ? card.Thumbnail.texture : null,
                  card != null ? card.HiResThumbnailUrl : null);
    }

    /// <summary>
    /// Downloads and opens a model (download screen → preview). Used by result cards and by the
    /// recommendations slideshow. Ignored while another model is downloading.
    /// </summary>
    public void OpenModel(string uid, string title, string author, Texture thumbnail, string hiResThumbnailUrl)
    {
        if (isDownloading || string.IsNullOrEmpty(uid)) return;
        // "Recents" quick action: newest first (small thumbnail when we have it: lighter for the grid)
        string small = string.IsNullOrEmpty(lastThumbUrl) ? hiResThumbnailUrl : lastThumbUrl;
        lastThumbUrl = null;
        RecentModels.Add(new Favourites.Item { uid = uid, name = title, author = author, thumb = small, hiRes = hiResThumbnailUrl });
        if (busyCard != null) busyCard.SetBusy(true); // card glows while its model downloads

        // Full-screen progress screen with the model's thumbnail, name and a live percentage
        var overlay = GetOrCreateOverlay();
        if (overlay != null) overlay.Show(title, author, thumbnail, CancelDownload);

        // The download gets the connection to itself: thumbnails, extra pages and recommendations pause
        NetworkPriority.PauseBackground();

        downloadToken++;
        pendingHiResUrl = hiResThumbnailUrl; // loaded after the download link (link first)
        StartCoroutine(FetchDownloadLink(uid, downloadToken, title, author));
    }

    private string pendingHiResUrl;

    private bool waitingForLink;

    /// <summary>Called by the overlay's Cancel button: aborts the download and returns to the list.</summary>
    private void CancelDownload()
    {
        downloadToken++; // makes the running download see itself as cancelled
        NetworkPriority.ResumeBackground();
        isDownloading = false;
        if (busyCard != null) busyCard.SetBusy(false);
        busyCard = null;
        if (downloadOverlay != null) downloadOverlay.Hide();
    }

    private const int MaxAutoLoadPages = 1;

    /// <summary>The visible area of the results list (used to load on-screen thumbnails first).</summary>
    private RectTransform ResultsViewport()
    {
        var scroll = resultsParent != null ? resultsParent.GetComponentInParent<ScrollRect>() : null;
        if (scroll == null) return null;
        return scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
    }

    private DownloadOverlay GetOrCreateOverlay()
    {
        if (downloadOverlay != null) return downloadOverlay;
        Canvas canvas = resultsParent != null ? resultsParent.GetComponentInParent<Canvas>() : null;
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return null;
        downloadOverlay = DownloadOverlay.Create(canvas.rootCanvas);
        return downloadOverlay;
    }

    private IEnumerator FetchDownloadLink(string uid, int token, string modelTitle = null, string modelAuthor = null)
    {
        isDownloading = true;
        bool Cancelled() => token != downloadToken;

        string url = $"https://api.sketchfab.com/v3/models/{uid}/download";
        string glbUrl = null;

        float linkStart = Time.realtimeSinceStartup;
        waitingForLink = true;
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Authorization", "Token " + apiToken);
            request.timeout = 15; // never sit on "Preparing" forever

            yield return request.SendWebRequest();
            waitingForLink = false;
            if (Cancelled()) yield break;
            Debug.Log($"[Download] Link request took {Time.realtimeSinceStartup - linkStart:0.00}s (HTTP {request.responseCode}).");

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[SketchfabAPIManager] Download link fetch failed (" + request.responseCode + "): " + request.error);
                bool timedOut = request.error != null && request.error.ToLowerInvariant().Contains("timeout");
                FailDownload(request.responseCode == 401 || request.responseCode == 403 || request.responseCode == 404
                    ? "This model isn't available for download. Please try another one."
                    : timedOut ? "Sketchfab is taking too long to respond. Please try again."
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

        // Link is in hand: now fetch the sharper overlay picture (small) alongside the model file
        if (!string.IsNullOrEmpty(pendingHiResUrl))
            StartCoroutine(LoadOverlayThumbnail(pendingHiResUrl, token));

        string saveFolder = Path.Combine(Application.persistentDataPath, "Models");
        string resultPath = null;
        bool finished = false;
        float fileStart = Time.realtimeSinceStartup;

        yield return ModelDownloader.DownloadAndExtractGLB(glbUrl, saveFolder,
            onComplete: glbPath => { resultPath = glbPath; finished = true; },
            onProgress: (progress, downloaded, total) =>
            {
                lastDownloadBytes = total > 0 ? total : downloaded;
                if (downloadOverlay != null) downloadOverlay.SetProgress(progress, downloaded, total);
            },
            isCancelled: Cancelled,
            onStage: stage =>
            {
                if (downloadOverlay == null) return;
                downloadOverlay.SetStage(stage == "unpacking" ? "Unpacking model…" : "Downloading…");
            });

        if (Cancelled() || !finished) yield break;
        Debug.Log($"[Download] Model file + unzip took {Time.realtimeSinceStartup - fileStart:0.00}s ({lastDownloadBytes / 1048576f:0.0} MB).");

        if (string.IsNullOrEmpty(resultPath))
        {
            FailDownload("Download failed. Please try again or pick another model.");
            yield break;
        }

        // New model selected — clear the cached instance so AR re-loads fresh
        ModelLoader.ClearCache();
        PlayerPrefs.SetString("LastModelPath", resultPath);
        // Shown on the preview screen (title, author credit, size)
        PlayerPrefs.SetString("LastModelTitle", string.IsNullOrEmpty(modelTitle) ? "3D model" : modelTitle);
        PlayerPrefs.SetString("LastModelAuthor", modelAuthor ?? string.Empty);
        PlayerPrefs.SetString("LastModelSizeBytes", lastDownloadBytes.ToString());
        PlayerPrefs.SetString("LastModelUid", uid);                     // ♥ on the preview screen
        PlayerPrefs.SetString("LastModelThumb", pendingHiResUrl ?? string.Empty);
        PlayerPrefs.Save();

        if (downloadOverlay != null) downloadOverlay.SetOpening();
        yield return new WaitForSecondsRealtime(0.35f); // let the bar visibly reach 100%

        // Remember the feed so Back from the preview returns to the same results and scroll position
        var scroll = resultsParent != null ? resultsParent.GetComponentInParent<ScrollRect>() : null;
        SearchSession.SaveForReturn(nextPageUrl, scroll != null ? scroll.verticalNormalizedPosition : 1f);

        // Async load keeps the progress screen animating while the preview scene loads
        var load = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("PreviewScene_new");
        isDownloading = false;
        yield return load;
    }

    private void FailDownload(string userMessage)
    {
        NetworkPriority.ResumeBackground();
        isDownloading = false;
        if (busyCard != null) busyCard.SetBusy(false);
        busyCard = null;
        if (downloadOverlay != null) downloadOverlay.Hide();
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
        rect.anchorMin = new Vector2(0.12f, 0.045f);
        rect.anchorMax = new Vector2(0.88f, 0.045f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(0f, 120f);

        // Dark glass pill with a thin cyan border, matching the cards
        var bg = toastInstance.GetComponent<Image>();
        bg.color = new Color(0.05f, 0.07f, 0.10f, 0.94f);
        bg.raycastTarget = false;
        var shape = toastInstance.AddComponent<UIRoundedCorners>();
        shape.SetRadius(9999f);
        shape.SetBorder(2f);

        var textGO = new GameObject("Text", typeof(RectTransform));
        var textRect = (RectTransform)textGO.transform;
        textRect.SetParent(rect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(48f, 12f);
        textRect.offsetMax = new Vector2(-48f, -12f);

        toastText = textGO.AddComponent<TextMeshProUGUI>();
        toastText.alignment = TextAlignmentOptions.Center;
        toastText.enableAutoSizing = true;
        toastText.fontSizeMin = 18f;
        toastText.fontSizeMax = 38f;
        UIFonts.Apply(toastText, UIFonts.SemiBold);
        toastText.color = new Color(0.90f, 0.96f, 1f, 1f);
        toastText.raycastTarget = false;

        toastInstance.SetActive(false);
        return toastText;
    }

    #endregion
}
