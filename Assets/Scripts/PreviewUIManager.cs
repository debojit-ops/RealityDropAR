// using UnityEngine;
// using UnityEngine.UI;

// public class PreviewUIManager : MonoBehaviour
// {
//     public ModelPreviewManager previewManager;
//     public PreviewControls previewControls;

//     public Button zoomInButton;
//     public Button zoomOutButton;
//     public Button resetButton;
//     public Toggle autoRotateToggle;

//     private void Start()
//     {
//         if (zoomInButton) zoomInButton.onClick.AddListener(() => OnZoomIn());
//         if (zoomOutButton) zoomOutButton.onClick.AddListener(() => OnZoomOut());
//         if (resetButton) resetButton.onClick.AddListener(() => OnReset());
//         if (autoRotateToggle) autoRotateToggle.onValueChanged.AddListener(OnToggleAutoRotate);
//     }

//     public void OnZoomIn()  { previewControls?.ZoomBy(-0.5f); }
//     public void OnZoomOut() { previewControls?.ZoomBy(0.5f); }

//     public void OnReset()
//     {
//         previewManager?.ResetView();
//         previewControls?.ResetDistance(3f);
//     }

//     public void OnToggleAutoRotate(bool on)
//     {
//         previewManager?.SetAutoRotate(on);
//     }
// }


// using UnityEngine;
// using UnityEngine.UI;
// using UnityEngine.SceneManagement;

// public class PreviewUIManager : MonoBehaviour
// {
//     public ModelPreviewManager previewManager;
//     public PreviewControls previewControls;

//     public Button zoomInButton;
//     public Button zoomOutButton;
//     public Button resetButton;
//     public Toggle autoRotateToggle;
//     public Button backButton;   // 🔹 new reference

//     private void Start()
//     {
//         if (zoomInButton) zoomInButton.onClick.AddListener(() => OnZoomIn());
//         if (zoomOutButton) zoomOutButton.onClick.AddListener(() => OnZoomOut());
//         if (resetButton) resetButton.onClick.AddListener(() => OnReset());
//         if (autoRotateToggle) autoRotateToggle.onValueChanged.AddListener(OnToggleAutoRotate);
//         if (backButton) backButton.onClick.AddListener(() => OnBack());
//     }

//     public void OnZoomIn()  { previewControls?.ZoomBy(-0.5f); }
//     public void OnZoomOut() { previewControls?.ZoomBy(0.5f); }

//     public void OnReset()
//     {
//         previewManager?.ResetView();
//         previewControls?.ResetDistance(3f);
//     }

//     public void OnToggleAutoRotate(bool on)
//     {
//         previewManager?.SetAutoRotate(on);
//     }

//     public void OnBack()
//     {
//         Debug.Log("Going back to SampleScene...");
//         SceneManager.LoadScene("SampleScene");  // 🔹 change to your search scene name
//     }
// }


using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class PreviewUIManager : MonoBehaviour
{
    public ModelPreviewManager previewManager;
    public PreviewControls previewControls;

    public Button zoomInButton;
    public Button zoomOutButton;
    public Button resetButton;
    public Toggle autoRotateToggle;
    public Button backButton;
    public Button viewInARButton;

    private void Start()
    {
        if (zoomInButton) zoomInButton.onClick.AddListener(OnZoomIn);
        if (zoomOutButton) zoomOutButton.onClick.AddListener(OnZoomOut);
        if (resetButton) resetButton.onClick.AddListener(OnReset);

        // ✅ Proper usage: Toggle gives us a bool
        if (autoRotateToggle) 
            autoRotateToggle.onValueChanged.AddListener(OnToggleAutoRotate);

        if (backButton) backButton.onClick.AddListener(OnBack);
        if (viewInARButton) viewInARButton.onClick.AddListener(OnViewInAR);
    }

    public void OnZoomIn()
    {
        previewControls?.ZoomBy(-0.5f);
    }

    public void OnZoomOut()
    {
        previewControls?.ZoomBy(0.5f);
    }

    public void OnReset()
    {
        previewManager?.ResetView();
        previewControls?.ResetDistance(3f);
    }

    // ✅ Use bool from toggle
    public void OnToggleAutoRotate(bool on)
    {
        if (on)
        {
            previewManager?.SetAutoRotate(true);
            Debug.Log("Auto-rotate ON");
        }
        else
        {
            previewManager?.SetAutoRotate(false);
            Debug.Log("Auto-rotate OFF");
        }
    }

    public void OnBack()
    {
        Debug.Log("Going back to SampleScene...");
        SceneManager.LoadScene("SampleScene");
    }

    public void OnViewInAR()
    {
        string path = PlayerPrefs.GetString("LastModelPath", "");
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            Debug.LogError("[PreviewUIManager] Cannot go to AR — file missing or path empty: '" + path + "'");
            return;
        }

        // Ensure bridge exists in scene
        if (ARModelBridge.Instance == null)
        {
            var bridgeGO = new GameObject("ARModelBridge");
            bridgeGO.AddComponent<ARModelBridge>();
        }

        // Store the already-loaded GltfImport so ARScene doesn't need to re-load
        if (previewManager != null && previewManager.LoadedGltf != null)
        {
            ARModelBridge.Instance.SetModel(previewManager.LoadedGltf, path);
            Debug.Log("[PreviewUIManager] GltfImport stored in bridge. Switching to ARScene.");
        }
        else
        {
            // Fallback: bridge will carry path only, ARScene will re-load
            ARModelBridge.Instance.SetModel(null, path);
            Debug.LogWarning("[PreviewUIManager] No GltfImport available — ARScene will re-load from path.");
        }

        SceneManager.LoadScene("ARScene");
    }
}
