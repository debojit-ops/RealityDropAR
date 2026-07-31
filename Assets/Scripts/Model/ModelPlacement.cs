using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ModelPlacement : MonoBehaviour
{
    public enum PlacementState
    {
        Scanning,
        SurfaceTargeted,
        Placed
    }

    [Header("AR & Scene References")]
    public ARRaycastManager raycastManager;
    public ModelLoader modelLoader;
    public ARSceneInitializer sceneInitializer;

    [Header("Reticle / Indicator")]
    [Tooltip("Optional prefab for placement reticle. If empty, a procedural ring reticle is created automatically.")]
    public GameObject reticlePrefab;

    [Header("Placement Configuration")]
    [Tooltip("Target normalized initial size in meters.")]
    public float targetInitialSize = 0.5f;

    [Tooltip("Minimum and maximum scale limits (meters).")]
    public Vector2 scaleLimits = new Vector2(0.05f, 5.0f);

    [Tooltip("Sensitivity for 2-finger twist rotation.")]
    public float rotationMultiplier = 0.6f;

    [Header("Selection & Layer Setup")]
    public LayerMask selectableLayers = ~0;

    // Combined trackable types for broad plane detection (Polygon + Bounds + Planes)
    private const TrackableType BroadPlaneTypes = TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds | TrackableType.Planes;

    // --- State Variables ---
    public PlacementState CurrentState { get; private set; } = PlacementState.Scanning;
    public bool IsPlaced => CurrentState == PlacementState.Placed;

    private GameObject reticleInstance;
    private Pose lastReticlePose;
    private bool hasReticlePose = false;

    private GameObject placedModel;
    private Camera arCamera;
    private static readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

    // Gesture tracking variables
    private bool isDraggingModel = false;
    private float startPinchDist = 0f;
    private float startAngle = 0f;
    private Vector3 startModelScale = Vector3.one;
    private float groundOffsetY = 0f;

    void Start()
    {
        arCamera = Camera.main;

        if (raycastManager == null)
            raycastManager = FindFirstObjectByType<ARRaycastManager>();

        if (modelLoader == null)
            modelLoader = FindFirstObjectByType<ModelLoader>();

        if (sceneInitializer == null)
            sceneInitializer = FindFirstObjectByType<ARSceneInitializer>();

        InitializeReticle();
        SetState(PlacementState.Scanning);
        Debug.Log("[ModelPlacement] Started and initialized.");
    }

    void Update()
    {
        // 1. Update reticle raycast from screen center
        UpdateReticle();

        // 2. Handle Touch Input
        if (Input.touchCount == 0) return;

        // Two-finger gestures: Pinch Scale & Twist Rotate when placed
        if (Input.touchCount == 2 && IsPlaced && placedModel != null)
        {
            HandleTwoFingerGestures();
            return;
        }

        // Single-finger gestures: Placement / Tap / Drag Reposition
        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);

            // Filter out touches directly over UI buttons
            if (IsPointerOverUI(touch)) return;

            if (CurrentState == PlacementState.Scanning || CurrentState == PlacementState.SurfaceTargeted)
            {
                if (touch.phase == TouchPhase.Began)
                {
                    Debug.Log($"[ModelPlacement] Screen tap detected at {touch.position}. Attempting placement.");
                    TryPlaceModelFromTouch(touch.position);
                }
            }
            else if (IsPlaced && placedModel != null)
            {
                HandleSingleFingerDrag(touch);
            }
        }
    }

    #region Reticle System

    private void InitializeReticle()
    {
        if (reticlePrefab != null)
        {
            reticleInstance = Instantiate(reticlePrefab);
        }
        else
        {
            reticleInstance = CreateProceduralReticle();
        }

        if (reticleInstance != null)
        {
            reticleInstance.name = "AR_Placement_Reticle";
            reticleInstance.SetActive(false);
        }
    }

    private GameObject CreateProceduralReticle()
    {
        GameObject container = new GameObject("ProceduralReticle");
        
        LineRenderer line = container.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.startWidth = 0.02f;
        line.endWidth = 0.02f;
        line.positionCount = 40;

        float radius = 0.25f;
        for (int i = 0; i < 40; i++)
        {
            float angle = i * (Mathf.PI * 2f / 40);
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0.002f, Mathf.Sin(angle) * radius));
        }

        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") 
                          ?? Shader.Find("Unlit/Color") 
                          ?? Shader.Find("Sprites/Default");
        Material mat = new Material(unlitShader);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0.1f, 0.85f, 1.0f, 0.9f));
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.1f, 0.85f, 1.0f, 0.9f));
        line.material = mat;

        GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        dot.transform.SetParent(container.transform, false);
        dot.transform.localScale = new Vector3(0.05f, 0.001f, 0.05f);
        if (dot.TryGetComponent<Collider>(out var col)) Destroy(col);
        if (dot.TryGetComponent<Renderer>(out var dotRend)) dotRend.material = mat;

        return container;
    }

    private void UpdateReticle()
    {
        if (IsPlaced)
        {
            if (reticleInstance != null && reticleInstance.activeSelf)
                reticleInstance.SetActive(false);
            return;
        }

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        if (raycastManager != null && raycastManager.Raycast(screenCenter, hits, BroadPlaneTypes))
        {
            lastReticlePose = hits[0].pose;
            hasReticlePose = true;

            if (reticleInstance != null)
            {
                reticleInstance.transform.position = lastReticlePose.position;
                reticleInstance.transform.rotation = lastReticlePose.rotation;
                if (!reticleInstance.activeSelf) reticleInstance.SetActive(true);
            }

            if (CurrentState != PlacementState.SurfaceTargeted)
                SetState(PlacementState.SurfaceTargeted);
        }
        else
        {
            hasReticlePose = false;
            if (reticleInstance != null && reticleInstance.activeSelf)
                reticleInstance.SetActive(false);

            if (CurrentState != PlacementState.Scanning)
                SetState(PlacementState.Scanning);
        }
    }

    #endregion

    #region Model Placement Logic

    public void OnSpawnButtonPressed()
    {
        Debug.Log("[ModelPlacement] Spawn Button Pressed.");

        GameObject modelToPlace = GetAvailableModel();
        if (modelToPlace == null)
        {
            Debug.LogError("[ModelPlacement] Spawn pressed but model is null!");
            if (sceneInitializer != null) sceneInitializer.SetStatus("Error: Model not ready.");
            return;
        }

        if (IsPlaced)
        {
            if (hasReticlePose)
            {
                RepositionPlacedModel(lastReticlePose);
            }
            else
            {
                Pose cameraFallback = GetCameraFallbackPose();
                RepositionPlacedModel(cameraFallback);
            }
            return;
        }

        // Primary: Place at active reticle pose
        if (hasReticlePose)
        {
            Debug.Log($"[ModelPlacement] Spawning model at reticle pose: {lastReticlePose.position}");
            PlaceModelAtPose(lastReticlePose);
            return;
        }

        // Secondary: Center screen raycast
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        if (raycastManager != null && raycastManager.Raycast(screenCenter, hits, BroadPlaneTypes))
        {
            Debug.Log($"[ModelPlacement] Spawning model via center screen raycast at: {hits[0].pose.position}");
            PlaceModelAtPose(hits[0].pose);
            return;
        }

        // Tertiary (Guaranteed Fallback): Place 1.5 meters directly in front of camera
        Debug.Log("[ModelPlacement] No planes hit — placing model using camera forward fallback pose.");
        Pose fallbackPose = GetCameraFallbackPose();
        PlaceModelAtPose(fallbackPose);
    }

    private void TryPlaceModelFromTouch(Vector2 touchPosition)
    {
        GameObject modelToPlace = GetAvailableModel();
        if (modelToPlace == null) return;

        if (raycastManager != null && raycastManager.Raycast(touchPosition, hits, BroadPlaneTypes))
        {
            Debug.Log($"[ModelPlacement] Touch hit plane at {hits[0].pose.position}. Placing model.");
            PlaceModelAtPose(hits[0].pose);
            return;
        }

        if (hasReticlePose)
        {
            Debug.Log($"[ModelPlacement] Touch missed plane, fallback to reticle pose at {lastReticlePose.position}.");
            PlaceModelAtPose(lastReticlePose);
            return;
        }

        // Guaranteed fallback if touch missed detected planes
        Debug.Log("[ModelPlacement] Touch placement fallback — placing in front of camera.");
        PlaceModelAtPose(GetCameraFallbackPose());
    }

    private GameObject GetAvailableModel()
    {
        if (modelLoader != null && modelLoader.LastLoadedModel != null)
            return modelLoader.LastLoadedModel;

        var bridge = ARModelBridge.Instance;
        if (bridge != null && bridge.LoadedGltf != null)
        {
            var found = GameObject.Find("LoadedModel");
            if (found != null) return found;
        }

        return null;
    }

    private Pose GetCameraFallbackPose()
    {
        if (arCamera == null) arCamera = Camera.main;

        Vector3 camPos = arCamera != null ? arCamera.transform.position : Vector3.zero;
        Vector3 camForward = arCamera != null ? arCamera.transform.forward : Vector3.forward;

        // Position 1.5 meters in front of camera, lowered slightly
        Vector3 targetPos = camPos + (camForward * 1.5f);
        targetPos.y = camPos.y - 0.5f;

        Quaternion targetRot = Quaternion.Euler(0, arCamera != null ? arCamera.transform.eulerAngles.y : 0, 0);
        return new Pose(targetPos, targetRot);
    }

    private void PlaceModelAtPose(Pose pose)
    {
        placedModel = GetAvailableModel();
        if (placedModel == null)
        {
            Debug.LogError("[ModelPlacement] Cannot place model — model reference is null!");
            return;
        }

        placedModel.transform.SetParent(null, true);
        placedModel.SetActive(true);

        // Calculate initial scale & ground boundary offset
        NormalizeScaleAndCalculateGroundOffset(placedModel);

        // Align model base flat on surface
        Vector3 spawnPos = pose.position + (Vector3.up * (groundOffsetY * placedModel.transform.localScale.y));
        Quaternion spawnRot = Quaternion.Euler(0, pose.rotation.eulerAngles.y, 0);

        placedModel.transform.position = spawnPos;
        placedModel.transform.rotation = spawnRot;

        // Ensure colliders are present for touch dragging
        AddCollidersRecursively(placedModel);

        SetState(PlacementState.Placed);
        Debug.Log($"[ModelPlacement] Model placed & visible at {spawnPos} (Scale: {placedModel.transform.localScale})");
    }

    private void RepositionPlacedModel(Pose pose)
    {
        if (placedModel == null) return;
        Vector3 newPos = pose.position + (Vector3.up * (groundOffsetY * placedModel.transform.localScale.y));
        placedModel.transform.position = newPos;
        Debug.Log($"[ModelPlacement] Model repositioned to {newPos}");
    }

    private void NormalizeScaleAndCalculateGroundOffset(GameObject model)
    {
        model.transform.localScale = Vector3.one;

        var renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            groundOffsetY = 0f;
            return;
        }

        Vector3 min = Vector3.one * float.MaxValue;
        Vector3 max = Vector3.one * float.MinValue;

        foreach (var r in renderers)
        {
            r.enabled = true; // Ensure renderers are enabled
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

        if (maxDim > 0f)
        {
            float desiredScale = targetInitialSize / maxDim;
            model.transform.localScale = Vector3.one * desiredScale;
        }

        groundOffsetY = -min.y;
    }

    #endregion

    #region Gesture Manipulations

    private void HandleSingleFingerDrag(Touch touch)
    {
        if (touch.phase == TouchPhase.Began)
        {
            isDraggingModel = TouchHitsPlacedModel(touch.position);
        }
        else if ((touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary) && isDraggingModel)
        {
            if (raycastManager != null && raycastManager.Raycast(touch.position, hits, BroadPlaneTypes))
            {
                Pose hitPose = hits[0].pose;
                RepositionPlacedModel(hitPose);
            }
        }
        else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
        {
            isDraggingModel = false;
        }
    }

    private void HandleTwoFingerGestures()
    {
        Touch t0 = Input.GetTouch(0);
        Touch t1 = Input.GetTouch(1);

        if (t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began)
        {
            startPinchDist = Vector2.Distance(t0.position, t1.position);
            startAngle = Vector2.SignedAngle(t1.position - t0.position, Vector2.right);
            startModelScale = placedModel.transform.localScale;
            return;
        }

        float currentDist = Vector2.Distance(t0.position, t1.position);
        float currentAngle = Vector2.SignedAngle(t1.position - t0.position, Vector2.right);

        if (startPinchDist > 0.01f)
        {
            float scaleFactor = currentDist / startPinchDist;
            Vector3 targetScale = startModelScale * scaleFactor;
            float clampedX = Mathf.Clamp(targetScale.x, scaleLimits.x, scaleLimits.y);
            placedModel.transform.localScale = new Vector3(clampedX, clampedX, clampedX);
        }

        float deltaAngle = Mathf.DeltaAngle(startAngle, currentAngle);
        placedModel.transform.Rotate(0f, -deltaAngle * rotationMultiplier, 0f, Space.World);

        startPinchDist = currentDist;
        startAngle = currentAngle;
        startModelScale = placedModel.transform.localScale;
    }

    private bool TouchHitsPlacedModel(Vector2 screenPos)
    {
        if (arCamera == null || placedModel == null) return false;

        Ray ray = arCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, selectableLayers))
        {
            return hit.transform == placedModel.transform || hit.transform.IsChildOf(placedModel.transform);
        }
        return false;
    }

    private void AddCollidersRecursively(GameObject root)
    {
        try
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.gameObject.GetComponent<Collider>() == null && mf.sharedMesh != null)
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.convex = true;
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[ModelPlacement] AddCollidersRecursively exception handled: " + ex.Message);
        }
    }

    #endregion

    #region Helper Methods & UI

    private bool IsPointerOverUI(Touch touch)
    {
        if (EventSystem.current == null) return false;
        return EventSystem.current.IsPointerOverGameObject(touch.fingerId);
    }

    private void SetState(PlacementState newState)
    {
        CurrentState = newState;

        if (sceneInitializer == null) return;

        switch (CurrentState)
        {
            case PlacementState.Scanning:
                sceneInitializer.SetStatus("Scan floor/surface with camera or tap Spawn.");
                break;
            case PlacementState.SurfaceTargeted:
                sceneInitializer.SetStatus("Surface targeted! Tap Spawn or screen to place.");
                break;
            case PlacementState.Placed:
                sceneInitializer.SetStatus("Model placed! Drag to move, 2 fingers to scale/rotate.");
                break;
        }
    }

    public void ResetPlacement()
    {
        if (placedModel != null)
        {
            placedModel.SetActive(false);
            placedModel = null;
        }

        SetState(PlacementState.Scanning);
    }

    #endregion
}
