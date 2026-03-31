using System.Collections;
using System.IO;
using UnityEngine;
using TMPro;

public class ARSceneInitializer : MonoBehaviour
{
    public ModelLoader modelLoader;
    public float loadTimeout = 10f;
    public TextMeshProUGUI statusText;

    public bool IsModelReady { get; private set; } = false;

    IEnumerator Start()
    {
        IsModelReady = false;
        SetStatus("Loading model...");

        if (modelLoader == null)
            modelLoader = FindFirstObjectByType<ModelLoader>();

        if (modelLoader == null)
        {
            SetStatus("Error: ModelLoader not found.");
            Debug.LogError("[ARSceneInitializer] ModelLoader not found in scene.");
            yield break;
        }

        var bridge = ARModelBridge.Instance;

        if (bridge != null && bridge.LoadedGltf != null)
        {
            // Fast path — instantiate from the GltfImport already loaded in PreviewScene
            Debug.Log("[ARSceneInitializer] Instantiating from ARModelBridge.");
            yield return InstantiateFromBridge(bridge);
        }
        else
        {
            // Fallback — re-load the GLB from disk
            Debug.Log("[ARSceneInitializer] No bridge data — loading from path.");
            yield return LoadFromPath();
        }
    }

    IEnumerator InstantiateFromBridge(ARModelBridge bridge)
    {
        yield return null; // wait one frame for scene to settle

        var container = new GameObject("LoadedModel");
        var task = bridge.LoadedGltf.InstantiateMainSceneAsync(container.transform);

        while (!task.IsCompleted)
            yield return null;

        if (task.IsFaulted)
        {
            Debug.LogWarning("[ARSceneInitializer] Bridge instantiation faulted: " +
                task.Exception?.InnerException?.Message ?? task.Exception?.Message);
            Destroy(container);
            yield return LoadFromPath();
            yield break;
        }

        if (!task.Result)
        {
            Debug.LogWarning("[ARSceneInitializer] Bridge instantiation returned false — falling back.");
            Destroy(container);
            yield return LoadFromPath();
            yield break;
        }

        FinishSetup(container);
    }

    IEnumerator LoadFromPath()
    {
        ModelLoader.ClearCache();

        string path = PlayerPrefs.GetString("LastModelPath", "");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            SetStatus("Error: Model file not found.");
            Debug.LogError("[ARSceneInitializer] Invalid or missing model path: " + path);
            yield break;
        }

        modelLoader.LoadModelFromLocalPath(path);

        float t = 0f;
        while (modelLoader.LastLoadedModel == null && t < loadTimeout)
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (modelLoader.LastLoadedModel == null)
        {
            SetStatus("Error: Model failed to load. Go back and try again.");
            Debug.LogError("[ARSceneInitializer] Model failed to load in time.");
            yield break;
        }

        FinishSetup(modelLoader.LastLoadedModel);
    }

    void FinishSetup(GameObject model)
    {
        modelLoader.SetLastLoadedModel(model);
        model.SetActive(false);
        IsModelReady = true;
        SetStatus("Point at a surface and tap Spawn.");
        Debug.Log("[ARSceneInitializer] Model ready for AR placement.");
    }

    void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
    }
}
