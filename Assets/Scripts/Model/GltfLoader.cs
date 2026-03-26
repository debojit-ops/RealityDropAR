using System;
using System.Linq;
using System.Reflection;
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

    public static object CreateURPMaterialGeneratorIfAvailable()
    {
        string[] candidateTypeNames =
        {
            "GLTFast.Materials.MaterialGeneratorUniversalRP",
            "MaterialGeneratorUniversalRP",
            "GLTFast.Materials.UniversalMaterialGenerator",
            "GLTFast.Materials.URPMaterialGenerator"
        };

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var tn in candidateTypeNames)
                {
                    var t = asm.GetType(tn, false, true);
                    if (t == null) continue;
                    var ctor = t.GetConstructor(Type.EmptyTypes);
                    if (ctor == null) continue;
                    try { return ctor.Invoke(null); }
                    catch { }
                }
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Converts a raw file system path to a file:// URI for glTFast on Android.
    /// Safe to call multiple times — won't double-prefix.
    /// </summary>
    public static string ToGltfUri(string path)
    {
        if (path.StartsWith("http://") || path.StartsWith("https://") || path.StartsWith("file://"))
            return path;
        return "file://" + path;
    }

    public static async Task<bool> InvokeLoadWithReflection(GltfImport gltf, string path, ImportSettings importSettings, object materialGenerator = null)
    {
        var methods = gltf.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => string.Equals(m.Name, "Load", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (methods.Length == 0)
        {
            Debug.LogError("[GltfLoader] GltfImport.Load method not found via reflection.");
            return false;
        }

        object[][] candidateArgs =
        {
            new object[] { path },
            new object[] { path, importSettings },
            new object[] { path, materialGenerator },
            new object[] { path, null, null, importSettings, null },
            new object[] { path, null, null, null, null }
        };

        foreach (var args in candidateArgs)
        {
            var matched = methods.FirstOrDefault(m =>
            {
                var ps = m.GetParameters();
                if (ps.Length != args.Length) return false;
                for (int i = 0; i < ps.Length; ++i)
                {
                    if (args[i] == null) continue;
                    if (!ps[i].ParameterType.IsAssignableFrom(args[i].GetType())) return false;
                }
                return true;
            });

            if (matched == null) continue;

            try
            {
                var result = matched.Invoke(gltf, args);
                if (result == null) return true;

                var resultType = result.GetType();
                if (typeof(Task<bool>).IsAssignableFrom(resultType))
                    return await (Task<bool>)result;

                if (typeof(Task).IsAssignableFrom(resultType))
                {
                    await (Task)result;
                    return true;
                }

                if (result is bool b) return b;
            }
            catch (TargetInvocationException tie)
            {
                Debug.LogWarning("[GltfLoader] " + (tie.InnerException?.Message ?? tie.Message));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GltfLoader] " + ex.Message);
            }
        }

        Debug.LogError("[GltfLoader] No compatible GltfImport.Load overload succeeded.");
        return false;
    }

    /// <summary>
    /// Replaces any shader that isn't a URP shader with URP/Lit.
    /// This covers glTFast shaders that get stripped in Android builds.
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
            var mats = rend.sharedMaterials ?? rend.materials;
            if (mats == null) continue;

            bool needsUpdate = false;
            var arr = rend.materials;

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

                // Replace if: shader is missing, error, or not a URP shader
                bool isUrp = shaderName.StartsWith("Universal Render Pipeline") ||
                             shaderName.StartsWith("Sprites/") ||
                             shaderName.StartsWith("UI/");

                if (!isUrp)
                {
                    // Preserve albedo color and main texture when possible
                    var newMat = new Material(urp) { name = mat.name + "_URP" };
                    try
                    {
                        if (mat.HasProperty("_Color"))
                            newMat.color = mat.color;
                        if (mat.HasProperty("_MainTex") && mat.mainTexture != null)
                            newMat.mainTexture = mat.mainTexture;
                        if (mat.HasProperty("_BaseColor"))
                            newMat.SetColor("_BaseColor", mat.GetColor("_BaseColor"));
                        if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null)
                            newMat.SetTexture("_BaseMap", mat.GetTexture("_BaseMap"));
                    }
                    catch { }

                    arr[i] = newMat;
                    needsUpdate = true;
                    Debug.Log($"[GltfLoader] Replaced shader '{shaderName}' on {rend.name} with URP/Lit");
                }
            }

            if (needsUpdate)
                rend.materials = arr;
        }
    }
}
