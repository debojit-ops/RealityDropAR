using System.Collections;
using System.IO;
using UnityEngine;

public class ARSceneInitializer : MonoBehaviour
{
    public ModelLoader modelLoader;
    public float loadTimeout = 10f;

    IEnumerator Start()
    {
        if (modelLoader == null)
            modelLoader = FindFirstObjectByType<ModelLoader>();

        if (modelLoader == null)
        {
            Debug.LogError("[ARSceneInitializer] ModelLoader not found in scene.");
            yield break;
        }

        string path = PlayerPrefs.GetString("LastModelPath", "");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Debug.LogError("[ARSceneInitializer] Invalid or missing model path.");
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
            Debug.LogError("[ARSceneInitializer] Model failed to load in time.");
            yield break;
        }

        // Hide until the user taps a plane — ModelPlacement will show it
        modelLoader.LastLoadedModel.SetActive(false);
        Debug.Log("[ARSceneInitializer] Model ready. Waiting for plane tap to place.");
    }
}
