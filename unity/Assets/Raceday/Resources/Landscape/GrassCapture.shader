// Draws the circuit from straight above into the grass's map (Grass.cs): the height of the top surface at each texel,
// and 1 where that surface is grass, 0 where it is anything else (the road, a kerb, gravel, a roof). The highest surface
// wins, so the ground under the road never grows grass through it. The view is worked out here, not by the camera: x
// and z across the map, height down from the top of the map into depth.
Shader "Hidden/TarmacGauntlet/GrassCapture"
{
    Properties
    {
        _Grass ("Is grass", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite On ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _Grass;
            // The map's middle (xz), 1 / its width, and its top, m.
            float4 _CaptureAt;
            // How far below the top the map reaches, m.
            float _CaptureDepth;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float height : TEXCOORD0;
            };

            Varyings vert(float4 positionOS : POSITION)
            {
                float3 w = TransformObjectToWorld(positionOS.xyz);
                float2 across = (w.xz - _CaptureAt.xy) * _CaptureAt.z * 2;
                float d = saturate((_CaptureAt.w - w.y) / _CaptureDepth);
                Varyings o;
                #if UNITY_UV_STARTS_AT_TOP
                    across.y = -across.y;
                #endif
                #if UNITY_REVERSED_Z
                    o.positionCS = float4(across, 1 - d, 1);
                #else
                    o.positionCS = float4(across, lerp(UNITY_NEAR_CLIP_VALUE, 1, d), 1);
                #endif
                o.height = w.y;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                return float4(i.height, _Grass, 0, 0);
            }
            ENDHLSL
        }
    }
}
