// The clouds' shadows for the sun's cookie (CloudShadows): each texel is a line of the sun's light, followed up from the
// sun's plane to the clouds' height, and darkened by the cloud there.
Shader "Hidden/TarmacGauntlet/CloudShadow"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "Cloud shadow"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Assets/Rendering/Shaders/CloudCover.hlsl"

            float4x4 _TgCookieToWorld;
            float4 _TgSunDir;

            half4 Frag(Varyings input) : SV_Target
            {
                float3 p = mul(_TgCookieToWorld, float4(input.texcoord, 0.0, 1.0)).xyz;
                float3 sun = _TgSunDir.xyz;
                // A sun lower than this still casts the shadows long, but no further.
                float up = max(sun.y, 0.08);
                float2 xz = p.xz + sun.xz * ((TG_CLOUD_BASE - p.y) / up);
                return half4((half)(1.0 - TgCloudCover(xz) * _TgCloudMap.w), 0, 0, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
