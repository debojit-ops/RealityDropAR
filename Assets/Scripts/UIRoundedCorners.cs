using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gives any Image / RawImage smooth, anti-aliased rounded corners (plus optional border or soft glow)
/// using the shared "RealityDrop/UI/RoundedRect" shader. Shape data is written into the mesh,
/// so every rounded element shares one material.
/// </summary>
[RequireComponent(typeof(Graphic))]
[DisallowMultipleComponent]
public class UIRoundedCorners : BaseMeshEffect
{
    [Tooltip("Corner radius in canvas units. Values larger than half the size become a pill/circle.")]
    public float topLeft = 20f, topRight = 20f, bottomRight = 20f, bottomLeft = 20f;
    [Tooltip("Border width in canvas units (color comes from the material's Border Color).")]
    public float borderWidth = 0f;
    [Tooltip(">0 turns the shape into a soft glow/shadow that fades over this many units.")]
    public float softness = 0f;

    private const string ShaderPath = "RealityDropUI/RoundedRect";
    private static Material sharedMaterial;

    /// <summary>Shared material for all rounded UI. Border color can be changed here once for all.</summary>
    public static Material SharedMaterial
    {
        get
        {
            if (sharedMaterial == null)
            {
                var shader = Resources.Load<Shader>(ShaderPath);
                if (shader == null)
                {
                    Debug.LogError("[UIRoundedCorners] Shader not found at Resources/" + ShaderPath);
                    return null;
                }
                sharedMaterial = new Material(shader) { name = "RoundedUI (shared)" };
                sharedMaterial.SetColor("_BorderColor", new Color(0.30f, 0.85f, 1f, 0.35f));
            }
            return sharedMaterial;
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        var mat = SharedMaterial;
        if (mat != null && graphic != null && graphic.material != mat)
            graphic.material = mat;
        EnableCanvasChannels(graphic != null ? graphic.canvas : null);
    }

    /// <summary>The shader reads TEXCOORD1-3; canvases strip them unless enabled.</summary>
    public static void EnableCanvasChannels(Canvas canvas)
    {
        if (canvas == null) return;
        canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1
                                        | AdditionalCanvasShaderChannels.TexCoord2
                                        | AdditionalCanvasShaderChannels.TexCoord3;
    }

    public void SetRadius(float all) => SetRadii(all, all, all, all);

    public void SetRadii(float tl, float tr, float br, float bl)
    {
        if (Mathf.Approximately(tl, topLeft) && Mathf.Approximately(tr, topRight) &&
            Mathf.Approximately(br, bottomRight) && Mathf.Approximately(bl, bottomLeft))
            return;
        topLeft = tl; topRight = tr; bottomRight = br; bottomLeft = bl;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public void SetBorder(float width)
    {
        if (Mathf.Approximately(width, borderWidth)) return;
        borderWidth = width;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public void SetSoftness(float value)
    {
        if (Mathf.Approximately(value, softness)) return;
        softness = value;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive()) return;

        Rect r = graphic.rectTransform.rect;
        if (r.width <= 0f || r.height <= 0f) return;

        // Shader order: topRight, bottomRight, topLeft, bottomLeft
        var radii = new Vector4(topRight, bottomRight, topLeft, bottomLeft);
        var extra = new Vector4(borderWidth, softness, 0f, 0f);

        UIVertex v = default;
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            float nx = (v.position.x - r.xMin) / r.width;
            float ny = (v.position.y - r.yMin) / r.height;
            v.uv1 = new Vector4(nx, ny, r.width, r.height);
            v.uv2 = radii;
            v.uv3 = extra;
            vh.SetUIVertex(v, i);
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (graphic != null) graphic.SetVerticesDirty();
    }
#endif
}
