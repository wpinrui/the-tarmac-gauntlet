// A brake disc's glow (CarEffects): a ring inside the wheel, behind the spokes, added to the scene in the disc's heat
// colour. Brightest at the rubbing band, with faint vents across it. No texture is read.
Shader "Hidden/TarmacGauntlet/BrakeGlow"
{
    Properties
    {
        [HDR] _Glow ("Glow", Color) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Glow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fog : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                // The rubbing band, from 0.5 to 0.95 of the disc's radius, softest at its edges.
                float band = smoothstep(0.42, 0.55, r) * (1.0 - smoothstep(0.88, 0.98, r));
                float vents = 0.85 + 0.15 * sin(atan2(p.y, p.x) * 36.0);
                half3 col = _Glow.rgb * (band * vents);
                col *= ComputeFogIntensity(i.fog);
                return half4(col, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
