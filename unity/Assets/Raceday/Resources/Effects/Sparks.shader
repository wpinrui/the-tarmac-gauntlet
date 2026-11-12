// Sparks off a car's underside (CarEffects): stretched particles drawn added to the scene, white hot at the core and
// glowing out to their tips, bright enough for the bloom to catch. The particle's colour carries its cooling; _Heat lifts
// it past what a vertex colour can hold. No texture is read.
Shader "Hidden/TarmacGauntlet/Sparks"
{
    Properties
    {
        _Heat ("Heat", Float) = 9
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
                float _Heat;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float fog : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                // Thin across, tapering to both ends along its flight.
                float across = saturate(1.0 - abs(p.x));
                float along = saturate(1.0 - abs(p.y));
                float glow = across * across * along;
                float core = pow(across, 6.0) * along;
                float3 hot = i.color.rgb * glow + core * 0.6;
                half3 col = hot * (_Heat * i.color.a);
                // Added light fades into the fog rather than taking its colour.
                col *= ComputeFogIntensity(i.fog);
                return half4(col, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
