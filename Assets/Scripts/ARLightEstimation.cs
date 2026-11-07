using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Matches the virtual lighting to the real room using ARCore/ARKit light estimation:
/// brightness, color, main light direction and ambient (spherical harmonics) when the device
/// provides them. Changes are smoothed so the model never flickers.
/// Added automatically by ARScreenUI to the scene's directional light.
/// </summary>
[RequireComponent(typeof(Light))]
public class ARLightEstimation : MonoBehaviour
{
    [Tooltip("Multiplier applied to the estimated brightness.")]
    public float brightnessScale = 1.6f;
    [Tooltip("How quickly lighting follows the estimate (higher = faster).")]
    public float smoothing = 3f;

    private ARCameraManager cameraManager;
    private Light lightComponent;

    private float targetIntensity, baseIntensity;
    private Color targetColor = Color.white;
    private Quaternion targetRotation;
    private bool hasDirection;
    private SphericalHarmonicsL2 targetAmbient;
    private bool hasAmbient;

    public static void Ensure()
    {
        Light key = null;
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { key = l; break; }
        if (key == null)
        {
            key = new GameObject("AR Key Light").AddComponent<Light>();
            key.type = LightType.Directional;
            key.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
        if (key.GetComponent<ARLightEstimation>() == null) key.gameObject.AddComponent<ARLightEstimation>();
    }

    void Awake()
    {
        lightComponent = GetComponent<Light>();
        baseIntensity = targetIntensity = lightComponent.intensity;
        targetRotation = transform.rotation;
        lightComponent.shadows = LightShadows.None;
    }

    void OnEnable()
    {
        cameraManager = FindFirstObjectByType<ARCameraManager>();
        if (cameraManager == null) return;
        cameraManager.requestedLightEstimation =
            LightEstimation.AmbientIntensity | LightEstimation.AmbientColor |
            LightEstimation.MainLightDirection | LightEstimation.MainLightIntensity |
            LightEstimation.AmbientSphericalHarmonics;
        cameraManager.frameReceived += OnFrame;
    }

    void OnDisable()
    {
        if (cameraManager != null) cameraManager.frameReceived -= OnFrame;
    }

    private void OnFrame(ARCameraFrameEventArgs args)
    {
        var le = args.lightEstimation;

        if (le.averageMainLightBrightness.HasValue)
            targetIntensity = le.averageMainLightBrightness.Value * brightnessScale;
        else if (le.averageBrightness.HasValue)
            targetIntensity = le.averageBrightness.Value * brightnessScale;

        if (le.mainLightColor.HasValue)
            targetColor = le.mainLightColor.Value;
        else if (le.colorCorrection.HasValue)
            targetColor = le.colorCorrection.Value;

        if (le.mainLightDirection.HasValue && le.mainLightDirection.Value.sqrMagnitude > 1e-4f)
        {
            targetRotation = Quaternion.LookRotation(le.mainLightDirection.Value);
            hasDirection = true;
        }

        if (le.ambientSphericalHarmonics.HasValue)
        {
            targetAmbient = le.ambientSphericalHarmonics.Value;
            hasAmbient = true;
        }
    }

    void Update()
    {
        float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        lightComponent.intensity = Mathf.Lerp(lightComponent.intensity, Mathf.Clamp(targetIntensity, 0.15f, 3f), k);
        lightComponent.color = Color.Lerp(lightComponent.color, targetColor, k);
        if (hasDirection) transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, k);

        if (hasAmbient)
        {
            RenderSettings.ambientMode = AmbientMode.Skybox;
            var current = RenderSettings.ambientProbe;
            RenderSettings.ambientProbe = current * (1f - k) + targetAmbient * k;
        }
    }
}
