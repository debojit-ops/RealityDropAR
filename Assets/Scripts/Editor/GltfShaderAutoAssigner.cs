using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor tool that automatically finds and assigns all glTFast + URP shaders
/// into the GltfShaderPreloader component on the selected GameObject.
/// Menu: Tools > RealityDrop > Auto-Assign glTF Shaders
/// </summary>
public static class GltfShaderAutoAssigner
{
    [MenuItem("Tools/RealityDrop/Auto-Assign glTF Shaders")]
    public static void AutoAssign()
    {
        var preloader = Object.FindFirstObjectByType<GltfShaderPreloader>();

        if (preloader == null)
        {
            EditorUtility.DisplayDialog(
                "Auto-Assign glTF Shaders",
                "No GltfShaderPreloader component found in the open scene.\n\n" +
                "Please add it to a GameObject first (e.g. APIManager), then run this tool again.",
                "OK");
            return;
        }

        Undo.RecordObject(preloader, "Auto-Assign glTF Shaders");

        int assigned = 0;

        preloader.pbrMetallicRoughness = TryFind(ref assigned, "glTF/PbrMetallicRoughness", "glTF-pbrMetallicRoughness", "PbrMetallicRoughness");
        preloader.pbrSpecularGlossiness = TryFind(ref assigned, "glTF/PbrSpecularGlossiness", "glTF-pbrSpecularGlossiness", "PbrSpecularGlossiness");
        preloader.gltfUnlit = TryFind(ref assigned, "glTF/Unlit", "glTF-Unlit", "Unlit");
        preloader.urpLit = TryFind(ref assigned, "Universal Render Pipeline/Lit");
        preloader.urpUnlit = TryFind(ref assigned, "Universal Render Pipeline/Unlit");

        EditorUtility.SetDirty(preloader);

        string report = $"Auto-assigned {assigned}/5 shaders to GltfShaderPreloader on '{preloader.gameObject.name}'.\n\n";

        report += $"Pbr Metallic Roughness : {(preloader.pbrMetallicRoughness != null ? preloader.pbrMetallicRoughness.name : "NOT FOUND")}\n";
        report += $"Pbr Specular Glossiness: {(preloader.pbrSpecularGlossiness != null ? preloader.pbrSpecularGlossiness.name : "NOT FOUND")}\n";
        report += $"glTF Unlit             : {(preloader.gltfUnlit != null ? preloader.gltfUnlit.name : "NOT FOUND")}\n";
        report += $"URP Lit                : {(preloader.urpLit != null ? preloader.urpLit.name : "NOT FOUND")}\n";
        report += $"URP Unlit              : {(preloader.urpUnlit != null ? preloader.urpUnlit.name : "NOT FOUND")}\n";

        if (assigned == 5)
            report += "\n✅ All shaders assigned successfully! Mobile APK will not strip them.";
        else
            report += "\n⚠️ Some shaders were not found. Make sure the glTFast package is installed.";

        Debug.Log("[GltfShaderAutoAssigner] " + report);
        EditorUtility.DisplayDialog("Auto-Assign glTF Shaders", report, "OK");
    }

    private static Shader TryFind(ref int count, params string[] names)
    {
        foreach (var name in names)
        {
            var s = Shader.Find(name);
            if (s != null)
            {
                count++;
                return s;
            }
        }
        Debug.LogWarning($"[GltfShaderAutoAssigner] Could not find any of: {string.Join(", ", names)}");
        return null;
    }
}
