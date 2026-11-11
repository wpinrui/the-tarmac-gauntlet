// Trees and hedge bushes as hemi-octahedral impostors: each is one quad that turns to face the
// viewer and shows the baked view of the tree (tools/landscape/bake.html) taken from the nearest
// of 8 x 8 directions over the upper hemisphere. The colour and normal atlases are texture arrays,
// one layer a kind of tree, so a whole chunk of mixed trees is one draw call. In the shadow pass
// the quad faces the sun instead, so the shadow has the tree's outline seen from the sun; only
// trees within _ShadowReach of the camera cast one.
//
// Per vertex: position is the foot of the trunk in world space; uv0 the quad corner (-1 to 1);
// uv1 the quad's half size, the height of the tree's middle above its foot, its turn about the
// vertical in radians and its kind; uv2.x a height squash for hedge bushes; colour the leaf tint.
Shader "Hidden/TarmacGauntlet/Impostor"
{
    Properties
    {
        _Atlas ("Colour", 2DArray) = "" {}
        _Normals ("Normals", 2DArray) = "" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _Frames ("Frames across", Float) = 8
        _ShadowReach ("Trees cast shadows this far from the camera, m", Float) = 160
    }
    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "DisableBatching" = "True" "IgnoreProjector" = "True" }
        Cull Off

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert alphatest:_Cutoff addshadow nolightmap nodynlightmap nodirlightmap noforwardadd nometa
        #pragma target 3.5
        #pragma require 2darray

        UNITY_DECLARE_TEX2DARRAY(_Atlas);
        UNITY_DECLARE_TEX2DARRAY(_Normals);
        float _Frames, _ShadowReach;

        struct Input
        {
            float3 atlas;   // uv in the atlas, layer
            float4 color : COLOR;
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

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float3 foot = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1)).xyz;
            float radius = v.texcoord1.x, lift = v.texcoord1.y, yaw = v.texcoord1.z;
            float squash = v.texcoord2.x;
            float3 centre = foot + float3(0, lift, 0);

            float3 toEye;
            #if defined(UNITY_PASS_SHADOWCASTER)
                // This pass draws both the sun's shadow map and the camera's own depth. Face whichever
                // view is drawing, read from its own matrices: an orthographic one is the shadow map,
                // looking along the sun, and the quad must turn square to it so its shadow is the
                // tree's outline seen from the sun; a perspective one is the camera, which must
                // match what the camera sees.
                if (UNITY_MATRIX_P[3][3] > 0.5)
                {
                    toEye = UNITY_MATRIX_V[2].xyz;   // the view's +z: back towards the light
                    // Only trees near the camera cast a shadow: far out the shadow map is too coarse
                    // for a leafy outline and it breaks up into blotches and streaks.
                    float far = distance(_WorldSpaceCameraPos, centre);
                    if (far > _ShadowReach) radius = 0;
                    else radius *= saturate((_ShadowReach - far) / 30);
                }
                else toEye = UNITY_MATRIX_I_V._m03_m13_m23 - centre;
            #else
                toEye = _WorldSpaceCameraPos - centre;
            #endif

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

            float3 offset = (v.texcoord.x * R + v.texcoord.y * U) * radius;
            // Back into Unity's frame and the tree's turn.
            float3 rW = Turn(Flip(R), yaw), uW = Turn(Flip(U), yaw), dW = Turn(Flip(D), yaw);
            float3 world = Turn(Flip(offset), yaw);
            world.y *= squash;
            v.vertex.xyz = mul(unity_WorldToObject, float4(centre + world, 1)).xyz;
            // The baked normals are in the image's axes: x right, y up, z towards the viewer. Flipping
            // z turned the frame inside out, so the binormal (normal x tangent) needs w = -1 to come out as up.
            v.normal = mul((float3x3)unity_WorldToObject, dW);
            v.tangent = float4(mul((float3x3)unity_WorldToObject, rW), -1);
            o.atlas = float3((frame + v.texcoord.xy * 0.5 + 0.5) / _Frames, v.texcoord1.w);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            #if defined(UNITY_PASS_SHADOWCASTER)
                // A smaller mip for the outline: one shadow-map texel covers many leaves, and the
                // softer outline casts a solid crown shadow rather than speckle.
                fixed4 c = UNITY_SAMPLE_TEX2DARRAY_LOD(_Atlas, IN.atlas, 3);
            #else
                fixed4 c = UNITY_SAMPLE_TEX2DARRAY(_Atlas, IN.atlas);
            #endif
            o.Albedo = c.rgb * IN.color.rgb;
            o.Alpha = c.a;
            o.Normal = normalize(UNITY_SAMPLE_TEX2DARRAY(_Normals, IN.atlas).xyz * 2 - 1);
        }
        ENDCG
    }
}
