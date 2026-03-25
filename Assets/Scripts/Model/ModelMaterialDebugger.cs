using System;
using System.IO;
using System.Text;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class ModelMaterialDebugger : MonoBehaviour
{
    [Header("Runtime Debug / Report")]
    [Tooltip("Root transform to inspect. If empty, this GameObject is used.")]
    public Transform rootToInspect;

    [Tooltip("Write a detailed text report to persistentDataPath")]
    public bool writeReportFile = true;

    [Tooltip("Filename for the report (in persistentDataPath)")]
    public string reportFileName = "ModelMaterialReport.txt";

    [Header("Automatic fixes")]
    [Tooltip("If true, attempt to replace unsupported shaders with fallbackShaderName")]
    public bool attemptShaderFix = false;

    [Tooltip("Name of fallback shader to apply if material.shader.isSupported == false")]
    public string fallbackShaderName = "Universal Render Pipeline/Lit"; // or "Standard" if using Built-in

    [Tooltip("Try to assign fallback only when shader.isSupported == false")]
    public bool onlyWhenUnsupported = true;

    [Header("Run controls")]
    public bool runAutomaticallyOnStart = true;
    [Tooltip("If true, waits this many seconds on Start before running (allow async loads)")]
    public float waitSecondsBeforeRun = 0.5f;

    private string ReportPath => Path.Combine(Application.persistentDataPath, reportFileName);

    void Start()
    {
        if (rootToInspect == null) rootToInspect = transform;
        if (runAutomaticallyOnStart) StartCoroutine(RunAfterDelay(waitSecondsBeforeRun));
    }

    IEnumerator RunAfterDelay(float sec)
    {
        if (sec > 0f) yield return new WaitForSeconds(sec);
        RunDiagnostics();
    }

    [ContextMenu("Run Diagnostics Now")]
    public void RunDiagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Model Material Report: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Platform: {Application.platform}, persistentPath: {Application.persistentDataPath}");
        sb.AppendLine($"Root inspected: {rootToInspect.name}");
        sb.AppendLine("--------------------------------------------------");

        var renderers = rootToInspect.GetComponentsInChildren<Renderer>(true);
        sb.AppendLine($"Renderer count: {renderers.Length}");
        Debug.Log($"[ModelMaterialDebugger] Renderer count: {renderers.Length}");

        int matIndex = 0;
        foreach (var r in renderers)
        {
            sb.AppendLine($"--- Renderer #{matIndex++} ---");
            try
            {
                sb.AppendLine($"Renderer: {r.name} active={r.gameObject.activeInHierarchy} enabled={r.enabled} castShadow={r.shadowCastingMode} receiveShadow={r.receiveShadows}");
                var mats = r.sharedMaterials;
                sb.AppendLine($"Materials on renderer: {mats?.Length ?? 0}");
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null)
                    {
                        sb.AppendLine($"  Material[{i}] = null");
                        continue;
                    }

                    sb.AppendLine($"  Material[{i}] name={m.name} shader={m.shader?.name ?? "NULL"}");
                    bool shaderSupported = m.shader != null ? m.shader.isSupported : false;
                    sb.AppendLine($"    shaderSupported: {shaderSupported}");
                    sb.AppendLine($"    renderQueue: {m.renderQueue}");
                    sb.AppendLine($"    shaderKeywords count: {m.shaderKeywords?.Length ?? 0}");
                    if (m.shaderKeywords != null && m.shaderKeywords.Length > 0)
                    {
                        sb.AppendLine($"    shaderKeywords: {string.Join(", ", m.shaderKeywords)}");
                    }

                    // check main texture
                    try
                    {
                        var mainTex = m.mainTexture;
                        if (mainTex == null)
                        {
                            sb.AppendLine("    mainTexture: null");
                        }
                        else
                        {
                            string tname = mainTex.name;
                            sb.AppendLine($"    mainTexture: {tname} (type {mainTex.GetType().Name}) width/height unavailable at generic Texture level");
                            var tex2d = mainTex as Texture2D;
                            if (tex2d != null)
                            {
                                sb.AppendLine($"      tex2D size: {tex2d.width}x{tex2d.height} format: {tex2d.format} mipmapCount: {tex2d.mipmapCount} isReadable: {tex2d.isReadable}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"    mainTexture read error: {ex.Message}");
                    }

                    // list common texture property names that glTF may use
                    string[] propNames = new string[] { "_MainTex", "_BaseMap", "_MetallicGlossMap", "_OcclusionMap", "_BumpMap", "_EmissionMap", "_SpecGlossMap" };
                    foreach (var pn in propNames)
                    {
                        if (m.HasProperty(pn))
                        {
                            var t = m.GetTexture(pn);
                            sb.AppendLine($"    prop {pn}: {(t == null ? "null" : t.name + " (" + t.GetType().Name + ")")}");
                        }
                    }

                    // Optionally attempt fix
                    if (attemptShaderFix && (!onlyWhenUnsupported || !shaderSupported))
                    {
                        TryApplyFallbackShader(m, sb);
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Renderer inspection error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // Extra checks
        sb.AppendLine("----- Additional checks -----");
        try
        {
            sb.AppendLine($"Active scene cameras: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().Length}");
        }
        catch { }

        string report = sb.ToString();
        Debug.Log(report);

        if (writeReportFile)
        {
            try
            {
                File.WriteAllText(ReportPath, report);
                Debug.Log($"[ModelMaterialDebugger] Wrote report to: {ReportPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModelMaterialDebugger] Failed to write report: {ex}");
            }
        }
    }

    private void TryApplyFallbackShader(Material m, StringBuilder sb)
    {
        if (string.IsNullOrEmpty(fallbackShaderName))
        {
            sb.AppendLine("  No fallback shader name provided.");
            return;
        }

        var fallback = Shader.Find(fallbackShaderName);
        if (fallback == null)
        {
            sb.AppendLine($"  Fallback shader not found: {fallbackShaderName}");
            return;
        }

        try
        {
            sb.AppendLine($"  Replacing shader '{m.shader?.name}' with fallback '{fallback.name}'");
            m.shader = fallback;
            sb.AppendLine($"  Replacement done. shaderSupported now: {m.shader.isSupported}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  Shader replacement failed: {ex.Message}");
        }
    }
}
