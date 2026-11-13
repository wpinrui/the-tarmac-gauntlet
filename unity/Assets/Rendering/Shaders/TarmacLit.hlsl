// Lighting for the game's own lit shaders, as URP Lit does it: the sun with its shadows, the other lights and theirs, the
// ambient light with the screen-space ambient occlusion, reflections and fog, and in a shader that defines
// TARMAC_HEADLAMPS the cars' headlamps on the ground (Headlamps.hlsl). Also the shadow caster and depth passes for shaders that move no vertices.
#ifndef TARMAC_LIT_INCLUDED
#define TARMAC_LIT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#if defined(TARMAC_HEADLAMPS)
#include "Assets/Rendering/Shaders/Headlamps.hlsl"
#endif

// Colours and multipliers written into the shaders while the game rendered in Gamma colour space, where they were sRGB
// values and scaled sRGB values. In Linear a colour is turned into linear light, and a multiplier on a colour is raised
// to the power 2.2, so each darkens or lightens what it touches as much as it did.
half3 GammaColour(half3 c)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return c;
#else
    return SRGBToLinear(c);
#endif
}

half GammaScale(half f)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return f;
#else
    return pow(max(f, 0.0), 2.2);
#endif
}

half3 GammaScale(half3 f)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return f;
#else
    return pow(max(f, 0.0), 2.2);
#endif
}

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
#if defined(TARMAC_HEADLAMPS)
    c.rgb += albedo * (1 - metallic) * HeadlampLight(positionWS, d.normalWS);
#endif
    c.rgb = MixFog(c.rgb, d.fogCoord);
    return c;
}

// The highest specular a light's Fresnel correction may reach, so a glancing ripple can't flare into a firefly.
#define TARMAC_FRESNEL_MOST 64.0

// URP's direct specular folds the Fresnel term into a rough fit that hardly grows at a glancing angle, so a road seen
// towards a low sun or a floodlight misses most of its sheen. This is the difference between that fit and the full
// GGX highlight with Smith's height-correlated visibility and Schlick's Fresnel, for one light. On a dielectric.
half3 FresnelCorrection(BRDFData brdf, half3 n, half3 v, Light light)
{
    half NoL = saturate(dot(n, light.direction));
    half3 lit = light.color * (light.distanceAttenuation * light.shadowAttenuation * NoL);
    if (NoL <= 0) return 0;
    float3 h = SafeNormalize(float3(light.direction) + float3(v));
    float NoH = saturate(dot(float3(n), h));
    float LoH = saturate(dot(float3(light.direction), h));
    float NoV = max(dot(n, v), 1e-4);
    float a2 = brdf.roughness2;
    float d = NoH * NoH * (a2 - 1) + 1;
    // URP's lights carry the pi a Lambert surface divides by, so D drops its own.
    float D = a2 / (d * d);
    float visibility = 0.5 / (NoL * sqrt(NoV * NoV * (1 - a2) + a2) + NoV * sqrt(NoL * NoL * (1 - a2) + a2));
    float f0 = brdf.specular.x;
    float fresnel = f0 + (1 - f0) * Pow4(1 - LoH) * (1 - LoH);
    float full = min(D * visibility * fresnel, TARMAC_FRESNEL_MOST);
    float urp = DirectBRDFSpecular(brdf, n, light.direction, v) * f0;
    return lit * (half)(full - urp);
}

// A dielectric surface (a road, a kerb) lit as ShadePbr lights it, with the Fresnel sheen URP's fit leaves out added
// for the sun and every other light: the surface shines as it turns from the eye.
half4 ShadeDielectric(float3 positionWS, half3 normalWS, float4 positionCS, half fogFactor, half3 albedo, half smoothness)
{
    InputData d = TarmacInputData(positionWS, normalWS, positionCS, fogFactor);
    SurfaceData s = (SurfaceData)0;
    s.albedo = albedo;
    s.metallic = 0;
    s.smoothness = smoothness;
    s.normalTS = half3(0, 0, 1);
    s.occlusion = 1;
    s.alpha = 1;
    half4 c = UniversalFragmentPBR(d, s);

    BRDFData brdf;
    half alpha = 1;
    InitializeBRDFData(albedo, 0, half3(0, 0, 0), smoothness, alpha, brdf);
    half3 sheen = FresnelCorrection(brdf, d.normalWS, d.viewDirectionWS, GetMainLight(d.shadowCoord, d.positionWS, d.shadowMask));
#if defined(_ADDITIONAL_LIGHTS)
    uint count = GetAdditionalLightsCount();
    for (uint li = 0u; li < count; li++)
        sheen += FresnelCorrection(brdf, d.normalWS, d.viewDirectionWS, GetAdditionalLight(li, d.positionWS, d.shadowMask));
#endif
    c.rgb = max(c.rgb + sheen, 0);
#if defined(TARMAC_HEADLAMPS)
    c.rgb += albedo * HeadlampLight(positionWS, d.normalWS);
#endif
    c.rgb = MixFog(c.rgb, d.fogCoord);
    return c;
}

// A matte surface lit by the sun and its shadows and the ambient light, both under the ambient occlusion, fogged: the
// built-in Lambert model with no extra lights.
half3 ShadeLambert(float3 positionWS, half3 normalWS, float4 positionCS, half fogFactor, half3 albedo)
{
    InputData d = TarmacInputData(positionWS, normalWS, positionCS, fogFactor);
    AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(d.normalizedScreenSpaceUV, 1);
    Light sun = GetMainLight(d.shadowCoord, d.positionWS, d.shadowMask);
    half3 lit = sun.color * (sun.distanceAttenuation * sun.shadowAttenuation * ao.directAmbientOcclusion);
    half3 light = LightingLambert(lit, sun.direction, d.normalWS) + d.bakedGI * ao.indirectAmbientOcclusion;
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
