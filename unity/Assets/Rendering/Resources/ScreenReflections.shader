// The screen-space reflections (ScreenReflectionsFeature). Pass 0 copies the frame. Pass 1 traces a ray from each
// reflecting pixel along its reflection, through the depth buffer a pixel or more a step, and takes the frame's colour
// where it hits: blurred more the rougher the surface and the further the hit, and trusted less towards the screen's
// edges, where the ray runs back towards the camera, and beyond its reach. Pass 2 swaps the probe reflection the
// surface's forward pass drew for the traced one, as far as the trace is trusted. What each surface reflects comes from
// its TarmacReflect pass (TarmacReflect.hlsl).
Shader "Hidden/TarmacGauntlet/ScreenReflections"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

        TEXTURE2D(_SsrSource);
        TEXTURE2D(_SsrScene);
        TEXTURE2D_FLOAT(_SsrDepth);
        TEXTURE2D(_SsrSurface);
        TEXTURE2D(_SsrProbe);
        TEXTURE2D(_SsrTrace);
        SAMPLER(sampler_SsrPointClamp);
        SAMPLER(sampler_SsrLinearClamp);

        // From world space to the screen, unflipped, so x and y run as the textures' uv do, and back.
        float4x4 _SsrViewProj;
        float4x4 _SsrInvViewProj;
        float3 _SsrCamera;
        float3 _SsrForward;
        // Width, height, one over each, pixels.
        float4 _SsrSize;
        float _SsrNear;
        float _SsrReach;
        float _SsrSteps;
        float _SsrThickness;

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        Varyings Vert(uint id : SV_VertexID)
        {
            Varyings o;
            o.positionCS = GetFullScreenTriangleVertexPosition(id);
            o.uv = GetFullScreenTriangleTexCoord(id);
            return o;
        }

        float RawDepth(float2 uv)
        {
            return SAMPLE_TEXTURE2D_LOD(_SsrDepth, sampler_SsrPointClamp, uv, 0).r;
        }

        float3 WorldAt(float2 uv, float raw)
        {
            float4 p = mul(_SsrInvViewProj, float4(uv * 2 - 1, raw, 1));
            return p.xyz / p.w;
        }
        ENDHLSL

        Pass
        {
            Name "Copy"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Copy

            half4 Copy(Varyings i) : SV_Target
            {
                return SAMPLE_TEXTURE2D_LOD(_SsrSource, sampler_SsrPointClamp, i.uv, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Trace"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Trace

            // How many binary steps narrow a hit down between the last two samples.
            #define SSR_REFINE 5
            // The most pixels a rough reflection blurs over.
            #define SSR_MOST_BLUR 24.0
            // The brightest colour a reflection takes, so the sun in a puddle can't bloom into a square.
            #define SSR_BRIGHTEST 32.0

            // How far the ray has gone at t, 0 to 1 of its length on the screen: its uv and its depth from the camera.
            float2 RayUv(float2 s0, float2 s1, float t) { return lerp(s0, s1, t); }
            float RayDepth(float k0, float k1, float t) { return 1 / lerp(k0, k1, t); }

            half4 Trace(Varyings i) : SV_Target
            {
                half4 surface = SAMPLE_TEXTURE2D_LOD(_SsrSurface, sampler_SsrPointClamp, i.uv, 0);
                if (surface.a <= 0.001) return 0;
                float raw = RawDepth(i.uv);
                float3 p = WorldAt(i.uv, raw);
                float3 n = UnpackNormalOctQuadEncode(surface.xy * 2 - 1);
                float3 v = normalize(_SsrCamera - p);
                float3 r = reflect(-v, n);
                half roughness = surface.z;

                // The ray's ends on the screen, the far one kept in front of the camera.
                float reach = _SsrReach;
                float4 c0 = mul(_SsrViewProj, float4(p, 1));
                float4 c1 = mul(_SsrViewProj, float4(p + r * reach, 1));
                if (c1.w < _SsrNear)
                {
                    reach *= (c0.w - _SsrNear) / max(c0.w - c1.w, 1e-5);
                    c1 = mul(_SsrViewProj, float4(p + r * reach, 1));
                }
                float k0 = 1 / c0.w, k1 = 1 / c1.w;
                float2 s0 = c0.xy * k0 * 0.5 + 0.5, s1 = c1.xy * k1 * 0.5 + 0.5;

                // Only as far as the screen's edge.
                float2 dir = s1 - s0;
                float2 room = lerp(s0, 1 - s0, step(0, dir)) / max(abs(dir), 1e-6);
                float tEnd = saturate(min(room.x, room.y));
                float pixels = length(dir * tEnd * _SsrSize.xy);
                if (pixels < 2) return 0;
                float count = clamp(ceil(pixels), 1, _SsrSteps);
                // Start a pixel and a half out, so the surface can't hit itself.
                float tStart = 1.5 / length(dir * _SsrSize.xy);
                float dt = (tEnd - tStart) / count;
                if (dt <= 0) return 0;
                float jitter = InterleavedGradientNoise(i.uv * _SsrSize.xy, 0);

                float tPrev = tStart, zPrev = RayDepth(k0, k1, tStart);
                float tHit = -1;
                UNITY_LOOP
                for (float j = 0; j < count; j++)
                {
                    float t = tStart + dt * (j + jitter);
                    float z = RayDepth(k0, k1, t);
                    float scene = LinearEyeDepth(RawDepth(RayUv(s0, s1, t)), _ZBufferParams);
                    float near = min(zPrev, z), far = max(zPrev, z);
                    // The ray has passed behind what the screen shows there, by no more than that thing is thick.
                    if (far >= scene && near <= scene + _SsrThickness * (1 + scene * 0.02))
                    {
                        tHit = t;
                        break;
                    }
                    tPrev = t;
                    zPrev = z;
                }
                if (tHit < 0) return 0;

                float lo = tPrev, hi = tHit;
                for (int b = 0; b < SSR_REFINE; b++)
                {
                    float mid = (lo + hi) * 0.5;
                    float scene = LinearEyeDepth(RawDepth(RayUv(s0, s1, mid)), _ZBufferParams);
                    if (RayDepth(k0, k1, mid) >= scene) hi = mid; else lo = mid;
                }
                float2 at = RayUv(s0, s1, hi);

                // Trust: none at the screen's edge, none for a ray turning back towards the camera, less near its reach.
                float2 edge = min(at, 1 - at);
                half trust = smoothstep(0, 0.06, min(edge.x, edge.y));
                trust *= smoothstep(-0.3, 0.05, dot(r, _SsrForward));
                // How far along the ray the hit lies in the world, 0 to 1 of its reach: perspective-correct from the screen.
                float along = hi * k1 / lerp(k0, k1, hi) * reach / _SsrReach;
                trust *= 1 - smoothstep(0.7, 1, along);
                // Nor where the hit is the sky: it has no depth to stop a ray, and the probes hold it.
                #if UNITY_REVERSED_Z
                    trust *= RawDepth(at) > 0 ? 1 : 0;
                #else
                    trust *= RawDepth(at) < 1 ? 1 : 0;
                #endif

                // A rough surface blurs what it reflects, more the further the hit lies from it.
                float spread = min(roughness * roughness * length(dir * _SsrSize.xy) * hi * 0.5, SSR_MOST_BLUR);
                half3 colour = SAMPLE_TEXTURE2D_LOD(_SsrScene, sampler_SsrLinearClamp, at, 0).rgb;
                UNITY_BRANCH
                if (spread > 0.75)
                {
                    float turn = jitter * TWO_PI;
                    float2 a = float2(cos(turn), sin(turn)) * spread * _SsrSize.zw, b2 = float2(-sin(turn), cos(turn)) * spread * _SsrSize.zw;
                    colour += SAMPLE_TEXTURE2D_LOD(_SsrScene, sampler_SsrLinearClamp, at + a, 0).rgb;
                    colour += SAMPLE_TEXTURE2D_LOD(_SsrScene, sampler_SsrLinearClamp, at - a, 0).rgb;
                    colour += SAMPLE_TEXTURE2D_LOD(_SsrScene, sampler_SsrLinearClamp, at + b2, 0).rgb;
                    colour += SAMPLE_TEXTURE2D_LOD(_SsrScene, sampler_SsrLinearClamp, at - b2, 0).rgb;
                    colour *= 0.2;
                }
                return half4(min(colour, SSR_BRIGHTEST), trust);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Composite

            // A neighbour's trace counts towards a pixel's as much as their surfaces face the same way.
            half4 Neighbour(float2 uv, half3 n, inout half total)
            {
                half4 s = SAMPLE_TEXTURE2D_LOD(_SsrSurface, sampler_SsrPointClamp, uv, 0);
                half w = s.a > 0.001 ? pow(saturate(dot(n, UnpackNormalOctQuadEncode(s.xy * 2 - 1))), 16) : 0;
                total += w;
                return SAMPLE_TEXTURE2D_LOD(_SsrTrace, sampler_SsrPointClamp, uv, 0) * w;
            }

            half4 Composite(Varyings i) : SV_Target
            {
                half4 scene = SAMPLE_TEXTURE2D_LOD(_SsrScene, sampler_SsrPointClamp, i.uv, 0);
                half4 surface = SAMPLE_TEXTURE2D_LOD(_SsrSurface, sampler_SsrPointClamp, i.uv, 0);
                if (surface.a <= 0.001) return scene;
                half3 n = UnpackNormalOctQuadEncode(surface.xy * 2 - 1);
                // The trace's dither smoothed over the pixel and its four neighbours on the same surface.
                half total = 1;
                half4 traced = SAMPLE_TEXTURE2D_LOD(_SsrTrace, sampler_SsrPointClamp, i.uv, 0);
                traced += Neighbour(i.uv + float2(_SsrSize.z, 0), n, total);
                traced += Neighbour(i.uv - float2(_SsrSize.z, 0), n, total);
                traced += Neighbour(i.uv + float2(0, _SsrSize.w), n, total);
                traced += Neighbour(i.uv - float2(0, _SsrSize.w), n, total);
                traced /= total;
                half3 probe = SAMPLE_TEXTURE2D_LOD(_SsrProbe, sampler_SsrPointClamp, i.uv, 0).rgb;
                scene.rgb = max(scene.rgb + surface.a * traced.a * (traced.rgb - probe), 0);
                return scene;
            }
            ENDHLSL
        }
    }
}
