// A car's paint: a base coat under a clear coat, with metallic flakes in the base that glint in the sun. The clear coat
// and the flakes are graphics options (CarLook), switched with the global keywords _TARMAC_CLEARCOAT and _TARMAC_FLAKE;
// with both off the paint is lit as ShadePbr lights it. Include in place of TarmacLit.hlsl, after the keywords' pragmas.
// A shader that always wants the clear coat defines _CLEARCOAT before including this.
#ifndef TARMAC_CAR_PAINT_INCLUDED
#define TARMAC_CAR_PAINT_INCLUDED

// URP's own lighting draws the clear coat when _CLEARCOAT is defined before Lighting.hlsl.
#if defined(_TARMAC_CLEARCOAT) && !defined(_CLEARCOAT)
#define _CLEARCOAT 1
#endif

#include "Assets/Rendering/Shaders/TarmacLit.hlsl"

// Under a clear coat the base coat is less glossy: the shine is the coat's.
#define CAR_COAT_BASE_SMOOTHNESS 0.55
#define CAR_COAT_SMOOTHNESS 0.94
// Flaked paint is part metal.
#define CAR_FLAKE_METALLIC 0.45
// The smallest flake cell, metres. Further away the cells grow, so a glint stays about a pixel and never shimmers.
#define CAR_FLAKE_CELL 0.003
// How far a flake can lean from the panel, and how tight its glint is.
#define CAR_FLAKE_TILT 0.35
#define CAR_FLAKE_SHARPNESS 300.0
#define CAR_FLAKE_GAIN 1.6
// How much each flake lightens or darkens the base colour, seen close up.
#define CAR_FLAKE_SPECKLE 0.06

float3 TarmacHash33(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}

// One flake per cell of the given size, leaning at random: how much of the half vector it catches.
half FlakeGlint(float3 p, float cell, half3 n, half3 h)
{
    float3 lean = TarmacHash33(floor(p / cell)) * 2 - 1;
    half3 flake = normalize(n + lean * CAR_FLAKE_TILT);
    return pow(saturate(dot(flake, h)), CAR_FLAKE_SHARPNESS);
}

// The paint lit and fogged. flakePos is a point on the car that moves with it, in metres: the flakes sit there.
half4 ShadeCarPaint(float3 positionWS, half3 normalWS, float4 positionCS, half fogFactor, half3 albedo, half metallic,
    half smoothness, float3 flakePos)
{
    InputData d = TarmacInputData(positionWS, normalWS, positionCS, fogFactor);
    SurfaceData s = (SurfaceData)0;
    s.albedo = albedo;
    s.metallic = metallic;
    s.smoothness = smoothness;
    s.normalTS = half3(0, 0, 1);
    s.occlusion = 1;
    s.alpha = 1;

#if defined(_TARMAC_FLAKE)
    // Cells about a pixel and a half across, blended between powers of two as the distance changes.
    float footprint = max(length(ddx(flakePos)), length(ddy(flakePos)));
    float level = max(0, log2(footprint * 1.5 / CAR_FLAKE_CELL));
    float whole = floor(level);
    float cell = CAR_FLAKE_CELL * exp2(whole);
    s.metallic = max(s.metallic, CAR_FLAKE_METALLIC);
    half speckle = (TarmacHash33(floor(flakePos / CAR_FLAKE_CELL)).x * 2 - 1) * CAR_FLAKE_SPECKLE * saturate(1 - level);
    s.albedo *= 1 + speckle;
#endif

#if defined(_CLEARCOAT)
    s.smoothness = min(s.smoothness, CAR_COAT_BASE_SMOOTHNESS);
    s.clearCoatMask = 1;
    s.clearCoatSmoothness = CAR_COAT_SMOOTHNESS;
#endif

    half4 c = UniversalFragmentPBR(d, s);

#if defined(_TARMAC_FLAKE)
    Light sun = GetMainLight(d.shadowCoord, d.positionWS, d.shadowMask);
    half3 h = SafeNormalize(sun.direction + d.viewDirectionWS);
    half glint = lerp(FlakeGlint(flakePos, cell, d.normalWS, h), FlakeGlint(flakePos, cell * 2, d.normalWS, h), level - whole);
    half lit = sun.distanceAttenuation * sun.shadowAttenuation * saturate(dot(d.normalWS, sun.direction));
    c.rgb += sun.color * (lit * glint * CAR_FLAKE_GAIN) * lerp(half3(1, 1, 1), albedo, 0.4);
#endif

    c.rgb = MixFog(c.rgb, d.fogCoord);
    return c;
}

#endif
