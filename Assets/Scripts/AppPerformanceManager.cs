using UnityEngine;

public static class AppPerformanceManager
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void InitializePerformanceSettings()
    {
        // 1. Disable VSync and enforce 60 FPS target frame rate for smooth AR camera and UI
        QualitySettings.vSyncCount = 0;
        
        int targetFPS = 60;
        if (Screen.currentResolution.refreshRateRatio.value > 60.0)
        {
            targetFPS = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
        }
        Application.targetFrameRate = targetFPS;

        // 2. Prevent screen dimming/sleeping during AR sessions
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        // 3. Optimize GC garbage collection slice
        System.GC.Collect();

        Debug.Log($"[AppPerformanceManager] Applied performance settings: Target FPS = {Application.targetFrameRate}, VSync = 0, NeverSleep = True");
    }
}
