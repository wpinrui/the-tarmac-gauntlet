// A team's livery on a car's paint, projected from its five views (Livery.hlsl). The race number is drawn into the
// livery's number boxes from the Digits atlas: the livery's own, or the shared one. The paint has the clear coat and
// metallic flakes of CarPaint.hlsl, as much of each as the livery's finish map gives.
// See docs/car-paint.md.
Shader "TarmacGauntlet/Livery"
{
    Properties
    {
        _Atlas ("Livery atlas", 2D) = "white" {}
        _Digits ("Digits", 2D) = "black" {}
        _Finish ("Finish", 2D) = "white" {}
        _Base ("Base colour", Color) = (1, 1, 1, 1)
        [HideInInspector] _FinishBase ("", Vector) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.8
        _Metallic ("Metallic", Range(0, 1)) = 0.1
        [HideInInspector] _ALeft ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _BLeft ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ARight ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _BRight ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ATop ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _BTop ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _AFront ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _BFront ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ARear ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _BRear ("", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        // Batching would move vertices out of the model's own space, which the projection needs.
        Tags { "RenderType" = "Opaque" "DisableBatching" = "True" "RenderPipeline" = "UniversalPipeline" }

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
            #include "Assets/Raceday/Resources/Livery/Livery.hlsl"

            half4 frag(Varyings i) : SV_Target
            {
                float3 p = i.objPos, n = normalize(i.objNormal);
                half3 f = LiveryFinish(p, n);
                return ShadeCarPaint(i.positionWS, i.normalWS, i.positionCS, i.fog, LiveryAlbedo(p, n),
                    _Metallic * f.b, _Smoothness * f.r, p, f.g, f.b);
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
            #pragma vertex vert
            #pragma fragment reflectFrag
            #pragma multi_compile_fragment _ _TARMAC_CLEARCOAT
            #include_with_pragmas "Assets/Rendering/Shaders/LitKeywords.hlsl"
            #include "Assets/Raceday/Resources/CarPaint/CarPaintReflect.hlsl"
            #include "Assets/Raceday/Resources/Livery/Livery.hlsl"

            // Only where the livery's finish has a clear coat.
            TarmacReflectOutput reflectFrag(Varyings i)
            {
                return CarCoatReflect(i.positionWS, i.normalWS, i.positionCS, i.fog, LiveryFinish(i.objPos, normalize(i.objNormal)).g);
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
