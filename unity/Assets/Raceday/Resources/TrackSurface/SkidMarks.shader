// Skid marks: rubber laid on the track where a tyre slid, drawn as one mesh of strips (SkidMarks.cs). Each corner's
// alpha says how hard the tyre slid there. Drawn just over the surface it lies on and pulled towards the camera in
// depth, so it never flickers through the road, lines or kerbs. Fogged like the road under it.
Shader "Hidden/TarmacGauntlet/SkidMarks"
{
    Properties
    {
        [MainColor] _BaseColor ("Rubber", Color) = (0.02, 0.02, 0.02, 0.85)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SkidMarks"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -2

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half alpha : TEXCOORD0;
                half fog : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.alpha = v.color.a;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(MixFog(_BaseColor.rgb, i.fog), _BaseColor.a * i.alpha);
            }
            ENDHLSL
        }
    }
}
