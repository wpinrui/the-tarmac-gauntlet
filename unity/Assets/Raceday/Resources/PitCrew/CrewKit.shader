// The pit crews' suits in their team's colours. The suit's own texture is navy cloth with red panels;
// the kit mask (tools/pit-crew/kit_masks.py) marks the cloth in red and the panels in green. Each is
// painted over by its own colour, kept shaded by the texture's folds, as far as that colour's alpha says:
// alpha 0 leaves the suit as it is. The colours are per instance, so every crew member in the race
// shares one material for each part of the suit.
Shader "Hidden/TarmacGauntlet/CrewKit"
{
    Properties
    {
        [MainColor] _Color ("Tint", Color) = (1, 1, 1, 1)
        [MainTexture] _MainTex ("Colour", 2D) = "white" {}
        [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _Mask ("Kit mask", 2D) = "black" {}
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        _Cloth ("Cloth", Color) = (1, 1, 1, 0)
        _Panel ("Panels", Color) = (1, 1, 1, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_BumpMap);
        SAMPLER(sampler_BumpMap);
        TEXTURE2D(_Mask);
        SAMPLER(sampler_Mask);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            half _Smoothness;
        CBUFFER_END

        UNITY_INSTANCING_BUFFER_START(Kit)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Cloth)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Panel)
        UNITY_INSTANCING_BUFFER_END(Kit)
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Assets/Rendering/Shaders/LitKeywords.hlsl"
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half fog : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // The texture's own brightness over the part's usual brightness: the folds and seams to keep.
            half3 Shade(half3 colour, half value, half usual)
            {
                #ifndef UNITY_COLORSPACE_GAMMA
                usual = SRGBToLinear(usual);
                #endif
                return colour * min(value / usual, 1.6);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 raw = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                half2 m = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, i.uv).rg;
                half v = max(raw.r, max(raw.g, raw.b));
                half3 c = raw * _Color.rgb;
                half4 cloth = UNITY_ACCESS_INSTANCED_PROP(Kit, _Cloth);
                half4 panel = UNITY_ACCESS_INSTANCED_PROP(Kit, _Panel);
                half3 rgb = lerp(c, Shade(cloth.rgb, v, 0.36), m.r * cloth.a);
                rgb = lerp(rgb, Shade(panel.rgb, v, 0.74), m.g * panel.a);

                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv));
                float3 bitangent = i.tangentWS.w * cross(i.normalWS, i.tangentWS.xyz);
                half3 normalWS = TransformTangentToWorld(normalTS, half3x3(i.tangentWS.xyz, bitangent, i.normalWS));
                return ShadePbr(i.positionWS, normalWS, i.positionCS, i.fog, rgb, 0, _Smoothness);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex TarmacShadowVertex
            #pragma fragment TarmacNullFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma vertex TarmacDepthVertex
            #pragma fragment TarmacNullFragment
            #pragma multi_compile_instancing
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TarmacMotionVertex
            #pragma fragment TarmacMotionFragment
            #pragma multi_compile_instancing
            #include "Assets/Rendering/Shaders/TarmacMotion.hlsl"
            ENDHLSL
        }
    }
}
