using System.IO;
using UnityEngine;

public class ModelLoader : MonoBehaviour
{
    public GameObject LastLoadedModel { get; private set; }

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

        if (LastLoadedModel != null)
            Destroy(LastLoadedModel);

        LastLoadedModel = container;
        Debug.Log("[ModelLoader] Successfully loaded: " + Path.GetFileName(filePath));
    }
}
