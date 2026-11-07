using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Rock-solid AR placement.
///
/// Stability:
///  - Placement is only allowed once tracking has been stable for a moment, and only on
///    confirmed horizontal surfaces (inside a detected plane's polygon, never guessed planes)
///  - The model is locked to ONE ARAnchor; while dragging it moves freely and a single new
///    anchor is made when the finger lifts (no anchor churn, no leaks)
///  - The model follows its anchor through a jitter dead-zone + smoothing, so tiny tracking
///    corrections never show as shaking, and real corrections glide instead of popping
///  - If tracking is lost the model freezes in place and the HUD asks the user to move slowly
///
/// Feel:
///  - Smoothed neon reticle on the surface, scale-in animation on placement, soft contact shadow
///  - Drag the model to move it (projected onto its floor plane: smooth even past the plane edge)
///  - Tap another spot on the floor to glide it there; pinch to resize; twist to rotate
///  - Surface outlines are hidden once the model is placed
/// </summary>
public class ModelPlacement : MonoBehaviour
{
    public enum PlacementState { Initializing, Scanning, Ready, Placed }

    [Header("AR & Scene References (auto-found if empty)")]
    public ARRaycastManager raycastManager;
    public ARPlaneManager planeManager;
    public ARAnchorManager anchorManager;
    public ModelLoader modelLoader;
    public ARSceneInitializer sceneInitializer;

    [Header("Reticle (optional prefab; a neon ring is built if empty)")]
    public GameObject reticlePrefab;
    public float reticleDiameter = 0.3f;

    [Header("Model")]
    [Tooltip("Largest dimension of the model when first placed (meters).")]
    public float targetInitialSize = 0.5f;
    [Tooltip("Pinch range relative to the initial size.")]
    public Vector2 userScaleLimits = new Vector2(0.1f, 10f);
    [Tooltip("Sensitivity for 2-finger twist rotation.")]
    public float rotationMultiplier = 1f;
    public LayerMask selectableLayers = ~0; // legacy

    [Header("Stability")]
    [Tooltip("Seconds of continuous good tracking before placement is allowed.")]
    public float requiredStableTracking = 0.6f;
    [Tooltip("Smallest surface (m²) we will place on.")]
    public float minPlaneArea = 0.04f;
    [Tooltip("Anchor movements smaller than this (meters) are ignored as jitter.")]
    public float jitterDeadZone = 0.003f;
    [Tooltip("How quickly the model follows its anchor / drag target.")]
    public float followSharpness = 18f;

    // ------------------------------------------------------------------ state

    public PlacementState CurrentState { get; private set; } = PlacementState.Initializing;
    public bool IsPlaced => CurrentState == PlacementState.Placed;
    public bool IsTrackingLost { get; private set; }
    public bool IsPinching { get; private set; }
    public bool IsDragging => dragging;
    /// <summary>Current size relative to the first placement (1 = 100%).</summary>
    public float ScaleFactor => userScale;
    public string ModelTitle { get; set; }
    /// <summary>The model being placed (null until ready). Used by the animation controls.</summary>
    public GameObject Model => model != null ? model : GetModel();

    public event System.Action OnPlaced;
    public event System.Action OnUserManipulated;

    private Camera arCamera;
    private static readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

    // Reticle
    private GameObject reticle;
    private Material reticleMaterial;
    private bool hasReticlePose;
    private Pose reticleTargetPose;
    private ARPlane reticlePlane;
    private float reticleVisible;
    private float placeFlash;   // 1 → 0 after the model is placed (ring flare)
    private float stableTrackingTime;
    private bool hadTracking;

    // Model
    private GameObject model;
    private Transform modelRoot;     // world pose (follows anchor), uniform user scale
    private Transform modelPivot;    // normalized model, centered on its footprint
    private GameObject shadow;
    private Material shadowMaterial;
    private ARAnchor anchor;
    private Quaternion anchorRotationAtCreation = Quaternion.identity;
    private float userYaw;
    private float userScale = 1f;
    private float appear = 1f;
    private Vector3 followTarget;
    private bool settled;

    // Gestures
    private bool dragging;
    private Vector3 dragOffset;
    private Vector2 touchStartPos;
    private float touchStartTime;
    private bool touchStartedOnUI;
    private bool touchStartedOnModel;
    private float lastPinchDistance, lastTwistAngle;
    private Vector2 lastTwoFingerMid;

    // Planes
    private bool planesVisible = true;
    private float planeVisibilityTimer;

    // ------------------------------------------------------------------ setup

    void Awake()
    {
        // Guard against scene setup mistakes that cause drift/jitter:
        // only one placement script may run, and only the XR camera may render.
        var all = FindObjectsByType<ModelPlacement>(FindObjectsSortMode.InstanceID);
        if (all.Length > 1 && all[0] != this)
        {
            Debug.LogWarning("[ModelPlacement] Duplicate ModelPlacement found on '" + name + "'; disabling it.");
            enabled = false;
            return;
        }
        DisableStrayARCameras();
    }

    void Start()
    {
        if (!enabled) return;
        arCamera = Camera.main;
        if (raycastManager == null) raycastManager = FindFirstObjectByType<ARRaycastManager>();
        if (planeManager == null) planeManager = FindFirstObjectByType<ARPlaneManager>();
        if (anchorManager == null) anchorManager = FindFirstObjectByType<ARAnchorManager>();
        if (anchorManager == null)
        {
            var host = raycastManager != null ? raycastManager.gameObject : gameObject;
            anchorManager = host.AddComponent<ARAnchorManager>();
        }
        if (modelLoader == null) modelLoader = FindFirstObjectByType<ModelLoader>();
        if (sceneInitializer == null) sceneInitializer = FindFirstObjectByType<ARSceneInitializer>();

        // Render in step with the camera feed (smooth AR); restored to 60 fps when leaving
        var session = FindFirstObjectByType<ARSession>();
        if (session != null) session.matchFrameRateRequested = true;

        // Floors and tables only (no walls): fewer, cleaner glowing panels and faster detection
        if (planeManager != null) planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        ARGlowPlane.Visible = true;

        CreateReticle();
        SetState(PlacementState.Initializing);
    }

    void OnDestroy()
    {
        if (reticleMaterial != null) Destroy(reticleMaterial);
        if (shadowMaterial != null) Destroy(shadowMaterial);
        AppPerformanceManager.InitializePerformanceSettings(); // back to 60 fps outside AR
    }

    private void DisableStrayARCameras()
    {
        var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        Camera xrCam = origin != null ? origin.Camera : Camera.main;
        foreach (var bg in FindObjectsByType<ARCameraBackground>(FindObjectsSortMode.None))
        {
            var cam = bg.GetComponent<Camera>();
            if (cam != null && cam != xrCam)
            {
                Debug.LogWarning("[ModelPlacement] Extra AR camera on '" + cam.name + "' disabled (it caused drift).");
                bg.enabled = false;
                cam.enabled = false;
            }
        }
    }

    // ------------------------------------------------------------------ public API

    /// <summary>Places (or moves) the model at the reticle. Used by the PLACE button.</summary>
    public void PlaceAtReticle()
    {
        if (!hasReticlePose || CurrentState != PlacementState.Ready && CurrentState != PlacementState.Placed) return;
        PlaceAt(reticleTargetPose, reticlePlane);
    }

    /// <summary>Legacy name used by the old Spawn button.</summary>
    public void OnSpawnButtonPressed() => PlaceAtReticle();

    /// <summary>Removes the model so the user can scan and place again.</summary>
    public void ResetPlacement()
    {
        RemoveAnchor();
        dragging = false;
        userScale = 1f;
        if (LiteMode)
        {
            // Lite mode has no surfaces to scan: put the model back in front of the user
            if (modelRoot != null) modelRoot.gameObject.SetActive(false);
            PlaceInFrontLite();
            return;
        }
        if (modelRoot != null) modelRoot.gameObject.SetActive(false);
        SetState(PlacementState.Scanning);
    }

    // ------------------------------------------------------------------ Lite mode (no ARCore tracking)

    /// <summary>True when ARCore could not start tracking on this phone (offer Lite mode).</summary>
    public bool TrackingFailed { get; private set; }
    /// <summary>Lite mode: model shown over the live camera, phone rotation from the gyroscope, no world tracking.</summary>
    public bool LiteMode { get; private set; }

    [Header("Lite mode (fallback when AR tracking fails)")]
    [Tooltip("Seconds without any AR tracking (after the model is ready) before Lite mode is offered.")]
    public float trackingFailTimeout = 12f;
    [Tooltip("Assumed camera height above the floor in Lite mode (meters).")]
    public float liteEyeHeight = 1.3f;
    [Tooltip("Distance of the model in front of the user in Lite mode (meters).")]
    public float liteDistance = 1.5f;
    [Tooltip("Rotate the view with the gyroscope in Lite mode. Off = model stays steady on screen (no drift).")]
    public bool liteLookAround = false;

    private float noTrackingTime;

    [Header("Quick placement")]
    [Tooltip("Allow placing on ARCore feature points before a plane is confirmed (model snaps to the floor later).")]
    public bool allowFeaturePointPlacement = true;
    private bool anchoredToPlane;
    private float snapCheckTimer;
    private Quaternion liteYawOffset = Quaternion.identity;
    private float liteFloorY;

    /// <summary>
    /// Switches to Lite mode: stops following ARCore's (broken) camera pose, rotates the camera with the
    /// gyroscope instead, hides surface visuals and places the model on a virtual floor in front of the user.
    /// Drag / pinch / twist keep working; walking around the model does not (no position tracking).
    /// </summary>
    public void EnterLiteMode()
    {
        if (LiteMode || GetModel() == null) return;
        LiteMode = true;
        if (arCamera == null) arCamera = Camera.main;

        // ARCore's pose driver keeps feeding a broken pose — turn it off and drive rotation ourselves
        if (arCamera != null)
            foreach (var b in arCamera.GetComponents<Behaviour>())
                if (b != null && b.GetType().Name == "TrackedPoseDriver") b.enabled = false;

        Input.gyro.enabled = true;
        if (arCamera != null)
        {
            // Keep the current heading: align the gyro's yaw to where the camera is looking now
            float camYaw = arCamera.transform.eulerAngles.y;
            float gyroYaw = SystemInfo.supportsGyroscope ? GyroRotation().eulerAngles.y : camYaw;
            liteYawOffset = Quaternion.Euler(0f, camYaw - gyroYaw, 0f);
            liteFloorY = arCamera.transform.position.y - liteEyeHeight;
            // Level view looking slightly down at the virtual floor, so the model sits in the lower middle
            arCamera.transform.rotation = Quaternion.Euler(30f, camYaw, 0f);
        }

        RemoveAnchor();
        ARGlowPlane.Visible = false;
        if (reticle != null) reticle.SetActive(false);
        PlaceInFrontLite();
        Debug.Log("[ModelPlacement] AR tracking unavailable — Lite mode enabled (gyro rotation, virtual floor).");
    }

    /// <summary>Leaves Manual mode: camera follows ARCore again, surfaces glow, user scans and places with PLACE MODEL.</summary>
    public void ExitLiteMode()
    {
        if (!LiteMode) return;
        LiteMode = false;
        if (arCamera == null) arCamera = Camera.main;
        if (arCamera != null)
            foreach (var b in arCamera.GetComponents<Behaviour>())
                if (b != null && b.GetType().Name == "TrackedPoseDriver") b.enabled = true;

        noTrackingTime = 0f;     // give AR a fresh chance before offering manual again
        TrackingFailed = false;
        ARGlowPlane.Visible = true;
        ResetPlacement();        // back to scanning
        Debug.Log("[ModelPlacement] Manual mode off — back to AR placement.");
    }

    private void PlaceInFrontLite()
    {
        if (arCamera == null) return;
        Vector3 fwd = arCamera.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 pos = arCamera.transform.position + fwd * liteDistance;
        pos.y = liteFloorY;
        PlaceAt(new Pose(pos, Quaternion.identity), null);
    }

    /// <summary>Phone orientation from the gyroscope, converted to Unity's coordinate system (portrait).</summary>
    private static Quaternion GyroRotation()
    {
        Quaternion q = Input.gyro.attitude;
        return Quaternion.Euler(90f, 0f, 0f) * new Quaternion(q.x, q.y, -q.z, -q.w);
    }

    private void UpdateLiteCamera(float dt)
    {
        // Default: camera stays still, so the model is steady on screen (no drift). Gyro look-around is
        // optional: without position tracking it drifts when the user moves, so it is off by default.
        if (!liteLookAround || arCamera == null || !SystemInfo.supportsGyroscope) return;
        Quaternion target = liteYawOffset * GyroRotation();
        arCamera.transform.rotation = Quaternion.Slerp(arCamera.transform.rotation, target, 1f - Mathf.Exp(-20f * dt));
    }

    // ------------------------------------------------------------------ frame loop

    void Update()
    {
        if (arCamera == null) arCamera = Camera.main;
        float dt = Time.deltaTime;

        if (LiteMode)
        {
            IsTrackingLost = false;
            UpdateLiteCamera(dt);
            HandleInput();
            return;
        }

        bool sessionTracking = ARSession.state == ARSessionState.SessionTracking &&
                               ARSession.notTrackingReason == NotTrackingReason.None;
        stableTrackingTime = sessionTracking ? stableTrackingTime + dt : 0f;
        if (sessionTracking) hadTracking = true;
        bool anchorOk = anchor == null || anchor.trackingState == TrackingState.Tracking;
        // Placed: the model freezes while lost. Before placing: only "lost" after tracking had started.
        IsTrackingLost = CurrentState == PlacementState.Placed ? (!sessionTracking || !anchorOk)
                                                               : (hadTracking && !sessionTracking);

        // Never tracked at all (phone not supported / not calibrated): offer Lite mode
        bool modelReady = GetModel() != null;
        if (!hadTracking && modelReady) noTrackingTime += dt;
        TrackingFailed = !hadTracking && (ARSession.state == ARSessionState.Unsupported ||
                                          (modelReady && noTrackingTime >= trackingFailTimeout));

        UpdateReticle(sessionTracking, dt);
        HandleInput();
        UpdatePlaneVisibility(dt);
        SnapToPlaneWhenFound(dt);

        if (CurrentState == PlacementState.Initializing && GetModel() != null && stableTrackingTime > 0f)
            SetState(PlacementState.Scanning);
    }

    void LateUpdate()
    {
        if (CurrentState != PlacementState.Placed || modelRoot == null) return;
        float dt = Time.deltaTime;

        // Where the model should be: the drag point, or its anchor (frozen while tracking is lost)
        if (!LiteMode && !dragging && anchor != null && !IsTrackingLost)
        {
            followTarget = anchor.transform.position;
        }

        Vector3 delta = followTarget - modelRoot.position;
        float dist = delta.magnitude;
        if (dragging || !settled || dist > jitterDeadZone)
        {
            modelRoot.position = Vector3.Lerp(modelRoot.position, followTarget, 1f - Mathf.Exp(-followSharpness * dt));
            settled = !dragging && (followTarget - modelRoot.position).magnitude < jitterDeadZone * 0.5f;
        }

        // Rotation: user yaw, plus any drift correction the anchor itself applies (yaw only)
        Quaternion anchorDelta = anchor != null && !IsTrackingLost
            ? anchor.transform.rotation * Quaternion.Inverse(anchorRotationAtCreation)
            : Quaternion.identity;
        float anchorYaw = anchorDelta.eulerAngles.y;
        Quaternion targetRot = Quaternion.Euler(0f, userYaw + anchorYaw, 0f);
        modelRoot.rotation = Quaternion.Slerp(modelRoot.rotation, targetRot, 1f - Mathf.Exp(-followSharpness * dt));

        // Scale (with pop-in on placement)
        if (appear < 1f) appear = Mathf.Min(1f, appear + dt / 0.45f);
        float pop = EaseOutBack(appear);
        float s = userScale * Mathf.Max(0.001f, pop);
        modelRoot.localScale = Vector3.Lerp(modelRoot.localScale, Vector3.one * s, appear < 1f ? 1f : 1f - Mathf.Exp(-20f * dt));
    }

    // ------------------------------------------------------------------ reticle

    private void UpdateReticle(bool sessionTracking, float dt)
    {
        bool searching = CurrentState == PlacementState.Scanning || CurrentState == PlacementState.Ready;
        bool found = false;

        if (searching && sessionTracking && arCamera != null &&
            TryFindPlacement(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), out Pose pose, out ARPlane plane))
        {
            found = true;
            // Face the user: yaw towards the camera, flat on the surface
            Vector3 toCam = arCamera.transform.position - pose.position;
            toCam.y = 0f;
            Quaternion facing = toCam.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(-toCam.normalized, Vector3.up) : Quaternion.identity;
            reticleTargetPose = new Pose(pose.position, facing);
            reticlePlane = plane;
        }

        hasReticlePose = found;
        bool ready = found && stableTrackingTime >= requiredStableTracking && GetModel() != null;
        if (searching) SetState(ready ? PlacementState.Ready : PlacementState.Scanning);

        if (reticle == null) return;

        // Placement flash: the ring stays where the model landed, flares and expands, then fades out
        if (placeFlash > 0f)
        {
            placeFlash = Mathf.Max(0f, placeFlash - dt / 0.7f);
            float p = 1f - placeFlash; // 0 → 1
            reticle.SetActive(placeFlash > 0f);
            reticle.transform.localScale = Vector3.one * reticleDiameter * Mathf.Lerp(1f, 1.6f, EaseOutCubic(p));
            if (reticleMaterial != null)
            {
                reticleMaterial.SetFloat("_GlowStrength", 1.4f * placeFlash);
                reticleMaterial.SetColor("_RingColor", new Color(0.30f, 0.88f, 1f, 1f) * placeFlash);
                reticleMaterial.SetFloat("_PlatformAlpha", 0.25f * placeFlash);
            }
            reticleVisible = 0f;
            return;
        }

        // Smooth reticle motion (no shaking), fade/scale in and out
        reticleVisible = Mathf.MoveTowards(reticleVisible, searching && found ? 1f : 0f, dt * 5f);
        reticle.SetActive(reticleVisible > 0.001f);
        if (found)
        {
            float k = 1f - Mathf.Exp(-14f * dt);
            if (reticleVisible < 0.05f)
                reticle.transform.SetPositionAndRotation(reticleTargetPose.position, reticleTargetPose.rotation);
            else
                reticle.transform.SetPositionAndRotation(
                    Vector3.Lerp(reticle.transform.position, reticleTargetPose.position, k),
                    Quaternion.Slerp(reticle.transform.rotation, reticleTargetPose.rotation, k));
        }
        reticle.transform.localScale = Vector3.one * reticleDiameter * Mathf.Lerp(0.6f, 1f, EaseOutCubic(reticleVisible));
        if (reticleMaterial != null)
        {
            // Same neon turntable ring as the preview screen: grey while finding a spot, cyan when ready
            float t = Time.unscaledTime;
            reticleMaterial.SetFloat("_Pulse", 0.5f + 0.5f * Mathf.Sin(t * 3f));
            reticleMaterial.SetFloat("_GlowStrength", (ready ? 0.9f : 0.35f) * reticleVisible);
            reticleMaterial.SetColor("_RingColor", ready ? new Color(0.30f, 0.88f, 1f, 1f) * reticleVisible
                                                         : new Color(0.55f, 0.65f, 0.72f, 1f) * reticleVisible);
            reticleMaterial.SetFloat("_PlatformAlpha", (ready ? 0.35f : 0.1f) * reticleVisible);
        }
    }

    /// <summary>How the current placement spot was found (best → quickest).</summary>
    public enum PlacementSource { None, Plane, ExtendedPlane, FeaturePoint }
    public PlacementSource CurrentSource { get; private set; }

    /// <summary>
    /// Finds where the ring should sit, best source first:
    ///  1. inside a confirmed horizontal plane (most accurate)
    ///  2. on the infinite extension of an already-found horizontal plane (floors are flat, so the ring
    ///     can roam the whole floor as soon as one patch is known)
    ///  3. on ARCore feature points below eye level — lets the user place before any plane is confirmed
    ///     (the model then snaps onto the real floor once ARCore finds it, see SnapToPlaneWhenFound)
    /// </summary>
    private bool TryFindPlacement(Vector2 screenPoint, out Pose pose, out ARPlane plane)
    {
        CurrentSource = PlacementSource.None;
        if (TryRaycastSurface(screenPoint, out pose, out plane)) { CurrentSource = PlacementSource.Plane; return true; }

        if (raycastManager != null && raycastManager.Raycast(screenPoint, hits, TrackableType.PlaneWithinInfinity))
        {
            foreach (var hit in hits)
            {
                var p = planeManager != null ? planeManager.GetPlane(hit.trackableId) : null;
                if (p == null || p.alignment != PlaneAlignment.HorizontalUp || p.trackingState != TrackingState.Tracking) continue;
                if (hit.distance > 6f) continue; // far grazing hits on an extended plane are unreliable
                pose = hit.pose;
                plane = p;
                CurrentSource = PlacementSource.ExtendedPlane;
                return true;
            }
        }

        if (allowFeaturePointPlacement && arCamera != null &&
            raycastManager != null && raycastManager.Raycast(screenPoint, hits, TrackableType.FeaturePoint))
        {
            foreach (var hit in hits)
            {
                // Only points clearly below the eyes (floor / table height), at a sensible distance
                if (hit.pose.position.y > arCamera.transform.position.y - 0.25f) continue;
                if (hit.distance < 0.3f || hit.distance > 4f) continue;
                pose = new Pose(hit.pose.position, Quaternion.identity);
                plane = null;
                CurrentSource = PlacementSource.FeaturePoint;
                return true;
            }
        }
        return false;
    }

    /// <summary>Raycast against confirmed horizontal, upward-facing surfaces only.</summary>
    private bool TryRaycastSurface(Vector2 screenPoint, out Pose pose, out ARPlane plane)
    {
        pose = default;
        plane = null;
        if (raycastManager == null || !raycastManager.Raycast(screenPoint, hits, TrackableType.PlaneWithinPolygon))
            return false;

        foreach (var hit in hits)
        {
            var p = planeManager != null ? planeManager.GetPlane(hit.trackableId) : null;
            if (p == null) continue;
            if (p.alignment != PlaneAlignment.HorizontalUp) continue;
            if (p.trackingState != TrackingState.Tracking) continue;
            if (p.size.x * p.size.y < minPlaneArea) continue;
            pose = hit.pose;
            plane = p;
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ placement

    private void PlaceAt(Pose pose, ARPlane plane)
    {
        GameObject m = GetModel();
        if (m == null)
        {
            if (sceneInitializer != null) sceneInitializer.SetStatus("Model not ready yet.");
            return;
        }

        bool firstTime = modelRoot == null || !modelRoot.gameObject.activeSelf;
        PrepareModel(m);

        if (firstTime)
        {
            // Face the user, appear right on the spot with a pop
            Vector3 toCam = arCamera != null ? arCamera.transform.position - pose.position : Vector3.back;
            toCam.y = 0f;
            userYaw = toCam.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toCam.normalized, Vector3.up).eulerAngles.y : 0f;
            modelRoot.gameObject.SetActive(true);
            modelRoot.SetPositionAndRotation(pose.position, Quaternion.Euler(0f, userYaw, 0f));
            modelRoot.localScale = Vector3.one * 0.001f;
            appear = 0f;
        }

        if (!LiteMode) CreateAnchor(pose.position, plane); // Lite mode: nothing to anchor to
        followTarget = pose.position;
        settled = false;
        SetState(PlacementState.Placed);
        if (firstTime)
        {
            // Ring flares at the exact spot as the model drops onto it
            if (reticle != null) reticle.transform.SetPositionAndRotation(pose.position, reticle.transform.rotation);
            placeFlash = 1f;
            OnPlaced?.Invoke();
        }
    }

    /// <summary>Wraps the loaded model: normalized size, centered over its footprint, contact shadow.</summary>
    private void PrepareModel(GameObject m)
    {
        if (modelRoot != null && model == m) return;
        model = m;

        modelRoot = new GameObject("ARModelRoot").transform;
        modelPivot = new GameObject("Pivot").transform;
        modelPivot.SetParent(modelRoot, false);

        m.SetActive(true);
        m.transform.SetParent(null, true);
        m.transform.localScale = Vector3.one;
        m.transform.rotation = Quaternion.identity;
        m.transform.position = Vector3.zero;

        Bounds b = WorldBounds(m);
        float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
        float baseScale = maxDim > 1e-5f ? targetInitialSize / maxDim : 1f;

        m.transform.SetParent(modelPivot, false);
        m.transform.localScale = Vector3.one * baseScale;
        m.transform.localRotation = Quaternion.identity;
        // Bottom-center of the model sits exactly on the anchor point
        Vector3 bottomCenter = new Vector3(b.center.x, b.min.y, b.center.z) * baseScale;
        m.transform.localPosition = -bottomCenter;

        foreach (var r in m.GetComponentsInChildren<Renderer>(true)) r.enabled = true;

        // Soft contact shadow (grounds the model visually)
        var shader = Resources.Load<Shader>("RealityDropUI/Turntable");
        if (shader != null)
        {
            shadow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shadow.name = "ContactShadow";
            Destroy(shadow.GetComponent<Collider>());
            shadowMaterial = new Material(shader);
            shadowMaterial.SetColor("_RingColor", Color.black); // no ring, shadow only
            shadowMaterial.SetFloat("_GlowStrength", 0f);
            shadowMaterial.SetFloat("_PlatformAlpha", 0f);
            shadowMaterial.SetFloat("_ShadowAlpha", 0.55f);
            shadowMaterial.SetFloat("_ShadowRadius", 0.85f);
            var sr = shadow.GetComponent<MeshRenderer>();
            sr.sharedMaterial = shadowMaterial;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
            shadow.transform.SetParent(modelRoot, false);
            shadow.transform.localPosition = new Vector3(0f, 0.002f, 0f);
            shadow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            float footprint = Mathf.Max(b.size.x, b.size.z) * baseScale;
            shadow.transform.localScale = new Vector3(footprint * 1.5f, footprint * 1.5f, 1f);
        }
    }

    private void CreateAnchor(Vector3 position, ARPlane plane)
    {
        RemoveAnchor();
        var pose = new Pose(position, Quaternion.identity);
        ARAnchor created = null;

        if (anchorManager != null && plane != null)
            created = anchorManager.AttachAnchor(plane, pose);

        if (created == null)
        {
            // No plane under it: a world anchor is still far more stable than no anchor
            var go = new GameObject("ARAnchor");
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            created = go.AddComponent<ARAnchor>();
        }

        anchor = created;
        anchorRotationAtCreation = anchor != null ? anchor.transform.rotation : Quaternion.identity;
        anchoredToPlane = plane != null;
        snapCheckTimer = 0.5f;
    }

    /// <summary>
    /// Placed quickly on feature points (no plane yet)? As soon as ARCore confirms a horizontal plane
    /// right under the model, re-anchor onto it so the model sits exactly on the real floor.
    /// </summary>
    private void SnapToPlaneWhenFound(float dt)
    {
        if (!IsPlaced || anchoredToPlane || dragging || modelRoot == null || raycastManager == null) return;
        snapCheckTimer -= dt;
        if (snapCheckTimer > 0f) return;
        snapCheckTimer = 0.5f;

        var ray = new Ray(modelRoot.position + Vector3.up * 0.5f, Vector3.down);
        if (!raycastManager.Raycast(ray, hits, TrackableType.PlaneWithinInfinity)) return;
        foreach (var hit in hits)
        {
            var p = planeManager != null ? planeManager.GetPlane(hit.trackableId) : null;
            if (p == null || p.alignment != PlaneAlignment.HorizontalUp || p.trackingState != TrackingState.Tracking) continue;
            if (Mathf.Abs(hit.pose.position.y - modelRoot.position.y) > 0.3f) continue; // a different surface
            Vector3 onPlane = new Vector3(modelRoot.position.x, hit.pose.position.y, modelRoot.position.z);
            CreateAnchor(onPlane, p);
            followTarget = onPlane;
            settled = false; // glide the small correction instead of popping
            Debug.Log("[ModelPlacement] Model snapped onto the detected floor plane.");
            return;
        }
    }

    private void RemoveAnchor()
    {
        if (anchor == null) return;
        Destroy(anchor.gameObject); // removes the anchor and its GameObject (no leaks)
        anchor = null;
    }

    // ------------------------------------------------------------------ gestures

    private void HandleInput()
    {
        if (Input.touchCount == 0)
        {
            if (dragging) EndDrag();
            IsPinching = false;
            lastPinchDistance = 0f;
            HandleEditorMouse();
            return;
        }

        if (Input.touchCount >= 2)
        {
            if (dragging) EndDrag();
            if (IsPlaced) HandleTwoFingers();
            return;
        }

        IsPinching = false;
        lastPinchDistance = 0f;
        Touch t = Input.GetTouch(0);
        switch (t.phase)
        {
            case TouchPhase.Began:
                touchStartPos = t.position;
                touchStartTime = Time.unscaledTime;
                touchStartedOnUI = IsOverUI(t.fingerId);
                // Manual mode: a one-finger drag anywhere moves the model (easier than hitting it exactly)
                touchStartedOnModel = !touchStartedOnUI && IsPlaced && (LiteMode || TouchHitsModel(t.position));
                if (touchStartedOnModel) BeginDrag(t.position);
                break;
            case TouchPhase.Moved:
            case TouchPhase.Stationary:
                if (dragging) UpdateDrag(t.position);
                break;
            case TouchPhase.Ended:
                if (dragging) { EndDrag(); break; }
                if (!touchStartedOnUI) HandleTap(t.position);
                break;
            case TouchPhase.Canceled:
                if (dragging) EndDrag();
                break;
        }
    }

    // Mouse support for testing with XR Simulation in the editor
    private void HandleEditorMouse()
    {
        if (!Application.isEditor) return;
        if (Input.GetMouseButtonDown(0))
        {
            touchStartPos = Input.mousePosition;
            touchStartTime = Time.unscaledTime;
            touchStartedOnUI = IsOverUI(-1);
            touchStartedOnModel = !touchStartedOnUI && IsPlaced && (LiteMode || TouchHitsModel(Input.mousePosition));
            if (touchStartedOnModel) BeginDrag(Input.mousePosition);
        }
        else if (Input.GetMouseButton(0) && dragging) UpdateDrag(Input.mousePosition);
        else if (Input.GetMouseButtonUp(0))
        {
            if (dragging) EndDrag();
            else if (!touchStartedOnUI) HandleTap(Input.mousePosition);
        }
    }

    private void HandleTap(Vector2 position)
    {
        bool quick = Time.unscaledTime - touchStartTime < 0.35f && (position - touchStartPos).magnitude < 25f;
        if (!quick) return;

        // Before placing: the glowing ring is the indicator and only the PLACE MODEL button places,
        // so a stray tap never drops the model somewhere unexpected.
        if (IsPlaced && !touchStartedOnModel)
        {
            // Lite mode: no real surfaces — glide along the virtual floor instead
            if (LiteMode)
            {
                if (ScreenToFloor(position, out Vector3 floorPoint))
                {
                    followTarget = floorPoint;
                    settled = false;
                    OnUserManipulated?.Invoke();
                }
                return;
            }
            // Tap another spot on the floor: glide the model there
            if (TryRaycastSurface(position, out Pose pose, out ARPlane plane))
            {
                PlaceAt(pose, plane);
                OnUserManipulated?.Invoke();
            }
        }
    }

    private void BeginDrag(Vector2 screenPos)
    {
        if (!ScreenToFloor(screenPos, out Vector3 floorPoint)) return;
        dragging = true;
        dragOffset = modelRoot.position - floorPoint;
        followTarget = modelRoot.position;
        OnUserManipulated?.Invoke();
    }

    private void UpdateDrag(Vector2 screenPos)
    {
        if (ScreenToFloor(screenPos, out Vector3 floorPoint))
            followTarget = floorPoint + dragOffset;
    }

    private void EndDrag()
    {
        dragging = false;
        if (modelRoot == null) return;
        if (LiteMode) { settled = false; return; } // no anchors without tracking
        // One anchor at the final spot (attach to the surface under it when possible)
        Vector2 screen = arCamera != null ? (Vector2)arCamera.WorldToScreenPoint(followTarget) : Vector2.zero;
        ARPlane plane = null;
        Vector3 pos = followTarget;
        if (TryRaycastSurface(screen, out Pose pose, out ARPlane p))
        {
            plane = p;
            pos = new Vector3(followTarget.x, pose.position.y, followTarget.z);
        }
        CreateAnchor(pos, plane);
        followTarget = pos;
        settled = false;
    }

    /// <summary>Projects a screen point onto the horizontal plane the model stands on (smooth, no raycast noise).</summary>
    private bool ScreenToFloor(Vector2 screenPos, out Vector3 point)
    {
        point = default;
        if (arCamera == null || modelRoot == null) return false;
        Ray ray = arCamera.ScreenPointToRay(screenPos);
        var floor = new Plane(Vector3.up, new Vector3(0f, modelRoot.position.y, 0f));
        if (!floor.Raycast(ray, out float enter) || enter > 10f) return false;
        point = ray.GetPoint(enter);
        return true;
    }

    private void HandleTwoFingers()
    {
        Touch t0 = Input.GetTouch(0), t1 = Input.GetTouch(1);
        if (IsOverUI(t0.fingerId) && IsOverUI(t1.fingerId)) return;

        float dist = Vector2.Distance(t0.position, t1.position);
        float angle = Mathf.Atan2(t1.position.y - t0.position.y, t1.position.x - t0.position.x) * Mathf.Rad2Deg;

        Vector2 mid = (t0.position + t1.position) * 0.5f;

        if (t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began || lastPinchDistance <= 0f)
        {
            lastPinchDistance = dist;
            lastTwistAngle = angle;
            lastTwoFingerMid = mid;
            IsPinching = true;
            return;
        }

        if (dist > 1f)
            userScale = Mathf.Clamp(userScale * dist / lastPinchDistance, userScaleLimits.x, userScaleLimits.y);
        userYaw -= Mathf.DeltaAngle(lastTwistAngle, angle) * rotationMultiplier;

        // Manual mode: sliding two fingers up/down raises/lowers the model (free X/Y/Z placement)
        if (LiteMode && arCamera != null && modelRoot != null)
        {
            float dy = mid.y - lastTwoFingerMid.y;
            float distance = Mathf.Max(0.3f, Vector3.Distance(arCamera.transform.position, modelRoot.position));
            float metersPerPixel = 2f * Mathf.Tan(arCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance / Mathf.Max(1f, Screen.height);
            followTarget.y += dy * metersPerPixel;
            settled = false;
        }

        lastPinchDistance = dist;
        lastTwistAngle = angle;
        lastTwoFingerMid = mid;
        IsPinching = true;
        OnUserManipulated?.Invoke();
    }

    private bool TouchHitsModel(Vector2 screenPos)
    {
        if (model == null || arCamera == null) return false;
        Bounds b = WorldBounds(model);
        if (b.size == Vector3.zero) return false;

        // Screen-space box of the model's bounds (+ generous margin for fingers)
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        Vector3 c = b.center, e = b.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 sp = arCamera.WorldToScreenPoint(corner);
            if (sp.z <= 0f) continue;
            min = Vector2.Min(min, sp);
            max = Vector2.Max(max, sp);
        }
        if (min.x > max.x) return false;
        float margin = Screen.dpi > 0 ? Screen.dpi * 0.15f : 40f;
        return screenPos.x >= min.x - margin && screenPos.x <= max.x + margin &&
               screenPos.y >= min.y - margin && screenPos.y <= max.y + margin;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Detected surfaces glow (ARGlowPlane) while the user looks for a spot; they fade away once the
    /// model is placed and come back on "re-place".
    /// </summary>
    private void UpdatePlaneVisibility(float dt)
    {
        if (planeManager == null) return;
        ARGlowPlane.Visible = !IsPlaced;
        planesVisible = ARGlowPlane.Visible;

        planeVisibilityTimer -= dt;
        if (planeVisibilityTimer > 0f) return;
        planeVisibilityTimer = 0.25f; // pick up newly detected planes
        foreach (var plane in planeManager.trackables)
            if (plane.GetComponent<ARGlowPlane>() == null) plane.gameObject.AddComponent<ARGlowPlane>();
    }

    private GameObject GetModel()
    {
        if (model != null) return model;
        if (sceneInitializer != null && !sceneInitializer.IsModelReady) return null;
        if (modelLoader != null && modelLoader.LastLoadedModel != null) return modelLoader.LastLoadedModel;
        var bridge = ARModelBridge.Instance;
        if (bridge != null && bridge.LoadedGltf != null) return GameObject.Find("LoadedModel");
        return null;
    }

    private void SetState(PlacementState s)
    {
        if (CurrentState == s) return;
        CurrentState = s;
        if (sceneInitializer == null) return;
        switch (s)
        {
            case PlacementState.Initializing: sceneInitializer.SetStatus("Starting AR…"); break;
            case PlacementState.Scanning: sceneInitializer.SetStatus("Move your phone slowly to find a floor or table."); break;
            case PlacementState.Ready: sceneInitializer.SetStatus("Aim the glowing ring, then tap PLACE MODEL."); break;
            case PlacementState.Placed: sceneInitializer.SetStatus("Drag to move · Pinch to resize · Twist to rotate."); break;
        }
    }

    private void CreateReticle()
    {
        if (reticlePrefab != null)
        {
            reticle = Instantiate(reticlePrefab);
            reticle.SetActive(false);
            return;
        }
        var shader = Resources.Load<Shader>("RealityDropUI/Turntable");
        if (shader == null) return;

        reticle = new GameObject("AR_Placement_Reticle");
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(reticle.transform, false);
        quad.transform.localPosition = new Vector3(0f, 0.003f, 0f);
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        reticleMaterial = new Material(shader);
        reticleMaterial.SetFloat("_RingRadius", 0.8f);
        reticleMaterial.SetFloat("_CoreWidth", 0.025f);
        reticleMaterial.SetFloat("_GlowWidth", 0.09f);
        reticleMaterial.SetFloat("_ShadowAlpha", 0f);
        var r = quad.GetComponent<MeshRenderer>();
        r.sharedMaterial = reticleMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        reticle.SetActive(false);
    }

    private static Bounds WorldBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        Bounds b = default;
        foreach (var r in renderers)
        {
            if (r.gameObject.name == "ContactShadow") continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return any ? b : new Bounds(go.transform.position, Vector3.zero);
    }

    private static bool IsOverUI(int pointerId)
    {
        var es = EventSystem.current;
        if (es == null) return false;
        return pointerId < 0 ? es.IsPointerOverGameObject() : es.IsPointerOverGameObject(pointerId);
    }

    private static float EaseOutCubic(float t) { t = 1f - t; return 1f - t * t * t; }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.5f, c3 = c1 + 1f;
        float x = t - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }
}
