using System.IO;
using UnityEngine;

public class ModelLoader : MonoBehaviour
{
    public GameObject LastLoadedModel { get; private set; }

    // Called by ARSceneInitializer when instantiating via ARModelBridge
    public void SetLastLoadedModel(GameObject model)
    {
        LastLoadedModel = model;
    }

    // Called by ARSceneInitializer when it instantiates via the bridge
    public void SetLastLoadedModel(GameObject model)
    {
        LastLoadedModel = model;
    }

    // Static cache — survives scene transitions so the same GLB is never parsed twice
    private static string _cachedPath;
    private static GameObject _cachedModel;

    public async void LoadModelFromLocalPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            Debug.LogError("[ModelLoader] Invalid file path (null or empty).");
            return;
        }

        if (!File.Exists(filePath))
        {
            Debug.LogError("[ModelLoader] File does not exist: " + filePath);
            return;
        }

        // Re-use cached model if the path hasn't changed and the GameObject is still alive
        if (filePath == _cachedPath && _cachedModel != null && _cachedModel.scene.IsValid())
        {
            Debug.Log("[ModelLoader] Using cached model for: " + Path.GetFileName(filePath));
            LastLoadedModel = _cachedModel;
            return;
        }

        Debug.Log("[ModelLoader] Loading: " + filePath);

        var (loaded, gltf) = await GltfLoader.Load(filePath);

        if (!loaded)
        {
            Debug.LogError("[ModelLoader] Failed to load model with glTFast.");
            return;
        }

        GameObject container = new GameObject("LoadedModel");
        bool instantiated = await gltf.InstantiateMainSceneAsync(container.transform);

        if (!instantiated)
        {
            Debug.LogError("[ModelLoader] Failed to instantiate glTF scene.");
            Destroy(container);
            return;
        }

        if (LastLoadedModel != null && LastLoadedModel != _cachedModel)
            Destroy(LastLoadedModel);

        LastLoadedModel = container;
        _cachedPath = filePath;
        _cachedModel = container;
        Debug.Log("[ModelLoader] Successfully loaded and cached: " + Path.GetFileName(filePath));
    }

    // Call this when the user picks a new model so the old cache is cleared
    public static void ClearCache()
    {
        if (_cachedModel != null)
            Destroy(_cachedModel);
        _cachedModel = null;
        _cachedPath = null;
    }
}
