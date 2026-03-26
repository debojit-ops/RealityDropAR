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

    public static async Task<bool> InvokeLoadWithReflection(GltfImport gltf, string path, ImportSettings importSettings, object materialGenerator = null)
    {
        // Ensure proper URI format for Android — raw paths silently fail on device
        if (!path.StartsWith("file://") && !path.StartsWith("http"))
            path = "file://" + path;
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

            for (int i = 0; i < mats.Length; ++i)
            {
                var mat = mats[i];
                bool replace = mat == null || mat.shader == null;
                if (!replace)
                {
                    string sname = mat.shader.name ?? "";
                    replace = sname.Contains("Hidden/InternalErrorShader") || sname.ToLower().Contains("error");
                }

                if (replace)
                {
                    var arr = rend.materials;
                    arr[i] = new Material(urp) { name = "URP_Fallback_Material" };
                    rend.materials = arr;
                    Debug.LogWarning("[GltfLoader] Replaced broken material on " + rend.name + " with URP/Lit");
                }
            }
        }
    }
}
