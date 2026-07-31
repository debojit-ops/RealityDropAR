using UnityEngine;

/// <summary>
/// Preloads and holds references to glTFast and URP shaders so that Unity's
/// build pipeline does NOT strip them during APK compilation.
/// </summary>
public class GltfShaderPreloader : MonoBehaviour
{
    [Header("Shader References (Prevents Android Shader Stripping)")]
    public Shader pbrMetallicRoughness;
    public Shader pbrSpecularGlossiness;
    public Shader gltfUnlit;
    public Shader urpLit;
    public Shader urpUnlit;

    void Awake()
    {
        if (pbrMetallicRoughness == null) pbrMetallicRoughness = Shader.Find("glTF/PbrMetallicRoughness");
        if (pbrSpecularGlossiness == null) pbrSpecularGlossiness = Shader.Find("glTF/PbrSpecularGlossiness");
        if (gltfUnlit == null) gltfUnlit = Shader.Find("glTF/Unlit");
        if (urpLit == null) urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpUnlit == null) urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");

        Debug.Log("[GltfShaderPreloader] Shader references preloaded for mobile build.");
    }
}
