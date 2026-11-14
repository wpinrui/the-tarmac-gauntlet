// The ground round the circuit, and the grass runoff and verges: the fields' colours from a map laid
// over the whole ground, times a fine grass grain repeating every 8 m, with stripes mown along the
// track where the grass is mown. All are made in code (Landscape.cs) and read by world position, so
// the runoff and the ground beyond it match.
//
// The mowing map holds, per map point: the distance beyond the nearest road edge, m; how far out
// from the road the grass is mown, m, 0 where none is; and the way that road runs across the
// ground, as long as the stripes are fresh (LandscapePlan.Mowing). Each stripe is mown the opposite way
// to the next, so its grass leans along the road one way or the other: a stripe whose grass leans
// away from the viewer looks lighter, one leaning towards them darker, and the stripes swap, fainter,
// as the view turns round.
Shader "Hidden/TarmacGauntlet/Ground"
{
    Properties
    {
        [MainTexture] _MainTex ("Fields", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Grain ("Grain", 2D) = "gray" {}
        _Mowing ("Mowing", 2D) = "black" {}
        _Map ("Map's corner and 1 / size, world xz", Vector) = (0, 0, 1, 1)
        _StripeWidth ("Stripe width, m (LandscapePlan.StripeWidth)", Float) = 5
        _StripeContrast ("Stripe contrast", Range(0, 0.5)) = 0.15
        _Smoothness ("Smoothness", Range(0, 1)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_Grain);
        SAMPLER(sampler_Grain);
        TEXTURE2D(_Mowing);
        SAMPLER(sampler_Mowing);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _BaseColor;
            float4 _Grain_ST;
            float4 _Mowing_ST;
            float4 _Map;
            float _StripeWidth;
            half _StripeContrast;
            half _Smoothness;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local_fragment _ _GROUND_STRIPES
            #include_with_pragmas "Assets/Rendering/Shaders/LitKeywords.hlsl"
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = (i.positionWS.xz - _Map.xy) * _Map.zw;
                half3 fields = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                half grain = SAMPLE_TEXTURE2D(_Grain, sampler_Grain, i.positionWS.xz / 8).r;
                half3 albedo = fields * GammaScale(grain * 2) * _BaseColor.rgb;
                #if defined(_GROUND_STRIPES)
                    half4 mow = SAMPLE_TEXTURE2D(_Mowing, sampler_Mowing, uv);
                    // The grain roughens the stripes' edges a touch, as cut grass is never quite clean.
                    float d = (mow.r + (grain - 0.5) * 0.3) / _StripeWidth;
                    float across = fwidth(d);
                    // A square wave, +1 on one stripe and -1 on the next, its edges softened over about a pixel.
                    float wave = clamp((abs(frac(d * 0.5) - 0.5) * 4 - 1) / max(across * 2, 0.08), -1, 1);
                    // Gone where the stripes would be narrower than a couple of pixels, and past the mower's last pass.
                    half fresh = length(mow.ba);
                    half fade = fresh * saturate(1.5 - across * 2.5) * saturate((mow.g - mow.r) / max(fwidth(mow.r), 0.05) + 0.5);
                    half2 along = mow.ba / max(fresh, 1e-3);
                    float2 view = i.positionWS.xz - _WorldSpaceCameraPos.xz;
                    half lean = dot(view, along) / max(length(view), 1e-3);
                    albedo *= GammaScale(1 + _StripeContrast * wave * fade * (0.35 + 0.65 * lean));
                #endif
                return ShadeGrass(i.positionWS, i.normalWS, i.positionCS, i.fog, albedo, _Smoothness);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex TarmacShadowVertex
            #pragma fragment TarmacNullFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma vertex TarmacDepthVertex
            #pragma fragment TarmacNullFragment
            #pragma multi_compile_instancing
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"
            ENDHLSL
        }
    }
}
