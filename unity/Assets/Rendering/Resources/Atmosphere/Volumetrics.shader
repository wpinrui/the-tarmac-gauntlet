// The volumetric clouds and fog (VolumetricsFeature), at reduced resolution and smoothed over frames.
//
// March: each low resolution pixel follows its view ray through a low mist out to _TgSteps.z metres, lit by the sun
// through the shadow map and the clouds' shadows, so the sun's light falls through the trees in shafts, and by the
// floodlights' beams at night. Past the scene, a sky ray climbs through the cloud layer: 3D noise shaped by the same
// cloud map as the clouds' shadows, lit towards the sun, with a bright rim where the sun is behind a cloud. The start of
// each ray steps on a little every frame. Out goes the light the air adds, and in alpha how much of what is behind shows.
//
// Resolve: blends this frame's march with the frames before, found where they were on screen last frame, kept within
// what the pixels round it saw now so a moved object leaves no trail.
//
// Composite: lays the result over the full resolution frame, each pixel taking the low resolution pixels nearest its own
// depth so the fog keeps to the edges of what is in front.
Shader "Hidden/TarmacGauntlet/Volumetrics"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        HLSLINCLUDE
        // The mist reads the sun's cascaded shadow map itself, whatever the frame's keywords: at High and Ultra the scene
        // takes its shadows from the screen, which says nothing of the air in front of it.
        #define _MAIN_LIGHT_SHADOWS_CASCADE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "Assets/Rendering/Shaders/CloudCover.hlsl"

        #define TG_MOST_BEAMS 16

        TEXTURE2D(_TgDepth);
        TEXTURE2D(_TgHistory);
        TEXTURE3D(_TgCloudNoise3D);
        SAMPLER(sampler_TgCloudNoise3D);
        TEXTURE2D(_TgCloudShadowTex);

        float4x4 _TgInvViewProj, _TgPrevViewProj, _TgWorldToCloudShadow;
        float4 _TgCamPos;      // xyz the camera, w the frame number
        float4 _TgLowSize;     // the low resolution: 1 / width, 1 / height, width, height
        float4 _TgSteps;       // cloud steps, fog steps, fog reach (m), 1 when there is a history to blend with
        float4 _TgCloudSlab;   // the cloud layer's base and top, m, its extinction per m, and 1 to draw clouds
        float4 _TgCloudDrift;  // how far the wind has carried the clouds, m: x east, y north
        float4 _TgSunDir, _TgSunLight, _TgCloudLit, _TgCloudShade;
        float4 _TgHaze;        // the haze's colour, and in w its density per m
        float4 _TgMist;        // the mist's density at the ground per m, how fast it thins with height (m), the ground's height
        float4 _TgBeams;       // how many beams, and their light
        float4 _TgBeamPos[TG_MOST_BEAMS];   // each lens, and in w how far its beam reaches
        float4 _TgBeamAim[TG_MOST_BEAMS];   // each beam's aim, and in w the cosine of half its cone

        float RawDepth(float2 uv)
        {
            return SAMPLE_TEXTURE2D_LOD(_TgDepth, sampler_PointClamp, uv, 0).r;
        }

        bool IsSky(float raw)
        {
        #if UNITY_REVERSED_Z
            return raw <= 1e-7;
        #else
            return raw >= 1.0 - 1e-7;
        #endif
        }

        float3 WorldAt(float2 uv, float raw)
        {
        #if !UNITY_REVERSED_Z
            raw = raw * 2.0 - 1.0;
        #endif
            return ComputeWorldSpacePosition(uv, raw, _TgInvViewProj);
        }

        // Henyey-Greenstein scattering, per steradian.
        float Phase(float cs, float g)
        {
            float k = 1.0 + g * g - 2.0 * g * cs;
            return (1.0 - g * g) / (4.0 * PI * k * sqrt(k));
        }

        // The same, 1 looking straight at the light.
        float Glow(float cs, float g)
        {
            float k = 1.0 + g * g - 2.0 * g * cs;
            return (1.0 - g) * (1.0 - g) * (1.0 - g) / (k * sqrt(k));
        }
        ENDHLSL

        Pass
        {
            Name "March"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float SunShadow(float3 p)
            {
                return MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
            }

            float CloudShadow(float3 p)
            {
                float2 uv = mul(_TgWorldToCloudShadow, float4(p, 1.0)).xy;
                if (any(uv < 0.0) || any(uv > 1.0)) return 1.0;
                return SAMPLE_TEXTURE2D_LOD(_TgCloudShadowTex, sampler_LinearClamp, uv, 0).r;
            }

            // The floodlights' light falling on this point of the air.
            float3 Beams(float3 p)
            {
                float lit = 0.0;
                int n = (int)_TgBeams.x;
                UNITY_LOOP
                for (int k = 0; k < n; k++)
                {
                    float3 v = p - _TgBeamPos[k].xyz;
                    float dd = dot(v, v);
                    float reach = _TgBeamPos[k].w;
                    if (dd < reach * reach)
                    {
                        float c = dot(v * rsqrt(dd + 1e-4), _TgBeamAim[k].xyz);
                        float edge = _TgBeamAim[k].w;
                        float cone = smoothstep(edge, lerp(edge, 1.0, 0.4), c);
                        lit += cone * saturate(1.0 - dd / (reach * reach)) / (dd + 9.0);
                    }
                }
                return _TgBeams.yzw * (lit / (4.0 * PI));
            }

            // How thick the cloud is here, 0 to 1.
            float CloudDensity(float3 p)
            {
                float h = saturate((p.y - _TgCloudSlab.x) / (_TgCloudSlab.y - _TgCloudSlab.x));
                float cover = TgCloudCover(p.xz);
                // Flat bases, and tops that climb as the cover thickens: thin cloud is a low puff, a deck fills the layer.
                float c = cover * saturate(h * 7.0) * saturate((cover * 1.15 - h) * 3.5);
                if (c < 0.01) return 0.0;
                float3 q = (p + float3(_TgCloudDrift.x, 0.0, _TgCloudDrift.y)) / 1800.0;
                float billows = SAMPLE_TEXTURE3D_LOD(_TgCloudNoise3D, sampler_TgCloudNoise3D, q, 0).r;
                float d = saturate((billows - (1.0 - c)) / max(c, 0.05));
                if (d <= 0.0) return 0.0;
                float detail = SAMPLE_TEXTURE3D_LOD(_TgCloudNoise3D, sampler_TgCloudNoise3D, q * 4.0, 0).g;
                // The fine cells eat the edges, more at the wispy tops than the base.
                float eat = (1.0 - detail) * 0.35 * (1.0 - 0.5 * h);
                return saturate((d - eat) / (1.0 - eat));
            }

            // Optical depth from here towards the sun, through five steps of growing length.
            float TowardsSun(float3 p, float3 sun)
            {
                float depth = 0.0, step = 60.0;
                UNITY_UNROLL
                for (int j = 0; j < 5; j++)
                {
                    p += sun * step;
                    depth += CloudDensity(p) * step;
                    step += 60.0;
                }
                return depth * _TgCloudSlab.z;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float raw = RawDepth(uv);
                float3 cam = _TgCamPos.xyz;
                float3 ray = WorldAt(uv, raw) - cam;
                float dist = length(ray);
                float3 d = ray / max(dist, 1e-4);
                if (IsSky(raw)) dist = 1e7;
                float jitter = InterleavedGradientNoise(input.positionCS.xy, (int)_TgCamPos.w);
                float3 sun = _TgSunDir.xyz;
                float cs = dot(d, sun);

                // The mist near the ground, out to the reach or the scene.
                float3 fogLight = 0.0;
                float fogT = 1.0;
                int fogSteps = (int)_TgSteps.y;
                float reach = min(dist, _TgSteps.z);
                float sunPhase = 0.75 * Phase(cs, 0.6) + 0.25 * Phase(cs, -0.2);
                bool beams = _TgBeams.x > 0.5;
                UNITY_LOOP
                for (int i = 0; i < fogSteps; i++)
                {
                    // Steps closer together near the camera, where the shafts and beams are seen up close.
                    float a = (float)i / fogSteps, b = (float)(i + 1) / fogSteps;
                    float t0 = reach * a * a, t1 = reach * b * b;
                    float t = lerp(t0, t1, jitter);
                    float3 p = cam + d * t;
                    float rho = _TgMist.x * exp(-max(p.y - _TgMist.z, 0.0) / _TgMist.y);
                    if (rho < 1e-7) continue;
                    float3 light = _TgHaze.rgb + _TgSunLight.rgb * (sunPhase * SunShadow(p) * CloudShadow(p));
                    if (beams) light += Beams(p);
                    float through = exp(-rho * (t1 - t0));
                    fogLight += fogT * light * (1.0 - through);
                    fogT *= through;
                }

                // The clouds, for a ray that climbs past the scene.
                float3 cloudLight = 0.0;
                float cloudT = 1.0;
                if (_TgCloudSlab.w > 0.5 && d.y > 0.01)
                {
                    float enter = max((_TgCloudSlab.x - cam.y) / d.y, 0.0);
                    float leave = min(min((_TgCloudSlab.y - cam.y) / d.y, dist), 40000.0);
                    if (leave > enter)
                    {
                        int steps = (int)_TgSteps.x;
                        float dt = (leave - enter) / steps;
                        float t = enter + dt * jitter;
                        float rim = Glow(cs, 0.6);
                        UNITY_LOOP
                        for (int s = 0; s < steps; s++)
                        {
                            float3 p = cam + d * t;
                            float rho = CloudDensity(p);
                            if (rho > 0.002)
                            {
                                float h = saturate((p.y - _TgCloudSlab.x) / (_TgCloudSlab.y - _TgCloudSlab.x));
                                float od = TowardsSun(p, sun);
                                // Light that gets through, with some scattered on through the thick of the cloud.
                                float lit = max(exp(-od), 0.7 * exp(-od * 0.25));
                                // The edges facing the sun are darker than the light behind them suggests.
                                float powder = 1.0 - exp(-rho * _TgCloudSlab.z * 240.0);
                                float3 shade = _TgCloudShade.rgb * lerp(0.75, 1.1, h);
                                float3 light = shade + max(_TgCloudLit.rgb - shade, 0.0) * (lit * lerp(0.5, 1.0, powder) * (0.75 + 1.6 * rim));
                                float through = exp(-rho * _TgCloudSlab.z * dt);
                                cloudLight += cloudT * light * (1.0 - through);
                                cloudT *= through;
                                if (cloudT < 0.01) break;
                            }
                            t += dt;
                        }
                        // Low down the clouds fade into the haze at the horizon, and the furthest go altogether.
                        float haze = exp(-d.y * 10.0) * 0.8;
                        cloudLight = lerp(cloudLight, _TgHaze.rgb * (1.0 - cloudT), haze);
                        float keep = saturate(1.0 - (enter - 20000.0) / 15000.0);
                        cloudLight *= keep;
                        cloudT = lerp(1.0, cloudT, keep);
                    }
                }

                return float4(cloudLight * fogT + fogLight, cloudT * fogT);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Resolve"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float4 now = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
                if (_TgSteps.w < 0.5) return now;
                float4 lo = now, hi = now;
                UNITY_UNROLL
                for (int y = -1; y <= 1; y++)
                    UNITY_UNROLL
                    for (int x = -1; x <= 1; x++)
                    {
                        float4 n = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv + float2(x, y) * _TgLowSize.xy, 0);
                        lo = min(lo, n);
                        hi = max(hi, n);
                    }
                float raw = RawDepth(uv);
                float3 p = WorldAt(uv, raw);
                // The sky and the clouds are far enough off to follow the camera's turn alone.
                if (IsSky(raw)) p = _TgCamPos.xyz + normalize(p - _TgCamPos.xyz) * 8000.0;
                float2 before = ComputeNormalizedDeviceCoordinates(p, _TgPrevViewProj);
                if (any(before < 0.0) || any(before > 1.0)) return now;
                float4 history = clamp(SAMPLE_TEXTURE2D_LOD(_TgHistory, sampler_LinearClamp, before, 0), lo, hi);
                return lerp(now, history, 0.9);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            Blend One SrcAlpha, Zero One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float z = LinearEyeDepth(RawDepth(uv), _ZBufferParams);
                float2 at = uv * _TgLowSize.zw - 0.5;
                float2 base = floor(at), f = at - base;
                float4 sum = 0.0;
                float weights = 0.0;
                UNITY_UNROLL
                for (int k = 0; k < 4; k++)
                {
                    float2 o = float2(k & 1, k >> 1);
                    float2 tuv = (base + o + 0.5) * _TgLowSize.xy;
                    float near = LinearEyeDepth(RawDepth(tuv), _ZBufferParams);
                    float2 bw = lerp(1.0 - f, f, o);
                    float w = bw.x * bw.y / (0.01 + abs(z - near) / max(z, 1e-3));
                    sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, tuv, 0) * w;
                    weights += w;
                }
                float4 v = sum / max(weights, 1e-5);
                return float4(max(v.rgb, 0.0), saturate(v.a));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
