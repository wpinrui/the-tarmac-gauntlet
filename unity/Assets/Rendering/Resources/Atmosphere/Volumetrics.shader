// The volumetric clouds and fog (VolumetricsFeature), at reduced resolution and smoothed over frames.
//
// March: each low resolution pixel follows its view ray through a low mist out to _TgSteps.z metres, lit by the sun
// through the shadow map and the clouds' shadows, so the sun's light falls through the trees in shafts, and by the
// floodlights' beams at night. Past the scene, a sky ray climbs through the cloud layer: fair weather cumulus drawn by the
// 3D noise where the cloud map lets them be, or under an overcast one low rolling deck, lit by the sun through them and
// by the sky. Every constant of their look is in CloudLook.hlsl, which tools/cloud-preview reads to draw the same clouds
// offline. The start of each ray steps on a little every frame. Out goes the light the air adds, and in alpha how much of
// what is behind shows.
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
        #include "Assets/Rendering/Shaders/CloudLook.hlsl"

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
        float4 _TgCloudSlab;   // w 1 to draw clouds
        float4 _TgCloudDrift;  // how far the wind has carried the clouds, m: x east, y north
        float4 _TgSunDir, _TgSunLight, _TgCloudLit, _TgCloudShade;
        float4 _TgSkyLight, _TgGroundLight;   // the light in the clouds that is not the sun's: at their tops, at their bases
        float4 _TgHaze;        // the haze's colour, and in w its density per m
        float4 _TgMist;        // the mist's density at the ground per m, how fast it thins with height (m), the ground's height
        float4 _TgBeams;       // how many beams, and their light
        float _TgBeamMost;     // the most light the beams in the mist ever add, linear
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

            // How much of floodlight k's light falls on this point of the air, before its colour and strength.
            float BeamAt(int k, float3 p)
            {
                float3 v = p - _TgBeamPos[k].xyz;
                float dd = dot(v, v);
                float reach = _TgBeamPos[k].w;
                if (dd >= reach * reach) return 0.0;
                float c = dot(v * rsqrt(dd + 1e-4), _TgBeamAim[k].xyz);
                float edge = _TgBeamAim[k].w;
                float cone = smoothstep(edge, lerp(edge, 1.0, 0.4), c);
                return cone * saturate(1.0 - dd / (reach * reach)) / (dd + 9.0);
            }

            #define TG_BEAM_SAMPLES 8

            // The floodlights' light the mist scatters towards the camera along the ray, out to far, rising one for one
            // up to half of _TgBeamMost and then easing towards it, so however near a lens the camera is and however many
            // beams it looks along, the mist never glows brighter than that. Each beam is
            // sampled only over the stretch of the ray that passes through it: around where the ray comes nearest the
            // beam's axis, as long as the beam is wide there. So a narrow beam far off gets as many samples as a near one,
            // and never breaks up into dots the way it did sampled at the mist's own steps.
            float3 BeamMist(float3 cam, float3 d, float far, float jitter)
            {
                float lit = 0.0;
                int n = (int)_TgBeams.x;
                UNITY_LOOP
                for (int k = 0; k < n; k++)
                {
                    float3 lens = _TgBeamPos[k].xyz, aim = _TgBeamAim[k].xyz;
                    float reach = _TgBeamPos[k].w;
                    // The ray inside the beam's reach.
                    float3 w0 = cam - lens;
                    float b = dot(w0, d), c = dot(w0, w0) - reach * reach;
                    float disc = b * b - c;
                    if (disc <= 0.0) continue;
                    float root = sqrt(disc);
                    float t0 = max(-b - root, 0.0), t1 = min(-b + root, far);
                    // Where the ray comes nearest the axis, and how long it stays as near as the beam is wide there.
                    float da = dot(d, aim), e = dot(aim, w0);
                    float side = max(1.0 - da * da, 1e-4);
                    float tNear = (da * e - b) / side;
                    float sNear = max((e - da * b) / side, 0.0);
                    float cosEdge = _TgBeamAim[k].w;
                    float wide = sNear * sqrt(max(1.0 - cosEdge * cosEdge, 0.0)) / max(cosEdge, 0.05) + 2.0;
                    float stay = wide / max(sqrt(side), 0.05);
                    float ta = max(t0, tNear - stay), tb = min(t1, tNear + stay);
                    if (tb <= ta) continue;
                    float dt = (tb - ta) / TG_BEAM_SAMPLES;
                    UNITY_LOOP
                    for (int i = 0; i < TG_BEAM_SAMPLES; i++)
                    {
                        float t = ta + dt * (i + jitter);
                        float3 p = cam + d * t;
                        float rho = _TgMist.x * exp(-max(p.y - _TgMist.z, 0.0) / _TgMist.y);
                        lit += BeamAt(k, p) * rho * exp(-rho * t) * dt;
                    }
                }
                float3 glow = _TgBeams.yzw * (lit / (4.0 * PI));
                float most = max(_TgBeamMost, 1e-4), knee = most * 0.5;
                return min(glow, knee) + (most - knee) * (1.0 - exp(-max(glow - knee, 0.0) / (most - knee)));
            }

            // The cloud layer's base and thickness, m: lower and thicker as the sky closes into a deck.
            void CloudLayer(out float base, out float thick)
            {
                float o = _TgCloudMap.z;
                base = TG_CLOUD_BASE - TG_CLOUD_DECK_LOWERS * o;
                thick = TG_CLOUD_THICKNESS + TG_CLOUD_DECK_THICKENS * o;
            }

            // The 3D noise at q (in its own repeats), stretched from its 0.25 to 0.7 spread to 0 to 1.
            float Noise3(float3 q, int channel)
            {
                float4 n = SAMPLE_TEXTURE3D_LOD(_TgCloudNoise3D, sampler_TgCloudNoise3D, q, 0);
                return saturate(((channel == 0 ? n.r : n.g) - 0.25) / 0.45);
            }

            // The cloud before its billows and detail eat into it: never less than CloudDensity, so where this is empty
            // the march can stride. Also how high in the layer the point is, 0 to 1, and how far into the far field.
            // Fair weather cumulus: the 3D noise draws each cloud, the map only says how much of the sky they may take.
            // A flat base, then narrowing to a rounded top. Far off they merge into fewer, larger, flatter shapes, and
            // thin out, as perspective packs them together. Under an overcast they close into one deck: its underside
            // rolls up and down, it is thick in lumps and thin in patches.
            float CloudShape(float3 p, out float h, out float far)
            {
                float o = _TgCloudMap.z;
                float base, thick;
                CloudLayer(base, thick);
                h = (p.y - base) / thick;
                far = 0.0;
                if (h <= 0.0 || h >= 1.0) return 0.0;
                float n = TgCloudNoise(p.xz * _TgCloudMap.x, o);
                far = smoothstep(TG_CLOUD_FAR_NEAR, TG_CLOUD_FAR, length(p.xz - _TgCamPos.xz));
                float cover = saturate(TG_CLOUD_COVER_FLOOR + (n - _TgCloudMap.y) * TG_CLOUD_COVER_SLOPE) * (1.0 - TG_CLOUD_FAR_THINS * far) * (1.0 - o) + 0.95 * o;
                float3 q = p + float3(_TgCloudDrift.x, 0.0, _TgCloudDrift.y);
                float big = Noise3(q / TG_CLOUD_SHAPE_PERIOD, 0);
                if (far > 0.0) big = lerp(big, Noise3(q / TG_CLOUD_FAR_SHAPE_PERIOD, 0), far);
                float hf = h / (1.0 - 0.65 * far);
                float profile = hf < 1.0 ? saturate(hf / 0.06) * (1.0 - smoothstep(TG_CLOUD_TOP_ROUNDS, 1.0, hf) * (1.0 - 0.6 * o)) : 0.0;
                float d = saturate((big * profile - (1.0 - cover)) / max(cover, 1e-3)) * cover;
                d = saturate((d - TG_CLOUD_SHAPE_CUT) / (1.0 - TG_CLOUD_SHAPE_CUT));
                if (o > 0.0)
                {
                    float roll = Noise3(q / TG_CLOUD_DECK_ROLL_PERIOD, 1);
                    float lump = smoothstep(0.05, 0.95, 0.75 * Noise3(q / TG_CLOUD_DECK_LUMP_PERIOD + 0.37, 0) + 0.25 * Noise3(q / TG_CLOUD_DECK_PATCH_PERIOD + 0.61, 1));
                    float under = 0.04 + 0.22 * (1.0 - roll);
                    float deck = saturate((h - under) / 0.05) * (1.0 - smoothstep(0.45, 0.9, h)) * lerp(TG_CLOUD_DECK_THIN, 1.0, lump) * TG_CLOUD_DECK_DENSITY;
                    d = max(d * (1.0 - o), o * deck);
                }
                return d;
            }

            // How thick the cloud is here, 0 to 1: its shape, with the billows eating into the outside, more towards the
            // top so the tops are cauliflower and the base stays flat, and the detail fraying the edges. Thin wisps
            // between the clouds read as grain from the ground, so they are cleared.
            float CloudDensity(float3 p)
            {
                float o = _TgCloudMap.z;
                float h, far;
                float d = CloudShape(p, h, far);
                if (d <= 0.0) return 0.0;
                float3 q = p + float3(_TgCloudDrift.x, 0.0, _TgCloudDrift.y);
                float billow = Noise3(q / TG_CLOUD_BILLOW_PERIOD, 0);
                float erode = (1.0 - billow) * TG_CLOUD_EROSION * (0.4 + h) * (1.0 - 0.7 * far) * (1.0 - 0.8 * o);
                d = saturate((d - erode) / (1.0 - erode));
                if (d <= 0.0) return 0.0;
                float detail = Noise3(q / TG_CLOUD_DETAIL_PERIOD, 1);
                float fray = (1.0 - detail) * TG_CLOUD_FRAY * (1.0 - far) * (1.0 - 0.8 * o);
                d = saturate((d - fray) / (1.0 - fray));
                return saturate((d - TG_CLOUD_CUT) / (1.0 - TG_CLOUD_CUT) * TG_CLOUD_DENSITY);
            }

            // Optical depth from here towards the sun, over four steps of growing length.
            float TowardsSun(float3 p, float3 sun)
            {
                float depth = 0.0;
                float step = 50.0;
                UNITY_UNROLL
                for (int j = 0; j < 4; j++)
                {
                    p += sun * step;
                    depth += CloudDensity(p) * step;
                    step *= 2.0;
                }
                return depth * TG_CLOUD_EXTINCTION;
            }

            // Henyey-Greenstein times 4 pi, so 1 is isotropic: half forward, half back.
            float CloudPhase(float cs, float k)
            {
                float a = 0.8 * k, b = -0.3 * k;
                float ka = 1.0 + a * a - 2.0 * a * cs, kb = 1.0 + b * b - 2.0 * b * cs;
                return 0.5 * (1.0 - a * a) / (ka * sqrt(ka)) + 0.5 * (1.0 - b * b) / (kb * sqrt(kb));
            }

            // The short step length at this distance along the ray, m.
            float FineStep(float t)
            {
                return clamp(t * TG_CLOUD_STEP_SHARE, TG_CLOUD_STEP_MIN, TG_CLOUD_STEP_MAX);
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
                    float through = exp(-rho * (t1 - t0));
                    fogLight += fogT * light * (1.0 - through);
                    fogT *= through;
                }
                if (beams && fogSteps > 0) fogLight += BeamMist(cam, d, reach, jitter);

                // The clouds, for a ray that climbs past the scene.
                float3 cloudLight = 0.0;
                float cloudT = 1.0;
                if (_TgCloudSlab.w > 0.5 && d.y > 0.01)
                {
                    float base, thick;
                    CloudLayer(base, thick);
                    float o = _TgCloudMap.z;
                    float enter = max((base - cam.y) / d.y, 0.0);
                    // Rays low over the horizon cross the layer for many kilometres: they stop at 15, by when the haze has the clouds.
                    float leave = min(min((base + thick - cam.y) / d.y, dist), min(enter + 15000.0, 30000.0));
                    if (leave > enter)
                    {
                        // Each bounce of light inside a cloud loses some of the forward glow: thick cloud before the sun
                        // is dark, its thin edges silver.
                        float phase0 = CloudPhase(cs, 1.0), phase1 = CloudPhase(cs, 0.5), phase2 = CloudPhase(cs, 0.25);
                        float ambientK = TG_CLOUD_AMBIENT * lerp(1.0, TG_CLOUD_AMBIENT_DECK, o);
                        int steps = (int)_TgSteps.x;
                        // Strides through empty air on the cloud's shape; on touching one, back up a stride and walk it in
                        // short steps with the full density, until three steps in a row find nothing.
                        bool fine = false, clear = true;
                        int empty = 0;
                        float t = enter + FineStep(enter) * TG_CLOUD_STRIDE * jitter;
                        UNITY_LOOP
                        for (int s = 0; s < steps; s++)
                        {
                            if (t >= leave || cloudT < 0.01) break;
                            float3 p = cam + d * t;
                            float step = FineStep(t);
                            if (!fine)
                            {
                                float h, far;
                                if (CloudShape(p, h, far) > 0.0)
                                {
                                    if (clear) t = max(t - step * (TG_CLOUD_STRIDE - 1.0), enter);
                                    fine = true;
                                    empty = 0;
                                }
                                else
                                {
                                    t += step * TG_CLOUD_STRIDE;
                                    clear = true;
                                }
                                continue;
                            }
                            float rho = CloudDensity(p);
                            if (rho > 0.002)
                            {
                                float h = saturate((p.y - base) / thick);
                                float od = TowardsSun(p, sun);
                                // The sun's light through the cloud, with the light scattered on inside it lifting the shade.
                                float sunlit = exp(-od) * phase0 + TG_CLOUD_SCATTER_ONCE * exp(-od * 0.25) * phase1 + TG_CLOUD_SCATTER_TWICE * exp(-od * 0.06) * phase2;
                                float powder = 1.0 - exp(-rho * TG_CLOUD_EXTINCTION * 200.0);
                                float3 light = _TgCloudLit.rgb * (TG_CLOUD_SUN * sunlit * (0.3 + 0.7 * powder));
                                // Thick cloud above shuts the sky's light out: the undersides of thick cores darken.
                                light += lerp(_TgGroundLight.rgb, _TgSkyLight.rgb, h) * (ambientK * lerp(TG_CLOUD_SHUT_OUT, 1.0, exp(-od * 0.08)));
                                float through = exp(-rho * TG_CLOUD_EXTINCTION * step);
                                cloudLight += cloudT * light * (1.0 - through);
                                cloudT *= through;
                                empty = 0;
                            }
                            else empty++;
                            t += step;
                            if (empty >= 3)
                            {
                                fine = false;
                                clear = false;
                            }
                        }
                        // The far clouds sink into the haze: what shows of them fades, and the sky behind shows through.
                        float fade = exp(-enter * _TgHaze.w / lerp(TG_CLOUD_HAZE_REACH, TG_CLOUD_DECK_HAZE_REACH, o));
                        cloudLight *= fade;
                        cloudT = 1.0 - (1.0 - cloudT) * fade;
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
