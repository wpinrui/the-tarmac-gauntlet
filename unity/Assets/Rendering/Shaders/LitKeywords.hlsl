// URP's lighting keywords for the forward pass of the game's own lit shaders, the ones the pipeline asset uses:
// the sun's cascaded soft shadows, the clouds' shadows in its cookie (CloudShadows), other lights per pixel, and fog.
// Include with #include_with_pragmas.
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
#pragma multi_compile _ _ADDITIONAL_LIGHTS
#pragma multi_compile_fragment _ _LIGHT_COOKIES
#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
