// The game's one way of shading the lights that are not the sun or the moon: URP's own PBR lighting with the night's
// exposure budget on top. At the race the only such lights are the floodlights and the cars' headlamps, all real URP spot
// lights (FloodlightRig, HeadlampRig), and every lit shader in the game reaches them through here: TarmacLit's ShadePbr,
// ShadeDielectric and ShadeLambert, the car paint, the game's copy of URP Lit (Rendering/Resources/TarmacLit) and the smoke.
//
// The budget (_TgLampBudget, set by NightLighting from NightBudget.cs) bounds the lamps by construction:
// - Each lamp puts at most y of irradiance on any surface, however near it is: past half of y its light eases towards y.
//   So a car right behind another lights its tail no brighter than its own beam lights the road.
// - All the lamps together put at most z on any surface, however many overlap, eased the same way.
// - A lamp-lit pixel gives back at most w of linear light, specular included, eased the same way. w sits just over the
//   bloom's threshold, so a white car under every lamp at once glows a little and never floods the screen.
// With x 0 (the Team HQ, and the race by day) every light is URP's own, untouched.
// tools/night-budget/Check.cs sweeps the C# copy of this (Raceday/Scripts/Night/LampLight.cs) over every lamp, distance, angle and surface.
// Keep the two in step.
#ifndef TARMAC_LAMPS_INCLUDED
#define TARMAC_LAMPS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

float4 _TgLampBudget;

#define TG_LAMP_LUMA half3(0.2126, 0.7152, 0.0722)

// The highest specular a light's Fresnel correction may reach, so a glancing ripple can't flare into a firefly.
#define TARMAC_FRESNEL_MOST 64.0

// Rises one for one up to half of most, then eases towards most and never reaches it (LampLight.Knee).
float LampKnee(float x, float most)
{
    float k = most * 0.5;
    return x <= k ? x : k + (most - k) * (1.0 - exp(-(x - k) / (most - k)));
}

// The share of a lamp's light that reaches a surface facing this way under the per-lamp cap, and in e the irradiance
// it then gives the surface.
half LampShare(Light light, half3 normalWS, out float e)
{
    float raw = dot(light.color, TG_LAMP_LUMA) * light.distanceAttenuation * light.shadowAttenuation
        * saturate(dot(normalWS, light.direction));
    e = LampKnee(raw, _TgLampBudget.y);
    return raw > 1e-6 ? (half)(e / raw) : 1;
}

// What the lamps give back, summed, with the budget's cap on all of them together and on the pixel.
half3 LampTotal(half3 lamps, float irradiance)
{
    float s = irradiance > 1e-6 ? LampKnee(irradiance, _TgLampBudget.z) / irradiance : 1;
    half3 c = lamps * (half)s;
    float l = dot(c, TG_LAMP_LUMA);
    return l > 1e-6 ? c * (half)(LampKnee(l, _TgLampBudget.w) / l) : c;
}

// URP's direct specular folds the Fresnel term into a rough fit that hardly grows at a glancing angle, so a road seen
// towards a low sun misses most of its sheen. This is the difference between that fit and the full
// GGX highlight with Smith's height-correlated visibility and Schlick's Fresnel, for one light. On a dielectric.
half3 FresnelCorrection(BRDFData brdf, half3 n, half3 v, Light light)
{
    half NoL = saturate(dot(n, light.direction));
    if (NoL <= 0) return 0;
    half3 lit = light.color * (light.distanceAttenuation * light.shadowAttenuation * NoL);
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

// URP's UniversalFragmentPBR (URP 17.6), line for line, but for the other lights: with the budget on they are the
// lamps, each under its cap and all of them under the cap together. fresnel adds FresnelCorrection for the sun or the
// moon only: on the lamps, whose light grazes the road from every side at once, the full sheen turned a floodlit road
// white from end to end (tools/night-budget's renders), where URP's own fit gives it a soft shine.
half4 TarmacFragmentPBR(InputData inputData, SurfaceData surfaceData, bool fresnel)
{
    #if defined(_SPECULARHIGHLIGHTS_OFF)
    bool specularHighlightsOff = true;
    #else
    bool specularHighlightsOff = false;
    #endif
    BRDFData brdfData;
    InitializeBRDFData(surfaceData, brdfData);

    #if defined(DEBUG_DISPLAY)
    half4 debugColor;
    if (CanDebugOverrideOutputColor(inputData, surfaceData, brdfData, debugColor))
        return debugColor;
    #endif

    BRDFData brdfDataClearCoat = CreateClearCoatBRDFData(surfaceData, brdfData);
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
    uint meshRenderingLayers = GetMeshRenderingLayer();
    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);
    LightingData lightingData = CreateLightingData(inputData, surfaceData);
    lightingData.giColor = GlobalIllumination(brdfData, brdfDataClearCoat, surfaceData.clearCoatMask,
                                              inputData.bakedGI, aoFactor.indirectAmbientOcclusion, inputData.positionWS,
                                              inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        lightingData.mainLightColor = LightingPhysicallyBased(brdfData, brdfDataClearCoat, mainLight,
                                                              inputData.normalWS, inputData.viewDirectionWS,
                                                              surfaceData.clearCoatMask, specularHighlightsOff);
        if (fresnel)
            lightingData.mainLightColor = max(lightingData.mainLightColor
                + FresnelCorrection(brdfData, inputData.normalWS, inputData.viewDirectionWS, mainLight), 0);
    }

    #if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    bool budget = _TgLampBudget.x > 0.5;
    half3 lamps = 0;
    float lampLight = 0;

    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, brdfDataClearCoat, light,
                                                                          inputData.normalWS, inputData.viewDirectionWS,
                                                                          surfaceData.clearCoatMask, specularHighlightsOff);
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            float e = 0;
            if (budget) light.color *= LampShare(light, inputData.normalWS, e);
            half3 lit = LightingPhysicallyBased(brdfData, brdfDataClearCoat, light, inputData.normalWS,
                                                inputData.viewDirectionWS, surfaceData.clearCoatMask, specularHighlightsOff);
            if (budget) { lamps += lit; lampLight += e; }
            else lightingData.additionalLightsColor += lit;
        }
    LIGHT_LOOP_END
    if (budget) lightingData.additionalLightsColor += LampTotal(lamps, lampLight);
    #endif

    #if defined(_ADDITIONAL_LIGHTS_VERTEX)
    lightingData.vertexLightingColor += inputData.vertexLighting * brdfData.diffuse;
    #endif

#if REAL_IS_HALF
    return min(CalculateFinalColor(lightingData, surfaceData.alpha), HALF_MAX);
#else
    return CalculateFinalColor(lightingData, surfaceData.alpha);
#endif
}

half4 TarmacFragmentPBR(InputData inputData, SurfaceData surfaceData)
{
    return TarmacFragmentPBR(inputData, surfaceData, false);
}

// The lamps' light on a matte surface, Lambert, irradiance under the budget: what ShadeLambert multiplies its colour by.
// Needs inputData's positionWS, normalWS and normalizedScreenSpaceUV.
half3 LampLambert(InputData inputData)
{
    half3 sum = 0;
    #if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    bool budget = _TgLampBudget.x > 0.5;
    half3 lamps = 0;
    float lampLight = 0;
    half4 shadowMask = half4(1, 1, 1, 1);
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, shadowMask);
        float e = 0;
        if (budget) light.color *= LampShare(light, inputData.normalWS, e);
        half3 lit = LightingLambert(light.color * (light.distanceAttenuation * light.shadowAttenuation), light.direction, inputData.normalWS);
        if (budget) { lamps += lit; lampLight += e; }
        else sum += lit;
    LIGHT_LOOP_END
    // The pixel cap is on what the surface gives back; for white it is the irradiance itself.
    if (budget) sum += LampTotal(lamps, lampLight);
    #endif
    return sum;
}

// The lamps' light all round a point in the air, with no surface to face them, under the budget: for the smoke. Needs
// inputData's positionWS and normalizedScreenSpaceUV.
half3 LampAround(InputData inputData)
{
    half3 sum = 0;
    #if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    bool budget = _TgLampBudget.x > 0.5;
    half3 lamps = 0;
    float lampLight = 0;
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS);
        half3 lit = light.color * light.distanceAttenuation;
        if (budget)
        {
            float raw = dot(lit, TG_LAMP_LUMA);
            float e = LampKnee(raw, _TgLampBudget.y);
            lamps += raw > 1e-6 ? lit * (half)(e / raw) : lit;
            lampLight += e;
        }
        else sum += lit;
    LIGHT_LOOP_END
    if (budget) sum += LampTotal(lamps, lampLight);
    #endif
    return sum;
}

#endif
