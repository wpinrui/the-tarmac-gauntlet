// The circuit's paved surfaces: the road, the pit lane and the kerbs. A grain texture with a normal map, both made in
// code (TrackTextures.cs), with broad patches laid over it in world space so the 4 m tile never reads as a repeat. On
// the road, a rubbered racing line: a map along the lap gives where the line runs across the road and how much rubber
// lies on it, and the band is darkest under the two tyre tracks. The mesh has no tangents, so the normal map is turned
// into the surface from the screen-space change of position and uv. In the wet (_TRACK_WET) the surface darkens as its
// pores fill, water fills the grain and shines with the scene's reflection probes, the cars clear a drier line, and
// puddles stand in the road's dips and at the edge its camber drains to, rippled by the rain. The water's depth and the
// rain come from WeatherView as globals. The keywords _TRACK_DETAIL (normals and patches), _TRACK_RUBBER (the line)
// and _TRACK_WET switch the parts on, so a part switched off costs nothing; puddles and ripples follow globals too.
Shader "Hidden/TarmacGauntlet/Track"
{
    Properties
    {
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [MainTexture] _MainTex ("Grain, gloss in alpha", 2D) = "white" {}
        [NoScaleOffset] _BumpMap ("Normals", 2D) = "bump" {}
        _BumpScale ("Bumpiness", Float) = 1
        [NoScaleOffset] _Macro ("Patches", 2D) = "gray" {}
        _MacroScale ("Metres per patch tile", Float) = 60
        _MacroStrength ("Patchiness", Range(0, 1)) = 0.5
        [NoScaleOffset] _Line ("Racing line along the lap", 2D) = "black" {}
        _LineLength ("Lap length in uv metres", Float) = 1
        _UvMetres ("Metres per uv unit", Vector) = (4, 4, 0, 0)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.25
        [NoScaleOffset] _Pools ("Where water pools along the lap", 2D) = "black" {}
        _Porosity ("How much it darkens wet", Range(0, 1)) = 1
        _Puddles ("Holds puddles", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_BumpMap);
        SAMPLER(sampler_BumpMap);
        TEXTURE2D(_Macro);
        SAMPLER(sampler_Macro);
        TEXTURE2D(_Line);
        SAMPLER(sampler_Line);
        TEXTURE2D(_Pools);
        SAMPLER(sampler_Pools);
        TEXTURE2D(_TrackRippleTex);
        SAMPLER(sampler_TrackRippleTex);

        // The weather on the track, set by WeatherView for every paved surface at once: the water film's depth, mm; how
        // hard the rain falls, 0 to 1; how much of the film the cars have cleared from the racing line, 0 to 1; and
        // whether puddles and their ripples are drawn, 0 or 1. _TrackRippleTiles is ripple tiles a metre.
        float _TrackWater;
        float _TrackRain;
        float _TrackLineDry;
        float _TrackPuddlesOn;
        float _TrackRipplesOn;
        float _TrackRippleTiles;

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float4 _MainTex_ST;
            half _BumpScale;
            float _MacroScale;
            half _MacroStrength;
            float _LineLength;
            float4 _UvMetres;
            half _Smoothness;
            half _Porosity;
            half _Puddles;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local_fragment _ _TRACK_DETAIL
            #pragma multi_compile_local_fragment _ _TRACK_RUBBER
            #pragma multi_compile_local_fragment _ _TRACK_WET
            #include_with_pragmas "Assets/Rendering/Shaders/LitKeywords.hlsl"
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // A tangent-space normal turned into world space by the frame the screen-space derivatives of position
            // and uv give, so the mesh needs no tangents (Schüler's cotangent frame).
            float3 Perturb(float3 n, float3 p, float2 uv, float3 tangentNormal)
            {
                float3 dp1 = ddx(p), dp2 = ddy(p);
                float2 duv1 = ddx(uv), duv2 = ddy(uv);
                float3 dp2perp = cross(dp2, n), dp1perp = cross(n, dp1);
                float3 t = dp2perp * duv1.x + dp1perp * duv2.x;
                float3 b = dp2perp * duv1.y + dp1perp * duv2.y;
                float scale = rsqrt(max(max(dot(t, t), dot(b, b)), 1e-20));
                return normalize(t * (scale * tangentNormal.x) + b * (scale * tangentNormal.y) + n * tangentNormal.z);
            }

            half Bell(float d, float width)
            {
                return exp(-d * d / (2 * width * width));
            }

            // One layer of raindrop rings (Lagarde's rain ripples): each drop's ring spreads out from its centre over
            // its cycle and fades as it goes. A slope across the surface, in world x and z. Sampled with the gradients
            // given, as it is read inside a branch.
            float2 Ripple(float2 at, float2 dx, float2 dy, float time, half weight)
            {
                half4 r = SAMPLE_TEXTURE2D_GRAD(_TrackRippleTex, sampler_TrackRippleTex, at, dx, dy);
                half2 away = r.gb * 2 - 1;
                float cycle = frac(r.a + time);
                float wave = cycle - 1 + r.r;
                half fade = saturate(0.2 + weight * 0.8 - cycle);
                return away * (fade * r.r * sin(clamp(wave * 9, 0, 3) * PI));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                half4 grain = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                half3 albedo = grain.rgb * _BaseColor.rgb;
                half smoothness = _Smoothness * lerp(0.7, 1.3, grain.a);
                float3 flat = normalize(i.normalWS);
                float3 normal = flat;

                #if _TRACK_DETAIL
                    // Two scales of patches, one turned against the other, so neither repeat lines up.
                    float2 w = i.positionWS.xz / _MacroScale;
                    half broad = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, w).r;
                    half fine = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, float2(w.y, -w.x) * 3.7 + 0.31).r;
                    half patch = (broad * 0.65 + fine * 0.35) * 2 - 1;
                    albedo *= 1 + patch * _MacroStrength;
                    smoothness *= 1 - patch * _MacroStrength * 0.5;
                    float3 t = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv).xyz * 2 - 1;
                    t.xy *= _BumpScale;
                    normal = Perturb(normal, i.positionWS, uv, normalize(t));
                #endif

                #if _TRACK_RUBBER || _TRACK_WET
                    float2 m = i.uv * _UvMetres.xy;
                    float lap = m.y / _LineLength;
                    float2 onLine = SAMPLE_TEXTURE2D_LOD(_Line, sampler_Line, float2(lap, 0.5), 0).rg;
                    float d = m.x - onLine.r;
                    // A broad band where the field's lines spread, darkest under the tyres of a car on the line.
                    half band = Bell(d, 1.1) * 0.5 + (Bell(d - 0.8, 0.24) + Bell(d + 0.8, 0.24)) * 0.5;
                #endif

                #if _TRACK_RUBBER
                    // Streaks along the line, long in the direction of travel.
                    half streak = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, float2(m.x * 0.45, m.y * 0.012)).g;
                    half rubber = saturate(band * onLine.g * (0.45 + streak * 1.1));
                    albedo *= 1 - rubber * 0.55;
                    smoothness = saturate(smoothness + rubber * 0.08);
                #endif

                #if _TRACK_WET
                {
                    // The water here: the film, thinner on the line the cars sweep (a road with a line map only), and
                    // more or less of it with the surface's dips and the stone's peaks.
                    half dips = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, i.positionWS.xz / 13.0).b;
                    half onRoad = saturate(onLine.g * 3);
                    float water = _TrackWater * (1 - saturate(band * 1.25) * onRoad * _TrackLineDry);
                    float held = water * (0.75 + 0.5 * dips) * lerp(1.15, 0.85, grain.a);
                    // Dark as soon as it is damp; shiny once a film covers the grain.
                    half damp = saturate(held / 0.06);
                    half film = smoothstep(0.04, 0.35, held);

                    // Puddles: in a dip along the road, or at the edge the camber drains to, where the surface dips too.
                    half puddle = 0;
                    UNITY_BRANCH
                    if (_Puddles * _TrackPuddlesOn > 0.5 && water > 0.12)
                    {
                        float3 pools = SAMPLE_TEXTURE2D_LOD(_Pools, sampler_Pools, float2(lap, 0.5), 0).rgb;
                        float right = max(pools.b - m.x, 0), left = max(m.x, 0);
                        half edge = exp(-right / 1.3) * saturate(0.5 + pools.g) + exp(-left / 1.3) * saturate(0.5 - pools.g);
                        half ready = saturate(max(pools.r, edge * 0.9) * (dips * 1.7 - 0.25));
                        // Ready ground stands water from a fifth of a millimetre; open road only near flooding.
                        float stands = lerp(2.2, 0.2, ready);
                        puddle = smoothstep(0, 0.3, water - stands);
                    }

                    albedo *= lerp(1, 1 - 0.55 * _Porosity, damp) * lerp(1, 0.8, puddle);
                    smoothness = lerp(smoothness, lerp(0.8, 0.88, _Porosity), film);
                    smoothness = lerp(smoothness, 0.96, puddle);
                    // Water fills the grain: a film keeps some of its bumps, a puddle lies flat.
                    normal = normalize(lerp(normal, flat, film * 0.55 + puddle * 0.45));

                    // Raindrops ring the water: plain in a puddle, faint on the film, gone in the distance.
                    float2 at = i.positionWS.xz * _TrackRippleTiles;
                    float2 atX = ddx(at), atY = ddy(at);
                    half rippling = _TrackRain * _TrackRipplesOn * (puddle + film * 0.3) * saturate(2 - distance(i.positionWS, _WorldSpaceCameraPos) / 25);
                    UNITY_BRANCH
                    if (rippling > 0.01)
                    {
                        float time = _Time.y * 1.3;
                        float2 slope = Ripple(at, atX, atY, time, _TrackRain)
                            + Ripple(at * 0.83 + float2(0.37, 0.61), atX * 0.83, atY * 0.83, time * 0.91 + 0.5, saturate(_TrackRain * 1.6 - 0.6));
                        normal = normalize(normal + float3(slope.x, 0, slope.y) * (rippling * 0.6));
                    }
                    smoothness = saturate(smoothness);
                }
                #endif

                return ShadePbr(i.positionWS, normal, i.positionCS, i.fog, albedo, 0, smoothness);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex TarmacShadowVertex
            #pragma fragment TarmacNullFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma vertex TarmacDepthVertex
            #pragma fragment TarmacNullFragment
            #pragma multi_compile_instancing
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"
            ENDHLSL
        }
    }
}
