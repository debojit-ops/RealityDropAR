using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using GLTFast;

public static class GltfLoader
{
    public static ImportSettings CreateImportSettings() => new ImportSettings
    {
        GenerateMipMaps = true,
        AnisotropicFilterLevel = 2
    };

    /// <summary>
    /// Converts a raw file system path to a properly formatted URI for glTFast.
    /// Handles both Windows (file:///C:/...) and Android (file:///data/...).
    /// </summary>
    public static string ToGltfUri(string path)
    {
        if (path.StartsWith("http://") || path.StartsWith("https://"))
            return path;

        // Already a URI
        if (path.StartsWith("file://"))
            return path;

        // Use Uri class to correctly format for the current platform
        return new Uri(path).AbsoluteUri;
    }

    public static async Task<bool> InvokeLoadWithReflection(GltfImport gltf, string path, ImportSettings importSettings, object materialGenerator = null)
    {
        // Direct call — no reflection needed, gltf.Load(string) exists in all glTFast versions
        try
        {
            bool result = await gltf.Load(path, importSettings);
            Debug.Log("[GltfLoader] gltf.Load completed: " + result);
            return result;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[GltfLoader] gltf.Load(path, settings) failed: " + ex.Message + " — retrying with path only");
        }

        // Fallback: try without import settings
        try
        {
            bool result = await gltf.Load(path);
            Debug.Log("[GltfLoader] gltf.Load (no settings) completed: " + result);
            return result;
        }
        catch (Exception ex)
        {
            Debug.LogError("[GltfLoader] All load attempts failed: " + ex.Message);
            return false;
        }
    }

    public static object CreateURPMaterialGeneratorIfAvailable() => null;

    /// <summary>
    /// Replaces any non-URP shader with URP/Lit, preserving albedo color and texture.
    /// Covers glTFast shaders that get stripped in Android builds.
    /// </summary>
    public static void FixMaterialsToURP(GameObject root)
    {
        var urp = Shader.Find("Universal Render Pipeline/Lit");
        if (urp == null)
        {
            Debug.LogError("[GltfLoader] URP Lit shader not found.");
            return;
        }

        foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
        {
            var arr = rend.materials;
            if (arr == null) continue;

            bool needsUpdate = false;
            for (int i = 0; i < arr.Length; ++i)
            {
                var mat = arr[i];
                if (mat == null || mat.shader == null)
                {
                    arr[i] = new Material(urp) { name = "URP_Fallback" };
                    needsUpdate = true;
                    continue;
                }

                string shaderName = mat.shader.name ?? "";
                bool isUrp = shaderName.StartsWith("Universal Render Pipeline") ||
                             shaderName.StartsWith("Sprites/") ||
                             shaderName.StartsWith("UI/");

                if (!isUrp)
                {
                    var newMat = new Material(urp) { name = mat.name + "_URP" };
                    try
                    {
                        if (mat.HasProperty("_Color"))
                            newMat.color = mat.color;
                        if (mat.mainTexture != null)
                            newMat.mainTexture = mat.mainTexture;
                        if (mat.HasProperty("_BaseColor"))
                            newMat.SetColor("_BaseColor", mat.GetColor("_BaseColor"));
                        if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null)
                            newMat.SetTexture("_BaseMap", mat.GetTexture("_BaseMap"));
                    }
                    catch { }

                    arr[i] = newMat;
                    needsUpdate = true;
                    Debug.Log($"[GltfLoader] Replaced '{shaderName}' on {rend.name} with URP/Lit");
                }
            }

            if (needsUpdate)
                rend.materials = arr;
        }
    }
}
