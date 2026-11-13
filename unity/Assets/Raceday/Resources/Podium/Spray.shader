// The podium's champagne spray and confetti (Podium): alpha blended particles lit by the sky's ambient light and the
// sun, so they sit in the scene by day and dim with it at night. Spray is a soft round droplet of foam; confetti
// (_Paper 1) is a hard edged scrap of paper, lit on whichever face turns to the light. No texture is read.
Shader "Hidden/TarmacGauntlet/PodiumSpray"
{
    Properties
    {
        _Paper ("Paper", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Paper;
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
                half3 light : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                Light sun = GetMainLight();
                // Ambient from above, and the sun half on, as a small thing tumbling through the air catches it.
                o.light = SampleSH(half3(0, 1, 0)) + sun.color * 0.5;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                float drop = saturate(1.0 - r);
                float a = lerp(drop * drop * 1.6, 1.0, _Paper);
                if (a <= 0.003) discard;
                // Foam is brighter in its middle; paper is flat but for a glint that comes and goes as it spins.
                half3 shade = lerp(0.75 + 0.35 * drop, 0.85 + 0.3 * abs(p.x), _Paper);
                half3 col = i.color.rgb * shade * i.light;
                col = MixFog(col, i.fog);
                return half4(col, saturate(a) * i.color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
