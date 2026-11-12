// What a surface gives the screen-space reflections (ScreenReflectionsFeature). A shader that wants them adds a pass
// tagged "LightMode" = "TarmacReflect", drawn over the surface the forward pass already drew (ZTest LEqual, ZWrite Off,
// Offset -1 -1), that works out the same normal and smoothness and returns TarmacReflect. Target 0 holds the normal
// (octahedral, 0 to 1), the perceptual roughness, and how much of the environment the forward pass reflected there: its
// environment BRDF, fogged. Target 1 holds the probe reflection the forward pass used. The composite swaps that probe
// reflection for what the ray hit, as far as it trusts the hit, so off screen the probes stay.
// Include with the forward pass's keywords (LitKeywords.hlsl), so the probe and the fog match it.
#ifndef TARMAC_REFLECT_INCLUDED
#define TARMAC_REFLECT_INCLUDED

#include "Assets/Rendering/Shaders/TarmacLit.hlsl"

// Rougher than this a reflection is a blur, which the probes give well enough. It fades out from the first number.
#define TARMAC_REFLECT_FADE_FROM 0.35
#define TARMAC_REFLECT_MAX_ROUGHNESS 0.5

struct TarmacReflectOutput
{
    half4 surface : SV_Target0;
    half4 probe : SV_Target1;
};

// The share of the environment a dielectric layer of this smoothness reflects, as URP's EnvironmentBRDFSpecular gives
// it: the same for a clear coat as for a bare surface.
half TarmacEnvironmentWeight(half smoothness, half NoV)
{
    BRDFData brdf;
    half alpha = 1;
    InitializeBRDFData(half3(0, 0, 0), 0, half3(0, 0, 0), smoothness, alpha, brdf);
    return EnvironmentBRDFSpecular(brdf, Pow4(1 - NoV)).x;
}

// A surface that takes no screen-space reflection.
TarmacReflectOutput TarmacReflectNone()
{
    TarmacReflectOutput o;
    o.surface = half4(0.5, 0.5, 1, 0);
    o.probe = half4(0, 0, 0, 0);
    return o;
}

// A reflecting surface: its normal, the perceptual roughness of the layer that reflects, and the weight the forward
// pass gave that layer's environment reflection (TarmacEnvironmentWeight, times any mask).
TarmacReflectOutput TarmacReflect(float3 positionWS, half3 normalWS, float4 positionCS, half fogFactor, half perceptualRoughness, half weight)
{
    half3 n = NormalizeNormalPerPixel(normalWS);
    half3 v = GetWorldSpaceNormalizeViewDir(positionWS);
    half3 r = reflect(-v, n);
    float2 screenUV = GetNormalizedScreenSpaceUV(positionCS);
    half3 probe = GlossyEnvironmentReflection(r, positionWS, perceptualRoughness, 1, screenUV);
    // The fog hides the reflection as much as the rest of the surface.
    half fogCoord = InitializeInputDataFog(float4(positionWS, 1), fogFactor);
    half seen = MixFog(half3(1, 1, 1), fogCoord).x - MixFog(half3(0, 0, 0), fogCoord).x;
    half sharp = 1 - smoothstep(TARMAC_REFLECT_FADE_FROM, TARMAC_REFLECT_MAX_ROUGHNESS, perceptualRoughness);

    TarmacReflectOutput o;
    o.surface = half4(PackNormalOctQuadEncode(n) * 0.5 + 0.5, perceptualRoughness, weight * seen * sharp);
    o.probe = half4(probe, 1);
    return o;
}

#endif
