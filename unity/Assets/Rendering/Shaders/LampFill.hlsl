// For the night's lamps that brighten the scene's own colour (HeadlampBeam, LightPool): how much brighter a point would be
// out of the sun's or moon's shadow. The scene's colour there is its albedo times the light the opaque pass gave it, so a
// lamp that only multiplied it would leave a car's moon shadow black inside its beam, and carry the shadow's dithered
// penumbra into the beam at ten times the contrast. Times this, the lamp lights the shadow as brightly as the ground
// beside it, as a real lamp does.
//
// The shadow is read exactly as the opaque pass read it, so it divides out pixel for pixel, dither and all: from the
// screen-space shadow (ScreenShadows.shader) while that is on, else from URP's shadow map with the same filter.
// Include after Core.hlsl, with #include_with_pragmas.
#ifndef TARMAC_LAMP_FILL_INCLUDED
#define TARMAC_LAMP_FILL_INCLUDED

#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

// Drawn in the transparent pass, where URP reads its shadow map itself.
#define _SURFACE_TYPE_TRANSPARENT 1
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// 1 while the screen-space shadows are drawn (QualityLevels): the opaque pass read its shadow from them.
float _TgScreenShadows;

// The most a lamp is made to brighten a shadowed point over what it gives the scene's colour there.
#define TARMAC_LAMP_FILL_MOST 12.0

// positionWS and normalWS: the ground the lamp lights; uv: its pixel on the screen.
half3 LampFill(float3 positionWS, half3 normalWS, float2 uv)
{
    half shadow = 1;
#if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    if (_TgScreenShadows > 0.5)
        shadow = SAMPLE_TEXTURE2D_LOD(_ScreenSpaceShadowmapTexture, sampler_PointClamp, uv, 0).x;
    else
        shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(positionWS));
#endif
    Light main = GetMainLight();
    half3 direct = main.color * saturate(dot(normalWS, main.direction));
    half3 open = max(SampleSH(normalWS), 0) + direct;
    half3 shaded = max(SampleSH(normalWS), 0) + direct * shadow;
    return min(open / max(shaded, open / TARMAC_LAMP_FILL_MOST + 1e-5), TARMAC_LAMP_FILL_MOST);
}

#endif
