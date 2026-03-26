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
        Debug.Log("[GltfLoader] Loading URI: " + uri);

        // Use URP material generator so glTFast generates URP-compatible materials natively
        var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        var materialGenerator = urpAsset != null
            ? new UniversalRPMaterialGenerator(urpAsset)
            : null;
        var gltf = new GltfImport(materialGenerator: materialGenerator);

        bool success = await gltf.Load(uri, CreateImportSettings());
        return (success, gltf);
    }
}
