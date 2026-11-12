// A car's headlamp beam on the ground ahead, cast like a decal. The beam is a box in front of the car, drawn from its
// inside faces; for each pixel under the box it finds what the scene drew there from the depth, and lights it only when
// it lies inside the beam, near the car's own ground and faces up. So the sky, walls, other cars and anything over a
// crest stay as they were. The light added is the scene's own colour there, from before the transparent pass, times
// _Color: so the road, the lines and the grass keep their colours, and a floodlit pool is not brightened again by
// its own floodlight. A soft cap (_Cap, linear luminance) keeps a brightly lit floor, such as the pit garage's, from
// running into bloom. _Lift adds a little so black ground still shows the light. It fades out at the beam's edge and
// into the fog with distance.
// The beam's shape matches CarLamps: _Shape is its length and its width at the nose and at the far end, m; _Band how
// far below and above the car's ground it lights, m.
Shader "Hidden/TarmacGauntlet/HeadlampBeam"
{
    Properties
    {
        _Color ("Brightening", Color) = (0, 0, 0, 1)
        _Lift ("Lift", Color) = (0, 0, 0, 1)
        _Cap ("Brightest added light", Float) = 0.5
        _Shape ("Length, near width, far width", Vector) = (24, 1.8, 9, 0)
        _Band ("Below, above", Vector) = (3, 0.6, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "UniversalForward" }
            // The inside faces, with no depth test: the box draws whether the camera is outside it or in it.
            Cull Front ZWrite Off ZTest Always
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _Lift;
                float _Cap;
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

            float4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float raw;
                float3 p = WorldAt(uv, raw);
                if (IsSky(raw)) return 0;

                // Where the point is in the beam: x across, y up from the car's ground, z ahead from the nose.
                float3 o = TransformWorldToObject(p);
                float t = o.z / _Shape.x;
                if (t <= 0.0 || t >= 1.0) return 0;
                float across = lerp(_Shape.y, _Shape.z, t) * 0.5;
                float u = o.x / across, v = pow(t, 0.7) * 2.0 - 1.0;
                float shape = saturate(1.0 - u * u - v * v);
                shape *= shape;
                float band = smoothstep(-_Band.x, -_Band.x * 0.5, o.y) * (1.0 - smoothstep(_Band.y * 0.5, _Band.y, o.y));
                float w = shape * band;
                if (w <= 1e-4) return 0;

                // Only ground facing up: the surface's slope from the neighbouring pixels' depth.
                float2 px = 1.0 / _ScaledScreenParams.xy;
                float r0;
                float3 dx = Step(p, WorldAt(uv - float2(px.x, 0), r0), WorldAt(uv + float2(px.x, 0), r0));
                float3 dy = Step(p, WorldAt(uv - float2(0, px.y), r0), WorldAt(uv + float2(0, px.y), r0));
                float3 n = normalize(cross(dy, dx));
                if (dot(n, _WorldSpaceCameraPos - p) < 0.0) n = -n;
                w *= smoothstep(0.55, 0.8, n.y);
                if (w <= 1e-4) return 0;

                // The headlamps' light on this point: the scene's colour here times the brightening, eased off towards
                // the cap so a brightly lit floor gains no more than _Cap.
                float3 add = SampleSceneColor(uv) * _Color.rgb * w;
                float lum = dot(add, float3(0.2126, 0.7152, 0.0722));
                add *= 1.0 / (1.0 + lum / max(_Cap, 1e-4));
                add += _Lift.rgb * w;

                // Into the fog the beam fades to nothing, not to the fog's colour.
                float fog = ComputeFogFactor(TransformWorldToHClip(p).z);
                return float4(MixFogColor(add, float3(0, 0, 0), fog), 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
