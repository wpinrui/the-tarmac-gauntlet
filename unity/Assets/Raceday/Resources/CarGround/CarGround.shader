// The soft dark the ground takes under and round a car, where the sky's light cannot reach: one soft rounded rectangle
// under the body and one under each tyre (CarGround.cs). Each quad's first UV holds the point's place from the quad's
// centre and the half size of its fully dark core, m; the second its soft edge, m, and how dark the core is. It
// multiplies what is drawn behind it, and fades in the fog like the road under it.
Shader "Hidden/TarmacGauntlet/CarGround"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "CarGround"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 shape : TEXCOORD0;
                float2 edge : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 shape : TEXCOORD0;
                float2 edge : TEXCOORD1;
                half fog : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.shape = v.shape;
                o.edge = v.edge;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Distance outside the core's rounded rectangle, m.
                float2 q = abs(i.shape.xy) - i.shape.zw;
                float outside = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
                float fall = saturate(1.0 - outside / max(i.edge.x, 1e-3));
                half dark = (half)(i.edge.y * fall * fall);
                half3 c = MixFogColor(half3(1, 1, 1) - dark, half3(1, 1, 1), i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
