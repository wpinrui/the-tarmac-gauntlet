// For the night's lamps that brighten the scene's own colour (HeadlampBeam, LightPool): how much brighter a point would be
// out of the sun's or moon's shadow. The scene's colour there is its albedo times the light the opaque pass gave it, so a
// lamp that only multiplied it would leave a car's moon shadow black inside its beam, and carry the shadow's dithered
// penumbra into the beam at ten times the contrast. Times this, the lamp lights the shadow as brightly as the ground
// beside it, as a real lamp does.
//
// The shadow is read exactly as the opaque pass read it, so it divides out pixel for pixel, dither and all: from the
// screen-space shadow (ScreenShadows.shader) while that is on, else from URP's shadow map with the same filter.
// Include after Core.hlsl, with #include_with_pragmas. The scene's colour comes from the opaque copy, which the
// pipeline makes while the headlamp beams are drawn (HeatHazeFeature).
#ifndef TARMAC_LAMP_FILL_INCLUDED
#define TARMAC_LAMP_FILL_INCLUDED

#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

// Drawn in the transparent pass, where URP reads its shadow map itself.
#define _SURFACE_TYPE_TRANSPARENT 1
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

// 1 while the screen-space shadows are drawn (QualityLevels): the opaque pass read its shadow from them.
float _TgScreenShadows;

// The most a lamp is made to brighten a shadowed point over what it gives the scene's colour there.
#define TARMAC_LAMP_FILL_MOST 12.0

// The ground's own sheen of the sky, which the shadow never darkened: URP's environment specular for a dielectric
// as smooth as the road on average. It stays out of what is brightened back, or a lit shadow shows paler than the
// road beside it at a glancing view.
#define TARMAC_LAMP_FILL_SMOOTHNESS 0.45

// How much brighter the lamp should take the scene's colour at a point than the colour itself: 1 out of the shadow,
// more in it. scene: the opaque pass's colour there; positionWS and normalWS: the ground the lamp lights; uv: its pixel.
half3 LampFill(half3 scene, float3 positionWS, half3 normalWS, float2 uv)
{
    half shadow = 1;
#if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    if (_TgScreenShadows > 0.5)
        shadow = SAMPLE_TEXTURE2D_LOD(_ScreenSpaceShadowmapTexture, sampler_PointClamp, uv, 0).x;
    else
        shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(positionWS));
#endif
    if (shadow >= 0.999) return 1;
    Light main = GetMainLight();
    half3 direct = main.color * saturate(dot(normalWS, main.direction));
    half3 sky = max(SampleSH(normalWS), 0);
    half3 open = sky + direct;
    half3 shaded = sky + direct * shadow;
    half3 fill = min(open / max(shaded, open / TARMAC_LAMP_FILL_MOST + 1e-5), TARMAC_LAMP_FILL_MOST);

    half3 view = GetWorldSpaceNormalizeViewDir(positionWS);
    half NoV = saturate(dot(normalWS, view));
    half roughness = (1 - TARMAC_LAMP_FILL_SMOOTHNESS) * (1 - TARMAC_LAMP_FILL_SMOOTHNESS);
    half grazing = saturate(TARMAC_LAMP_FILL_SMOOTHNESS + 0.04);
    half3 sheen = max(SampleSH(reflect(-view, normalWS)), 0) * (lerp(0.04, grazing, Pow4(1 - NoV)) / (roughness * roughness + 1));
    sheen = min(sheen, scene);
    half3 lit = (scene - sheen) * fill + sheen;
    return lerp(fill, lit / max(scene, 1e-5), step(1e-5, scene));
}

#endif
