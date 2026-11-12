// The trees nearest the race camera as real meshes (NearTrees.cs, from tools/landscape/trees3d.js): bark, and leaves
// on cards that show clusters of the tree's own leaves. One material draws the bark, two the leaves (_LEAVES): depth
// alone first, then lit with depth writes off, as the impostors do, so only the front card on each pixel is lit.
//
// Every tree of a kind and level of detail is one instanced draw. Each instance is three float4s in _Trees from _Start:
// the foot of the trunk and the scale; the turn about the vertical, the height squash, and the share of the time it
// shows (Foliage.hlsl's FoliageFade: a level of detail or the impostor takes the rest); and the leaf tint.
//
// Per vertex: position and normal in the tree's own frame; uv; uv1 the bark layer, a shade (the crown's own shadow,
// as the impostor bake gives it) and how far it sways.
Shader "Hidden/TarmacGauntlet/NearTree"
{
    Properties
    {
        _Bark ("Bark", 2DArray) = "" {}
        _Leaves ("Leaf clusters, a layer a kind", 2DArray) = "" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _ZWrite ("Lit pass writes depth", Float) = 1
        _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "DisableBatching" = "True" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull [_Cull]

        HLSLINCLUDE
        #pragma target 4.5
        #pragma require 2darray
        #pragma multi_compile_local _ _LEAVES
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Foliage.hlsl"

        TEXTURE2D_ARRAY(_Bark);
        SAMPLER(sampler_Bark);
        TEXTURE2D_ARRAY(_Leaves);
        SAMPLER(sampler_Leaves);
        StructuredBuffer<float4> _Trees;

        CBUFFER_START(UnityPerMaterial)
            float _Cutoff;
            float _ZWrite;
            float _Cull;
        CBUFFER_END
        // Per draw, from a property block: the first instance's place in _Trees, and the leaves' layer (the kind).
        uint _Start;
        float _Kind;
        // Per draw: what this level's leaves and bark are multiplied by, so that lit they match the impostor (trees3d.bytes).
        float3 _Colour;
        float3 _BarkColour;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float3 extra : TEXCOORD1;
            uint instance : SV_InstanceID;
        };

        struct Tree
        {
            float3 positionWS;
            float3 normalWS;
            float3 uv;      // u, v, layer
            float shade;
            float share;
            half3 tint;
        };

        // Unity turns a point by t about +Y as x' = x cos t + z sin t, z' = -x sin t + z cos t.
        float3 Turn(float3 v, float t)
        {
            float c = cos(t), s = sin(t);
            return float3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
        }

        Tree Place(Attributes v)
        {
            uint i = (_Start + v.instance) * 3;
            float4 a = _Trees[i], b = _Trees[i + 1], c = _Trees[i + 2];
            float scale = a.w, yaw = b.x, squash = b.y;
            float3 local = v.positionOS.xyz * float3(scale, scale * squash, scale);
            Tree t;
            t.positionWS = a.xyz + Turn(local, yaw);
            float sway = v.extra.z;
            #if defined(_LEAVES)
                // Leaves flutter a little on their own as well.
                t.positionWS += sin(_FoliageWind.w * 5.3 + dot(t.positionWS, float3(1.7, 2.3, 1.1))) * 0.03 * _FoliageWind.z * sway;
            #endif
            // The taller the tree, the further its top travels.
            t.positionWS.xz += FoliageSway(a.xyz, sway) * (0.15 + 0.012 * local.y);
            t.normalWS = Turn(v.normalOS / float3(1, max(squash, 0.2), 1), yaw);
            #if defined(_LEAVES)
                t.uv = float3(v.uv, _Kind);
            #else
                t.uv = float3(v.uv, v.extra.x);
            #endif
            t.shade = v.extra.y;
            t.share = b.z;
            t.tint = c.rgb;
            return t;
        }

        half SampleAlpha(float3 uv)
        {
            #if defined(_LEAVES)
                return SAMPLE_TEXTURE2D_ARRAY(_Leaves, sampler_Leaves, uv.xy, uv.z).a;
            #else
                return 1;
            #endif
        }

        struct DepthVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 uv : TEXCOORD0;
            float share : TEXCOORD1;
        };

        DepthVaryings DepthVert(Attributes v)
        {
            Tree t = Place(v);
            DepthVaryings o;
            o.positionCS = TransformWorldToHClip(t.positionWS);
            o.uv = t.uv;
            o.share = t.share;
            return o;
        }

        half4 DepthFrag(DepthVaryings i) : SV_Target
        {
            FoliageFade(i.share, i.positionCS.xy);
            #if defined(_LEAVES)
                clip(SampleAlpha(i.uv) - _Cutoff);
            #endif
            return 0;
        }
        ENDHLSL

        Pass
        {
            Name "TreeDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            ENDHLSL
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            // The leaves test against the depth TreeDepth wrote, so only the front card's fragments are lit.
            ZWrite [_ZWrite] ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Assets/Rendering/Shaders/LitKeywords.hlsl"
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tint : TEXCOORD3;     // rgb the tint, a the shade
                float share : TEXCOORD4;
                half fog : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Tree t = Place(v);
                Varyings o;
                o.positionCS = TransformWorldToHClip(t.positionWS);
                o.positionWS = t.positionWS;
                o.normalWS = normalize(t.normalWS);
                o.uv = t.uv;
                o.tint = half4(t.tint, t.shade);
                o.share = t.share;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half3 Calibrated()
            {
                #if defined(_LEAVES)
                    return _Colour;
                #else
                    return _BarkColour;
                #endif
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                FoliageFade(i.share, i.positionCS.xy);
                #if defined(_LEAVES)
                    half4 c = SAMPLE_TEXTURE2D_ARRAY(_Leaves, sampler_Leaves, i.uv.xy, i.uv.z);
                    clip(c.a - _Cutoff);
                    // The cards' normals follow the crown's round shape, the same from either side, turned a little
                    // toward the eye so the crown's far side is not lit as if it faced away.
                    half3 n = normalize(0.7 * normalize(i.normalWS) + 0.3 * normalize(_WorldSpaceCameraPos - i.positionWS));
                #else
                    half4 c = SAMPLE_TEXTURE2D_ARRAY(_Bark, sampler_Bark, i.uv.xy, i.uv.z);
                    half3 n = normalize(IS_FRONT_VFACE(face, i.normalWS, -i.normalWS));
                #endif
                half3 col = ShadeLambert(i.positionWS, n, i.positionCS, i.fog, c.rgb * GammaScale(i.tint.rgb) * i.tint.a * Calibrated());
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"

            DepthVaryings vert(Attributes v)
            {
                Tree t = Place(v);
                DepthVaryings o;
                o.positionCS = ShadowClipPosition(t.positionWS, normalize(t.normalWS));
                o.uv = t.uv;
                o.share = t.share;
                return o;
            }

            half4 frag(DepthVaryings i) : SV_Target
            {
                FoliageFade(i.share, i.positionCS.xy);
                #if defined(_LEAVES)
                    // A smaller mip for the outline, as the impostors' shadows take: a solid crown shadow, not speckle.
                    clip(SAMPLE_TEXTURE2D_ARRAY_LOD(_Leaves, sampler_Leaves, i.uv.xy, i.uv.z, 2).a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            ENDHLSL
        }
    }
}
