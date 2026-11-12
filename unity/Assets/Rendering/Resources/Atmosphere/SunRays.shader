// The sun's rays on screen (SunRaysFeature).
//
// Mask: at a quarter of the resolution, the bright sky round the sun: the sun, its halo and the lit edges of the clouds
// in front of it. Trees, buildings and thick cloud leave it dark. Alpha is 1 where the sky is bright.
//
// Blur: smears the mask out from the sun, twice, the second time further, so the light streams out between whatever is
// in front of it.
//
// Composite: adds the rays to the frame, with a glare round the sun as much as it shows, and with _TgFlare a faint
// string of lens flare ghosts across the screen from it.
Shader "Hidden/TarmacGauntlet/SunRays"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D(_TgDepth);
        TEXTURE2D(_TgMask);
        float4x4 _TgViewProj;
        float4 _TgCamPos;     // xyz the camera
        float4 _TgSunDir, _TgSunLight;
        float4 _TgRays;       // x the brightness the sky must pass to stream, y the rays' strength, z the glare's, w the flare's
        float4 _TgRayStep;    // x the share of the way to the sun one blur reaches, y the screen's width over its height

        // The sun on screen, 0 to 1 each way.
        float2 SunUv()
        {
            return ComputeNormalizedDeviceCoordinates(_TgCamPos.xyz + _TgSunDir.xyz * 10000.0, _TgViewProj);
        }

        // A screen offset with x stretched to match y, so the rays and the glare are round.
        float2 Square(float2 v)
        {
            return float2(v.x * _TgRayStep.y, v.y);
        }
        ENDHLSL

        Pass
        {
            Name "Mask"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float raw = SAMPLE_TEXTURE2D_LOD(_TgDepth, sampler_PointClamp, uv, 0).r;
            #if UNITY_REVERSED_Z
                bool sky = raw <= 1e-7;
            #else
                bool sky = raw >= 1.0 - 1e-7;
            #endif
                if (!sky) return 0.0;
                float3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                float l = Luminance(c);
                float bright = smoothstep(_TgRays.x, _TgRays.x * 2.0, l);
                float r = length(Square(uv - SunUv()));
                float near = saturate(1.0 - r / 0.6);
                return float4(c * (bright * near * near), bright);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Blur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #define TG_RAY_TAPS 16

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 step = (SunUv() - uv) * (_TgRayStep.x / TG_RAY_TAPS);
                float4 sum = 0.0;
                float weight = 1.0, total = 0.0;
                UNITY_UNROLL
                for (int i = 0; i < TG_RAY_TAPS; i++)
                {
                    sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + step * i, 0) * weight;
                    total += weight;
                    weight *= 0.93;
                }
                return sum / total;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            Blend One One, Zero One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float3 Ghost(float2 uv, float2 at, float size, float3 tint)
            {
                float r = length(Square(uv - at)) / size;
                return tint * (smoothstep(1.0, 0.7, r) * (0.4 + 0.6 * r * r));
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 sun = SunUv();
                float3 rays = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb * _TgRays.y;

                // How much of the sun shows: the bright sky in a ring round it.
                float shows = 0.0;
                UNITY_UNROLL
                for (int i = 0; i < 8; i++)
                {
                    float a = i * (TWO_PI / 8.0);
                    float2 o = float2(cos(a) / _TgRayStep.y, sin(a)) * 0.012;
                    shows += SAMPLE_TEXTURE2D_LOD(_TgMask, sampler_LinearClamp, sun + o, 0).a;
                }
                shows += SAMPLE_TEXTURE2D_LOD(_TgMask, sampler_LinearClamp, sun, 0).a * 2.0;
                shows /= 10.0;

                float r = length(Square(uv - sun));
                float3 light = _TgSunLight.rgb * shows;
                float3 glare = light * (_TgRays.z * (exp(-r * 9.0) * 0.6 + exp(-r * 2.5) * 0.12));

                float3 flare = 0.0;
                if (_TgRays.w > 0.0)
                {
                    float2 across = float2(0.5, 0.5) - sun;
                    flare += Ghost(uv, sun + across * 0.55, 0.035, float3(0.35, 0.55, 1.0));
                    flare += Ghost(uv, sun + across * 1.1, 0.08, float3(0.5, 1.0, 0.6));
                    flare += Ghost(uv, sun + across * 1.45, 0.025, float3(1.0, 0.75, 0.4));
                    flare += Ghost(uv, sun + across * 1.9, 0.13, float3(0.8, 0.5, 1.0));
                    // Brightest with the sun near the middle of the screen.
                    flare *= light * (_TgRays.w * saturate(1.0 - length(Square(across)) * 0.9));
                }
                return float4(rays + glare + flare, 0.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
