using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using GLTFast;

public class ModelPreviewManager : MonoBehaviour
{
    [Header("Hierarchy")]
    public Transform modelParent;
    public Camera previewCamera;

    [Header("Auto Fit")]
    public float targetSize = 1.5f;
    public float minCameraDistance = 1.5f;
    public float cameraDistanceMultiplier = 3.0f;

    [Header("Auto Rotate")]
    public bool autoRotate = false;
    public float autoRotateSpeed = 25f;

    [Header("Debug / Timing")]
    public bool debugMode = true;
    public int waitFramesAfterInstantiate = 2;
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

        // Always use raw path for File.Exists — never a file:// URI
        string path = PlayerPrefs.GetString("LastModelPath", "");
        Debug.Log("[ModelPreviewManager] LastModelPath = " + path);

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            await LoadModel(path);
        }
        else
        {
            Debug.LogError("[ModelPreviewManager] File not found at path: " + path);
        }
    }

    private void Update()
    {
        if (autoRotate && modelParent != null)
            modelParent.Rotate(Vector3.up, autoRotateSpeed * Time.deltaTime, Space.World);
    }

    public async Task LoadModel(string filePath)
    {
        // filePath must be a raw OS path here — File.Exists needs it
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Debug.LogError("[ModelPreviewManager] Invalid or missing path: " + filePath);
            return;
        }

        if (loadedModel != null) Destroy(loadedModel);

        if (debugMode) Debug.Log("[ModelPreviewManager] Loading: " + filePath);

        // Convert to proper URI for glTFast (handles Windows file:///C:/ and Android file:///data/)
        string uri = GltfLoader.ToGltfUri(filePath);
        if (debugMode) Debug.Log("[ModelPreviewManager] glTFast URI: " + uri);

        var gltf = new GltfImport();
        bool success = await GltfLoader.InvokeLoadWithReflection(
            gltf, uri,
            GltfLoader.CreateImportSettings(),
            GltfLoader.CreateURPMaterialGeneratorIfAvailable());

        if (!success)
        {
            Debug.LogError("[ModelPreviewManager] glTFast failed to load: " + uri);
            return;
        }

        loadedModel = new GameObject("LoadedModel");
        success = await gltf.InstantiateMainSceneAsync(loadedModel.transform);

        if (!success)
        {
            Debug.LogError("[ModelPreviewManager] glTFast failed to instantiate scene.");
            Destroy(loadedModel);
            loadedModel = null;
            return;
        }

        // Fix all non-URP shaders (covers glTFast shaders stripped on Android)
        GltfLoader.FixMaterialsToURP(loadedModel);

        loadedModel.transform.SetParent(modelParent, false);
        loadedModel.transform.localScale = Vector3.one;
        loadedModel.transform.localPosition = Vector3.zero;
        loadedModel.transform.localRotation = Quaternion.identity;

        for (int i = 0; i < waitFramesAfterInstantiate; i++)
            await Task.Yield();

        int waited = 0;
        var renderers = loadedModel.GetComponentsInChildren<Renderer>(true);
        while ((renderers == null || renderers.Length == 0) && waited < maxWaitFrames)
        {
            await Task.Yield();
            waited++;
            renderers = loadedModel.GetComponentsInChildren<Renderer>(true);
        }

        if (debugMode) Debug.Log($"[ModelPreviewManager] Renderers: {renderers?.Length ?? 0} (waited {waited} frames)");

        SetLayerRecursively(loadedModel, modelParent.gameObject.layer);

        if (renderers != null)
            foreach (var r in renderers) r.enabled = true;

        // Save raw path (not URI) so File.Exists works next time
        PlayerPrefs.SetString("LastModelPath", filePath);
        PlayerPrefs.Save();

        NormalizeAndFrame();
    }

    public void NormalizeAndFrame()
    {
        if (modelParent == null || loadedModel == null) return;

        Bounds preBounds = CalculateBoundsRelativeTo(loadedModel.transform, modelParent);
        if (preBounds.size == Vector3.zero)
        {
            Debug.LogWarning("[ModelPreviewManager] No renderer bounds found.");
            return;
        }

        float maxDim = Mathf.Max(preBounds.size.x, Mathf.Max(preBounds.size.y, preBounds.size.z));
        float scaleFactor = maxDim > 0f ? targetSize / maxDim : 1f;
        if (float.IsNaN(scaleFactor) || float.IsInfinity(scaleFactor)) scaleFactor = 1f;
        scaleFactor = Mathf.Clamp(scaleFactor, 1e-6f, 1e6f);

        loadedModel.transform.localScale = Vector3.one * scaleFactor;

        Bounds scaledBounds = CalculateBoundsRelativeTo(loadedModel.transform, modelParent);
        loadedModel.transform.localPosition = -scaledBounds.center;

        scaledBounds = CalculateBoundsRelativeTo(loadedModel.transform, modelParent);
        loadedModel.transform.localPosition += new Vector3(0f, -scaledBounds.min.y, 0f);

        scaledBounds = CalculateBoundsRelativeTo(loadedModel.transform, modelParent);

        if (previewCamera != null)
        {
            float distance = Mathf.Max(minCameraDistance, scaledBounds.extents.magnitude * cameraDistanceMultiplier);
            Vector3 dir = (previewCamera.transform.position - modelParent.position).normalized;
            if (dir == Vector3.zero) dir = -previewCamera.transform.forward;

            previewCamera.transform.position = modelParent.position + dir * distance;
            previewCamera.transform.LookAt(modelParent.position);

            currentCameraDistance = distance;
            initialCameraPosition = previewCamera.transform.position;
            initialCameraRotation = previewCamera.transform.rotation;
        }
    }

    private Bounds CalculateBoundsRelativeTo(Transform root, Transform relativeTo)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return new Bounds(Vector3.zero, Vector3.zero);

        Vector3 min = Vector3.one * float.MaxValue;
        Vector3 max = Vector3.one * float.MinValue;

        foreach (var r in renderers)
        {
            Bounds rb = r.bounds;
            Vector3 c = rb.center, e = rb.extents;
            Vector3[] corners =
            {
                new Vector3(c.x+e.x, c.y+e.y, c.z+e.z), new Vector3(c.x+e.x, c.y+e.y, c.z-e.z),
                new Vector3(c.x+e.x, c.y-e.y, c.z+e.z), new Vector3(c.x+e.x, c.y-e.y, c.z-e.z),
                new Vector3(c.x-e.x, c.y+e.y, c.z+e.z), new Vector3(c.x-e.x, c.y+e.y, c.z-e.z),
                new Vector3(c.x-e.x, c.y-e.y, c.z+e.z), new Vector3(c.x-e.x, c.y-e.y, c.z-e.z)
            };
            foreach (var corner in corners)
            {
                Vector3 lp = relativeTo.InverseTransformPoint(corner);
                min = Vector3.Min(min, lp);
                max = Vector3.Max(max, lp);
            }
        }
        return new Bounds((min + max) * 0.5f, max - min);
    }

    private void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform)
            SetLayerRecursively(t.gameObject, layer);
    }

    public void SetAutoRotate(bool on) => autoRotate = on;
    public bool GetAutoRotate() => autoRotate;

    public void ResetView()
    {
        if (!previewCamera || modelParent == null) return;
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
