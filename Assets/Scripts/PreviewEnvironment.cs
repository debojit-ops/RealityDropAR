using UnityEngine;

/// <summary>
/// The preview "showroom": neon turntable + contact shadow under the model, a studio backdrop,
/// switchable scenes (Studio / Room / Outdoor) and lighting presets (Soft / Bright / Dramatic).
/// Shaders live in Assets/Resources/RealityDropUI so they always ship in builds.
/// </summary>
public class PreviewEnvironment : MonoBehaviour
{
    public enum SceneMode { Studio, Room, Outdoor }
    public enum LightMode { Soft, Bright, Dramatic }

    public static readonly string[] SceneNames = { "Studio", "Room", "Outdoor" };
    public static readonly string[] LightNames = { "Soft", "Bright", "Dramatic" };

    public SceneMode CurrentScene { get; private set; } = SceneMode.Studio;
    public LightMode CurrentLight { get; private set; } = LightMode.Soft;

    private Camera cam;
    private Transform modelParent;
    private Light keyLight;
    private GameObject turntable;
    private Material turntableMaterial;
    private GameObject backdrop;
    private Material roomSkybox, outdoorSkybox, originalSkybox;
    private float pulseTime;

    public void Init(Camera camera, Transform parent)
    {
        cam = camera;
        modelParent = parent;
        originalSkybox = RenderSettings.skybox;
        roomSkybox = Resources.Load<Material>("Skybox");
        outdoorSkybox = Resources.Load<Material>("Skybox 1");
        if (roomSkybox == null) roomSkybox = originalSkybox;

        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { keyLight = l; break; }

        CreateBackdrop();
        SetScene(SceneMode.Studio);
        SetLight(LightMode.Soft);
    }

    /// <summary>Places and sizes the turntable under the loaded model.</summary>
    public void FitTurntable(Bounds boundsInParent)
    {
        if (modelParent == null) return;
        if (turntable == null) CreateTurntable();
        float footprint = Mathf.Max(boundsInParent.extents.x, boundsInParent.extents.z);
        float radius = Mathf.Max(0.2f, footprint * 1.25f);
        turntable.transform.SetParent(modelParent.parent, false);
        turntable.transform.position = modelParent.TransformPoint(new Vector3(0f, boundsInParent.min.y, 0f)) + Vector3.up * 0.002f;
        turntable.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        // Quad is 1x1; ring sits at 0.86 of the half-size
        float size = radius * 2f / 0.86f;
        turntable.transform.localScale = new Vector3(size, size, 1f);
        turntable.SetActive(true);
    }

    public SceneMode NextScene()
    {
        SetScene((SceneMode)(((int)CurrentScene + 1) % 3));
        return CurrentScene;
    }

    public LightMode NextLight()
    {
        SetLight((LightMode)(((int)CurrentLight + 1) % 3));
        return CurrentLight;
    }

    public void SetScene(SceneMode mode)
    {
        CurrentScene = mode;
        if (cam == null) return;

        switch (mode)
        {
            case SceneMode.Studio:
                // Keep the room HDRI for lighting/reflections, but show the dark neon studio
                if (roomSkybox != null) RenderSettings.skybox = roomSkybox;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.016f, 0.031f, 0.051f, 1f);
                if (backdrop != null) backdrop.SetActive(true);
                break;
            case SceneMode.Room:
                if (roomSkybox != null) RenderSettings.skybox = roomSkybox;
                cam.clearFlags = CameraClearFlags.Skybox;
                if (backdrop != null) backdrop.SetActive(false);
                break;
            case SceneMode.Outdoor:
                RenderSettings.skybox = outdoorSkybox != null ? outdoorSkybox : roomSkybox;
                cam.clearFlags = CameraClearFlags.Skybox;
                if (backdrop != null) backdrop.SetActive(false);
                break;
        }
        DynamicGI.UpdateEnvironment();

        // Neon ring reads best in the studio; softer in real environments
        if (turntableMaterial != null)
        {
            turntableMaterial.SetFloat("_GlowStrength", mode == SceneMode.Studio ? 0.6f : 0.35f);
            turntableMaterial.SetFloat("_PlatformAlpha", mode == SceneMode.Studio ? 0.35f : 0.18f);
        }
    }

    public void SetLight(LightMode mode)
    {
        CurrentLight = mode;
        if (keyLight == null) return;
        switch (mode)
        {
            case LightMode.Soft:
                keyLight.intensity = 1.0f;
                keyLight.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                RenderSettings.ambientIntensity = 1.1f;
                break;
            case LightMode.Bright:
                keyLight.intensity = 1.6f;
                keyLight.transform.rotation = Quaternion.Euler(60f, 20f, 0f);
                RenderSettings.ambientIntensity = 1.35f;
                break;
            case LightMode.Dramatic:
                keyLight.intensity = 1.3f;
                keyLight.transform.rotation = Quaternion.Euler(18f, -125f, 0f);
                RenderSettings.ambientIntensity = 0.55f;
                break;
        }
    }

    void Update()
    {
        if (turntableMaterial == null) return;
        pulseTime += Time.unscaledDeltaTime;
        turntableMaterial.SetFloat("_Pulse", 0.5f + 0.5f * Mathf.Sin(pulseTime * 2f));
    }

    void OnDestroy()
    {
        if (originalSkybox != null) RenderSettings.skybox = originalSkybox;
        if (turntableMaterial != null) Destroy(turntableMaterial);
    }

    // ------------------------------------------------------------------ builders

    private void CreateTurntable()
    {
        var shader = Resources.Load<Shader>("RealityDropUI/Turntable");
        if (shader == null) { Debug.LogWarning("[PreviewEnvironment] Turntable shader missing."); return; }
        turntable = GameObject.CreatePrimitive(PrimitiveType.Quad);
        turntable.name = "NeonTurntable";
        Destroy(turntable.GetComponent<Collider>());
        turntableMaterial = new Material(shader);
        var r = turntable.GetComponent<MeshRenderer>();
        r.sharedMaterial = turntableMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        turntable.SetActive(false);
        SetScene(CurrentScene); // apply glow settings
    }

    private void CreateBackdrop()
    {
        var shader = Resources.Load<Shader>("RealityDropUI/Backdrop");
        if (shader == null || cam == null) return;
        backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backdrop.name = "StudioBackdrop";
        Destroy(backdrop.GetComponent<Collider>());
        var mat = new Material(shader);
        mat.SetFloat("_Aspect", Screen.width > 0 ? (float)Screen.height / Screen.width : 2.1f);
        var r = backdrop.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        // Fill the view, just inside the far plane, always facing the camera
        backdrop.transform.SetParent(cam.transform, false);
        float d = cam.farClipPlane * 0.9f;
        float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * Mathf.Max(0.3f, cam.aspect);
        backdrop.transform.localPosition = new Vector3(0f, 0f, d);
        backdrop.transform.localRotation = Quaternion.identity;
        backdrop.transform.localScale = new Vector3(w * 1.1f, h * 1.1f, 1f);
    }
}
