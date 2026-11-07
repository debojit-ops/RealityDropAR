using UnityEngine;
using UnityEngine.UI;

/// <summary>Vertical (top → bottom) color gradient for any Image / RawImage. Multiplies the graphic's color.</summary>
[RequireComponent(typeof(Graphic))]
[DisallowMultipleComponent]
public class UIGradient : BaseMeshEffect
{
    public Color top = Color.white;
    public Color bottom = Color.white;

    public void Set(Color topColor, Color bottomColor)
    {
        top = topColor;
        bottom = bottomColor;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive()) return;
        Rect r = graphic.rectTransform.rect;
        if (r.height <= 0f) return;

        UIVertex v = default;
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            float t = Mathf.Clamp01((v.position.y - r.yMin) / r.height);
            Color c = Color.Lerp(bottom, top, t);
            v.color = (Color32)((Color)v.color * c);
            vh.SetUIVertex(v, i);
        }
    }
}
