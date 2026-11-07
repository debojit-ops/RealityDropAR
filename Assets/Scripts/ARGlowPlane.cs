using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Replaces an AR plane's default translucent visual with a glowing neon panel
/// (bright edge of constant width, faint dot grid, soft scanning pulse — see ARGlowPlane.shader).
/// Added at runtime to every detected plane by ModelPlacement. Fades in when a plane appears and
/// fades out when it is merged into another plane, loses tracking, or the model has been placed.
/// </summary>
[RequireComponent(typeof(ARPlane))]
public class ARGlowPlane : MonoBehaviour
{
    /// <summary>Global switch (ModelPlacement turns planes off once the model is placed).</summary>
    public static bool Visible = true;

    /// <summary>Width of the glowing edge band in meters.</summary>
    public const float EdgeWidth = 0.05f;

    private static Material sharedMaterial;

    private ARPlane plane;
    private Mesh mesh;
    private MeshRenderer glowRenderer;
    private MaterialPropertyBlock block;
    private float appear;
    private float visible;

    void Awake()
    {
        plane = GetComponent<ARPlane>();

        // Turn off the prefab's default visuals (and the component that keeps re-enabling them)
        var visualizer = GetComponent<ARPlaneMeshVisualizer>();
        if (visualizer != null) visualizer.enabled = false;
        foreach (var r in GetComponents<Renderer>()) r.enabled = false;
        var line = GetComponent<LineRenderer>();
        if (line != null) line.enabled = false;

        if (sharedMaterial == null)
        {
            var shader = Resources.Load<Shader>("RealityDropUI/ARGlowPlane");
            if (shader != null) sharedMaterial = new Material(shader) { name = "ARGlowPlane (shared)" };
        }

        var child = new GameObject("NeonGlow");
        child.transform.SetParent(transform, false);
        mesh = new Mesh { name = "ARGlowPlaneMesh" };
        mesh.MarkDynamic();
        child.AddComponent<MeshFilter>().sharedMesh = mesh;
        glowRenderer = child.AddComponent<MeshRenderer>();
        glowRenderer.sharedMaterial = sharedMaterial;
        glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        glowRenderer.receiveShadows = false;
        block = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        plane.boundaryChanged += OnBoundaryChanged;
        Rebuild();
    }

    void OnDisable() => plane.boundaryChanged -= OnBoundaryChanged;

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }

    private void OnBoundaryChanged(ARPlaneBoundaryChangedEventArgs args) => Rebuild();

    /// <summary>
    /// Builds the panel mesh from the plane's boundary: an outer ring on the boundary (uv.x = 0),
    /// an inner ring inset by EdgeWidth (uv.x = 1) and a fan to the center (uv.x = 1).
    /// </summary>
    private void Rebuild()
    {
        var boundary = plane.boundary;
        int n = boundary.Length;
        if (n < 3) { mesh.Clear(); return; }

        Vector2 center = Vector2.zero;
        for (int i = 0; i < n; i++) center += boundary[i];
        center /= n;

        var verts = new Vector3[1 + 2 * n];
        var uvs = new Vector2[verts.Length];
        verts[0] = new Vector3(center.x, 0f, center.y);
        uvs[0] = new Vector2(1f, 0f);
        for (int i = 0; i < n; i++)
        {
            Vector2 p = boundary[i];
            Vector2 toCenter = center - p;
            float inset = Mathf.Min(EdgeWidth, toCenter.magnitude * 0.5f);
            Vector2 inner = p + (toCenter.sqrMagnitude > 1e-8f ? toCenter.normalized * inset : Vector2.zero);
            verts[1 + i] = new Vector3(p.x, 0f, p.y);           // boundary (plane space x,y → local x,z)
            uvs[1 + i] = new Vector2(0f, 0f);
            verts[1 + n + i] = new Vector3(inner.x, 0f, inner.y); // inner ring
            uvs[1 + n + i] = new Vector2(1f, 0f);
        }

        var tris = new int[n * 9];
        int t = 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int bi = 1 + i, bj = 1 + j, ii = 1 + n + i, ij = 1 + n + j;
            // edge band (two triangles)
            tris[t++] = bi; tris[t++] = bj; tris[t++] = ij;
            tris[t++] = bi; tris[t++] = ij; tris[t++] = ii;
            // interior fan
            tris[t++] = 0; tris[t++] = ii; tris[t++] = ij;
        }

        mesh.Clear();
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        bool show = Visible && plane.subsumedBy == null && plane.trackingState == TrackingState.Tracking;
        appear = Mathf.MoveTowards(appear, 1f, dt * 2.5f);              // fade in when first found
        visible = Mathf.MoveTowards(visible, show ? 1f : 0f, dt * 3f);  // fade out on place / merge
        glowRenderer.enabled = visible > 0.001f;
        if (!glowRenderer.enabled) return;
        block.SetFloat("_Appear", appear * appear * (3f - 2f * appear));
        block.SetFloat("_Visible", visible);
        glowRenderer.SetPropertyBlock(block);
    }
}
