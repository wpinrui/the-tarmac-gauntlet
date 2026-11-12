// A spinning wheel's rim as the eye sees it at speed (WheelBlur): a disc over the wheel face, each ring the average of the
// spokes and gaps turning through it, read from a strip worked out from the rim's own triangles, lit as the rim is, with
// faint streaks across the part-covered rings. Drawn see-through after the opaque scene, so it writes no depth and no
// motion: behind it the scene keeps its own motion vectors.
Shader "Hidden/TarmacGauntlet/WheelBlur"
{
    Properties
    {
        _Profile ("Profile", 2D) = "black" {}
        _Fade ("Fade", Range(0, 1)) = 0
        _Metallic ("Metallic", Range(0, 1)) = 0.8
        _Smoothness ("Smoothness", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Assets/Rendering/Shaders/LitKeywords.hlsl"
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"

            TEXTURE2D(_Profile);
            SAMPLER(sampler_Profile);

            CBUFFER_START(UnityPerMaterial)
                float4 _Profile_ST;
                half _Fade;
                half _Metallic;
                half _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 axleWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.axleWS = TransformObjectToWorldNormal(float3(1, 0, 0));
                o.uv = v.uv;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv);
                clip(1.0 - r);
                half4 ring = SAMPLE_TEXTURE2D(_Profile, sampler_Profile, float2(saturate(r) * (63.0 / 64.0) + 0.5 / 64.0, 0.5));
                // Streaks only where the ring is part spoke and part gap, turning with the disc.
                half a = ring.a;
                half streak = sin(atan2(i.uv.y, i.uv.x) * 5.0) * 0.5 + sin(atan2(i.uv.y, i.uv.x) * 3.0 + r * 4.0) * 0.5;
                a = saturate(a + streak * 0.35 * a * (1.0 - a));
                // Lit on the side facing the camera: the face outboard.
                half3 n = normalize(i.axleWS);
                if (dot(n, GetWorldSpaceViewDir(i.positionWS)) < 0) n = -n;
                half4 c = ShadePbr(i.positionWS, n, i.positionCS, i.fog, ring.rgb, _Metallic, _Smoothness);
                return half4(c.rgb, a * _Fade);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
