using UnityEngine;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(ARPlane))]
[RequireComponent(typeof(MeshCollider))]
public class UpdateMeshColliderOnBoundaryChanged : MonoBehaviour
{
    ARPlane _plane;
    MeshCollider _meshCollider;
    MeshFilter _meshFilter;

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
        // Assign the latest mesh to the collider
        if (_meshFilter != null && _meshFilter.sharedMesh != null)
        {
            _meshCollider.sharedMesh = _meshFilter.sharedMesh;
        }
    }
}
