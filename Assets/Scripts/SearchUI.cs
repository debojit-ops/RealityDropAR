
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SearchUI : MonoBehaviour
{
    public TMP_InputField searchBar;
    public Button searchButton;
    public SketchfabAPIManager api; // Drag your SketchfabAPIManager here in Inspector

    void Awake()
    {
        if (searchButton) searchButton.onClick.AddListener(OnSearchClicked);
        if (searchBar) searchBar.onSubmit.AddListener(OnSubmit);
    }

    void OnDestroy()
    {
        if (searchButton) searchButton.onClick.RemoveListener(OnSearchClicked);
        if (searchBar) searchBar.onSubmit.RemoveListener(OnSubmit);
    }

    void OnSearchClicked()
    {
        if (api && searchBar)
        {
            api.SearchModels(searchBar.text);
        }
    }

    void OnSubmit(string text)
    {
        if (api)
        {
            api.SearchModels(text);
        }
    }

    // 🟢 Step 6 — Handle when a thumbnail is clicked
    // This will be called by your ThumbnailItem buttons
    public void OnThumbnailClicked(string modelPath)
    {
        Debug.Log("🖼️ Thumbnail clicked: " + modelPath);

        // Save chosen model globally
        SelectedModel.ModelPath = modelPath;

        // Load preview scene
        UnityEngine.SceneManagement.SceneManager.LoadScene("PreviewScene_new");
    }
}
