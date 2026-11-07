using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// AR-screen-only performance profile, so ARCore's motion tracking gets the CPU/GPU time it needs
/// (Google: "VIO frequency low" = ARCore starved → wobbly anchors, few planes).
///  • URP render scale 0.8 (camera feed + model still look sharp on a phone screen)
///  • MSAA off, HDR off, post-processing off on the AR camera
/// Everything is restored when the AR scene closes. Added by ARSceneInitializer.
/// </summary>
public class ARPerformanceProfile : MonoBehaviour
{
    [Range(0.5f, 1f)] public float arRenderScale = 0.8f;

    private UniversalRenderPipelineAsset urp;
    private float originalScale;
    private int originalMsaa;
    private bool originalHdr;
    private bool applied;

    public static void Ensure()
    {
        if (FindFirstObjectByType<ARPerformanceProfile>() == null)
            new GameObject("ARPerformanceProfile").AddComponent<ARPerformanceProfile>();
    }

    void Start()
    {
        urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null)
        {
            originalScale = urp.renderScale;
            originalMsaa = urp.msaaSampleCount;
            originalHdr = urp.supportsHDR;
            urp.renderScale = Mathf.Min(originalScale, arRenderScale);
            urp.msaaSampleCount = 1;
            urp.supportsHDR = false;
            applied = true;
        }

        var cam = Camera.main;
        if (cam != null)
        {
            cam.allowMSAA = false;
            cam.allowHDR = false;
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null)
            {
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = false;
            }
        }
        Debug.Log($"[ARPerformanceProfile] AR profile on: renderScale {(urp != null ? urp.renderScale : 0f):0.00}, MSAA off, HDR off, post off.");
    }

    void OnDestroy()
    {
        // The URP asset is shared by every scene (and is a project asset in the editor): put it back
        if (!applied || urp == null) return;
        urp.renderScale = originalScale;
        urp.msaaSampleCount = originalMsaa;
        urp.supportsHDR = originalHdr;
    }
}
