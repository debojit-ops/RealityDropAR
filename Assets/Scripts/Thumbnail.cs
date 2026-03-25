
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using System.Collections;

public class Thumbnail : MonoBehaviour
{
    [Header("UI Components")]
    public RawImage thumbnailImage; 
    public TMP_Text titleText;
    public Button button; // 👈 attach the Button component from your prefab root

    private string modelUid;     
    private string thumbnailUrl; 
    private string glbUrl;       // 👈 Direct .glb download link

    /// <summary>
    /// Called by SketchfabAPIManager when spawning the thumbnail prefab
    /// </summary>
    public void Setup(string uid, string title, string imageUrl, string glbDownloadUrl)
    {
        modelUid = uid;
        thumbnailUrl = imageUrl;
        glbUrl = glbDownloadUrl;

        if (titleText != null)
            titleText.text = title;

        if (!string.IsNullOrEmpty(imageUrl))
            StartCoroutine(LoadThumbnail(imageUrl));

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClicked);
        }
    }

    private IEnumerator LoadThumbnail(string url)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (thumbnailImage != null)
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(request);
                    thumbnailImage.texture = texture;
                    Debug.Log("✅ Thumbnail loaded successfully");
                }
            }
            else
            {
                Debug.LogWarning("⚠️ Failed to load thumbnail: " + url + " Error: " + request.error);
            }
        }
    }

    private void OnClicked()
    {
        Debug.Log($"🖱️ Thumbnail clicked for model UID: {modelUid}");

        // Save GLB path globally so PreviewScene can load it
        SelectedModel.ModelPath = glbUrl;

        // 👉 Switch to PreviewScene
        UnityEngine.SceneManagement.SceneManager.LoadScene("PreviewScene_new");
    }
}
