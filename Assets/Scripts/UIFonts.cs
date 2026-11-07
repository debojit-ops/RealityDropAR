using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using TMPro;

/// <summary>
/// App typography. Font files live in Assets/Resources/RealityDropUI/Fonts (SIL Open Font License):
///   Inter    — body text (iOS-like, very readable at small sizes): Regular / SemiBold / Bold
///   Rajdhani — techy display accents (buttons, percentages, badges) to match the neon logo
/// TMP font assets are generated at runtime in high quality (90pt sampling, 2048 atlas, SDFAA),
/// so text stays razor sharp at any size. Falls back to TMP's default font if a file is missing.
/// </summary>
public static class UIFonts
{
    public static TMP_FontAsset Regular  => Get("Inter-Regular");
    public static TMP_FontAsset SemiBold => Get("Inter-SemiBold");
    public static TMP_FontAsset Bold     => Get("Inter-Bold");
    public static TMP_FontAsset Display  => Get("Rajdhani-Bold");
    public static TMP_FontAsset DisplaySemiBold => Get("Rajdhani-SemiBold");

    private const string Folder = "RealityDropUI/Fonts/";
    private static readonly Dictionary<string, TMP_FontAsset> cache = new Dictionary<string, TMP_FontAsset>();

    /// <summary>Applies a font and resets the style (the weight comes from the font file, not fake bold).</summary>
    public static void Apply(TMP_Text text, TMP_FontAsset font)
    {
        if (text == null || font == null) return;
        text.font = font;
        text.fontSharedMaterial = font.material;
        text.fontStyle &= ~FontStyles.Bold;
    }

    private static TMP_FontAsset Get(string fileName)
    {
        if (cache.TryGetValue(fileName, out var cached) && cached != null) return cached;

        TMP_FontAsset asset = null;
        var font = Resources.Load<Font>(Folder + fileName);
        if (font != null)
        {
            asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048,
                                                  AtlasPopulationMode.Dynamic, true);
        }

        if (asset != null)
        {
            asset.name = fileName + " SDF (runtime)";
            var fallback = TMP_Settings.defaultFontAsset;
            if (fallback != null)
            {
                // Use the same SDF shader as the default font: it is guaranteed to be included in builds
                if (fallback.material != null && asset.material != null)
                    asset.material.shader = fallback.material.shader;
                if (asset.fallbackFontAssetTable == null) asset.fallbackFontAssetTable = new List<TMP_FontAsset>();
                asset.fallbackFontAssetTable.Add(fallback);
            }
        }
        else
        {
            Debug.LogWarning("[UIFonts] Could not create font '" + fileName + "', using TMP default.");
            asset = TMP_Settings.defaultFontAsset;
        }

        cache[fileName] = asset;
        return asset;
    }
}
