// A car's paint: a base coat under a clear coat, with metallic flakes in the base that glint in the sun. The clear coat
// and the flakes are graphics options (CarMaterials), switched with the global keywords _TARMAC_CLEARCOAT and
// _TARMAC_FLAKE. Flakes show only close up, where each spans a few pixels; further off the paint is an even metallic
// sheen. Highlights are filtered so curved panels never sparkle. Include in place of TarmacLit.hlsl, after the keywords'
// pragmas.
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
// A flake cell, metres.
#define CAR_FLAKE_CELL 0.004
// Flakes show only where a cell spans at least the first number of pixels, at full strength from the second. Smaller
// than that they would alias into pixel noise, so the paint reads as an even metallic sheen instead.
#define CAR_FLAKE_SHOW_FROM 2.0
#define CAR_FLAKE_FULL_AT 4.0
// How far a flake can lean from the panel, how tight its glint is, and how bright.
#define CAR_FLAKE_TILT 0.3
#define CAR_FLAKE_SHARPNESS 120.0
#define CAR_FLAKE_GAIN 0.25
// The most roughness a curved surface gains so its highlight can't break into sparkles between pixels.
#define CAR_SPECULAR_AA_MAX 0.2

float3 TarmacHash33(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}

// One flake per cell, leaning at random: how much of the half vector it catches.
half FlakeGlint(float3 p, half3 n, half3 h)
{
    float3 lean = TarmacHash33(floor(p / CAR_FLAKE_CELL)) * 2 - 1;
    half3 flake = normalize(n + lean * CAR_FLAKE_TILT);
    return pow(saturate(dot(flake, h)), CAR_FLAKE_SHARPNESS);
}

// A smoothness lowered where the normal turns fast across a pixel (Kaplanyan and Hoffman's specular antialiasing), so
// a sharp highlight on a curved panel spreads over the pixel instead of flickering on and off.
half FilteredSmoothness(half smoothness, half3 normalWS)
{
    float3 dx = ddx(normalWS), dy = ddy(normalWS);
    float spread = min(0.5 * (dot(dx, dx) + dot(dy, dy)), CAR_SPECULAR_AA_MAX);
    float roughness = (1 - smoothness) * (1 - smoothness);
    return 1 - sqrt(sqrt(saturate(roughness * roughness + spread)));
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
    s.metallic = max(s.metallic, CAR_FLAKE_METALLIC);
    // How many pixels a flake cell spans.
    float footprint = max(length(ddx(flakePos)), length(ddy(flakePos)));
    float pixels = CAR_FLAKE_CELL / max(footprint, 1e-6);
    half flakes = smoothstep(CAR_FLAKE_SHOW_FROM, CAR_FLAKE_FULL_AT, pixels);
#endif

#if defined(_CLEARCOAT)
    s.smoothness = min(s.smoothness, CAR_COAT_BASE_SMOOTHNESS);
    s.clearCoatMask = 1;
    s.clearCoatSmoothness = FilteredSmoothness(CAR_COAT_SMOOTHNESS, d.normalWS);
#endif
    s.smoothness = FilteredSmoothness(s.smoothness, d.normalWS);

    half4 c = UniversalFragmentPBR(d, s);

#if defined(_TARMAC_FLAKE)
    if (flakes > 0)
    {
        Light sun = GetMainLight(d.shadowCoord, d.positionWS, d.shadowMask);
        half3 h = SafeNormalize(sun.direction + d.viewDirectionWS);
        half lit = sun.distanceAttenuation * sun.shadowAttenuation * saturate(dot(d.normalWS, sun.direction));
        c.rgb += sun.color * (lit * flakes * FlakeGlint(flakePos, d.normalWS, h) * CAR_FLAKE_GAIN) * lerp(half3(1, 1, 1), albedo, 0.5);
    }
#endif

    c.rgb = MixFog(c.rgb, d.fogCoord);
    return c;
}

#endif
