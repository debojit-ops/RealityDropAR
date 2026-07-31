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
    public ARPlaneManager planeManager;
    public ARAnchorManager anchorManager;
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

    // Combined trackable types for broad plane detection
    private const TrackableType BroadPlaneTypes = TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds | TrackableType.Planes;

    // --- State Variables ---
    public PlacementState CurrentState { get; private set; } = PlacementState.Scanning;
    public bool IsPlaced => CurrentState == PlacementState.Placed;

    private GameObject reticleInstance;
    private Pose lastReticlePose;
    private ARPlane lastReticlePlane;
    private bool hasReticlePose = false;

    private GameObject placedModel;
    private ARAnchor currentAnchor;
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

        if (planeManager == null)
            planeManager = FindFirstObjectByType<ARPlaneManager>();

        if (anchorManager == null)
            anchorManager = FindFirstObjectByType<ARAnchorManager>();

        if (anchorManager == null)
        {
            var origin = raycastManager != null ? raycastManager.gameObject : gameObject;
            anchorManager = origin.AddComponent<ARAnchorManager>();
            Debug.Log("[ModelPlacement] Added ARAnchorManager dynamically to " + origin.name);
        }

        if (modelLoader == null)
            modelLoader = FindFirstObjectByType<ModelLoader>();

        if (sceneInitializer == null)
            sceneInitializer = FindFirstObjectByType<ARSceneInitializer>();

        InitializeReticle();
        SetState(PlacementState.Scanning);
        Debug.Log("[ModelPlacement] Anti-Drift AR placement system initialized.");
    }

    void Update()
    {
        // 1. Update reticle raycast from screen center
        UpdateReticle();

        // 2. Handle Touch Inputs
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
            lastReticlePlane = planeManager != null ? planeManager.GetPlane(hits[0].trackableId) : null;
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
            lastReticlePlane = null;
            if (reticleInstance != null && reticleInstance.activeSelf)
                reticleInstance.SetActive(false);

            if (CurrentState != PlacementState.Scanning)
                SetState(PlacementState.Scanning);
        }
    }

    #endregion

    #region Model Placement & AR Anchor System (Anti-Drift)

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
                RepositionPlacedModel(lastReticlePose, lastReticlePlane);
            }
            else
            {
                Pose cameraFallback = GetCameraFallbackPose();
                RepositionPlacedModel(cameraFallback, null);
            }
            return;
        }

        // Primary: Place at active reticle pose
        if (hasReticlePose)
        {
            Debug.Log($"[ModelPlacement] Spawning anchored model at reticle pose: {lastReticlePose.position}");
            PlaceModelAtPose(lastReticlePose, lastReticlePlane);
            return;
        }

        // Secondary: Center screen raycast
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        if (raycastManager != null && raycastManager.Raycast(screenCenter, hits, BroadPlaneTypes))
        {
            ARPlane hitPlane = planeManager != null ? planeManager.GetPlane(hits[0].trackableId) : null;
            Debug.Log($"[ModelPlacement] Spawning anchored model via center screen raycast at: {hits[0].pose.position}");
            PlaceModelAtPose(hits[0].pose, hitPlane);
            return;
        }

        // Tertiary (Guaranteed Fallback): Place 1.5 meters directly in front of camera
        Debug.Log("[ModelPlacement] No planes hit — placing model using camera forward fallback pose.");
        Pose fallbackPose = GetCameraFallbackPose();
        PlaceModelAtPose(fallbackPose, null);
    }

    private void TryPlaceModelFromTouch(Vector2 touchPosition)
    {
        GameObject modelToPlace = GetAvailableModel();
        if (modelToPlace == null) return;

        if (raycastManager != null && raycastManager.Raycast(touchPosition, hits, BroadPlaneTypes))
        {
            ARPlane hitPlane = planeManager != null ? planeManager.GetPlane(hits[0].trackableId) : null;
            Debug.Log($"[ModelPlacement] Touch hit plane at {hits[0].pose.position}. Placing anchored model.");
            PlaceModelAtPose(hits[0].pose, hitPlane);
            return;
        }

        if (hasReticlePose)
        {
            Debug.Log($"[ModelPlacement] Touch missed plane, fallback to reticle pose at {lastReticlePose.position}.");
            PlaceModelAtPose(lastReticlePose, lastReticlePlane);
            return;
        }

        Debug.Log("[ModelPlacement] Touch placement fallback — placing in front of camera.");
        PlaceModelAtPose(GetCameraFallbackPose(), null);
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

        Vector3 targetPos = camPos + (camForward * 1.5f);
        targetPos.y = camPos.y - 0.5f;

        Quaternion targetRot = Quaternion.Euler(0, arCamera != null ? arCamera.transform.eulerAngles.y : 0, 0);
        return new Pose(targetPos, targetRot);
    }

    private void PlaceModelAtPose(Pose pose, ARPlane plane)
    {
        placedModel = GetAvailableModel();
        if (placedModel == null)
        {
            Debug.LogError("[ModelPlacement] Cannot place model — model reference is null!");
            return;
        }

        placedModel.SetActive(true);

        // Normalize initial scale & ground boundary offset
        NormalizeScaleAndCalculateGroundOffset(placedModel);

        // Attach ARAnchor to prevent drifting
        AttachModelToAnchor(placedModel, pose, plane);

        // Ensure lightweight root collider exists for touch dragging
        AddSingleRootCollider(placedModel);

        SetState(PlacementState.Placed);
        Debug.Log($"[ModelPlacement] Model anchored & placed at {pose.position}");
    }

    private void RepositionPlacedModel(Pose pose, ARPlane plane)
    {
        if (placedModel == null) return;
        AttachModelToAnchor(placedModel, pose, plane);
        Debug.Log($"[ModelPlacement] Model re-anchored to {pose.position}");
    }

    private void AttachModelToAnchor(GameObject model, Pose pose, ARPlane plane)
    {
        // Destroy existing anchor if present
        if (currentAnchor != null)
        {
            Destroy(currentAnchor);
            currentAnchor = null;
        }

        // 1. Try attaching anchor to ARPlane
        if (anchorManager != null && plane != null)
        {
            currentAnchor = anchorManager.AttachAnchor(plane, pose);
        }

        // 2. Fallback: Create free-standing ARAnchor at pose for ARFoundation 5/6
        if (currentAnchor == null)
        {
            GameObject anchorGO = new GameObject("ARAnchor_GameObject");
            anchorGO.transform.position = pose.position;
            anchorGO.transform.rotation = pose.rotation;
            currentAnchor = anchorGO.AddComponent<ARAnchor>();
        }

        // Position offset calculation for floor ground alignment
        Vector3 modelOffset = Vector3.up * (groundOffsetY * model.transform.localScale.y);
        Quaternion uprightRot = Quaternion.Euler(0, pose.rotation.eulerAngles.y, 0);

        if (currentAnchor != null)
        {
            model.transform.SetParent(currentAnchor.transform, false);
            model.transform.localPosition = modelOffset;
            model.transform.localRotation = uprightRot;
            Debug.Log("[ModelPlacement] Attached model under physical ARAnchor.");
        }
        else
        {
            model.transform.SetParent(null, true);
            model.transform.position = pose.position + modelOffset;
            model.transform.rotation = uprightRot;
            Debug.LogWarning("[ModelPlacement] Placed model without ARAnchor (fallback).");
        }
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
            r.enabled = true;
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

    #region Gesture Manipulations (Drag, Pinch Scale, Twist Rotate)

    private void HandleSingleFingerDrag(Touch touch)
    {
        if (touch.phase == TouchPhase.Began)
        {
            isDraggingModel = TouchHitsPlacedModel(touch.position);
            Debug.Log($"[ModelPlacement] Single touch Began. Touch hits model = {isDraggingModel}");
        }
        else if ((touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary) && isDraggingModel)
        {
            if (raycastManager != null && raycastManager.Raycast(touch.position, hits, BroadPlaneTypes))
            {
                Pose hitPose = hits[0].pose;
                ARPlane hitPlane = planeManager != null ? planeManager.GetPlane(hits[0].trackableId) : null;
                RepositionPlacedModel(hitPose, hitPlane);
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

        // 1. Pinch to Scale
        if (startPinchDist > 0.01f)
        {
            float scaleFactor = currentDist / startPinchDist;
            Vector3 targetScale = startModelScale * scaleFactor;
            float clampedFactor = Mathf.Clamp(targetScale.x, scaleLimits.x, scaleLimits.y);
            placedModel.transform.localScale = Vector3.one * clampedFactor;
        }

        // 2. Twist to Rotate
        float deltaAngle = Mathf.DeltaAngle(startAngle, currentAngle);
        placedModel.transform.Rotate(Vector3.up, -deltaAngle * rotationMultiplier, Space.World);

        // Refresh base gesture values
        startPinchDist = currentDist;
        startAngle = currentAngle;
        startModelScale = placedModel.transform.localScale;
    }

    private bool TouchHitsPlacedModel(Vector2 screenPos)
    {
        if (arCamera == null) arCamera = Camera.main;
        if (arCamera == null || placedModel == null) return false;

        // 1. Physics Raycast Pick
        Ray ray = arCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, selectableLayers))
        {
            if (hit.transform == placedModel.transform || hit.transform.IsChildOf(placedModel.transform))
                return true;
        }

        // 2. Fallback: Proximity distance pick on screen
        Vector3 modelScreenPos = arCamera.WorldToScreenPoint(placedModel.transform.position);
        if (modelScreenPos.z > 0)
        {
            float dist = Vector2.Distance(screenPos, new Vector2(modelScreenPos.x, modelScreenPos.y));
            if (dist < 200f) // Touch within 200 pixels of model screen center
                return true;
        }

        return false;
    }

    private void AddSingleRootCollider(GameObject root)
    {
        if (root == null) return;
        if (root.GetComponent<Collider>() != null || root.GetComponentInChildren<Collider>() != null) return;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

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
                Vector3 lp = root.transform.InverseTransformPoint(corner);
                min = Vector3.Min(min, lp);
                max = Vector3.Max(max, lp);
            }
        }

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = (min + max) * 0.5f;
        box.size = max - min;
        Debug.Log($"[ModelPlacement] Added lightweight root BoxCollider (Center: {box.center}, Size: {box.size})");
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
        if (currentAnchor != null)
        {
            Destroy(currentAnchor);
            currentAnchor = null;
        }

        if (placedModel != null)
        {
            placedModel.SetActive(false);
            placedModel = null;
        }

        SetState(PlacementState.Scanning);
    }

    #endregion
}
