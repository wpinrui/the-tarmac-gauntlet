// The racing line assist: translucent chevrons sitting on the track ahead of the car you drive (RacingLineAssist.cs). Each vertex
// carries its colour and how strongly it shows. Unlit, so it reads the same by day and night. The mesh is laid
// far to near and writes depth, so the blocks stack right. Fogged.
Shader "Hidden/TarmacGauntlet/RacingLine"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-90" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "RacingLine"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Off
            Offset -1, -3

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : TEXCOORD0;
                half fog : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(MixFog(i.color.rgb, i.fog), i.color.a);
            }
            ENDHLSL
        }
    }
}
