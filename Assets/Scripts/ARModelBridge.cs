using UnityEngine;
using GLTFast;

/// <summary>
/// Singleton that survives scene transitions.
/// PreviewScene stores the loaded GltfImport here before switching to ARScene.
/// ARSceneInitializer reads it to instantiate the model without re-loading.
/// </summary>
public class ARModelBridge : MonoBehaviour
{
    public static ARModelBridge Instance { get; private set; }

    // The loaded glTFast importer — set by PreviewScene before scene switch
    public GltfImport LoadedGltf { get; private set; }

    // Raw file path — used as fallback if GltfImport is null
    public string ModelPath { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void SetModel(GltfImport gltf, string path)
    {
        LoadedGltf = gltf;
        ModelPath = path;
        Debug.Log("[ARModelBridge] Model stored. Path: " + path);
    }

    public void Clear()
    {
        LoadedGltf = null;
        ModelPath = null;
    }
}
