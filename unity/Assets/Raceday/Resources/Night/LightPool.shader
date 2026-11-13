// A pool of light lying on the ground: the floodlights' pools. It brightens what is
// already drawn under it, so the road, the lines and the grass keep their own colours: the first pass multiplies
// the colour behind by 1 + _Color, the second adds a little _Lift so black ground still shows the light. Both
// fade to nothing at the pool's edge, where its UVs reach 1, and into the fog with distance.
// URP draws an object's SRPDefaultUnlit pass before its UniversalForward one, which keeps the two in order.
Shader "Hidden/TarmacGauntlet/LightPool"
{
    Properties
    {
        _Color ("Brightening", Color) = (0, 0, 0, 1)
        _Lift ("Lift", Color) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest LEqual
        Offset -1, -1

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _Lift;
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
            float fog : TEXCOORD1;
        };

        Varyings vert(Attributes v)
        {
            UNITY_SETUP_INSTANCE_ID(v);
            Varyings o;
            o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
            o.uv = v.uv;
            o.fog = ComputeFogFactor(o.positionCS.z);
            return o;
        }

        float Falloff(float2 uv)
        {
            float f = saturate(1.0 - dot(uv, uv));
            return f * f;
        }

        // Into the fog the pool fades to nothing, not to the fog's colour.
        float4 Fogged(float3 c, float fog)
        {
            return float4(MixFogColor(c, float3(0, 0, 0), fog), 0);
        }
        ENDHLSL

        Pass
        {
            Name "Brighten"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend DstColor One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            float4 frag(Varyings i) : SV_Target
            {
                return Fogged(_Color.rgb * Falloff(i.uv), i.fog);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Lift"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            float4 frag(Varyings i) : SV_Target
            {
                return Fogged(_Lift.rgb * Falloff(i.uv), i.fog);
            }
            ENDHLSL
        }
    }
}
