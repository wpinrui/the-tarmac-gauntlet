// Trees and hedge bushes as hemi-octahedral impostors: each is one quad that turns to face the
// viewer and shows the baked view of the tree (tools/landscape/bake.html) taken from the nearest
// of 8 x 8 directions over the upper hemisphere. The colour and normal atlases are texture arrays,
// one layer a kind of tree, so a whole chunk of mixed trees is one draw call. In the shadow pass
// the quad faces the sun instead, so the shadow has the tree's outline seen from the sun; only
// trees within _ShadowReach of the camera cast one.
//
// Each quad is cut down to the leaves' bounds in the frame it shows (_Crop, from tools/landscape/frames.py): most of a
// whole frame is empty, and every fragment of it would still be sampled and thrown away.
//
// Woods seen across or along a slope stack many trees on each pixel. So the trees draw in two goes, as two materials
// (Landscape.TreeMaterials): first depth alone (the "TreeDepth" pass, a cheap alpha test and nothing else), then the
// lit pass with depth writes off, so the depth test runs before the shader and only the tree in front is lit.
//
// Near the race camera the trees are real meshes (NearTree.shader); each impostor fades in over them by its share
// (Foliage.hlsl), every pass alike, so its shadow hands over too, and it is not drawn at all where the mesh stands whole.
//
// Per vertex: position is the foot of the trunk in world space; uv0 the quad corner (-1 to 1);
// uv1 the quad's half size, the height of the tree's middle above its foot, its turn about the
// vertical in radians and its kind; uv2.x a height squash for hedge bushes; colour half the leaf tint.
Shader "Hidden/TarmacGauntlet/Impostor"
{
    Properties
    {
        _Atlas ("Colour", 2DArray) = "" {}
        _Normals ("Normals", 2DArray) = "" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _Frames ("Frames across", Float) = 8
        _ShadowReach ("Trees cast shadows this far from the camera, m", Float) = 160
        _Crop ("Leaves' bounds in each frame, a row a kind", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "DisableBatching" = "True" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #pragma target 3.5
        #pragma require 2darray
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Foliage.hlsl"

        TEXTURE2D_ARRAY(_Atlas);
        SAMPLER(sampler_Atlas);
        TEXTURE2D_ARRAY(_Normals);
        SAMPLER(sampler_Normals);
        TEXTURE2D(_Crop);

        CBUFFER_START(UnityPerMaterial)
            float _Cutoff;
            float _Frames;
            float _ShadowReach;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float4 color : COLOR;
            float4 uv0 : TEXCOORD0;
            float4 uv1 : TEXCOORD1;
            float4 uv2 : TEXCOORD2;
        };

        // One corner of a tree's quad, turned to face a view.
        struct Corner
        {
            float3 positionWS;
            float3 normalWS;
            float3 tangentWS;
            float3 atlas;   // uv in the atlas, layer
        };

        // Unity turns a point by t about +Y as x' = x cos t + z sin t, z' = -x sin t + z cos t.
        float3 Turn(float3 v, float t)
        {
            float c = cos(t), s = sin(t);
            return float3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
        }

        // The atlases were baked in three.js's right-handed frame. Unity's is that frame with z
        // turned round, so a direction goes between the two by flipping z.
        float3 Flip(float3 v) { return float3(v.x, v.y, -v.z); }

        float3 Centre(Attributes v)
        {
            return TransformObjectToWorld(v.positionOS.xyz) + float3(0, v.uv1.y, 0);
        }

        // The corner of the quad for a tree seen along toEye (from the tree towards the view), radius across.
        Corner Place(Attributes v, float3 centre, float3 toEye, float radius)
        {
            float yaw = v.uv1.z, squash = v.uv2.x;

            // Into the tree's own baked frame: undo its turn, then flip into three.js's frame.
            float3 local = Flip(Turn(toEye, -yaw));
            local.y = max(local.y, 0);
            float3 d = local / (abs(local.x) + abs(local.y) + abs(local.z) + 1e-5);
            float2 frame = clamp(floor((float2(d.x + d.z, d.x - d.z) * 0.5 + 0.5) * (_Frames - 1) + 0.5), 0, _Frames - 1);
            float2 g = frame / (_Frames - 1) * 2 - 1;
            float px = (g.x + g.y) * 0.5, pz = (g.x - g.y) * 0.5;
            float3 D = normalize(float3(px, max(1 - abs(px) - abs(pz), 0), pz));
            // The bake camera's axes for that frame: right and up on the image.
            float3 up = abs(D.y) > 0.999 ? float3(0, 0, -1) : float3(0, 1, 0);
            float3 R = normalize(cross(up, D));
            float3 U = cross(D, R);

            // The corner of the leaves' bounds in the frame, 0 to 1 across it.
            float4 crop = LOAD_TEXTURE2D_LOD(_Crop, uint2(frame.x + frame.y * _Frames, v.uv1.w), 0);
            float2 at = lerp(crop.xy, crop.zw, v.uv0.xy * 0.5 + 0.5);
            float2 q = at * 2 - 1;
            float3 offset = (q.x * R + q.y * U) * radius;
            // Back into Unity's frame and the tree's turn.
            float3 world = Turn(Flip(offset), yaw);
            world.y *= squash;

            Corner c;
            c.positionWS = centre + world;
            c.normalWS = Turn(Flip(D), yaw);
            c.tangentWS = Turn(Flip(R), yaw);
            c.atlas = float3((frame + at) / _Frames, v.uv1.w);
            return c;
        }

        // Depth alone, for TreeDepth and DepthOnly. The lit pass places the quad with the same Place, so its depth
        // comes out the same and passes the LEqual test against what this wrote.
        struct DepthVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 atlas : TEXCOORD0;
            float share : TEXCOORD1;
        };

        // The quad's half size, none where the near 3D tree stands whole.
        float Radius(Attributes v, float share)
        {
            return share > 0 ? v.uv1.x : 0;
        }

        // Where the impostor is fading in, it keeps the pixels its 3D tree leaves.
        void Fade(float share, float2 pixel)
        {
            if (share < 1) FoliageFade(-share, pixel);
        }

        DepthVaryings DepthVert(Attributes v)
        {
            float3 centre = Centre(v);
            float share = ImpostorShare(centre);
            Corner c = Place(v, centre, _WorldSpaceCameraPos - centre, Radius(v, share));
            DepthVaryings o;
            o.positionCS = TransformWorldToHClip(c.positionWS);
            o.atlas = c.atlas;
            o.share = share;
            return o;
        }

        half4 DepthFrag(DepthVaryings i) : SV_Target
        {
            Fade(i.share, i.positionCS.xy);
            clip(SAMPLE_TEXTURE2D_ARRAY(_Atlas, sampler_Atlas, i.atlas.xy, i.atlas.z).a - _Cutoff);
            return 0;
        }
        ENDHLSL

        Pass
        {
            Name "TreeDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            ENDHLSL
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            // Against the depth TreeDepth wrote: only the front tree's fragments pass, and they pass before the shader runs.
            ZWrite Off ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Assets/Rendering/Shaders/LitKeywords.hlsl"
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 atlas : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half3 tangentWS : TEXCOORD3;
                half4 color : COLOR;
                half fog : TEXCOORD4;
                float share : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                float3 centre = Centre(v);
                float share = ImpostorShare(centre);
                Corner c = Place(v, centre, _WorldSpaceCameraPos - centre, Radius(v, share));
                Varyings o;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                o.positionWS = c.positionWS;
                o.normalWS = c.normalWS;
                o.tangentWS = c.tangentWS;
                o.atlas = c.atlas;
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.share = share;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                Fade(i.share, i.positionCS.xy);
                half4 c = SAMPLE_TEXTURE2D_ARRAY(_Atlas, sampler_Atlas, i.atlas.xy, i.atlas.z);
                clip(c.a - _Cutoff);
                // The baked normals are in the image's axes: x right, y up, z towards the viewer. Flipping
                // z turned the frame inside out, so the binormal (normal x tangent) takes w = -1 to come out as up.
                half3 normalTS = normalize(SAMPLE_TEXTURE2D_ARRAY(_Normals, sampler_Normals, i.atlas.xy, i.atlas.z).xyz * 2 - 1);
                half3 bitangent = -cross(i.normalWS, i.tangentWS);
                half3 normalWS = TransformTangentToWorld(normalTS, half3x3(i.tangentWS, bitangent, i.normalWS));
                half3 col = ShadeLambert(i.positionWS, normalWS, i.positionCS, i.fog, c.rgb * GammaScale(i.color.rgb * 2));
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"

            typedef DepthVaryings Varyings;

            Varyings vert(Attributes v)
            {
                float3 centre = Centre(v);
                // The quad turns square to the sun, so its shadow is the tree's outline seen from the sun.
                // Only trees near the camera cast a shadow: far out the shadow map is too coarse for a
                // leafy outline and it breaks up into blotches and streaks.
                float share = ImpostorShare(centre);
                float radius = Radius(v, share);
                float far = distance(_WorldSpaceCameraPos, centre);
                if (far > _ShadowReach) radius = 0;
                else radius *= saturate((_ShadowReach - far) / 30);
                Corner c = Place(v, centre, _LightDirection, radius);
                Varyings o;
                o.positionCS = ShadowClipPosition(c.positionWS, c.normalWS);
                o.atlas = c.atlas;
                o.share = share;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                Fade(i.share, i.positionCS.xy);
                // A smaller mip for the outline: one shadow-map texel covers many leaves, and the
                // softer outline casts a solid crown shadow rather than speckle.
                clip(SAMPLE_TEXTURE2D_ARRAY_LOD(_Atlas, sampler_Atlas, i.atlas.xy, i.atlas.z, 3).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            ENDHLSL
        }
    }
}
