// The air shimmering over far tarmac in the heat (HeatHazeFeature): the frame is redrawn moved by a small rising wobble,
// strongest where the view runs low along the ground into the distance, and at the horizon past it. A near thing in
// front of the shimmer is never pulled into it.
Shader "Hidden/TarmacGauntlet/HeatShimmer"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "Shimmer"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X_FLOAT(_ShimmerDepth);
            float _ShimmerStrength;
            // x: distance the shimmer starts, m. y: distance it is strongest by, m. z: its reach, pixels on a screen 1080 high.
            float4 _ShimmerParams;

            float RawDepth(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_ShimmerDepth, sampler_PointClamp, uv, 0).r;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.texcoord;
                half4 here = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);

                float raw = RawDepth(uv);
                #if !UNITY_REVERSED_Z
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                #endif
                float3 world = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 ray = world - _WorldSpaceCameraPos;
                float dist = length(ray);
                float3 dir = ray / max(dist, 1e-4);

                // Far out, and looking along the ground just under the horizon.
                float far = smoothstep(_ShimmerParams.x, _ShimmerParams.y, dist);
                float graze = saturate(1.0 - abs(dir.y + 0.014) / 0.045);
                float w = far * graze * graze * _ShimmerStrength;
                if (w < 0.003) return here;

                // A wobble in screen pixels that climbs, broken up across the screen so it never reads as stripes.
                float scale = 1080.0 / _ScreenParams.y;
                float2 px = uv * _ScreenParams.xy * scale;
                float t = _Time.y;
                float a = sin(px.y * 0.42 + t * 10.0 + sin(px.x * 0.051 + t * 1.7) * 2.6);
                float b = sin(px.y * 0.23 + px.x * 0.037 + t * 6.1 + sin(px.x * 0.013 - t * 0.9) * 3.1);
                float2 shift = float2(a * 0.65 + b * 0.35, (b - a) * 0.3) * (w * _ShimmerParams.z);
                float2 at = uv + shift / (_ScreenParams.xy * scale);

                // Keep a nearer thing out of the far air: where the moved sample lands on one, stay put.
                float rawAt = RawDepth(at);
                #if !UNITY_REVERSED_Z
                rawAt = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawAt);
                #endif
                if (LinearEyeDepth(rawAt, _ZBufferParams) < LinearEyeDepth(raw, _ZBufferParams) * 0.7) return here;
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, at, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
