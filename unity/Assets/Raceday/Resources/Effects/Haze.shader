// Exhaust heat haze (CarEffects): soft puffs that bend the scene behind them with a fast wobble, so the air behind a
// car's exhausts swims. Reads the copy of the scene the pipeline makes for it (HeatHazeFeature); the particle's alpha
// sets how hard each puff bends. Nothing is added to the colour.
// The copy is taken before the transparent pass, so it holds none of the headlamp beams, the floodlights' pools or the
// tyre smoke. Drawn over them, it would put back the unlit road: black blobs behind every car at night. So a puff does
// not draw the copy. It scales what is already drawn by how much the bend changes the copy there, which moves the
// texture of the road under the beam as the bend moves it, and leaves the beam's light where it was. The copy still
// holds the moon's shadows, which the lamps light away, so the haze fades out as the lamps come on: the air's shimmer
// shows against a sunlit road, not in the dark.
Shader "Hidden/TarmacGauntlet/Haze"
{
    Properties
    {
        _Bend ("Bend", Float) = 0.006
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend DstColor Zero
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Bend;
            CBUFFER_END
            // Set by CarEffects: how far the lamps are on, 0 by day to 1 at night.
            float _EffectsDark;

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
                float eye : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(world);
                o.color = v.color;
                o.uv = v.uv;
                o.eye = -TransformWorldToView(world).z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float r = saturate(1.0 - length(p));
                float puff = r * r;
                if (puff <= 0.0) discard;
                float t = _Time.y;
                // A wobble that turns with the particle and runs fast, as hot air does; weaker far away, as it looks.
                float2 w = float2(sin(p.y * 9.0 + t * 23.0) + 0.5 * sin(p.x * 13.0 - t * 17.0),
                                  sin(p.x * 8.0 - t * 19.0) + 0.5 * sin(p.y * 11.0 + t * 29.0));
                float strength = _Bend * puff * i.color.a / max(1.0, i.eye * 0.15);
                float2 screen = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 bent = SampleSceneColor(screen + w * strength);
                half3 still = SampleSceneColor(screen);
                // Kept within a narrow band, so a bend across an edge, from black into a lamp, can't flare or blacken.
                half3 change = clamp((bent + 1e-4) / (still + 1e-4), 0.6, 1.6);
                // Fully bent in the middle, easing out to the edge so no rim is seen.
                float bend = saturate(puff * 2.5) * saturate(i.color.a * 3.0) * (1.0 - _EffectsDark);
                return half4(lerp(1.0, change, bend), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
