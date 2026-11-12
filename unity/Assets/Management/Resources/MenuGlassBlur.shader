// The frosted glass behind the menus: the scene, shrunk, blurred and made more colourful (GlassBlurFeature).
// Pass 0 shrinks with a 4-tap box, pass 1 blurs across, pass 2 blurs down and saturates.
Shader "Hidden/TarmacGauntlet/MenuGlassBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float _Sigma;      // blur radius in texels of the source
        float _Saturation; // 1 leaves colours alone

        half4 Tap(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
        }

        half4 Shrink(Varyings i) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            float2 d = _BlitTexture_TexelSize.xy * 0.5;
            half4 c = Tap(i.texcoord + float2(-d.x, -d.y));
            c += Tap(i.texcoord + float2(d.x, -d.y));
            c += Tap(i.texcoord + float2(-d.x, d.y));
            c += Tap(i.texcoord + float2(d.x, d.y));
            return c * 0.25;
        }

        // 25 taps two texels apart; bilinear filtering fills in between, so the reach is about 3 sigma for sigma up to 16.
        half4 Gauss(float2 uv, float2 dir)
        {
            float3 sum = 0;
            float total = 0;
            float s2 = 2.0 * max(_Sigma * _Sigma, 0.01);
            [unroll] for (int k = -12; k <= 12; k++)
            {
                float x = k * 2.0;
                float w = exp(-(x * x) / s2);
                sum += Tap(uv + dir * x * _BlitTexture_TexelSize.xy).rgb * w;
                total += w;
            }
            return half4(sum / total, 1);
        }

        half4 Across(Varyings i) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            return Gauss(i.texcoord, float2(1, 0));
        }

        half4 Down(Varyings i) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            half4 c = Gauss(i.texcoord, float2(0, 1));
            float lum = dot(c.rgb, float3(0.2126, 0.7152, 0.0722));
            c.rgb = max(0, lerp(lum.xxx, c.rgb, _Saturation));
            return c;
        }
        ENDHLSL

        Pass
        {
            Name "Shrink"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Shrink
            ENDHLSL
        }
        Pass
        {
            Name "Across"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Across
            ENDHLSL
        }
        Pass
        {
            Name "Down"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Down
            ENDHLSL
        }
    }
    Fallback Off
}
