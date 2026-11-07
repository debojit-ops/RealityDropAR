using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// In-depth AR diagnostics. Everything is logged with the tag [ARDiag] (filter Logcat by "ARDiag").
///
///  • Once at start: phone model / Android / GPU / threading, Google Play Services for AR version,
///    ARCore's own device-support verdict (ArCoreApk.checkAvailability), active XR loader,
///    camera configurations, gyroscope support.
///  • Every second: session state + not-tracking reason, camera frames/s, camera timestamp health
///    (gaps, going backwards), whether the AR camera pose moves, planes, centre raycast, brightness, FPS.
///  • State changes are logged immediately.
///  • After 15 s without tracking: one "DIAGNOSIS" line with the most likely cause in plain words.
///
/// In Development builds a small live panel is shown on screen (tap it to hide/show).
/// Added by ARSceneInitializer.
/// </summary>
public class ARDiagnostics : MonoBehaviour
{
    const string Tag = "[ARDiag] ";
    const float DiagnoseAfter = 15f;

    private ARCameraManager cameraManager;
    private ARPlaneManager planeManager;
    private ARRaycastManager raycastManager;
    private Camera arCamera;

    // Static device info
    private string arcoreApkVersion = "n/a";
    private string arcoreAvailability = "n/a";
    private string loaderName = "n/a";

    // Rolling counters (per 1-second window)
    private int framesThisWindow, renderFramesThisWindow, backwardsTimestamps, bigGaps;
    private long lastTimestampNs;
    private float window;
    private float framesPerSecond, renderFps;
    private int totalBackwards, totalBigGaps;

    // Pose movement
    private Vector3 lastPos; private Quaternion lastRot;
    private float movedThisWindow, turnedThisWindow;
    private int poseUpdatesThisWindow;
    private float trackingHz;
    private int poseJumpSeconds;
    private float totalPoseMotion;

    // State
    private ARSessionState lastState = (ARSessionState)(-1);
    private NotTrackingReason lastReason = (NotTrackingReason)(-1);
    private float sinceStart, trackedSeconds;
    private bool everTracked, diagnosed;
    private float lastBrightness = -1f;
    private string intrinsicsText = "n/a";
    private bool centreHit;

    // Overlay
    private TextMeshProUGUI overlay;
    private GameObject overlayRoot;
    private static readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

    public static void Ensure()
    {
        if (FindFirstObjectByType<ARDiagnostics>() == null)
            new GameObject("ARDiagnostics").AddComponent<ARDiagnostics>();
    }

    void Start()
    {
        cameraManager = FindFirstObjectByType<ARCameraManager>();
        planeManager = FindFirstObjectByType<ARPlaneManager>();
        raycastManager = FindFirstObjectByType<ARRaycastManager>();
        arCamera = Camera.main;
        if (cameraManager != null) cameraManager.frameReceived += OnFrame;
        if (arCamera != null) { lastPos = arCamera.transform.position; lastRot = arCamera.transform.rotation; }

        ReadDeviceInfo();
        LogHeader();
        if (Debug.isDebugBuild) BuildOverlay();
    }

    void OnDestroy()
    {
        if (cameraManager != null) cameraManager.frameReceived -= OnFrame;
    }

    // ------------------------------------------------------------------ data collection

    private void OnFrame(ARCameraFrameEventArgs args)
    {
        framesThisWindow++;
        if (args.timestampNs.HasValue)
        {
            long ts = args.timestampNs.Value;
            if (lastTimestampNs != 0)
            {
                long delta = ts - lastTimestampNs;
                if (delta <= 0) { backwardsTimestamps++; totalBackwards++; }
                else if (delta > 200_000_000L) { bigGaps++; totalBigGaps++; } // > 200 ms
            }
            lastTimestampNs = ts;
        }
        if (args.lightEstimation.averageBrightness.HasValue) lastBrightness = args.lightEstimation.averageBrightness.Value;
        else if (args.lightEstimation.averageMainLightBrightness.HasValue) lastBrightness = args.lightEstimation.averageMainLightBrightness.Value;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        sinceStart += dt;
        window += dt;
        renderFramesThisWindow++;

        var state = ARSession.state;
        var reason = ARSession.notTrackingReason;
        bool tracking = state == ARSessionState.SessionTracking && reason == NotTrackingReason.None;
        if (tracking) { everTracked = true; trackedSeconds += dt; }

        if (state != lastState || reason != lastReason)
        {
            Debug.Log(Tag + $"STATE {lastState} → {state}, reason {reason} (t={sinceStart:0.0}s)");
            lastState = state;
            lastReason = reason;
        }

        if (arCamera == null) arCamera = Camera.main;
        if (arCamera != null)
        {
            if (arCamera.transform.position != lastPos || arCamera.transform.rotation != lastRot) poseUpdatesThisWindow++;
            movedThisWindow += Vector3.Distance(arCamera.transform.position, lastPos);
            turnedThisWindow += Quaternion.Angle(arCamera.transform.rotation, lastRot);
            lastPos = arCamera.transform.position;
            lastRot = arCamera.transform.rotation;
        }

        if (window >= 1f) Tick();

        if (!diagnosed && !everTracked && sinceStart >= DiagnoseAfter)
        {
            diagnosed = true;
            ReadDeviceInfo(); // ARCore's support check can still be "UNKNOWN_CHECKING" at startup
            Debug.Log(Tag + $"ARCore device support (re-checked): {arcoreAvailability} | Services: {arcoreApkVersion}");
            Debug.Log(Tag + "DIAGNOSIS: " + Diagnose());
        }
    }

    private void Tick()
    {
        framesPerSecond = framesThisWindow / window;
        trackingHz = poseUpdatesThisWindow / window; // how often ARCore actually updates the phone pose (Google: "VIO frequency")
        renderFps = renderFramesThisWindow / window;
        totalPoseMotion += movedThisWindow;

        int planes = planeManager != null ? planeManager.trackables.count : -1;
        centreHit = raycastManager != null &&
                    raycastManager.Raycast(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), hits, TrackableType.PlaneWithinPolygon);

        if (intrinsicsText == "n/a" && cameraManager != null && cameraManager.TryGetIntrinsics(out var k))
            intrinsicsText = $"{k.resolution.x}x{k.resolution.y} f=({k.focalLength.x:0},{k.focalLength.y:0})";

        string line = $"t={sinceStart:0}s state={ARSession.state} reason={ARSession.notTrackingReason} " +
                      $"camFPS={framesPerSecond:0.0} trackHz={trackingHz:0.0}{(trackingHz < 15f && everTracked ? " (STARVED)" : "")} renderFPS={renderFps:0} tsBackwards={backwardsTimestamps} gaps>200ms={bigGaps} " +
                      $"poseMoved={movedThisWindow * 100f:0.0}cm turned={turnedThisWindow:0}° planes={planes} centreHit={centreHit} " +
                      $"brightness={(lastBrightness < 0 ? "n/a" : lastBrightness.ToString("0.00"))} cfg={CurrentConfigText()}";
        Debug.Log(Tag + line);
        // A hand-held phone moves well under 2 m/s. Faster "movement" while tracking = ARCore's pose is jumping
        // (unreliable visual-inertial tracking on this device / environment) → planes can't be confirmed.
        if (ARSession.state == ARSessionState.SessionTracking && movedThisWindow / window > 2f)
        {
            poseJumpSeconds++;
            Debug.LogWarning(Tag + $"POSE JUMP: ARCore reports {movedThisWindow / window:0.0} m/s of phone movement — tracking is unreliable " +
                             $"({poseJumpSeconds}s so far). Plane detection and anchors will fail; use Manual mode.");
        }
        UpdateOverlay();

        window = 0f;
        framesThisWindow = renderFramesThisWindow = backwardsTimestamps = bigGaps = 0;
        movedThisWindow = turnedThisWindow = 0f;
        poseUpdatesThisWindow = 0;
    }

    private string CurrentConfigText()
    {
        if (cameraManager == null || cameraManager.descriptor == null || !cameraManager.descriptor.supportsCameraConfigurations) return "n/a";
        try
        {
            var cur = cameraManager.currentConfiguration;
            return cur.HasValue ? $"{cur.Value.width}x{cur.Value.height}@{cur.Value.framerate?.ToString() ?? "?"}" : "none";
        }
        catch { return "error"; }
    }

    // ------------------------------------------------------------------ verdict

    private string Diagnose()
    {
        if (arcoreAvailability.StartsWith("UNSUPPORTED"))
            return $"ARCore says this phone is NOT supported ({arcoreAvailability}). Full AR cannot work on this device; use Camera mode or a supported phone.";
        if (ARSession.state == ARSessionState.Unsupported)
            return "AR session reports Unsupported. The device or the installed AR services cannot run AR.";
        if (ARSession.state == ARSessionState.NeedsInstall || ARSession.state == ARSessionState.Installing)
            return "Google Play Services for AR is missing or updating. Update it from the Play Store.";
        if (framesPerSecond < 1f)
            return "No camera frames are reaching AR (camera blocked, permission denied, or camera used by another app).";
        if (framesPerSecond < 15f)
            return $"Camera frames are too slow ({framesPerSecond:0} fps). ARCore needs ~30 fps; the phone may be overloaded or the camera mode is wrong.";
        if (totalBackwards > 0 || totalBigGaps > 2)
            return $"Camera timestamps are broken (went backwards {totalBackwards}×, gaps >200 ms {totalBigGaps}×). This is a phone camera/driver issue ARCore can't track with.";
        switch (ARSession.notTrackingReason)
        {
            case NotTrackingReason.InsufficientLight: return "Too dark for tracking. Try a brighter room.";
            case NotTrackingReason.InsufficientFeatures: return "Not enough visual detail. Point at a textured floor (rug, wood) and move sideways.";
            case NotTrackingReason.ExcessiveMotion: return "Phone moving too fast. Move slowly.";
            case NotTrackingReason.CameraUnavailable: return "Camera unavailable to ARCore.";
        }
        if (totalPoseMotion < 0.01f)
            return $"ARCore gets healthy camera frames ({framesPerSecond:0} fps, availability {arcoreAvailability}) but its motion tracking never starts — " +
                   "classic sign of missing camera↔motion-sensor calibration for this phone model inside ARCore " +
                   "(check native log for 'imu_transform_sensor: 400 0 0 0 0 0 0 1' / 'CAM_IMU_DESYNC'). Not fixable in the app; use Camera mode or a supported phone.";
        return $"Tracking never stabilised although the pose moves. State={ARSession.state}, reason={ARSession.notTrackingReason}, camera {framesPerSecond:0} fps.";
    }

    // ------------------------------------------------------------------ device info

    private void ReadDeviceInfo()
    {
        try
        {
            var settings = UnityEngine.XR.Management.XRGeneralSettings.Instance;
            var loader = settings != null && settings.Manager != null ? settings.Manager.activeLoader : null;
            loaderName = loader != null ? loader.name : "none";
        }
        catch (System.Exception e) { loaderName = "error: " + e.Message; }

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                try
                {
                    using (var pm = activity.Call<AndroidJavaObject>("getPackageManager"))
                    using (var info = pm.Call<AndroidJavaObject>("getPackageInfo", "com.google.ar.core", 0))
                        arcoreApkVersion = info.Get<string>("versionName");
                }
                catch { arcoreApkVersion = "NOT INSTALLED"; }

                try
                {
                    using (var apk = new AndroidJavaClass("com.google.ar.core.ArCoreApk"))
                    using (var instance = apk.CallStatic<AndroidJavaObject>("getInstance"))
                    using (var availability = instance.Call<AndroidJavaObject>("checkAvailability", activity))
                        arcoreAvailability = availability.Call<string>("name");
                }
                catch (System.Exception e) { arcoreAvailability = "error: " + e.Message; }
            }
        }
        catch (System.Exception e) { arcoreApkVersion = "error: " + e.Message; }
#else
        arcoreApkVersion = "editor";
        arcoreAvailability = "editor (" + loaderName + ")";
#endif
    }

    private void LogHeader()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Tag + "===== AR DIAGNOSTICS =====");
        sb.AppendLine(Tag + $"Device: {SystemInfo.deviceModel} | OS: {SystemInfo.operatingSystem}");
        sb.AppendLine(Tag + $"GPU: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}) | threading: {SystemInfo.renderingThreadingMode}");
        sb.AppendLine(Tag + $"CPU: {SystemInfo.processorType} x{SystemInfo.processorCount} | RAM: {SystemInfo.systemMemorySize} MB");
        sb.AppendLine(Tag + $"Screen: {Screen.width}x{Screen.height} @ {Screen.dpi:0} dpi | orientation: {Screen.orientation} | targetFPS: {Application.targetFrameRate}");
        sb.AppendLine(Tag + $"XR loader: {loaderName}");
        sb.AppendLine(Tag + $"Google Play Services for AR: {arcoreApkVersion} | ARCore device support: {arcoreAvailability}");
        sb.AppendLine(Tag + $"Gyroscope: {SystemInfo.supportsGyroscope} | Accelerometer: {SystemInfo.supportsAccelerometer}");
        sb.AppendLine(Tag + $"Light estimation requested: {(cameraManager != null ? cameraManager.requestedLightEstimation.ToString() : "n/a")}");
        sb.AppendLine(Tag + $"Plane detection requested: {(planeManager != null ? planeManager.requestedDetectionMode.ToString() : "n/a")}");
        if (cameraManager != null && cameraManager.descriptor != null && cameraManager.descriptor.supportsCameraConfigurations)
        {
            try
            {
                using (var configs = cameraManager.GetConfigurations(Unity.Collections.Allocator.Temp))
                    for (int i = 0; i < configs.Length; i++)
                        sb.AppendLine(Tag + $"Camera config {i}: {configs[i].width}x{configs[i].height} @ {configs[i].framerate?.ToString() ?? "?"} fps");
                var cur = cameraManager.currentConfiguration;
                sb.AppendLine(Tag + "Current camera config: " + (cur.HasValue ? $"{cur.Value.width}x{cur.Value.height} @ {cur.Value.framerate?.ToString() ?? "?"} fps" : "none yet"));
            }
            catch (System.Exception e) { sb.AppendLine(Tag + "Camera configs: error " + e.Message); }
        }
        sb.Append(Tag + "==========================");
        Debug.Log(sb.ToString());
    }

    // ------------------------------------------------------------------ on-screen panel (Development builds)

    private void BuildOverlay()
    {
        var canvas = UIKit.CreateCanvas(transform, "ARDiagUI", 50);
        overlayRoot = UIKit.Rect("Panel", canvas.transform).gameObject;
        var rt = (RectTransform)overlayRoot.transform;
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(24f, -760f); rt.offsetMax = new Vector2(-24f, -330f);
        var bg = overlayRoot.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.65f);
        var btn = overlayRoot.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => overlay.gameObject.SetActive(!overlay.gameObject.activeSelf));
        overlay = UIKit.Text("Text", overlayRoot.transform, UIFonts.Regular, 30f, new Color(0.7f, 1f, 0.8f, 1f), TextAlignmentOptions.TopLeft);
        UIKit.Stretch(overlay.rectTransform, new Vector2(20f, 16f), new Vector2(-20f, -16f));
    }

    private void UpdateOverlay()
    {
        if (overlay == null) return;
        overlay.text =
            $"<b>AR DIAGNOSTICS</b>  (tap to hide)\n" +
            $"Device: {SystemInfo.deviceModel}\n" +
            $"ARCore support: {arcoreAvailability} | Services: {arcoreApkVersion}\n" +
            $"State: {ARSession.state}  Reason: {ARSession.notTrackingReason}\n" +
            $"Camera: {framesPerSecond:0.0} fps  Render: {renderFps:0} fps  {intrinsicsText}\n" +
            $"Timestamps backwards: {totalBackwards}  Gaps>200ms: {totalBigGaps}\n" +
            $"Pose motion total: {totalPoseMotion * 100f:0} cm  Planes: {(planeManager != null ? planeManager.trackables.count : -1)}  Centre hit: {centreHit}\n" +
            $"Tracked for: {trackedSeconds:0.0}s / {sinceStart:0}s" +
            (diagnosed ? "\n<color=#FFD166>DIAGNOSIS: " + Diagnose() + "</color>" : "");
    }
}
