// Rain streaks and wheel spray: unlit, soft edged particles drawn in the light around them.
// _Shape 0 fades a stretched quad to its edges for a streak, 1 draws a soft round puff. No texture is read.
Shader "Hidden/TarmacGauntlet/Drops"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Shape ("Shape", Float) = 0
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

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Shape;
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
                o.color = v.color * _Tint;
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                // A stretched particle is already a thin quad along its fall: fade it to every edge.
                float streak = saturate(1.0 - abs(p.x)) * saturate(1.0 - abs(p.y));
                float r = saturate(1.0 - length(p));
                float puff = r * r;
                half4 col = i.color;
                col.a *= lerp(streak, puff, _Shape);
                col.rgb = MixFog(col.rgb, i.fog);
                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
