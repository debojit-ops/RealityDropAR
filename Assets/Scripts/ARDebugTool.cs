// using System.Collections;
// using System.IO;
// using UnityEngine;
// using UnityEngine.UI;
// using TMPro;

// public class ARDebugTool : MonoBehaviour
// {
//     public ModelLoader modelLoader;           // assign or auto-find
//     public ModelPlacement modelPlacement;     // optional
//     public TextMeshProUGUI debugText;         // optional UI text for device
//     public float forceShowDistance = 1.5f;    // meters in front of camera
//     public float forceTargetSize = 0.5f;      // target max dimension in meters
//     public bool autoRunOnStart = false;       // run quick checks automatically
//     public float waitTimeout = 8f;            // seconds to wait for load

//     void Start()
//     {
//         if (modelLoader == null)
//             modelLoader = FindFirstObjectByType<ModelLoader>();

//         if (modelPlacement == null)
//             modelPlacement = FindFirstObjectByType<ModelPlacement>();

//         Display($"persistentDataPath: {Application.persistentDataPath}");
//         if (autoRunOnStart) StartCoroutine(AutoRun());
//     }

//     IEnumerator AutoRun()
//     {
//         yield return null;
//         CheckPlayerPrefs();
//         yield return StartCoroutine(EnsureModelLoadedAndReport());
//     }

//     // --- UI callable ---
//     public void CheckPlayerPrefs()
//     {
//         string path = PlayerPrefs.GetString("LastModelPath", "");
//         Log($"PlayerPrefs LastModelPath: '{path}'");
//         Log($"File exists: {File.Exists(path)}");
//         if (File.Exists(path))
//         {
//             var fi = new FileInfo(path);
//             Log($"File size: {fi.Length} bytes");
//         }
//     }

//     public void RunEnsureModelLoadedAndReport()
//     {
//         StartCoroutine(EnsureModelLoadedAndReport());
//     }

//     public IEnumerator EnsureModelLoadedAndReport()
//     {
//         string path = PlayerPrefs.GetString("LastModelPath", "");
//         if (string.IsNullOrEmpty(path))
//         {
//             Log("No LastModelPath set in PlayerPrefs.");
//             yield break;
//         }
//         if (!File.Exists(path))
//         {
//             Log($"File missing at path: {path}");
//             yield break;
//         }

//         if (modelLoader == null)
//         {
//             modelLoader = FindFirstObjectByType<ModelLoader>();
//             if (modelLoader == null)
//             {
//                 Log("ModelLoader not found in scene.");
//                 yield break;
//             }
//         }

//         if (modelLoader.LastLoadedModel == null)
//         {
//             Log("ModelLoader has no LastLoadedModel. Calling LoadModelFromLocalPath(...)");
//             modelLoader.LoadModelFromLocalPath(path);
//         }
//         else
//         {
//             Log("ModelLoader already contains LastLoadedModel.");
//         }

//         float t = 0f;
//         while (modelLoader.LastLoadedModel == null && t < waitTimeout)
//         {
//             t += Time.deltaTime;
//             yield return null;
//         }

//         if (modelLoader.LastLoadedModel == null)
//         {
//             Log("Timed out waiting for model to become available in ModelLoader.LastLoadedModel.");
//             yield break;
//         }

//         Log("Model loaded into scene. Reporting info:");
//         PrintModelInfo(modelLoader.LastLoadedModel);
//     }

//     public void PrintModelInfo(GameObject go)
//     {
//         if (go == null)
//         {
//             Log("PrintModelInfo: GameObject is null.");
//             return;
//         }

//         Log($"Name: {go.name}");
//         Log($"Active: {go.activeSelf} | Parent: {(go.transform.parent ? go.transform.parent.name : "null")}");
//         Log($"LocalPos: {go.transform.localPosition} | LocalScale: {go.transform.localScale} | LossyScale: {go.transform.lossyScale}");
//         var renderers = go.GetComponentsInChildren<Renderer>(true);
//         Log($"Renderers found: {renderers.Length}");
//         if (renderers.Length > 0)
//         {
//             Bounds b = renderers[0].bounds;
//             for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
//             Log($"World bounds center: {b.center} size: {b.size} maxDim: {Mathf.Max(b.size.x, b.size.y, b.size.z)}");
//         }
//         var colliders = go.GetComponentsInChildren<Collider>(true);
//         Log($"Colliders found: {colliders.Length}");
//         var meshFilters = go.GetComponentsInChildren<MeshFilter>(true);
//         Log($"MeshFilters found: {meshFilters.Length}");
//     }

//     public void ForceShowModel() { StartCoroutine(ForceShowCoroutine()); }

//     IEnumerator ForceShowCoroutine()
//     {
//         if (modelLoader == null) modelLoader = FindFirstObjectByType<ModelLoader>();
//         if (modelLoader == null) { Log("ModelLoader not found."); yield break; }

//         if (modelLoader.LastLoadedModel == null)
//         {
//             Log("No LastLoadedModel: attempting to load from PlayerPrefs path...");
//             string p = PlayerPrefs.GetString("LastModelPath", "");
//             if (string.IsNullOrEmpty(p) || !File.Exists(p)) { Log("Invalid path; cannot load."); yield break; }
//             modelLoader.LoadModelFromLocalPath(p);
//             float tt = 0f;
//             while (modelLoader.LastLoadedModel == null && tt < waitTimeout) { tt += Time.deltaTime; yield return null; }
//             if (modelLoader.LastLoadedModel == null) { Log("Load failed or timed out."); yield break; }
//         }

//         GameObject go = modelLoader.LastLoadedModel;
//         // compute bounds
//         var renders = go.GetComponentsInChildren<Renderer>(true);
//         Bounds bounds = new Bounds(go.transform.position, Vector3.zero);
//         if (renders.Length > 0)
//         {
//             bounds = renders[0].bounds;
//             for (int i = 1; i < renders.Length; i++) bounds.Encapsulate(renders[i].bounds);
//         }

//         float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
//         if (maxDim <= 0.000001f)
//         {
//             Log("Model max dimension is zero or extremely small — applying fallback scale x1000");
//             go.transform.localScale = go.transform.localScale * 1000f;
//         }
//         else
//         {
//             float factor = forceTargetSize / maxDim;
//             Log($"Scaling model by {factor:F4} to reach target size {forceTargetSize}m (maxDim={maxDim:F4}m)");
//             go.transform.localScale = go.transform.localScale * factor;
//         }

//         var cam = Camera.main;
//         if (cam == null) { Log("MainCamera not found."); yield break; }

//         go.transform.SetParent(null, true);
//         go.transform.position = cam.transform.position + cam.transform.forward * forceShowDistance;
//         go.transform.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
//         go.SetActive(true);

//         // add colliders for selection if needed
//         AddMeshColliders(go);

//         PrintModelInfo(go);
//         Log("ForceShow complete. Model should now be visible in front of camera.");
//         yield return null;
//     }

//     void AddMeshColliders(GameObject root)
//     {
//         foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
//         {
//             if (mf.gameObject.GetComponent<Collider>() == null)
//             {
//                 var mc = mf.gameObject.AddComponent<MeshCollider>();
//                 mc.convex = false;
//             }
//         }
//     }

//     void Log(string msg)
//     {
//         Debug.Log("[ARDebug] " + msg);
//         Display(msg);
//     }

//     void Display(string msg)
//     {
//         if (debugText != null) debugText.text = msg;
//     }
// }




//19th dec updated 
using UnityEngine;
using TMPro;

public class ARDebugTool : MonoBehaviour
{
    public ModelLoader modelLoader;
    public TextMeshProUGUI debugText;

    void Start()
    {
        if (modelLoader == null)
            modelLoader = FindFirstObjectByType<ModelLoader>();

        Log("ARDebugTool ready.");
    }

    public void PrintCurrentModelInfo()
    {
        if (modelLoader == null || modelLoader.LastLoadedModel == null)
        {
            Log("No model loaded.");
            return;
        }

        GameObject go = modelLoader.LastLoadedModel;
        Log($"Model Name: {go.name}");
        Log($"Active: {go.activeSelf}");
        Log($"Position: {go.transform.position}");
        Log($"Scale: {go.transform.lossyScale}");

        var renderers = go.GetComponentsInChildren<Renderer>(true);
        Log($"Renderers found: {renderers.Length}");
    }

    void Log(string msg)
    {
        Debug.Log("[ARDebug] " + msg);
        if (debugText != null)
            debugText.text = msg;
    }
}
