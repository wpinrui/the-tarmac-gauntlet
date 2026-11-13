// The circuit's paved surfaces: the road, the pit lane and the kerbs. A fine grain texture with a normal map, both
// made in code (TrackTextures.cs), with broad patches laid over it in world space so the 4 m tile never reads as a
// repeat, and repairs laid along the road: sealed cracks and rectangles of relaid tarmac, each repeat of their tile
// shifted and mirrored against the last. On the road, a rubbered racing line: a map along the lap gives where the line
// runs across the road and how much rubber lies on it, the band is darkest under the two tyre tracks, and rubber marks
// from locked and spinning tyres lie about it, most where the line holds most rubber. The mesh has no tangents, so the
// normal map is turned into the surface from the screen-space change of position and uv. Its smoothness varies wet or
// dry (TrackSurface.hlsl), and the full GGX Fresnel lights it, so it shines as it turns from the eye. In the wet
// (_TRACK_WET) the surface darkens as its pores fill, water fills the grain and shines with the scene's reflection
// probes and the screen-space reflections, the cars clear a drier line, and puddles stand in the road's dips and at the
// edge its camber drains to, rippled by the rain.
// The water's depth and the rain come from WeatherView as globals. The keywords _TRACK_DETAIL (normals, patches and
// repairs), _TRACK_RUBBER (the line and its marks) and _TRACK_WET switch the parts on, so a part switched off costs
// nothing; puddles and ripples follow globals too.
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
        [NoScaleOffset] _Repairs ("Sealed cracks, patches", 2D) = "black" {}
        _RepairAmount ("How much repair shows", Range(0, 1)) = 0
        [NoScaleOffset] _Marks ("Rubber marks about the line", 2D) = "black" {}
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
        TEXTURE2D(_Repairs);
        SAMPLER(sampler_Repairs);
        TEXTURE2D(_Marks);
        SAMPLER(sampler_Marks);
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
            half _RepairAmount;
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
            #include "Assets/Raceday/Resources/TrackSurface/TrackSurface.hlsl"

            half4 frag(Varyings i) : SV_Target
            {
                half3 albedo;
                float3 normal;
                half smoothness;
                TrackSurface(i, albedo, normal, smoothness);
                return ShadeDielectric(i.positionWS, normal, i.positionCS, i.fog, albedo, smoothness);
            }
            ENDHLSL
        }

        // The screen-space reflections' view of the surface (TarmacReflect.hlsl): the wet film and the puddles, and any
        // dry stretch smooth enough to take them.
        Pass
        {
            Name "TarmacReflect"
            Tags { "LightMode" = "TarmacReflect" }
            ZWrite Off ZTest LEqual Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment reflectFrag
            #pragma multi_compile_local_fragment _ _TRACK_DETAIL
            #pragma multi_compile_local_fragment _ _TRACK_RUBBER
            #pragma multi_compile_local_fragment _ _TRACK_WET
            #include_with_pragmas "Assets/Rendering/Shaders/LitKeywords.hlsl"
            #include "Assets/Rendering/Shaders/TarmacReflect.hlsl"
            #include "Assets/Raceday/Resources/TrackSurface/TrackSurface.hlsl"

            TarmacReflectOutput reflectFrag(Varyings i)
            {
                #if !_TRACK_WET
                    // Dry tarmac is too rough to take them, unless the even sheen of a wet track drawn plainly is on it.
                    UNITY_BRANCH
                    if (_Smoothness < 0.4) return TarmacReflectNone();
                #endif
                half3 albedo;
                float3 normal;
                half smoothness;
                TrackSurface(i, albedo, normal, smoothness);
                half NoV = saturate(dot(normal, GetWorldSpaceNormalizeViewDir(i.positionWS)));
                return TarmacReflect(i.positionWS, normal, i.positionCS, i.fog, PerceptualSmoothnessToPerceptualRoughness(smoothness),
                    TarmacEnvironmentWeight(smoothness, NoV));
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
