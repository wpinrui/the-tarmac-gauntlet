// Wear laid over the Team HQ's floor and walls: oil, tyre marks, grime. It multiplies the colour already drawn under it
// by the texture's RGB, as much as its alpha says, so it darkens the surface in whatever light falls there.
Shader "Hidden/TarmacGauntlet/Grime"
{
    Properties
    {
        _MainTex ("Wear", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-20" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Back ZWrite Off ZTest LEqual
        Offset -1, -1
        Blend DstColor Zero

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 w = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                return half4(lerp(half3(1, 1, 1), w.rgb, w.a), 1);
            }
            ENDHLSL
        }
    }
}
