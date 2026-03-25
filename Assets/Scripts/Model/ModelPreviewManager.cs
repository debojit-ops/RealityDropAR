
// using System.IO;
// using System.Threading.Tasks;
// using UnityEngine;
// using GLTFast;

// public class ModelPreviewManager : MonoBehaviour
// {
//     [Header("Hierarchy")]
//     public Transform modelParent;    // PreviewAnchor
//     public Camera previewCamera;     

//     [Header("Auto Fit")]
//     [Tooltip("Desired maximum model dimension in Unity units (meters).")]
//     public float targetSize = 1.5f;
//     public float minCameraDistance = 1.5f;
//     public float cameraDistanceMultiplier = 3.0f;

//     [Header("Auto Rotate")]
//     public bool autoRotate = false;
//     public float autoRotateSpeed = 25f; 

//     [Header("Debug / Timing")]
//     public bool debugMode = true;
//     [Tooltip("Frames to wait (yield) after instantiate to allow renderers to register")]
//     public int waitFramesAfterInstantiate = 2;
//     [Tooltip("If renderers are not found, keep waiting up to this many extra frames")]
//     public int maxWaitFrames = 12;

//     private GameObject loadedModel;
//     private Vector3 initialCameraPosition;
//     private Quaternion initialCameraRotation;
//     private float currentCameraDistance;

//     private async void Start()
//     {
//         if (!previewCamera) previewCamera = Camera.main;
//         if (previewCamera)
//         {
//             initialCameraPosition = previewCamera.transform.position;
//             initialCameraRotation = previewCamera.transform.rotation;
//         }

//         string path = PlayerPrefs.GetString("LastModelPath", "");
//         if (!string.IsNullOrEmpty(path) && File.Exists(path))
//         {
//             await LoadModel(path);
//         }
//     }

//     private void Update()
//     {
//         if (autoRotate && modelParent != null)
//         {
//             modelParent.Rotate(Vector3.up, autoRotateSpeed * Time.deltaTime, Space.World);
//         }
//     }

//     public async Task LoadModel(string filePath)
//     {
//         if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
//         {
//             Debug.LogError("Invalid path: " + filePath);
//             return;
//         }

//         if (loadedModel != null) Destroy(loadedModel);

//         if (debugMode) Debug.Log($"[ModelPreviewManager] Loading GLB from: {filePath}");

//         var gltf = new GltfImport();
//         bool success = await gltf.Load(filePath);
//         if (!success)
//         {
//             Debug.LogError("glTFast failed to load: " + filePath);
//             return;
//         }

//         // Create container
//         loadedModel = new GameObject("LoadedModel");

//         // Instantiate the glTF into that container
//         success = await gltf.InstantiateMainSceneAsync(loadedModel.transform);
//         if (!success)
//         {
//             Debug.LogError("glTFast failed to instantiate scene.");
//             Destroy(loadedModel);
//             return;
//         }

//         // Parent under modelParent (PreviewAnchor)
//         loadedModel.transform.SetParent(modelParent, false);

//         // Start from identity for the container; child nodes keep their local transforms
//         loadedModel.transform.localPosition = Vector3.zero;
//         loadedModel.transform.localRotation = Quaternion.identity;
//         loadedModel.transform.localScale = Vector3.one;

//         // Wait a couple frames so renderers & mesh filters can finish initialization
//         for (int i = 0; i < waitFramesAfterInstantiate; i++)
//             await Task.Yield();

//         // If renderers still not present, wait more (maxWaitFrames)
//         int waited = 0;
//         var renderers = loadedModel.GetComponentsInChildren<Renderer>(true);
//         while ((renderers == null || renderers.Length == 0) && waited < maxWaitFrames)
//         {
//             await Task.Yield();
//             waited++;
//             renderers = loadedModel.GetComponentsInChildren<Renderer>(true);
//         }

//         if (debugMode) Debug.Log($"[ModelPreviewManager] Renderer count after instantiate/wait: {(renderers != null ? renderers.Length : 0)} (waited {waited} frames)");

//         // Ensure model is on same layer as modelParent so preview camera sees it (unless you want a dedicated layer)
//         SetLayerRecursively(loadedModel, modelParent.gameObject.layer);

//         // Ensure all renderers are enabled (some imports might disable them)
//         if (renderers != null)
//         {
//             foreach (var r in renderers)
//                 r.enabled = true;
//         }

//         // Save model path
//         PlayerPrefs.SetString("LastModelPath", filePath);
//         PlayerPrefs.Save();

//         // Normalize and frame
//         NormalizeAndFrame();
//     }

//     /// <summary>
//     /// Robust normalization: computes bounds relative to modelParent, computes scale,
//     /// applies scale, centers model, grounds it (minY => 0), optionally forces orientation,
//     /// then frames the preview camera. Logs details if debugMode is true.
//     /// </summary>
//     public void NormalizeAndFrame()
//     {
//         if (modelParent == null || loadedModel == null)
//         {
//             Debug.LogWarning("[ModelPreviewManager] modelParent or loadedModel is null.");
//             return;
//         }

//         // If modelParent has non-1 scale that will distort the math
//         if (modelParent.lossyScale != Vector3.one && debugMode)
//             Debug.LogWarning($"[ModelPreviewManager] modelParent has lossyScale={modelParent.lossyScale}. Best if this is (1,1,1).");

//         // 1) Bounds BEFORE scaling, measured in modelParent local space
//         Bounds preBounds = CalculateBoundsRelativeTo(loadedModel.transform, modelParent);
//         if (preBounds.size == Vector3.zero)
//         {
//             Debug.LogWarning("[ModelPreviewManager] No renderer bounds found (pre-scale). Aborting normalization.");
//             return;
//         }

//         float maxDim = Mathf.Max(preBounds.size.x, Mathf.Max(preBounds.size.y, preBounds.size.z));
//         if (debugMode) Debug.Log($"[ModelPreviewManager] pre-scale bounds center={preBounds.center}, size={preBounds.size}, maxDim={maxDim}");

//         // 2) Compute scale factor so largest dimension equals targetSize
//         float scaleFactor = 1f;
//         if (maxDim > 0f)
//             scaleFactor = targetSize / maxDim;

//         // Sanity clamps to avoid absurd tiny/huge results (adjust as you wish)
//         if (float.IsNaN(scaleFactor) || float.IsInfinity(scaleFactor)) scaleFactor = 1f;
//         scaleFactor = Mathf.Clamp(scaleFactor, 1e-6f, 1e6f);

//         if (debugMode) Debug.Log($"[ModelPreviewManager] computed scaleFactor = {scaleFactor}");

//         // 3) Apply scale to the container
//         loadedModel.transform.localScale = Vector3.one * scaleFactor;

//         // 4) Recalculate bounds AFTER scaling
//         Bounds scaledBounds = CalculateBoundsRelativeTo(loadedModel.transform, modelParent);
//         if (debugMode) Debug.Log($"[ModelPreviewManager] post-scale bounds center={scaledBounds.center}, size={scaledBounds.size}");

//         // 5) Center model: move container so scaledBounds.center maps to (0,0,0) in modelParent
//         loadedModel.transform.localPosition = -scaledBounds.center;

//         // 6) Ground it: compute bounds again and shift up by -minY so model base sits at y=0
//         scaledBounds = CalculateBoundsRelativeTo(loadedModel.transform, modelParent);
//         float bottomY = scaledBounds.min.y;
//         if (debugMode) Debug.Log($"[ModelPreviewManager] bottomY (after centering) = {bottomY}");
//         loadedModel.transform.localPosition += new Vector3(0f, -bottomY, 0f);

//         // 7) Optionally force consistent orientation. Right now we leave child rotations intact,
//         //    but you can force upright with: loadedModel.transform.localRotation = Quaternion.identity;
//         //    Or force front to Z+: loadedModel.transform.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

//         // 8) Final bounds and framing
//         scaledBounds = CalculateBoundsRelativeTo(loadedModel.transform, modelParent);
//         if (debugMode) Debug.Log($"[ModelPreviewManager] final bounds center={scaledBounds.center}, size={scaledBounds.size}, extents={scaledBounds.extents}");

//         // Frame camera
//         if (previewCamera != null)
//         {
//             float distance = Mathf.Max(minCameraDistance, scaledBounds.extents.magnitude * cameraDistanceMultiplier);
//             Vector3 dir = (previewCamera.transform.position - modelParent.position).normalized;
//             if (dir == Vector3.zero) dir = previewCamera.transform.forward * -1f;

//             previewCamera.transform.position = modelParent.position + dir * distance;
//             previewCamera.transform.LookAt(modelParent.position);

//             currentCameraDistance = distance;
//             initialCameraPosition = previewCamera.transform.position;
//             initialCameraRotation = previewCamera.transform.rotation;

//             if (debugMode) Debug.Log($"[ModelPreviewManager] camera positioned at {previewCamera.transform.position}, looking at {modelParent.position}, distance {distance}");
//         }

//         // Extra debug: print transform chain
//         if (debugMode) PrintTransformChain(loadedModel.transform);
//     }

//     /// <summary>
//     /// Calculate bounds of all child renderers of root, expressed in the local space of 'relativeTo'.
//     /// </summary>
//     private Bounds CalculateBoundsRelativeTo(Transform root, Transform relativeTo)
//     {
//         var renderers = root.GetComponentsInChildren<Renderer>(true);
//         if (renderers == null || renderers.Length == 0)
//             return new Bounds(Vector3.zero, Vector3.zero);

//         Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
//         Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

//         foreach (var r in renderers)
//         {
//             Bounds rb = r.bounds; // world-space AABB
//             Vector3 c = rb.center;
//             Vector3 e = rb.extents;

//             // 8 corners of this renderer bounds
//             Vector3[] corners = new Vector3[8]
//             {
//                 new Vector3(c.x + e.x, c.y + e.y, c.z + e.z),
//                 new Vector3(c.x + e.x, c.y + e.y, c.z - e.z),
//                 new Vector3(c.x + e.x, c.y - e.y, c.z + e.z),
//                 new Vector3(c.x + e.x, c.y - e.y, c.z - e.z),
//                 new Vector3(c.x - e.x, c.y + e.y, c.z + e.z),
//                 new Vector3(c.x - e.x, c.y + e.y, c.z - e.z),
//                 new Vector3(c.x - e.x, c.y - e.y, c.z + e.z),
//                 new Vector3(c.x - e.x, c.y - e.y, c.z - e.z)
//             };

//             for (int i = 0; i < corners.Length; i++)
//             {
//                 Vector3 localPoint = relativeTo.InverseTransformPoint(corners[i]);
//                 min = Vector3.Min(min, localPoint);
//                 max = Vector3.Max(max, localPoint);
//             }
//         }

//         Vector3 center = (min + max) * 0.5f;
//         Vector3 size = max - min;
//         return new Bounds(center, size);
//     }

//     private void SetLayerRecursively(GameObject go, int layer)
//     {
//         go.layer = layer;
//         foreach (Transform t in go.transform)
//             SetLayerRecursively(t.gameObject, layer);
//     }

//     private void PrintTransformChain(Transform root)
//     {
//         Transform t = root;
//         string outStr = "[ModelPreviewManager] Transform chain (top-down):\n";
//         while (t != null)
//         {
//             outStr += $"{t.name} - localPos={t.localPosition:F4}, localRot={t.localEulerAngles:F2}, localScale={t.localScale:F4}, lossyScale={t.lossyScale:F4}\n";
//             if (t == modelParent) break;
//             t = t.parent;
//         }
//         Debug.Log(outStr);
//     }

//     public void SetAutoRotate(bool on) { autoRotate = on; }
//     public bool GetAutoRotate() { return autoRotate; }

//     public void ResetView()
//     {
//         if (!previewCamera || modelParent == null) return;
//         previewCamera.transform.position = initialCameraPosition;
//         previewCamera.transform.rotation = initialCameraRotation;
//         modelParent.localRotation = Quaternion.identity;
//     }

//     public void ChangeCameraDistance(float delta)
//     {
//         if (!previewCamera || modelParent == null) return;
//         currentCameraDistance = Mathf.Clamp(currentCameraDistance + delta, 0.01f, 100f);
//         previewCamera.transform.position = modelParent.position - previewCamera.transform.forward * currentCameraDistance;
//     }
// }



using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using GLTFast;

public class ModelPreviewManager : MonoBehaviour
{
    [Header("Hierarchy")]
    public Transform modelParent;    // PreviewAnchor
    public Camera previewCamera;     

    [Header("Auto Fit")]
    [Tooltip("Desired maximum model dimension in Unity units (meters).")]
    public float targetSize = 1.5f;
    public float minCameraDistance = 1.5f;
    public float cameraDistanceMultiplier = 3.0f;

    [Header("Auto Rotate")]
    public bool autoRotate = false;
    public float autoRotateSpeed = 25f; 

    [Header("Debug / Timing")]
    public bool debugMode = true;
    [Tooltip("Frames to wait (yield) after instantiate to allow renderers to register")]
    public int waitFramesAfterInstantiate = 2;
    [Tooltip("If renderers are not found, keep waiting up to this many extra frames")]
    public int maxWaitFrames = 12;

    private GameObject loadedModel;
    private Vector3 initialCameraPosition;
    private Quaternion initialCameraRotation;
    private float currentCameraDistance;

    private async void Start()
    {
        if (!previewCamera) previewCamera = Camera.main;
        if (previewCamera)
        {
            initialCameraPosition = previewCamera.transform.position;
            initialCameraRotation = previewCamera.transform.rotation;
        }

        string path = PlayerPrefs.GetString("LastModelPath", "");
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            await LoadModel(path);
        }
    }

    private void Update()
    {
        if (autoRotate && modelParent != null)
        {
            modelParent.Rotate(Vector3.up, autoRotateSpeed * Time.deltaTime, Space.World);
        }
    }

    public async Task LoadModel(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Debug.LogError("Invalid path: " + filePath);
            return;
        }

        if (loadedModel != null) Destroy(loadedModel);

        if (debugMode) Debug.Log($"[ModelPreviewManager] Loading GLB from: {filePath}");

        var gltf = new GltfImport();
        bool success = await gltf.Load(filePath);
        if (!success)
        {
            Debug.LogError("glTFast failed to load: " + filePath);
            return;
        }

        // Create container
        loadedModel = new GameObject("LoadedModel");

        // Instantiate the glTF into that container
        success = await gltf.InstantiateMainSceneAsync(loadedModel.transform);
        if (!success)
        {
            Debug.LogError("glTFast failed to instantiate scene.");
            Destroy(loadedModel);
            return;
        }

        // Parent under modelParent (PreviewAnchor)
        loadedModel.transform.SetParent(modelParent, false);

        // ✅ Reset root node’s scale so importer quirks don’t apply tiny scale
        loadedModel.transform.localScale = Vector3.one;

        // Start from identity for the container; child nodes keep their local transforms
        loadedModel.transform.localPosition = Vector3.zero;
        loadedModel.transform.localRotation = Quaternion.identity;

        // Wait a couple frames so renderers & mesh filters can finish initialization
        for (int i = 0; i < waitFramesAfterInstantiate; i++)
            await Task.Yield();

        // If renderers still not present, wait more (maxWaitFrames)
        int waited = 0;
        var renderers = loadedModel.GetComponentsInChildren<Renderer>(true);
        while ((renderers == null || renderers.Length == 0) && waited < maxWaitFrames)
        {
            await Task.Yield();
            waited++;
            renderers = loadedModel.GetComponentsInChildren<Renderer>(true);
        }

        if (debugMode) Debug.Log($"[ModelPreviewManager] Renderer count after instantiate/wait: {(renderers != null ? renderers.Length : 0)} (waited {waited} frames)");

        // Ensure model is on same layer as modelParent so preview camera sees it (unless you want a dedicated layer)
        SetLayerRecursively(loadedModel, modelParent.gameObject.layer);

        // Ensure all renderers are enabled (some imports might disable them)
        if (renderers != null)
        {
            foreach (var r in renderers)
                r.enabled = true;
        }

        // Save model path
        PlayerPrefs.SetString("LastModelPath", filePath);
        PlayerPrefs.Save();

        // Normalize and frame
        NormalizeAndFrame();
    }

    /// <summary>
    /// Robust normalization: computes bounds relative to modelParent, computes scale,
    /// applies scale, centers model, grounds it (minY => 0), optionally forces orientation,
    /// then frames the preview camera. Logs details if debugMode is true.
    /// </summary>
    public void NormalizeAndFrame()
    {
        if (modelParent == null || loadedModel == null)
        {
            Debug.LogWarning("[ModelPreviewManager] modelParent or loadedModel is null.");
            return;
        }

        // If modelParent has non-1 scale that will distort the math
        if (modelParent.lossyScale != Vector3.one && debugMode)
            Debug.LogWarning($"[ModelPreviewManager] modelParent has lossyScale={modelParent.lossyScale}. Best if this is (1,1,1).");

        // 1) Bounds BEFORE scaling, measured in modelParent local space
        Bounds preBounds = CalculateBoundsRelativeTo(loadedModel.transform, modelParent);
        if (preBounds.size == Vector3.zero)
        {
            Debug.LogWarning("[ModelPreviewManager] No renderer bounds found (pre-scale). Aborting normalization.");
            return;
        }

        float maxDim = Mathf.Max(preBounds.size.x, Mathf.Max(preBounds.size.y, preBounds.size.z));
        if (debugMode) Debug.Log($"[ModelPreviewManager] pre-scale bounds center={preBounds.center}, size={preBounds.size}, maxDim={maxDim}");

        // 2) Compute scale factor so largest dimension equals targetSize
        float scaleFactor = 1f;
        if (maxDim > 0f)
            scaleFactor = targetSize / maxDim;

        // Sanity clamps to avoid absurd tiny/huge results (adjust as you wish)
        if (float.IsNaN(scaleFactor) || float.IsInfinity(scaleFactor)) scaleFactor = 1f;
        scaleFactor = Mathf.Clamp(scaleFactor, 1e-6f, 1e6f);

        if (debugMode) Debug.Log($"[ModelPreviewManager] computed scaleFactor = {scaleFactor}");

        // 3) Apply scale to the container
        loadedModel.transform.localScale = Vector3.one * scaleFactor;

        // 4) Recalculate bounds AFTER scaling
        Bounds scaledBounds = CalculateBoundsRelativeTo(loadedModel.transform, modelParent);
        if (debugMode) Debug.Log($"[ModelPreviewManager] post-scale bounds center={scaledBounds.center}, size={scaledBounds.size}");

        // 5) Center model: move container so scaledBounds.center maps to (0,0,0) in modelParent
        loadedModel.transform.localPosition = -scaledBounds.center;

        // 6) Ground it: compute bounds again and shift up by -minY so model base sits at y=0
        scaledBounds = CalculateBoundsRelativeTo(loadedModel.transform, modelParent);
        float bottomY = scaledBounds.min.y;
        if (debugMode) Debug.Log($"[ModelPreviewManager] bottomY (after centering) = {bottomY}");
        loadedModel.transform.localPosition += new Vector3(0f, -bottomY, 0f);

        // 7) Final bounds and framing
        scaledBounds = CalculateBoundsRelativeTo(loadedModel.transform, modelParent);
        if (debugMode) Debug.Log($"[ModelPreviewManager] final bounds center={scaledBounds.center}, size={scaledBounds.size}, extents={scaledBounds.extents}");

        // Frame camera
        if (previewCamera != null)
        {
            float distance = Mathf.Max(minCameraDistance, scaledBounds.extents.magnitude * cameraDistanceMultiplier);
            Vector3 dir = (previewCamera.transform.position - modelParent.position).normalized;
            if (dir == Vector3.zero) dir = previewCamera.transform.forward * -1f;

            previewCamera.transform.position = modelParent.position + dir * distance;
            previewCamera.transform.LookAt(modelParent.position);

            currentCameraDistance = distance;
            initialCameraPosition = previewCamera.transform.position;
            initialCameraRotation = previewCamera.transform.rotation;

            if (debugMode) Debug.Log($"[ModelPreviewManager] camera positioned at {previewCamera.transform.position}, looking at {modelParent.position}, distance {distance}");
        }

        // Extra debug: print transform chain
        if (debugMode) PrintTransformChain(loadedModel.transform);
    }

    /// <summary>
    /// Calculate bounds of all child renderers of root, expressed in the local space of 'relativeTo'.
    /// </summary>
    private Bounds CalculateBoundsRelativeTo(Transform root, Transform relativeTo)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return new Bounds(Vector3.zero, Vector3.zero);

        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        foreach (var r in renderers)
        {
            Bounds rb = r.bounds; // world-space AABB
            Vector3 c = rb.center;
            Vector3 e = rb.extents;

            // 8 corners of this renderer bounds
            Vector3[] corners = new Vector3[8]
            {
                new Vector3(c.x + e.x, c.y + e.y, c.z + e.z),
                new Vector3(c.x + e.x, c.y + e.y, c.z - e.z),
                new Vector3(c.x + e.x, c.y - e.y, c.z + e.z),
                new Vector3(c.x + e.x, c.y - e.y, c.z - e.z),
                new Vector3(c.x - e.x, c.y + e.y, c.z + e.z),
                new Vector3(c.x - e.x, c.y + e.y, c.z - e.z),
                new Vector3(c.x - e.x, c.y - e.y, c.z + e.z),
                new Vector3(c.x - e.x, c.y - e.y, c.z - e.z)
            };

            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 localPoint = relativeTo.InverseTransformPoint(corners[i]);
                min = Vector3.Min(min, localPoint);
                max = Vector3.Max(max, localPoint);
            }
        }

        Vector3 center = (min + max) * 0.5f;
        Vector3 size = max - min;
        return new Bounds(center, size);
    }

    private void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform)
            SetLayerRecursively(t.gameObject, layer);
    }

    private void PrintTransformChain(Transform root)
    {
        Transform t = root;
        string outStr = "[ModelPreviewManager] Transform chain (top-down):\n";
        while (t != null)
        {
            outStr += $"{t.name} - localPos={t.localPosition:F4}, localRot={t.localEulerAngles:F2}, localScale={t.localScale:F4}, lossyScale={t.lossyScale:F4}\n";
            if (t == modelParent) break;
            t = t.parent;
        }
        Debug.Log(outStr);
    }

    public void SetAutoRotate(bool on) { autoRotate = on; }
    public bool GetAutoRotate() { return autoRotate; }

    public void ResetView()
{
    if (!previewCamera || modelParent == null) return;

    // Custom fixed values for preview
    previewCamera.transform.position = new Vector3(0f, 0.9f, -2.7f);
    previewCamera.transform.rotation = Quaternion.Euler(11.5f, 0f, 0f);

    modelParent.localRotation = Quaternion.identity;
}


    public void ChangeCameraDistance(float delta)
    {
        if (!previewCamera || modelParent == null) return;
        currentCameraDistance = Mathf.Clamp(currentCameraDistance + delta, 0.01f, 100f);
        previewCamera.transform.position = modelParent.position - previewCamera.transform.forward * currentCameraDistance;
    }
}
