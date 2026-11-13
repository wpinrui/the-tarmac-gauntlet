// The sun's shadow worked out once per pixel before the opaque objects are drawn, in place of URP's own screen-space
// shadows: URP's Screen Space Shadows feature on the renderer draws it with this shader (its hidden m_Shader). Every lit
// shader then reads the result. On top of URP's shadow it gives:
//  - soft penumbrae that widen with the distance from the shadow's caster, as the sun's disc makes them (a blocker search,
//    then a filter as wide as the penumbra);
//  - cascades blended across a band at each one's edge, so no seam shows where the shadow map's detail changes;
//  - contact shadows: a short march towards the sun through the depth buffer, for detail finer than the shadow map
//    (wheel arches, under the cars, kerbs, armco).
// Both test each pixel against its own surface's plane, rebuilt from the depth buffer, so a flat road or verge never
// shadows itself; and their noise turns each frame, so STP averages it away instead of leaving a fixed dot pattern.
// The quality level sets how much of each (QualityLevels.ShadowQuality).
Shader "Hidden/TarmacGauntlet/ScreenShadows"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        // x: the sun's spread, the penumbra's width per metre between caster and receiver; y: filter taps; z: blocker
        // search taps; w: the widest filter, in shadow map texels.
        float4 _TarmacShadowSoft;
        // x: contact shadow reach, m; y: steps; z: drawn out to this far from the camera, m; w: strength, 0 to 1.
        float4 _TarmacShadowContact;
        // x: the share of each cascade's radius, at its edge, blended into the next one.
        float4 _TarmacShadowBlend;
        // The frame, 0 to 63, that turns the noise while STP runs; 0 where it cannot, so the pattern stays still.
        float _TarmacShadowFrame;

        static const float2 Disc[16] =
        {
            float2(-0.94201624, -0.39906216), float2(0.94558609, -0.76890725), float2(-0.09418410, -0.92938870),
            float2(0.34495938, 0.29387760), float2(-0.91588581, 0.45771432), float2(-0.81544232, -0.87912464),
            float2(-0.38277543, 0.27676845), float2(0.97484398, 0.75648379), float2(0.44323325, -0.97511554),
            float2(0.53742981, -0.47373420), float2(-0.26496911, -0.41893023), float2(0.79197514, 0.19090188),
            float2(-0.24188840, 0.99706507), float2(-0.81409955, 0.91437590), float2(0.19984126, 0.78641367),
            float2(0.14383161, -0.14100790)
        };

        float Noise(float2 pixel)
        {
            pixel += 5.588238 * _TarmacShadowFrame;
            return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
        }

        // The surface a pixel shows: its world position, its normal, and the step in eye depth to its nearer neighbours.
        struct Surface
        {
            float3 position;
            float3 normal;
            float slope;
        };

        float3 WorldAt(float2 uv, float raw)
        {
        #if !UNITY_REVERSED_Z
            raw = raw * 2.0 - 1.0;
        #endif
            return ComputeWorldSpacePosition(uv, raw, unity_MatrixInvVP);
        }

        // uvX and uvY: the step in uv to the next pixel across and down, whichever way up the target is.
        Surface SurfaceAt(float2 pixel, float2 uv, float2 uvX, float2 uvY, float raw)
        {
            Surface o;
            o.position = WorldAt(uv, raw);
            float eye = LinearEyeDepth(raw, _ZBufferParams);
            float rawR = LoadSceneDepth(pixel + float2(1, 0)), rawL = LoadSceneDepth(pixel - float2(1, 0));
            float rawU = LoadSceneDepth(pixel + float2(0, 1)), rawD = LoadSceneDepth(pixel - float2(0, 1));
            float r = abs(LinearEyeDepth(rawR, _ZBufferParams) - eye), l = abs(LinearEyeDepth(rawL, _ZBufferParams) - eye);
            float u = abs(LinearEyeDepth(rawU, _ZBufferParams) - eye), d = abs(LinearEyeDepth(rawD, _ZBufferParams) - eye);
            // Each axis takes the nearer neighbour, so an edge to something else never bends the surface.
            float3 across = r < l ? WorldAt(uv + uvX, rawR) - o.position : o.position - WorldAt(uv - uvX, rawL);
            float3 down = u < d ? WorldAt(uv + uvY, rawU) - o.position : o.position - WorldAt(uv - uvY, rawD);
            float3 toCamera = _WorldSpaceCameraPos - o.position;
            float3 n = cross(across, down);
            n = dot(n, n) > 1e-12 ? normalize(n) : normalize(toCamera);
            o.normal = dot(n, toCamera) < 0.0 ? -n : n;
            o.slope = max(min(r, l), min(u, d));
            return o;
        }

        float Radius2(int c)
        {
            return c == 0 ? _CascadeShadowSplitSphereRadii.x : c == 1 ? _CascadeShadowSplitSphereRadii.y
                : c == 2 ? _CascadeShadowSplitSphereRadii.z : _CascadeShadowSplitSphereRadii.w;
        }

        float3 Centre(int c)
        {
            return c == 0 ? _CascadeShadowSplitSpheres0.xyz : c == 1 ? _CascadeShadowSplitSpheres1.xyz
                : c == 2 ? _CascadeShadowSplitSpheres2.xyz : _CascadeShadowSplitSpheres3.xyz;
        }

        bool Closer(float caster, float receiver)
        {
        #if UNITY_REVERSED_Z
            return caster > receiver;
        #else
            return caster < receiver;
        #endif
        }

        // The sun's shadow on a point from cascade c, 1 lit to 0 shadowed, with a penumbra as wide as the gap to its caster.
        float SoftShadow(int c, Surface surface, float2x2 spin)
        {
            float4x4 m = _MainLightWorldToShadow[c];
            float3 coord = mul(m, float4(surface.position, 1.0)).xyz;
            if (coord.z <= 0.0 || coord.z >= 1.0) return 1.0;
        #if UNITY_REVERSED_Z
            const float towardSun = 1.0;
        #else
            const float towardSun = -1.0;
        #endif
            // The receiver's own plane in the shadow map: how its depth changes across the map, so each sample is
            // compared with the surface where that sample lands rather than the flat depth of the pixel's centre.
            // Steep to the sun is held at a quarter, where the plane alone would push the samples too far.
            float3 n = surface.normal;
            float det = dot(m[0].xyz, cross(m[1].xyz, n));
            float least = 0.25 * length(m[0].xyz) * length(m[1].xyz);
            det = det < 0.0 ? min(det, -least) : max(det, least);
            float2 plane = float2(dot(m[2].xyz, cross(m[1].xyz, n)), dot(m[2].xyz, cross(n, m[0].xyz))) / det;
            // One texel's depth change along the plane more, for the texel the map rounds the surface to.
            float margin = towardSun * (abs(plane.x) + abs(plane.y)) * _MainLightShadowmapSize.x;

            // Shadow map units per metre across (x, y) and in depth (z), from the cascade's orthographic matrix.
            float2 uvPerMetre = float2(length(m[0].xyz), length(m[1].xyz));
            float metresPerDepth = 1.0 / max(length(m[2].xyz), 1e-6);
            float metresPerTexel = _MainLightShadowmapSize.x / uvPerMetre.x;
            // Samples stay inside the cascade's own tile of the shadow map.
            float2 tile = mul(m, float4(Centre(c), 1.0)).xy;
            float2 extent = sqrt(Radius2(c)) * uvPerMetre - _MainLightShadowmapSize.xy;
            float widest = _TarmacShadowSoft.w * metresPerTexel;

            // Blockers: how far above the point the shadow's casters are, on average. The search reaches as far as the
            // penumbra of a caster 20 m up.
            int search = (int)max(_TarmacShadowSoft.z, 1.0);
            float reach = clamp(_TarmacShadowSoft.x * 20.0, 2.0 * metresPerTexel, widest);
            float sum = 0.0, count = 0.0;
            [loop] for (int i = 0; i < search; i++)
            {
                float2 o = mul(spin, Disc[i & 15]) * (i < 16 ? 1.0 : 0.5);
                float2 offset = o * reach * uvPerMetre;
                float2 uv = clamp(coord.xy + offset, tile - extent, tile + extent);
                float d = SAMPLE_TEXTURE2D_LOD(_MainLightShadowmapTexture, sampler_PointClamp, uv, 0).r;
                if (Closer(d, coord.z + dot(plane, offset) + margin)) { sum += d; count += 1.0; }
            }
            if (count == 0.0) return 1.0;

            float gap = abs(coord.z - sum / count) * metresPerDepth;
            float radius = clamp(gap * _TarmacShadowSoft.x, 1.25 * metresPerTexel, widest);
            int taps = (int)max(_TarmacShadowSoft.y, 1.0);
            float lit = 0.0;
            [loop] for (int j = 0; j < taps; j++)
            {
                // Past 16 taps the disc turns and shrinks, so 32 fill it twice as densely.
                float2 o = mul(spin, Disc[j & 15]);
                if (j >= 16) o = float2(o.y, -o.x) * 0.6;
                float2 offset = o * radius * uvPerMetre;
                float2 uv = clamp(coord.xy + offset, tile - extent, tile + extent);
                float z = coord.z + dot(plane, offset) + margin;
                lit += SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, float3(uv, z));
            }
            return LerpWhiteTo(lit / taps, GetMainLightShadowParams().x);
        }

        // 1 lit to 0 shadowed by something between the point and the sun that the depth buffer shows within reach.
        float ContactShadow(Surface surface, float noise)
        {
            float distance = length(surface.position - _WorldSpaceCameraPos);
            float far = _TarmacShadowContact.z;
            if (_TarmacShadowContact.w <= 0.0 || distance > far) return 1.0;
            // The march starts two pixels' width off the surface, so it cannot begin inside the ground the pixel shows.
            float pixel = 2.0 * distance * _ScreenSize.w / abs(UNITY_MATRIX_P._m11);
            float3 start = surface.position + surface.normal * 2.0 * pixel;
            int steps = (int)max(_TarmacShadowContact.y, 1.0);
            // A little longer further off, where a pixel covers more.
            float reach = _TarmacShadowContact.x * (1.0 + distance * 0.02);
            float3 toSun = _MainLightPosition.xyz;
            float4x4 vp = GetWorldToHClipMatrix();
            float hit = 0.0;
            [loop] for (int i = 0; i < steps; i++)
            {
                float t = reach * (i + noise) / steps;
                float3 p = start + toSun * t;
                float3 ndc = ComputeNormalizedDeviceCoordinatesWithZ(p, vp);
                if (any(ndc.xy < 0.0) || any(ndc.xy > 1.0)) break;
                float scene = LinearEyeDepth(SampleSceneDepth(ndc.xy), _ZBufferParams);
                float ray = LinearEyeDepth(ndc.z, _ZBufferParams);
                float behind = ray - scene;
                // Past a pixel's own depth step too, which the depth buffer cannot tell apart from the surface itself.
                float bias = max(0.01 + 0.0015 * ray, surface.slope * Sq(ray / max(distance, 1e-3)) * 1.5);
                // Thin enough to be a real caster, not the far side of something in front.
                if (behind > bias && behind < 0.25 + 0.01 * ray)
                {
                    hit = 1.0 - (float)i / steps * 0.5;
                    break;
                }
            }
            float fade = 1.0 - smoothstep(far * 0.6, far, distance);
            // As faint as the sun's own shadow under cloud: 0.85 is its strength under a clear sky (CloudMap.Strength).
            float strength = _TarmacShadowContact.w * saturate(GetMainLightShadowParams().x / 0.85);
            return 1.0 - hit * fade * strength;
        }

        half4 Fragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uvX = ddx(input.texcoord.xy), uvY = ddy(input.texcoord.xy);

            float deviceDepth = LoadSceneDepth(input.positionCS.xy);
        #if UNITY_REVERSED_Z
            if (deviceDepth <= 0.0) return 1;
        #else
            if (deviceDepth >= 1.0) return 1;
        #endif
            Surface surface = SurfaceAt(input.positionCS.xy, input.texcoord.xy, uvX, uvY, deviceDepth);
            float3 positionWS = surface.position;

            float noise = Noise(input.positionCS.xy);
            float s, co;
            sincos(noise * TWO_PI, s, co);
            float2x2 spin = float2x2(co, -s, s, co);

        #if defined(_MAIN_LIGHT_SHADOWS_CASCADE)
            int c = (int)ComputeCascadeIndex(positionWS);
            if (c > 3 || Radius2(c) <= 0.0) return 1;
        #else
            int c = 0;
        #endif
            float shadow = SoftShadow(c, surface, spin);

        #if defined(_MAIN_LIGHT_SHADOWS_CASCADE)
            float band = _TarmacShadowBlend.x;
            if (band > 0.0 && c < 3 && Radius2(c + 1) > 0.0)
            {
                float edge = length(positionWS - Centre(c)) / sqrt(Radius2(c));
                float t = saturate((edge - (1.0 - band)) / band);
                float3 next = positionWS - Centre(c + 1);
                if (t > 0.0 && dot(next, next) < Radius2(c + 1))
                    shadow = lerp(shadow, SoftShadow(c + 1, surface, spin), t);
            }
        #endif

            return min(shadow, ContactShadow(surface, noise));
        }
        ENDHLSL

        Pass
        {
            Name "ScreenShadows"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma multi_compile _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma vertex Vert
            #pragma fragment Fragment
            ENDHLSL
        }
    }
}
