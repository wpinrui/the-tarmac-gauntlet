// Lighting for the game's own lit shaders, as URP Lit does it: the sun with its shadows, the other lights, the ambient
// light, reflections and fog. Also the shadow caster and depth passes for shaders that move no vertices.
#ifndef TARMAC_LIT_INCLUDED
#define TARMAC_LIT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Set by URP while it draws a shadow map: the direction to a directional light, the position of any other.
float3 _LightDirection;
float3 _LightPosition;

InputData TarmacInputData(float3 positionWS, half3 normalWS, float4 positionCS, half fogFactor)
{
    InputData d = (InputData)0;
    d.positionWS = positionWS;
    d.positionCS = positionCS;
    d.normalWS = NormalizeNormalPerPixel(normalWS);
    d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
    d.shadowCoord = TransformWorldToShadowCoord(positionWS);
    d.fogCoord = InitializeInputDataFog(float4(positionWS, 1.0), fogFactor);
    d.bakedGI = SampleSHPixel(half3(0, 0, 0), d.normalWS);
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    d.shadowMask = half4(1, 1, 1, 1);
    return d;
}

// A metallic surface lit as Standard and URP Lit light it, fogged.
half4 ShadePbr(float3 positionWS, half3 normalWS, float4 positionCS, half fogFactor, half3 albedo, half metallic, half smoothness)
{
    InputData d = TarmacInputData(positionWS, normalWS, positionCS, fogFactor);
    SurfaceData s = (SurfaceData)0;
    s.albedo = albedo;
    s.metallic = metallic;
    s.smoothness = smoothness;
    s.normalTS = half3(0, 0, 1);
    s.occlusion = 1;
    s.alpha = 1;
    half4 c = UniversalFragmentPBR(d, s);
    c.rgb = MixFog(c.rgb, d.fogCoord);
    return c;
}

// A matte surface lit by the sun and its shadows and the ambient light, fogged: the built-in Lambert model with no
// extra lights.
half3 ShadeLambert(float3 positionWS, half3 normalWS, float4 positionCS, half fogFactor, half3 albedo)
{
    InputData d = TarmacInputData(positionWS, normalWS, positionCS, fogFactor);
    Light sun = GetMainLight(d.shadowCoord, d.positionWS, d.shadowMask);
    half3 light = LightingLambert(sun.color * (sun.distanceAttenuation * sun.shadowAttenuation), sun.direction, d.normalWS) + d.bakedGI;
    return MixFog(albedo * light, d.fogCoord);
}

// A world position into the shadow map being drawn, pushed by the light's depth and normal bias.
float4 ShadowClipPosition(float3 positionWS, float3 normalWS)
{
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 toLight = normalize(_LightPosition - positionWS);
#else
    float3 toLight = _LightDirection;
#endif
    return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, toLight)));
}

struct TarmacPlainAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct TarmacPlainVaryings
{
    float4 positionCS : SV_POSITION;
};

TarmacPlainVaryings TarmacShadowVertex(TarmacPlainAttributes v)
{
    UNITY_SETUP_INSTANCE_ID(v);
    TarmacPlainVaryings o;
    o.positionCS = ShadowClipPosition(TransformObjectToWorld(v.positionOS.xyz), TransformObjectToWorldNormal(v.normalOS));
    return o;
}

TarmacPlainVaryings TarmacDepthVertex(TarmacPlainAttributes v)
{
    UNITY_SETUP_INSTANCE_ID(v);
    TarmacPlainVaryings o;
    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
    return o;
}

half4 TarmacNullFragment(TarmacPlainVaryings i) : SV_Target
{
    return 0;
}

#endif
