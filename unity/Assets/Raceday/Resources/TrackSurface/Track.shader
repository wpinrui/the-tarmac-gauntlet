// The circuit's paved surfaces: the road, the pit lane and the kerbs. A grain texture with a normal map, both made in
// code (TrackTextures.cs), with broad patches laid over it in world space so the 4 m tile never reads as a repeat. On
// the road, a rubbered racing line: a map along the lap gives where the line runs across the road and how much rubber
// lies on it, and the band is darkest under the two tyre tracks. The mesh has no tangents, so the normal map is turned
// into the surface from the screen-space change of position and uv. The keywords _TRACK_DETAIL (normals and patches)
// and _TRACK_RUBBER (the line) switch the parts on, so a part switched off costs nothing.
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

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float4 _MainTex_ST;
            half _BumpScale;
            float _MacroScale;
            half _MacroStrength;
            float _LineLength;
            float4 _UvMetres;
            half _Smoothness;
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

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                half4 grain = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                half3 albedo = grain.rgb * _BaseColor.rgb;
                half smoothness = _Smoothness * lerp(0.7, 1.3, grain.a);
                float3 normal = normalize(i.normalWS);

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

                #if _TRACK_RUBBER
                    float2 m = i.uv * _UvMetres.xy;
                    float2 onLine = SAMPLE_TEXTURE2D_LOD(_Line, sampler_Line, float2(m.y / _LineLength, 0.5), 0).rg;
                    float d = m.x - onLine.r;
                    // A broad band where the field's lines spread, darkest under the tyres of a car on the line.
                    half band = Bell(d, 1.1) * 0.5 + (Bell(d - 0.8, 0.24) + Bell(d + 0.8, 0.24)) * 0.5;
                    // Streaks along the line, long in the direction of travel.
                    half streak = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, float2(m.x * 0.45, m.y * 0.012)).g;
                    half rubber = saturate(band * onLine.g * (0.45 + streak * 1.1));
                    albedo *= 1 - rubber * 0.55;
                    smoothness = saturate(smoothness + rubber * 0.08);
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
