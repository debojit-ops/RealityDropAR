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

    public GameObject thumbnailPrefab;
    public Transform resultsParent;

    private string apiToken;

    private const string DefaultToken = "74a53107840d4c52870087a8022d4a3c";

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

    public void SearchModels(string query)
    {
        if (string.IsNullOrEmpty(apiToken))
        {
            Debug.LogError("[SketchfabAPIManager] Cannot search: API token is not set.");
            return;
        }
        StartCoroutine(SearchCoroutine(query));
    }

    private IEnumerator SearchCoroutine(string query)
    {
        string url = "https://api.sketchfab.com/v3/search?type=models&q=" + UnityWebRequest.EscapeURL(query);

        UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Authorization", "Token " + apiToken);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[SketchfabAPIManager] Search failed (" + request.responseCode + "): " + request.error);
        }
        else
        {
            ParseResults(request.downloadHandler.text);
        }
    }

    private void ParseResults(string json)
    {
        JObject data = JObject.Parse(json);
        JArray results = (JArray)data["results"];

        foreach (Transform child in resultsParent)
            Destroy(child.gameObject);

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
    }

    private IEnumerator LoadThumbnail(string url, RawImage image)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (image)
                    image.texture = DownloadHandlerTexture.GetContent(request);
            }
            else
            {
                Debug.LogWarning("[SketchfabAPIManager] Thumbnail load failed: " + request.error);
            }
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
                PlayerPrefs.SetString("LastModelPath", glbPath);
                PlayerPrefs.Save();
                UnityEngine.SceneManagement.SceneManager.LoadScene("PreviewScene_new");
            }
        }));
    }
}
