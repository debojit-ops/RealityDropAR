using UnityEngine;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(ARPlane))]
[RequireComponent(typeof(MeshCollider))]
public class UpdateMeshColliderOnBoundaryChanged : MonoBehaviour
{
    ARPlane _plane;
    MeshCollider _meshCollider;
    MeshFilter _meshFilter;

    private float lastUpdateTime = 0f;
    private const float MinUpdateInterval = 0.5f; // Throttle collider updates to max twice per second

    void Awake()
    {
        _plane = GetComponent<ARPlane>();
        _meshCollider = GetComponent<MeshCollider>();
        _meshFilter = GetComponent<MeshFilter>();
    }

    void OnEnable()
    {
        _plane.boundaryChanged += OnBoundaryChanged;
    }

    void OnDisable()
    {
        _plane.boundaryChanged -= OnBoundaryChanged;
    }

    void OnBoundaryChanged(ARPlaneBoundaryChangedEventArgs args)
    {
        // Throttle PhysX mesh collider baking to eliminate CPU frame spikes
        if (Time.time - lastUpdateTime < MinUpdateInterval) return;

        if (_meshFilter != null && _meshFilter.sharedMesh != null && _meshCollider != null)
        {
            _meshCollider.sharedMesh = _meshFilter.sharedMesh;
            lastUpdateTime = Time.time;
        }
    }
}
