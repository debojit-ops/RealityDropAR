// Anti-aliased rounded rectangle for uGUI (Image / RawImage).
// Per-vertex shape data comes from the UIRoundedCorners mesh effect, so one shared material
// serves every card, thumbnail, glow and pill (keeps batching).
//   TEXCOORD1.xy = position inside the rect (0..1), TEXCOORD1.zw = rect size (canvas units)
//   TEXCOORD2    = corner radii (topRight, bottomRight, topLeft, bottomLeft)
//   TEXCOORD3.x  = border width, TEXCOORD3.y = softness (>0 = soft glow/shadow falloff)
// Lives in Assets/Resources so it is always included in player builds.
Shader "RealityDrop/UI/RoundedRect"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BorderColor ("Border Color", Color) = (0.25, 0.85, 1, 0.35)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 shape    : TEXCOORD1;
                float4 radii    : TEXCOORD2;
                float4 extra    : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 shape         : TEXCOORD2;
                float4 radii         : TEXCOORD3;
                float4 extra         : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _BorderColor;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                OUT.shape = v.shape;
                OUT.radii = v.radii;
                OUT.extra = v.extra;
                return OUT;
            }

            // Signed distance to a rounded box with per-corner radii (Inigo Quilez).
            // r = (topRight, bottomRight, topLeft, bottomLeft)
            float sdRoundBox(float2 p, float2 b, float4 r)
            {
                r.xy = (p.x > 0.0) ? r.xy : r.zw;
                r.x  = (p.y > 0.0) ? r.x  : r.y;
                float2 q = abs(p) - b + r.x;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                float2 size = IN.shape.zw;
                if (size.x > 0.0 && size.y > 0.0)
                {
                    float2 halfSize = size * 0.5;
                    float2 p = (IN.shape.xy - 0.5) * size;
                    float4 r = min(IN.radii, min(halfSize.x, halfSize.y));
                    float d = sdRoundBox(p, halfSize, r);

                    float softness = IN.extra.y;
                    float shapeAlpha;
                    if (softness > 0.0)
                    {
                        // Soft falloff from the edge inwards (used for glows / shadows)
                        shapeAlpha = saturate(-d / softness);
                        shapeAlpha *= shapeAlpha;
                    }
                    else
                    {
                        float aa = max(fwidth(d), 1e-4);
                        shapeAlpha = saturate(0.5 - d / aa);

                        float border = IN.extra.x;
                        if (border > 0.0)
                        {
                            float inBorder = saturate((d + border) / aa + 0.5) * _BorderColor.a;
                            color.rgb = lerp(color.rgb, _BorderColor.rgb, inBorder);
                            color.a = lerp(color.a, 1.0, inBorder);
                        }
                    }
                    color.a *= shapeAlpha;
                }

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
