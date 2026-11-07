// Glowing neon panel for detected AR surfaces (floor / tables).
// Mesh comes from ARGlowPlane.cs: uv.x = 0 on the plane's boundary, 1 inside (a fixed-width edge band),
// so the edge glows with the same width on every plane. Inside: faint dot grid + a soft expanding pulse.
// Premultiplied alpha. Lives in Assets/Resources so it is always included in builds.
Shader "RealityDrop/ARGlowPlane"
{
    Properties
    {
        _Color ("Color", Color) = (0.30, 0.88, 1, 1)
        _GridScale ("Dots per meter", Float) = 12
        _Appear ("Appear", Range(0, 1)) = 1
        _Visible ("Visible", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent-20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset -1, -1

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float edge : TEXCOORD0; float3 world : TEXCOORD1; float3 center : TEXCOORD2; };

            fixed4 _Color;
            float _GridScale, _Appear, _Visible;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.edge = 1.0 - saturate(v.uv.x);          // 1 at the boundary → 0 inside
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.center = unity_ObjectToWorld._m03_m13_m23;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Bright neon rim + soft inner glow along the boundary
                float rim = smoothstep(0.55, 1.0, i.edge);
                float innerGlow = i.edge * i.edge * 0.45;

                // Faint dot grid in world space (stays put as planes grow)
                float2 g = i.world.xz * _GridScale;
                float2 f = abs(frac(g) - 0.5);
                float dots = smoothstep(0.11, 0.05, length(f)) * 0.35;

                // Scanning pulse rippling out from the plane's center
                float dist = length(i.world.xz - i.center.xz);
                float wave = frac(dist * 0.9 - _Time.y * 0.55);
                wave = smoothstep(0.86, 1.0, wave) * 0.35;

                float intensity = rim * 1.15 + innerGlow + dots + wave + 0.05;
                float a = saturate(intensity) * _Appear * _Visible;
                return fixed4(_Color.rgb * a, a * 0.55);
            }
            ENDCG
        }
    }
}
