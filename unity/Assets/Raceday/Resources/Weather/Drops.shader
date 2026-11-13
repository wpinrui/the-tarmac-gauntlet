// Rain streaks and wheel spray: unlit, soft edged particles drawn in the light around them.
// _Shape 0 draws a streak down a stretched quad: a thread _Thread of the quad's width, or a pixel wide and as much fainter
// where that is narrower, fading to its ends. 1 draws a soft round puff, 2 an evenly soft one with no bright core, which
// also fades where it meets the road or a car while the scene's depth is drawn (_SpraySoft, set by WeatherView), so the
// spray's mist never shows a hard edge along the ground. No texture is read.
Shader "Hidden/TarmacGauntlet/Drops"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Shape ("Shape", Float) = 0
        _Thread ("Streak width, share of the quad's", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Shape;
                float _Thread;
            CBUFFER_END
            // 1 while the scene's depth is drawn for the spray to fade into.
            float _SpraySoft;
            // How far in front of the road or a car the mist has faded in, m.
            #define SOFT_DEPTH 1.2

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float fog : TEXCOORD1;
                float eye : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(world);
                o.eye = -TransformWorldToView(world).z;
                o.color = v.color * _Tint;
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                // A stretched particle is a quad longer than it is wide: its short side spans the most per pixel.
                float2 px = fwidth(p);
                bool wide = px.x >= px.y;
                float across = wide ? p.x : p.y, along = wide ? p.y : p.x;
                float reach = min(max(_Thread, max(px.x, px.y)), 1.0);
                float streak = min(_Thread / reach, 1.0) * saturate(1.0 - abs(across) / reach) * saturate(1.0 - abs(along));
                float r = saturate(1.0 - length(p));
                float puff = r * r;
                float even = saturate(1.0 - dot(p, p));
                even *= even;
                half4 col = i.color;
                col.a *= _Shape < 0.5 ? streak : _Shape < 1.5 ? puff : even;
                if (_Shape > 1.5 && _SpraySoft > 0.5)
                {
                    float scene = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.positionCS)), _ZBufferParams);
                    col.a *= smoothstep(0.0, 1.0, saturate((scene - i.eye) / SOFT_DEPTH));
                }
                // Mist right at the lens thins out instead of filling the screen.
                if (_Shape > 1.5) col.a *= saturate((i.eye - 0.4) / 1.6);
                col.rgb = MixFog(col.rgb, i.fog);
                return half4(col.rgb * col.a, col.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
