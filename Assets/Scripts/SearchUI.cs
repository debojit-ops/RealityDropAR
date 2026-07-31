using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SearchUI : MonoBehaviour
{
    public TMP_InputField searchBar;
    public Button searchButton;
    public ScrollRect scrollRect;
    public SketchfabAPIManager api;

    [Header("Endless Scroll Configuration")]
    [Tooltip("Threshold ratio (0.0 = bottom, 1.0 = top) to trigger next page load.")]
    public float loadMoreThreshold = 0.15f;

    void Awake()
    {
        if (searchButton) searchButton.onClick.AddListener(OnSearchClicked);
        if (searchBar) searchBar.onSubmit.AddListener(OnSubmit);

        if (api == null) api = FindFirstObjectByType<SketchfabAPIManager>();
        if (scrollRect == null) scrollRect = GetComponentInChildren<ScrollRect>();

        if (scrollRect != null)
        {
            scrollRect.onValueChanged.AddListener(OnScrollValueChanged);
        }
    }

    void OnDestroy()
    {
        if (searchButton) searchButton.onClick.RemoveListener(OnSearchClicked);
        if (searchBar) searchBar.onSubmit.RemoveListener(OnSubmit);

        if (scrollRect != null)
        {
            scrollRect.onValueChanged.RemoveListener(OnScrollValueChanged);
        }
    }

    void OnSearchClicked()
    {
        if (api && searchBar)
        {
            api.SearchModels(searchBar.text);
            ResetScrollPosition();
        }
    }

    void OnSubmit(string text)
    {
        if (api)
        {
            api.SearchModels(text);
            ResetScrollPosition();
        }
    }

    private void OnScrollValueChanged(Vector2 scrollPos)
    {
        if (scrollRect == null || api == null) return;

        // When user scrolls down within loadMoreThreshold of the content bottom:
        if (scrollRect.verticalNormalizedPosition <= loadMoreThreshold)
        {
            api.FetchNextPage();
        }
    }

    private void ResetScrollPosition()
    {
        if (scrollRect != null)
        {
            scrollRect.verticalNormalizedPosition = 1f; // Scroll back to top
        }
    }

    public void OnThumbnailClicked(string modelPath)
    {
        Debug.Log("🖼️ Thumbnail clicked: " + modelPath);
        SelectedModel.ModelPath = modelPath;
        UnityEngine.SceneManagement.SceneManager.LoadScene("PreviewScene_new");
    }
}
