// Neon turntable under the preview model: a soft contact shadow + dark platform inside a glowing ring.
// Drawn on a flat quad whose UVs span -1..1; everything is computed from the distance to the center.
// Premultiplied output: rgb adds glow, alpha darkens what is behind (shadow/platform).
// Lives in Assets/Resources so it is always included in player builds.
Shader "RealityDrop/Turntable"
{
    Properties
    {
        _RingColor ("Ring Color", Color) = (0.30, 0.88, 1, 1)
        _RingRadius ("Ring Radius (0-1)", Range(0.1, 1)) = 0.86
        _CoreWidth ("Core Width", Range(0.001, 0.1)) = 0.012
        _GlowWidth ("Glow Width", Range(0.01, 0.3)) = 0.07
        _GlowStrength ("Glow Strength", Range(0, 2)) = 0.6
        _PlatformAlpha ("Platform Darkness", Range(0, 1)) = 0.35
        _ShadowAlpha ("Contact Shadow", Range(0, 1)) = 0.55
        _ShadowRadius ("Shadow Radius", Range(0.05, 1)) = 0.55
        _Pulse ("Pulse", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha
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

            fixed4 _RingColor;
            float _RingRadius, _CoreWidth, _GlowWidth, _GlowStrength;
            float _PlatformAlpha, _ShadowAlpha, _ShadowRadius, _Pulse;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * 2.0 - 1.0;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float r = length(i.uv);
                if (r > 1.0) discard;

                float dr = r - _RingRadius;
                float core = exp(-(dr * dr) / (_CoreWidth * _CoreWidth));
                float glow = exp(-(dr * dr) / (_GlowWidth * _GlowWidth)) * _GlowStrength * (1.0 + _Pulse * 0.35);

                // Dark platform inside the ring + soft contact shadow in the middle
                float platform = (1.0 - smoothstep(_RingRadius - 0.02, _RingRadius + 0.01, r)) * _PlatformAlpha;
                float shadow = (1.0 - smoothstep(0.0, _ShadowRadius, r));
                shadow = shadow * shadow * _ShadowAlpha;
                float darkness = saturate(max(platform, shadow));

                // Fade everything out at the quad edge
                float edge = 1.0 - smoothstep(0.92, 1.0, r);

                float3 emission = _RingColor.rgb * (core + glow) * edge;
                // Inner floor reflection of the ring
                emission += _RingColor.rgb * 0.08 * (1.0 - smoothstep(0.0, _RingRadius, r));

                return fixed4(emission, darkness * edge);
            }
            ENDCG
        }
    }
}
