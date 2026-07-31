using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using GLTFast;
using GLTFast.Materials;

public static class GltfLoader
{
    public static ImportSettings CreateImportSettings() => new ImportSettings
    {
        GenerateMipMaps = true,
        AnisotropicFilterLevel = 2
    };

    /// <summary>
    /// Converts a raw file system path to a properly formatted URI for glTFast.
    /// Uses System.Uri to correctly handle Windows (file:///C:/) and Android (file:///data/).
    /// </summary>
    public static string ToGltfUri(string path)
    {
        if (path.StartsWith("http://") || path.StartsWith("https://") || path.StartsWith("file://"))
            return path;
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// Loads a GLB/GLTF using glTFast with URP material generator.
    /// filePath must be a raw OS path — URI conversion is handled internally.
    /// </summary>
    public static async Task<(bool success, GltfImport gltf)> Load(string filePath)
    {
        string uri = ToGltfUri(filePath);
        Debug.Log("[GltfLoader] ── LOAD START ──────────────────────────────");
        Debug.Log("[GltfLoader] URI: " + uri);
        Debug.Log("[GltfLoader] Platform: " + Application.platform);
        Debug.Log("[GltfLoader] File exists: " + System.IO.File.Exists(filePath));
        long fileSize = System.IO.File.Exists(filePath) ? new System.IO.FileInfo(filePath).Length : -1;
        Debug.Log("[GltfLoader] File size: " + fileSize + " bytes");

        // ── Render Pipeline Diagnostics ──────────────────────────────────────
        var rpCurrent   = GraphicsSettings.currentRenderPipeline;
        var rpQuality   = QualitySettings.renderPipeline;
        var rpDefault   = GraphicsSettings.defaultRenderPipeline;
        Debug.Log("[GltfLoader] GraphicsSettings.currentRenderPipeline  = " + (rpCurrent  != null ? rpCurrent.GetType().Name  + " (" + rpCurrent.name  + ")" : "NULL"));
        Debug.Log("[GltfLoader] QualitySettings.renderPipeline          = " + (rpQuality  != null ? rpQuality.GetType().Name  + " (" + rpQuality.name  + ")" : "NULL"));
        Debug.Log("[GltfLoader] GraphicsSettings.defaultRenderPipeline  = " + (rpDefault  != null ? rpDefault.GetType().Name  + " (" + rpDefault.name  + ")" : "NULL"));

        var urpAsset = rpCurrent as UniversalRenderPipelineAsset
                    ?? rpQuality as UniversalRenderPipelineAsset
                    ?? rpDefault as UniversalRenderPipelineAsset;
        Debug.Log("[GltfLoader] URP Asset resolved: " + (urpAsset != null ? urpAsset.name : "NULL — material generator will be broken!"));

        // ── Shader Availability Diagnostics ─────────────────────────────────
        string[] criticalShaders = new[]
        {
            "glTF/PbrMetallicRoughness",
            "glTF/PbrSpecularGlossiness",
            "glTF/Unlit",
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Standard"
        };
        foreach (var sName in criticalShaders)
        {
            var s = Shader.Find(sName);
            Debug.Log($"[GltfLoader] Shader.Find(\"{sName}\") = " +
                      (s != null ? $"FOUND (supported={s.isSupported})" : "NOT FOUND — STRIPPED FROM APK!"));
        }
        Debug.Log("[GltfLoader] ────────────────────────────────────────────");

        var materialGenerator = new UniversalRPMaterialGenerator(urpAsset);
        var gltf = new GltfImport(materialGenerator: materialGenerator);

        bool success = await gltf.Load(uri, CreateImportSettings());
        Debug.Log("[GltfLoader] gltf.Load() result: " + success);
        return (success, gltf);
    }

    /// <summary>
    /// Scans all Renderers on root and replaces invalid, missing, or unsupported shaders
    /// (which render pink on mobile) with Universal Render Pipeline/Lit.
    /// Recovers materials from GltfImport if available and preserves textures & colors.
    /// </summary>
    public static void SanitizeMaterials(GameObject root, GltfImport gltf = null)
    {
        if (root == null) return;

        Shader fallbackShader = Shader.Find("Universal Render Pipeline/Lit");
        if (fallbackShader == null)
            fallbackShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (fallbackShader == null)
            fallbackShader = Shader.Find("Standard");

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        int fixedCount = 0;

        foreach (var r in renderers)
        {
            var mats = r.sharedMaterials;
            if (mats == null) continue;

            bool modified = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];

                if (m == null)
                {
                    Debug.LogWarning($"[GltfLoader] Null Material detected on '{r.name}' (slot {i}). Creating fallback Material with '{fallbackShader?.name}'.");
                    if (fallbackShader != null)
                    {
                        m = new Material(fallbackShader);
                        mats[i] = m;
                        modified = true;
                        fixedCount++;
                    }
                    continue;
                }

                bool isInvalid = m.shader == null ||
                                 !m.shader.isSupported ||
                                 m.shader.name.Contains("InternalErrorShader") ||
                                 m.shader.name == "Hidden/InternalErrorShader";

                if (isInvalid)
                {
                    Debug.LogWarning($"[GltfLoader] Unsupported/Pink shader '{m.shader?.name}' detected on '{r.name}' (Material '{m.name}'). Replacing with '{fallbackShader?.name}'.");

                    // Preserve textures and color before swapping shader
                    Texture mainTex = null;
                    if (m.HasProperty("_BaseMap")) mainTex = m.GetTexture("_BaseMap");
                    else if (m.HasProperty("_MainTex")) mainTex = m.GetTexture("_MainTex");

                    Color baseColor = Color.white;
                    if (m.HasProperty("_BaseColor")) baseColor = m.GetColor("_BaseColor");
                    else if (m.HasProperty("_Color")) baseColor = m.GetColor("_Color");

                    if (fallbackShader != null)
                    {
                        m.shader = fallbackShader;

                        if (mainTex != null)
                        {
                            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", mainTex);
                            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", mainTex);
                        }

                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseColor);
                        if (m.HasProperty("_Color")) m.SetColor("_Color", baseColor);
                    }

                    modified = true;
                    fixedCount++;
                }
            }

            if (modified)
            {
                r.sharedMaterials = mats;
            }
        }

        if (fixedCount > 0)
        {
            Debug.Log($"[GltfLoader] Successfully sanitized {fixedCount} material(s) for mobile rendering.");
        }
    }
}
