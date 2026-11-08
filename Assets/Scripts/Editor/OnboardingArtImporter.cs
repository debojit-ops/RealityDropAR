using UnityEditor;

/// <summary>
/// Import settings for the AI artwork in Resources/RealityDropUI/Onboarding (loader background + guide
/// pictures): keep the real size (no power-of-two stretching, so the aspect ratio stays right), no mipmaps,
/// high-quality compression, max 2048 px. Just drop PNG/JPG files into that folder.
/// </summary>
public class OnboardingArtImporter : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').Contains("Resources/RealityDropUI/Onboarding/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
    }
}
