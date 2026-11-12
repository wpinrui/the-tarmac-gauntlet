// A car's paint in one colour, with the clear coat and metallic flakes of CarPaint.hlsl. CarPaint.Respray puts it on a
// car's "body" material. A team's livery is drawn by the Livery shader, with the same paint. See docs/car-paint.md.
Shader "TarmacGauntlet/CarPaint"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.8
        _Metallic ("Metallic", Range(0, 1)) = 0.1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half _Smoothness, _Metallic;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _TARMAC_CLEARCOAT
            #pragma multi_compile_fragment _ _TARMAC_FLAKE
            #include_with_pragmas "Assets/Rendering/Shaders/LitKeywords.hlsl"
            #include "Assets/Raceday/Resources/CarPaint/CarPaint.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float3 flakePos : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs w = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = w.positionCS;
                o.positionWS = w.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fog = ComputeFogFactor(w.positionCS.z);
                // The part's own space, in metres whatever scale the importer gave it, so the flakes ride with the car.
                float3 scale = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22));
                o.flakePos = v.positionOS.xyz * scale;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return ShadeCarPaint(i.positionWS, i.normalWS, i.positionCS, i.fog, _BaseColor.rgb, _Metallic, _Smoothness, i.flakePos);
            }
            ENDHLSL
        }

        // The clear coat's view for the screen-space reflections (CarPaintReflect.hlsl).
        Pass
        {
            Name "TarmacReflect"
            Tags { "LightMode" = "TarmacReflect" }
            ZWrite Off ZTest LEqual Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CarReflectVertex
            #pragma fragment CarReflectFragment
            #pragma multi_compile_fragment _ _TARMAC_CLEARCOAT
            #include_with_pragmas "Assets/Rendering/Shaders/LitKeywords.hlsl"
            #include "Assets/Raceday/Resources/CarPaint/CarPaintReflect.hlsl"
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
            #include "Assets/Rendering/Shaders/TarmacMotion.hlsl"
            ENDHLSL
        }
    }
}
