using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// xr8-webar branch: "View in AR" on Android opens the 8th Wall XR8 WebAR page
/// (StreamingAssets/webar) in a full-screen WebView over the app, instead of the AR Foundation scene.
/// The preview scene stays loaded underneath (cameras off, 10 FPS) and comes back when AR closes.
/// In the Editor / other platforms this returns false and the AR Foundation ARScene is used as before.
/// Native side: Assets/Plugins/Android/WebARView.java. Page logs: logcat tag "RealityDropWebAR".
/// </summary>
public class WebARLauncher : MonoBehaviour
{
    /// <summary>Set false to fall back to the AR Foundation scene on device.</summary>
    public static bool UseWebAR = true;

    private const string JavaClass = "com.realitydrop.webar.WebARView";
    private static WebARLauncher instance;

    private readonly List<Camera> pausedCameras = new List<Camera>();
    private int savedFrameRate;
    private bool open;

    /// <summary>Opens WebAR for the model at <paramref name="path"/>. False = not available here.</summary>
    public static bool TryOpen(string path, string title)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UseWebAR || string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return false;
        if (instance == null)
        {
            var go = new GameObject("WebARBridge");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<WebARLauncher>();
        }
        if (instance.open) return true;
        instance.StartCoroutine(instance.OpenRoutine(path, string.IsNullOrEmpty(title) ? "3D model" : title));
        return true;
#else
        return false;
#endif
    }

    private IEnumerator OpenRoutine(string path, string title)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // The WebView can only use the camera if the app itself holds the permission
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            bool answered = false;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => answered = true;
            callbacks.PermissionDenied += _ => answered = true;
            Permission.RequestUserPermission(Permission.Camera, callbacks);
            float timeout = Time.realtimeSinceStartup + 30f;
            while (!answered && Time.realtimeSinceStartup < timeout) yield return null;
            // Opened even if denied: the page explains how to allow the camera
        }

        open = true;
        PauseUnity();
        Debug.Log($"[WebARLauncher] Opening XR8 WebAR for '{title}' ({path}).");
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var view = new AndroidJavaClass(JavaClass))
        {
            view.CallStatic("open", activity, path, title, gameObject.name);
        }
#endif
        yield break;
    }

    /// <summary>Called from Java (UnitySendMessage) when the AR page closes.</summary>
    public void OnWebARClosed(string _)
    {
        Debug.Log("[WebARLauncher] WebAR closed, resuming the app.");
        open = false;
        ResumeUnity();
    }

    // Unity keeps running under the WebView: stop rendering so XR8 gets the CPU/GPU
    private void PauseUnity()
    {
        savedFrameRate = Application.targetFrameRate;
        Application.targetFrameRate = 10;
        pausedCameras.Clear();
        foreach (var cam in Camera.allCameras)
        {
            if (!cam.enabled) continue;
            cam.enabled = false;
            pausedCameras.Add(cam);
        }
    }

    private void ResumeUnity()
    {
        Application.targetFrameRate = savedFrameRate;
        foreach (var cam in pausedCameras) if (cam != null) cam.enabled = true;
        pausedCameras.Clear();
    }

    void OnDestroy()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!open) return;
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var view = new AndroidJavaClass(JavaClass))
            view.CallStatic("close", activity);
#endif
    }
}
