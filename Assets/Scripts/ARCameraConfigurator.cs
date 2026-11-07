using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Picks a light ARCore/ARKit camera configuration as soon as the camera starts:
/// 30 fps and the resolution closest to 1280×720 (never a 60 fps / very high-res mode).
/// Heavier modes left ARCore processing only ~4 frames per second on device, so tracking
/// never started and no surfaces were found. Added by ARSceneInitializer.
/// </summary>
public class ARCameraConfigurator : MonoBehaviour
{
    public int preferredFramerate = 30;
    public Vector2Int preferredResolution = new Vector2Int(1280, 720);

    private ARCameraManager cameraManager;
    private bool done;

    public static void Ensure()
    {
        var cam = FindFirstObjectByType<ARCameraManager>();
        if (cam != null && cam.GetComponent<ARCameraConfigurator>() == null)
            cam.gameObject.AddComponent<ARCameraConfigurator>();
    }

    void OnEnable()
    {
        cameraManager = GetComponent<ARCameraManager>();
        if (cameraManager != null) cameraManager.frameReceived += OnFrame;
    }

    void OnDisable()
    {
        if (cameraManager != null) cameraManager.frameReceived -= OnFrame;
    }

    // Configurations are only available once the camera is running
    private void OnFrame(ARCameraFrameEventArgs args)
    {
        if (done) return;
        done = true;
        cameraManager.frameReceived -= OnFrame;
        Apply();
    }

    private void Apply()
    {
        // Some providers (e.g. XR Simulation in the editor) don't offer selectable configurations
        var descriptor = cameraManager.descriptor;
        if (descriptor == null || !descriptor.supportsCameraConfigurations)
        {
            Debug.Log("[ARCameraConfigurator] Camera configurations not supported here; keeping default.");
            return;
        }
        try { ApplyBest(); }
        catch (System.Exception e) { Debug.LogWarning("[ARCameraConfigurator] Could not set camera configuration: " + e.Message); }
    }

    private void ApplyBest()
    {
        using (NativeArray<XRCameraConfiguration> configs = cameraManager.GetConfigurations(Allocator.Temp))
        {
            if (!configs.IsCreated || configs.Length == 0)
            {
                Debug.Log("[ARCameraConfigurator] No selectable camera configurations; keeping default.");
                return;
            }

            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < configs.Length; i++)
            {
                var c = configs[i];
                int fps = c.framerate ?? preferredFramerate;
                // Prefer exactly 30 fps, then the resolution closest to 720p (penalize bigger more than smaller)
                float fpsPenalty = Mathf.Abs(fps - preferredFramerate) * 1000f;
                float pixels = c.width * c.height;
                float target = preferredResolution.x * preferredResolution.y;
                float resPenalty = pixels > target ? (pixels - target) / target * 2f : (target - pixels) / target;
                float score = fpsPenalty + resPenalty;
                if (score < bestScore) { bestScore = score; best = i; }
            }

            if (best >= 0)
            {
                var chosen = configs[best];
                var current = cameraManager.currentConfiguration;
                if (!current.HasValue || current.Value != chosen)
                {
                    cameraManager.currentConfiguration = chosen;
                    Debug.Log($"[ARCameraConfigurator] Camera set to {chosen.width}x{chosen.height} @ {chosen.framerate?.ToString() ?? "?"} fps");
                }
            }
        }
    }
}
