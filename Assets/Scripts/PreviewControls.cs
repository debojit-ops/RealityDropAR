using UnityEngine;

public class PreviewControls : MonoBehaviour
{
    public Transform target;     
    public Camera cam;           

    [Header("Rotate")]
    public float rotationSpeed = 200f;
    public bool invertY = true;

    [Header("Zoom")]
    public float scrollZoomSpeed = 2.0f;
    public float pinchZoomScale = 0.01f;
    public float minDistance = 0.5f;
    public float maxDistance = 15f;

    private float distance;
    private Vector3 lastMousePos;

    private void Start()
    {
        if (!cam) cam = Camera.main;
        if (cam && target)
        {
            distance = Vector3.Distance(cam.transform.position, target.position);
            if (distance <= 0.01f)
            {
                distance = 3f;
                cam.transform.position = target.position - cam.transform.forward * distance;
            }
        }
    }

    private void Update()
    {
        if (!cam || !target) return;
        HandleRotate();
        HandleZoomMouse();
        HandleZoomTouch();
    }

    private void HandleRotate()
    {
        if (Input.GetMouseButtonDown(0))
        {
            lastMousePos = Input.mousePosition;
        }
        else if (Input.GetMouseButton(0))
        {
            Vector3 delta = Input.mousePosition - lastMousePos;
            lastMousePos = Input.mousePosition;

            float yaw = -delta.x * rotationSpeed * Time.deltaTime;
            float pitch = (invertY ? delta.y : -delta.y) * rotationSpeed * Time.deltaTime;

            target.Rotate(Vector3.up, yaw, Space.World);
            target.Rotate(cam.transform.right, pitch, Space.World);
        }
    }

    private void HandleZoomMouse()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            distance = Mathf.Clamp(distance - scroll * scrollZoomSpeed, minDistance, maxDistance);
            cam.transform.position = target.position - cam.transform.forward * distance;
        }
    }

    private void HandleZoomTouch()
    {
        if (Input.touchCount != 2) return;
        Touch t0 = Input.GetTouch(0);
        Touch t1 = Input.GetTouch(1);

        Vector2 t0Prev = t0.position - t0.deltaPosition;
        Vector2 t1Prev = t1.position - t1.deltaPosition;

        float prevDist = (t0Prev - t1Prev).magnitude;
        float currDist = (t0.position - t1.position).magnitude;
        float diff = prevDist - currDist;

        distance = Mathf.Clamp(distance + diff * pinchZoomScale, minDistance, maxDistance);
        cam.transform.position = target.position - cam.transform.forward * distance;
    }

    public void ZoomBy(float delta)
    {
        distance = Mathf.Clamp(distance + delta, minDistance, maxDistance);
        if (cam && target) cam.transform.position = target.position - cam.transform.forward * distance;
    }

    public void ResetDistance(float newDist)
    {
        distance = Mathf.Clamp(newDist, minDistance, maxDistance);
        if (cam && target) cam.transform.position = target.position - cam.transform.forward * distance;
    }
}
