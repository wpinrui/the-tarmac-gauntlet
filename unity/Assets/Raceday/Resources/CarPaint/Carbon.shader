// Carbon fibre under a clear coat: a 2x2 twill weave of tows, each tow catching the light across its own direction,
// laid on the part along whichever of its own axes the face points down most. Far off, where a tow is under two pixels
// across, the weave fades to its average so it never shimmers. CarMaterials puts it on a car's "carbon" material.
Shader "TarmacGauntlet/Carbon"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Assets/Rendering/Shaders/LitKeywords.hlsl"
            #define _CLEARCOAT 1
            #include "Assets/Raceday/Resources/CarPaint/CarPaint.hlsl"

            // A tow's width, metres.
            #define TOW 0.004
            // The weave's darkest and lightest, and how far a tow's faces lean either side of its crown.
            #define WEAVE_DARK 0.012
            #define WEAVE_LIGHT 0.06
            #define WEAVE_LEAN 0.9

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalOS : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs w = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = w.positionCS;
                o.positionWS = w.positionWS;
                o.fog = ComputeFogFactor(w.positionCS.z);
                // The part's own space in metres, so the weave rides with the car at its true size.
                float3 scale = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22));
                o.positionOS = v.positionOS.xyz * scale;
                o.normalOS = v.normalOS;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalOS), a = abs(n);
                // The weave's plane: the two axes the face points down least, as uv and their directions.
                float2 uv;
                float3 du, dv;
                if (a.x >= a.y && a.x >= a.z) { uv = i.positionOS.zy; du = float3(0, 0, 1); dv = float3(0, 1, 0); }
                else if (a.y >= a.z) { uv = i.positionOS.xz; du = float3(1, 0, 0); dv = float3(0, 0, 1); }
                else { uv = i.positionOS.xy; du = float3(1, 0, 0); dv = float3(0, 1, 0); }

                float2 t = uv / TOW;
                float2 c = floor(t), f = t - c;
                // 2x2 twill: along each row the tow on top changes every two, a step on from the row before.
                bool alongU = frac((c.x + c.y) / 4) < 0.5;
                float across = alongU ? f.y : f.x;
                float crown = sin(across * PI);
                // Shows only where a tow spans at least two pixels, at full strength from four: smaller, it would alias.
                float footprint = max(length(ddx(t)), length(ddy(t)));
                float weave = smoothstep(0.5, 0.25, footprint);

                float3 leanAxis = alongU ? dv : du;
                float3 bent = normalize(n + leanAxis * ((across - 0.5) * WEAVE_LEAN * weave));
                half3 albedo = lerp((WEAVE_DARK + WEAVE_LIGHT) * 0.5, lerp(WEAVE_DARK, WEAVE_LIGHT, crown), weave).xxx;
                half3 normalWS = TransformObjectToWorldNormal(bent);
                return ShadeCarPaint(i.positionWS, normalWS, i.positionCS, i.fog, albedo, 0, 0.7, i.positionOS);
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
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TarmacMotionVertex
            #pragma fragment TarmacMotionFragment
            #include "Assets/Rendering/Shaders/TarmacMotion.hlsl"
            ENDHLSL
        }
    }
}
