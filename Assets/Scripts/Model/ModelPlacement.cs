
//working script

// using System.Collections.Generic;
// using UnityEngine;
// using UnityEngine.XR.ARFoundation;
// using UnityEngine.XR.ARSubsystems;

// public class ModelPlacement : MonoBehaviour
// {
//     [Header("Placement")]
//     [Tooltip("Allow user to drag and reposition model after placement")]
//     public bool allowReposition = true;

//     [Tooltip("Layers that can be touched to select the model")]
//     public LayerMask selectableLayers = ~0;

//     [Header("Scaling & Rotation")]
//     public Vector2 scaleLimits = new Vector2(0.1f, 3.0f);
//     public float rotationMultiplier = 0.5f;

//     private ARRaycastManager _raycastManager;
//     private Camera _cam;
//     private GameObject _spawned;
//     private bool _isDragging;

//     // gesture helpers
//     private float _startPinchDist;
//     private float _startAngle;
//     private Vector3 _startScale;

//     private static readonly List<ARRaycastHit> _hits = new List<ARRaycastHit>();

//     // Reference to loader
//     private ModelLoader _loader;

//     void Awake()
//     {
//         _raycastManager = GetComponent<ARRaycastManager>();
//         _cam = Camera.main;

//         if (_raycastManager == null)
//             Debug.LogError("❌ Missing ARRaycastManager. Add it to AR Session Origin.");
//         if (_cam == null)
//             Debug.LogWarning("⚠️ No Camera tagged MainCamera. Please tag your AR Camera.");

//         _loader = FindFirstObjectByType<ModelLoader>();
//         if (_loader == null)
//             Debug.LogError("❌ ModelLoader not found in scene!");
//     }

//     void Update()
//     {
//         if (Input.touchCount == 0) return;

//         // 🔹 Two-finger gestures → Scale + Rotate
//         if (Input.touchCount == 2 && _spawned != null)
//         {
//             HandlePinchRotate();
//             return;
//         }

//         Touch touch = Input.GetTouch(0);

//         if (touch.phase == TouchPhase.Began)
//         {
//             if (_spawned != null)
//                 _isDragging = TryTouchHitsModel(touch.position);
//         }

//         if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
//         {
//             if (_raycastManager.Raycast(touch.position, _hits, TrackableType.PlaneWithinPolygon))
//             {
//                 Pose pose = _hits[0].pose;

//                 // ✅ Place first time
//                 if (_spawned == null && _loader != null && _loader.LastLoadedModel != null)
//                 {
//                     _spawned = _loader.LastLoadedModel;
//                     _spawned.transform.SetParent(null); // detach from loader's hidden container
//                     _spawned.transform.position = pose.position;
//                     _spawned.transform.rotation = pose.rotation;

//                     Debug.Log("📦 Model placed in AR scene.");
//                 }
//                 // ✅ Reposition if dragging enabled
//                 else if (_spawned != null && allowReposition && _isDragging)
//                 {
//                     _spawned.transform.position = pose.position;
//                 }
//             }
//         }

//         if (touch.phase == TouchPhase.Ended)
//         {
//             _isDragging = false;
//         }
//     }

//     private bool TryTouchHitsModel(Vector2 screenPos)
//     {
//         if (_cam == null) return false;

//         Ray ray = _cam.ScreenPointToRay(screenPos);
//         if (Physics.Raycast(ray, out RaycastHit hit, 100f, selectableLayers))
//         {
//             return hit.transform == _spawned?.transform ||
//                    (_spawned != null && hit.transform.IsChildOf(_spawned.transform));
//         }
//         return false;
//     }

//     private void HandlePinchRotate()
//     {
//         Touch t0 = Input.GetTouch(0);
//         Touch t1 = Input.GetTouch(1);

//         if (t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began)
//         {
//             _startPinchDist = Vector2.Distance(t0.position, t1.position);
//             _startAngle = Vector2.SignedAngle(t1.position - t0.position, Vector2.right);
//             _startScale = _spawned.transform.localScale;
//             return;
//         }

//         float currDist = Vector2.Distance(t0.position, t1.position);
//         float currAngle = Vector2.SignedAngle(t1.position - t0.position, Vector2.right);

//         // 🔹 Scale
//         if (_startPinchDist > 0.001f)
//         {
//             float factor = currDist / _startPinchDist;
//             Vector3 targetScale = _startScale * factor;
//             float clamped = Mathf.Clamp(targetScale.x, scaleLimits.x, scaleLimits.y);
//             _spawned.transform.localScale = new Vector3(clamped, clamped, clamped);
//         }

//         // 🔹 Rotate
//         float deltaAngle = Mathf.DeltaAngle(_startAngle, currAngle);
//         _spawned.transform.Rotate(0f, deltaAngle * rotationMultiplier, 0f, Space.World);

//         // Refresh gesture base values
//         _startPinchDist = currDist;
//         _startAngle = currAngle;
//         _startScale = _spawned.transform.localScale;
//     }
// }



// using System.Collections.Generic;
// using UnityEngine;
// using UnityEngine.XR.ARFoundation;
// using UnityEngine.XR.ARSubsystems;

// public class ModelPlacement : MonoBehaviour
// {
//     [Header("Placement")]
//     [Tooltip("Allow user to drag and reposition model after placement")]
//     public bool allowReposition = true;

//     [Tooltip("Layers that can be touched to select the model")]
//     public LayerMask selectableLayers = ~0;

//     [Header("Scaling & Rotation")]
//     public Vector2 scaleLimits = new Vector2(0.1f, 3.0f);
//     public float rotationMultiplier = 0.5f;

//     private ARRaycastManager _raycastManager;
//     private Camera _cam;
//     private GameObject _spawned;
//     private bool _isDragging;
//     private bool _placementMode = false; // 🔹 New: controlled by Spawn button

//     // gesture helpers
//     private float _startPinchDist;
//     private float _startAngle;
//     private Vector3 _startScale;

//     private static readonly List<ARRaycastHit> _hits = new List<ARRaycastHit>();

//     // Reference to loader
//     private ModelLoader _loader;

//     void Awake()
//     {
//         _raycastManager = GetComponent<ARRaycastManager>();
//         _cam = Camera.main;

//         if (_raycastManager == null)
//             Debug.LogError("❌ Missing ARRaycastManager. Add it to AR Session Origin.");
//         if (_cam == null)
//             Debug.LogWarning("⚠️ No Camera tagged MainCamera. Please tag your AR Camera.");

//         _loader = FindFirstObjectByType<ModelLoader>();
//         if (_loader == null)
//             Debug.LogError("❌ ModelLoader not found in scene!");
//     }

//     void Update()
//     {
//         if (Input.touchCount == 0) return;

//         // 🔹 Two-finger gestures → Scale + Rotate
//         if (Input.touchCount == 2 && _spawned != null)
//         {
//             HandlePinchRotate();
//             return;
//         }

//         Touch touch = Input.GetTouch(0);

//         // 🔹 Placement Mode — wait for tap on plane to place model
//         if (_placementMode && touch.phase == TouchPhase.Began)
//         {
//             if (_raycastManager.Raycast(touch.position, _hits, TrackableType.PlaneWithinPolygon))
//             {
//                 Pose pose = _hits[0].pose;
//                 PlaceModelAtPose(pose);
//                 _placementMode = false; // exit placement mode after placing
//             }
//             return;
//         }

//         // 🔹 Drag reposition if allowed
//         if (_spawned != null)
//         {
//             if (touch.phase == TouchPhase.Began)
//                 _isDragging = TryTouchHitsModel(touch.position);

//             if ((touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary) && _isDragging)
//             {
//                 if (_raycastManager.Raycast(touch.position, _hits, TrackableType.PlaneWithinPolygon))
//                 {
//                     Pose pose = _hits[0].pose;
//                     if (allowReposition)
//                         _spawned.transform.position = pose.position;
//                 }
//             }

//             if (touch.phase == TouchPhase.Ended)
//                 _isDragging = false;
//         }
//     }

//     // 🔹 Called by Spawn Button in UI
//     public void EnablePlacementMode()
//     {
//         Debug.Log("🟢 Placement mode enabled. Tap on plane to place model.");
//         _placementMode = true;
//     }

//     // 🔹 Called by Cancel Button if needed
//     public void CancelPlacementMode()
//     {
//         Debug.Log("🔴 Placement mode cancelled.");
//         _placementMode = false;
//     }

//     private void PlaceModelAtPose(Pose pose)
//     {
//         if (_loader == null || _loader.LastLoadedModel == null)
//         {
//             Debug.LogWarning("⚠️ No model loaded to place.");
//             return;
//         }

//         _spawned = _loader.LastLoadedModel;
//         _spawned.transform.SetParent(null);
//         _spawned.transform.position = pose.position;
//         _spawned.transform.rotation = pose.rotation;
//         _spawned.SetActive(true);

//         // 🔹 Add colliders to all meshes
//         AddMeshColliders(_spawned);

//         // 🔹 Ensure placed model uses selectable layer
//         int placedLayer = Mathf.RoundToInt(Mathf.Log(selectableLayers.value, 2));
//         if (placedLayer >= 0)
//             SetLayerRecursively(_spawned, placedLayer);

//         Debug.Log("📦 Model placed in AR scene.");
//     }

//     private bool TryTouchHitsModel(Vector2 screenPos)
//     {
//         if (_cam == null) return false;

//         Ray ray = _cam.ScreenPointToRay(screenPos);
//         if (Physics.Raycast(ray, out RaycastHit hit, 100f, selectableLayers))
//         {
//             return hit.transform == _spawned?.transform ||
//             (_spawned != null && hit.transform.IsChildOf(_spawned.transform));
//         }
//         return false;
//     }

//     private void HandlePinchRotate()
//     {
//         Touch t0 = Input.GetTouch(0);
//         Touch t1 = Input.GetTouch(1);

//         if (t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began)
//         {
//             _startPinchDist = Vector2.Distance(t0.position, t1.position);
//             _startAngle = Vector2.SignedAngle(t1.position - t0.position, Vector2.right);
//             _startScale = _spawned.transform.localScale;
//             return;
//         }

//         float currDist = Vector2.Distance(t0.position, t1.position);
//         float currAngle = Vector2.SignedAngle(t1.position - t0.position, Vector2.right);

//         // 🔹 Scale
//         if (_startPinchDist > 0.001f)
//         {
//             float factor = currDist / _startPinchDist;
//             Vector3 targetScale = _startScale * factor;
//             float clamped = Mathf.Clamp(targetScale.x, scaleLimits.x, scaleLimits.y);
//             _spawned.transform.localScale = new Vector3(clamped, clamped, clamped);
//         }

//         // 🔹 Rotate
//         float deltaAngle = Mathf.DeltaAngle(_startAngle, currAngle);
//         _spawned.transform.Rotate(0f, deltaAngle * rotationMultiplier, 0f, Space.World);

//         _startPinchDist = currDist;
//         _startAngle = currAngle;
//         _startScale = _spawned.transform.localScale;
//     }

//     // 🔹 Add colliders to all meshes in loaded model
//     private void AddMeshColliders(GameObject root)
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

//     private void SetLayerRecursively(GameObject go, int layer)
//     {
//         go.layer = layer;
//         foreach (Transform t in go.transform)
//             SetLayerRecursively(t.gameObject, layer);
//     }
// }


//19th dec- updated

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ModelPlacement : MonoBehaviour
{
    public ARRaycastManager raycastManager;
    public ModelLoader modelLoader;
    public ARSceneInitializer sceneInitializer;

    static List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private bool placed = false;
    private bool placementModeActive = false;

    void Start()
    {
        if (raycastManager == null)
            raycastManager = FindFirstObjectByType<ARRaycastManager>();

        if (modelLoader == null)
            modelLoader = FindFirstObjectByType<ModelLoader>();

        if (sceneInitializer == null)
            sceneInitializer = FindFirstObjectByType<ARSceneInitializer>();
    }

    // Called by the Spawn button in ARScene UI
    public void OnSpawnButtonPressed()
    {
        if (modelLoader == null || modelLoader.LastLoadedModel == null)
        {
            Debug.LogError("[ModelPlacement] Spawn pressed but no model is loaded.");
            return;
        }

        if (sceneInitializer != null && !sceneInitializer.IsModelReady)
            Debug.LogWarning("[ModelPlacement] Model not ready yet — placement mode active, tap will be ignored until ready.");

        // Check how many AR planes are currently detected
        var planeManager = FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARPlaneManager>();
        int planeCount = 0;
        if (planeManager != null)
            foreach (var _ in planeManager.trackables) planeCount++;

        if (planeCount > 0)
            Debug.Log($"[ModelPlacement] Spawn pressed — {planeCount} plane(s) detected. Tap a surface to place.");
        else
            Debug.LogWarning("[ModelPlacement] Spawn pressed — NO planes detected yet. Move your device slowly over a flat surface.");

        placementModeActive = true;
    }

    void Update()
    {
        if (!placementModeActive) return;
        if (Input.touchCount == 0) return;

        Touch touch = Input.GetTouch(0);
        if (touch.phase != TouchPhase.Began) return;

        if (sceneInitializer != null && !sceneInitializer.IsModelReady)
        {
            Debug.Log("[ModelPlacement] Model still loading, tap ignored.");
            return;
        }

        if (raycastManager.Raycast(touch.position, hits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hits[0].pose;
            PlaceModel(hitPose);
        }
        else
        {
            Debug.Log("[ModelPlacement] Tap did not hit a detected plane. Keep scanning the surface.");
        }
    }

    void PlaceModel(Pose pose)
    {
        if (modelLoader == null || modelLoader.LastLoadedModel == null)
        {
            Debug.LogWarning("[ModelPlacement] Model not loaded yet.");
            return;
        }

        GameObject model = modelLoader.LastLoadedModel;

        model.SetActive(true);
        model.transform.position = pose.position;
        model.transform.rotation = pose.rotation;

        if (!placed)
        {
            NormalizeScale(model);
            placed = true;
        }

        placementModeActive = false;
        Debug.Log("[ModelPlacement] Model successfully placed in AR at: " + pose.position);
    }

    void NormalizeScale(GameObject model)
    {
        // Reset to identity first so bounds are measured at a known clean scale,
        // avoiding compounding errors from glTFast nested transform scales.
        model.transform.localScale = Vector3.one;

        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        // Use local-space bounds relative to the model root to avoid
        // world-position offset skewing the size measurement.
        Vector3 min = Vector3.one * float.MaxValue;
        Vector3 max = Vector3.one * float.MinValue;
        foreach (var r in renderers)
        {
            Bounds wb = r.bounds;
            Vector3 c = wb.center, e = wb.extents;
            Vector3[] corners = {
                new Vector3(c.x+e.x, c.y+e.y, c.z+e.z), new Vector3(c.x+e.x, c.y+e.y, c.z-e.z),
                new Vector3(c.x+e.x, c.y-e.y, c.z+e.z), new Vector3(c.x+e.x, c.y-e.y, c.z-e.z),
                new Vector3(c.x-e.x, c.y+e.y, c.z+e.z), new Vector3(c.x-e.x, c.y+e.y, c.z-e.z),
                new Vector3(c.x-e.x, c.y-e.y, c.z+e.z), new Vector3(c.x-e.x, c.y-e.y, c.z-e.z)
            };
            foreach (var corner in corners)
            {
                Vector3 lp = model.transform.InverseTransformPoint(corner);
                min = Vector3.Min(min, lp);
                max = Vector3.Max(max, lp);
            }
        }

        Vector3 localSize = max - min;
        float maxDim = Mathf.Max(localSize.x, localSize.y, localSize.z);
        if (maxDim <= 0f) return;

        float targetSize = 0.5f; // meters
        // Assign absolute scale directly — never multiply into an unknown existing scale.
        model.transform.localScale = Vector3.one * (targetSize / maxDim);
    }
}
