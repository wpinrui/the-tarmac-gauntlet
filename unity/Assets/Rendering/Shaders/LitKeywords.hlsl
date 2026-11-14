// URP's lighting keywords for the forward pass of the game's own lit shaders, the ones the pipeline assets use: the sun's
// shadows (one map, cascades, or worked out on the screen beforehand at High and Ultra), its soft shadows, other lights per
// pixel (Forward+'s clustered lights, or per object) and their shadows and cookies, the clouds' shadows in the sun's cookie (CloudShadows), the screen-space ambient occlusion and fog. Include with #include_with_pragmas.
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
#pragma multi_compile _ _ADDITIONAL_LIGHTS
#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
#pragma multi_compile_fragment _ _LIGHT_COOKIES
#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
