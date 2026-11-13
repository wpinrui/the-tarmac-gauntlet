// A car's headlamp beam on the ground ahead, cast like a decal. The beam is a box in front of the car, drawn from its
// inside faces; for each pixel under the box it finds what the scene drew there from the depth, and lights it only when
// it lies inside the beam, near the car's own ground and faces up. So the sky, walls, other cars and anything over a
// crest stay as they were. Like the floodlights' pools it brightens what is drawn there, multiplying it by 1 + _Color at
// the beam's brightest so the road, the lines and the grass keep their colours, then adds a little _Lift so a dark road
// still shows the light. URP draws the SRPDefaultUnlit pass before the UniversalForward one, which keeps the two in
// order. The pool is soft all round, as a real headlamp's is: dim at the
// nose, brightest from 6 to 9 m ahead, fading slowly out to the beam's end and gently to the sides, and into the fog
// with distance.
// The beam's shape matches CarLamps: _Shape is its length and its width at the nose and at the far end, m; _Band how
// far below and above the car's ground it lights, m.
Shader "Hidden/TarmacGauntlet/HeadlampBeam"
{
    Properties
    {
        _Color ("Brightening", Color) = (0, 0, 0, 1)
        _Lift ("Lift", Color) = (0, 0, 0, 1)
        _Shape ("Length, near width, far width", Vector) = (24, 1.8, 9, 0)
        _Band ("Below, above", Vector) = (3, 0.6, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        // The inside faces, with no depth test: the box draws whether the camera is outside it or in it.
        Cull Front ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _Lift;
            float4 _Shape;
            float4 _Band;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        Varyings vert(Attributes v)
        {
            Varyings o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v, o);
            o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
            return o;
        }

        bool IsSky(float raw)
        {
        #if UNITY_REVERSED_Z
            return raw <= 1e-6;
        #else
            return raw >= 1.0 - 1e-6;
        #endif
        }

        float3 WorldAt(float2 uv, out float raw)
        {
            raw = SampleSceneDepth(uv);
        #if UNITY_REVERSED_Z
            float z = raw;
        #else
            float z = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, raw);
        #endif
            return ComputeWorldSpacePosition(uv, z, UNITY_MATRIX_I_VP);
        }

        // The nearer of the two steps to a pixel's neighbours: across a silhouette it takes the side on the same
        // surface, so the normal does not lean over at the edges of cars and walls.
        float3 Step(float3 p, float3 back, float3 ahead)
        {
            float3 a = ahead - p, b = p - back;
            return dot(a, a) < dot(b, b) ? a : b;
        }

        // How much of the beam falls on what is drawn under this pixel, and how far into the fog it is.
        float Weight(Varyings i, out float fog)
        {
            UNITY_SETUP_INSTANCE_ID(i);
            fog = 0.0;
            float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
            float raw;
            float3 p = WorldAt(uv, raw);
            if (IsSky(raw)) return 0.0;

            // Where the point is in the beam: x across, y up from the car's ground, z ahead from the nose.
            float3 o = TransformWorldToObject(p);
            float t = o.z / _Shape.x;
            if (t <= 0.0 || t >= 1.0) return 0.0;
            float across = lerp(_Shape.y, _Shape.z, t) * 0.5;
            float u = abs(o.x) / across;
            float along = smoothstep(0.0, 6.0, o.z) * (1.0 - smoothstep(9.0, _Shape.x, o.z));
            float side = exp(-2.5 * u * u) * (1.0 - smoothstep(0.55, 1.0, u));
            float shape = along * side;
            float band = smoothstep(-_Band.x, -_Band.x * 0.5, o.y) * (1.0 - smoothstep(_Band.y * 0.5, _Band.y, o.y));
            float w = shape * band;
            if (w <= 1e-4) return 0.0;

            // Only ground facing up: the surface's slope from the neighbouring pixels' depth.
            float2 px = 1.0 / _ScaledScreenParams.xy;
            float r0;
            float3 dx = Step(p, WorldAt(uv - float2(px.x, 0), r0), WorldAt(uv + float2(px.x, 0), r0));
            float3 dy = Step(p, WorldAt(uv - float2(0, px.y), r0), WorldAt(uv + float2(0, px.y), r0));
            float3 n = normalize(cross(dy, dx));
            if (dot(n, _WorldSpaceCameraPos - p) < 0.0) n = -n;
            // Seen low along the road the slope is less certain, so the test eases in over a wide range.
            w *= smoothstep(0.4, 0.75, n.y);
            if (w <= 1e-4) return 0.0;

            fog = ComputeFogFactor(TransformWorldToHClip(p).z);
            return w;
        }

        // Into the fog the beam fades to nothing, not to the fog's colour.
        float4 Fogged(float3 c, float fog)
        {
            return float4(MixFogColor(c, float3(0, 0, 0), fog), 0);
        }
        ENDHLSL

        Pass
        {
            Name "Brighten"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend DstColor One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            float4 frag(Varyings i) : SV_Target
            {
                float fog;
                float w = Weight(i, fog);
                return Fogged(_Color.rgb * w, fog);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Lift"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            float4 frag(Varyings i) : SV_Target
            {
                float fog;
                float w = Weight(i, fog);
                return Fogged(_Lift.rgb * w, fog);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
