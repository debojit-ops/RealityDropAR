// Studio backdrop for the preview camera: a radial navy gradient drawn behind everything.
// Rendered on a quad parented to the camera. Lives in Assets/Resources so it ships in builds.
Shader "RealityDrop/Backdrop"
{
    Properties
    {
        _CenterColor ("Center Color", Color) = (0.063, 0.157, 0.227, 1)
        _MidColor ("Mid Color", Color) = (0.031, 0.071, 0.106, 1)
        _EdgeColor ("Edge Color", Color) = (0.016, 0.031, 0.051, 1)
        _Center ("Center (uv)", Vector) = (0.5, 0.55, 0, 0)
        _Aspect ("Aspect (h/w)", Float) = 2.1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Opaque" "IgnoreProjector"="True" }
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            fixed4 _CenterColor, _MidColor, _EdgeColor;
            float4 _Center;
            float _Aspect;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 d = i.uv - _Center.xy;
                d.y *= _Aspect * 0.55;
                float r = saturate(length(d) * 1.6);
                fixed3 c = r < 0.55
                    ? lerp(_CenterColor.rgb, _MidColor.rgb, r / 0.55)
                    : lerp(_MidColor.rgb, _EdgeColor.rgb, (r - 0.55) / 0.45);
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}
