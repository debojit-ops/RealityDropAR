using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Showroom orbit camera for the preview screen.
///  - 1 finger / mouse drag: orbit around the model (model stays upright on its turntable)
///  - flick: keeps spinning with inertia
///  - 2-finger pinch / mouse wheel: zoom (clamped to the model's size)
///  - double tap: reset to the default 3/4 view
///  - auto-spin: slow turntable orbit, paused while the user interacts
///  - touches that start on UI buttons are ignored
/// ModelPreviewManager calls FrameModel() once the model is loaded and normalized.
/// </summary>
public class PreviewControls : MonoBehaviour
{
    public Transform target;
    public Camera cam;

    [Header("Rotate")]
    [Tooltip("Degrees of orbit per screen width dragged.")]
    public float orbitDegreesPerScreen = 260f;
    public float rotationSpeed = 200f; // legacy (kept so the scene's serialized value still loads)
    public bool invertY = true;        // legacy
    public float minPitch = 2f;
    public float maxPitch = 60f;
    [Tooltip("How quickly a flick slows down (higher = stops sooner).")]
    public float inertiaDamping = 4f;

    [Header("Zoom")]
    public float scrollZoomSpeed = 2.0f;
    public float pinchZoomScale = 0.01f; // legacy
    public float minDistance = 0.5f;
    public float maxDistance = 15f;

    [Header("Default view")]
    public float defaultYaw = 35f;
    public float defaultPitch = 14f;

    [Header("Auto spin")]
    public bool autoSpin = true;
    public float autoSpinSpeed = 18f;
    public float autoSpinResumeDelay = 2.5f;

    [Header("Smoothing")]
    public float smoothing = 12f;

    /// <summary>Raised on the first user interaction (used to hide the gesture hint).</summary>
    public event System.Action OnUserInteracted;

    private float yaw, pitch, distance;
    private float targetYaw, targetPitch, targetDistance;
    private float yawVelocity;
    private float defaultDistance = 3f;
    private Vector3 focusPoint;
    private bool framed;
    private bool dragging;
    private bool pointerStartedOnUI;
    private float lastInteractionTime = -999f;
    private float lastTapTime = -999f;
    private Vector2 lastPointer;
    private float lastPinchDistance;
    private bool interacted;

    private void Start()
    {
        if (!cam) cam = Camera.main;
        if (target == null) target = transform;
        focusPoint = target.position;

        // Until the model is framed, keep the camera where the scene put it
        if (cam)
        {
            Vector3 offset = cam.transform.position - focusPoint;
            distance = targetDistance = Mathf.Max(0.5f, offset.magnitude);
        }
        yaw = targetYaw = defaultYaw;
        pitch = targetPitch = defaultPitch;
    }

    /// <summary>Called after the model is loaded: centers the orbit on it and sets the zoom range.</summary>
    public void FrameModel(Bounds boundsInTargetSpace)
    {
        if (target == null) return;
        focusPoint = target.TransformPoint(new Vector3(0f, boundsInTargetSpace.center.y, 0f));
        float radius = Mathf.Max(0.05f, boundsInTargetSpace.extents.magnitude);
        float fov = cam != null ? cam.fieldOfView : 60f;
        defaultDistance = radius / Mathf.Sin(Mathf.Deg2Rad * fov * 0.5f) * 1.15f;
        minDistance = radius * 1.1f;
        maxDistance = defaultDistance * 2.5f;
        framed = true;
        ResetView(immediate: true);
    }

    public void ResetView(bool immediate = false)
    {
        targetYaw = defaultYaw;
        targetPitch = defaultPitch;
        targetDistance = defaultDistance;
        yawVelocity = 0f;
        if (immediate)
        {
            yaw = targetYaw;
            pitch = targetPitch;
            distance = targetDistance;
            ApplyCamera();
        }
    }

    public void SetAutoSpin(bool on) => autoSpin = on;

    public void ZoomBy(float delta)
    {
        targetDistance = Mathf.Clamp(targetDistance + delta * defaultDistance * 0.25f, minDistance, maxDistance);
    }

    public void ResetDistance(float newDist) => ResetView();

    private void Update()
    {
        if (!cam || !target) return;
        float dt = Time.unscaledDeltaTime;

        HandleInput();

        // Inertia after a flick
        if (!dragging && Mathf.Abs(yawVelocity) > 0.01f)
        {
            targetYaw += yawVelocity * dt;
            yawVelocity = Mathf.Lerp(yawVelocity, 0f, 1f - Mathf.Exp(-inertiaDamping * dt));
        }

        // Turntable spin when idle
        if (autoSpin && framed && !dragging && Time.unscaledTime - lastInteractionTime > autoSpinResumeDelay)
            targetYaw += autoSpinSpeed * dt;

        targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);
        targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);

        float k = 1f - Mathf.Exp(-smoothing * dt);
        yaw = Mathf.Lerp(yaw, targetYaw, k);
        pitch = Mathf.Lerp(pitch, targetPitch, k);
        distance = Mathf.Lerp(distance, targetDistance, k);
        ApplyCamera();
    }

    private void ApplyCamera()
    {
        if (!cam) return;
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.position = focusPoint + rot * new Vector3(0f, 0f, -distance);
        cam.transform.rotation = Quaternion.LookRotation(focusPoint - cam.transform.position, Vector3.up);
    }

    // ------------------------------------------------------------------ input

    private void HandleInput()
    {
        if (Input.touchSupported && Input.touchCount > 0)
        {
            HandleTouches();
            return;
        }
        HandleMouse();
    }

    private void HandleTouches()
    {
        if (Input.touchCount == 1)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
            {
                pointerStartedOnUI = IsOverUI(t.fingerId);
                if (pointerStartedOnUI) return;
                BeginDrag(t.position);
                if (Time.unscaledTime - lastTapTime < 0.3f) { ResetView(); lastTapTime = -999f; }
                else lastTapTime = Time.unscaledTime;
            }
            else if ((t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary) && dragging)
            {
                Drag(t.position);
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                dragging = false;
            }
        }
        else if (Input.touchCount >= 2)
        {
            dragging = false;
            yawVelocity = 0f;
            Touch t0 = Input.GetTouch(0), t1 = Input.GetTouch(1);
            if (IsOverUI(t0.fingerId) && IsOverUI(t1.fingerId)) return;
            float d = Vector2.Distance(t0.position, t1.position);
            if (t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began || lastPinchDistance <= 0f)
            {
                lastPinchDistance = d;
                return;
            }
            if (d > 1f)
            {
                targetDistance *= lastPinchDistance / d;
                MarkInteraction();
            }
            lastPinchDistance = d;
            return;
        }
        lastPinchDistance = 0f;
    }

    private void HandleMouse()
    {
        if (Input.GetMouseButtonDown(0))
        {
            pointerStartedOnUI = IsOverUI(-1);
            if (!pointerStartedOnUI)
            {
                BeginDrag(Input.mousePosition);
                if (Time.unscaledTime - lastTapTime < 0.3f) { ResetView(); lastTapTime = -999f; }
                else lastTapTime = Time.unscaledTime;
            }
        }
        else if (Input.GetMouseButton(0) && dragging)
        {
            Drag(Input.mousePosition);
        }
        else if (Input.GetMouseButtonUp(0))
        {
            dragging = false;
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f && !IsOverUI(-1))
        {
            targetDistance *= 1f - scroll * scrollZoomSpeed * 0.5f;
            MarkInteraction();
        }
    }

    private void BeginDrag(Vector2 position)
    {
        dragging = true;
        lastPointer = position;
        yawVelocity = 0f;
        MarkInteraction();
    }

    private void Drag(Vector2 position)
    {
        Vector2 delta = position - lastPointer;
        lastPointer = position;
        float degPerPixel = orbitDegreesPerScreen / Mathf.Max(1f, Screen.width);
        float dYaw = delta.x * degPerPixel;
        float dPitch = -delta.y * degPerPixel * 0.6f;
        targetYaw += dYaw;
        targetPitch += dPitch;
        float dt = Mathf.Max(0.001f, Time.unscaledDeltaTime);
        yawVelocity = Mathf.Lerp(yawVelocity, dYaw / dt, 0.5f);
        MarkInteraction();
    }

    private void MarkInteraction()
    {
        lastInteractionTime = Time.unscaledTime;
        if (!interacted)
        {
            interacted = true;
            OnUserInteracted?.Invoke();
        }
    }

    private static bool IsOverUI(int pointerId)
    {
        var es = EventSystem.current;
        if (es == null) return false;
        return pointerId < 0 ? es.IsPointerOverGameObject() : es.IsPointerOverGameObject(pointerId);
    }
}
