// Blades of grass round the race camera (Grass.cs). The ground is drawn as ever (Ground.shader); the blades stand on it
// out to a few tens of metres, thinning and shortening into it with distance, so far off the grass is the ground's own
// colour and stripes, and close in it stands up as blades of that same colour.
//
// The blades come in square tiles, one mesh of 16 x 16 blades, and in rings round the camera: each ring's blades are
// twice as far apart and twice as wide as the last's. Over the last stretch of a ring three blades in four drop out, so
// it meets the next ring at that ring's density. Every tile is an instance: _Tiles holds its corner (world xz), the
// ring's tile size, and the ring plus a seed from 0 to 1, from _Start.
//
// Where a blade stands comes from a map drawn from above round the camera (GrassCapture.shader): the height of the top
// surface and whether it is grass. How tall it grows comes from the land's growth map (Landscape.Fields): short where
// the grass is mown, longer where it is left, none in the woods. Its colour is the ground's at its root, worked out
// as Ground.shader works it out: the fields' map, the grain and the mown stripes.
//
// Per vertex: uv0 the blade's root in the tile, 0 to 1; uv1 its side (-1 left, 1 right, 0 the tip), how far up it
// (0 to 1) and two random numbers.
Shader "Hidden/TarmacGauntlet/Grass"
{
    Properties
    {
        [MainTexture] _MainTex ("Fields", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Grain ("Grain", 2D) = "gray" {}
        _Mowing ("Mowing", 2D) = "black" {}
        _Growth ("Growth, 1 = 1 m", 2D) = "black" {}
        _Map ("Map's corner and 1 / size, world xz", Vector) = (0, 0, 1, 1)
        _StripeWidth ("Stripe width, m (LandscapePlan.StripeWidth)", Float) = 5
        _StripeContrast ("Stripe contrast", Range(0, 0.5)) = 0.15
        _Smoothness ("Smoothness", Range(0, 1)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" "DisableBatching" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #pragma target 4.5
        #pragma multi_compile_local _ _GROUND_STRIPES
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Foliage.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_Grain);
        SAMPLER(sampler_Grain);
        TEXTURE2D(_Mowing);
        SAMPLER(sampler_Mowing);
        TEXTURE2D(_Growth);
        SAMPLER(sampler_Growth);
        TEXTURE2D(_GrassCapture);
        SAMPLER(sampler_GrassCapture);
        StructuredBuffer<float4> _Tiles;

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _BaseColor;
            float4 _Grain_ST;
            float4 _Mowing_ST;
            float4 _Growth_ST;
            float4 _Map;
            float _StripeWidth;
            half _StripeContrast;
            half _Smoothness;
        CBUFFER_END
        // Per draw: the first tile's place in _Tiles.
        uint _Start;
        // The map from above: its middle (xz), 1 / its width, and whether it is drawn yet.
        float4 _GrassCaptureAt;
        // x where the blades start to thin out, y where the last of them have gone, m; z how wide the nearest ring's
        // blades are, m; w the camera's own blades thin out over the last this share of each ring.
        float4 _GrassReach;
        // The edge of each ring, m: the nearest ring's out to x, the next to y, the last to z; w the number of rings.
        float4 _GrassRings;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 root : TEXCOORD0;
            float4 blade : TEXCOORD1;
            uint instance : SV_InstanceID;
        };

        struct Blade
        {
            float3 positionWS;
            float3 normalWS;
            half3 albedo;
            bool alive;
        };

        // The ground's colour at a point, as Ground.shader draws it, with the width of a pixel there, m.
        half3 GroundAlbedo(float3 positionWS, float pixel)
        {
            float2 uv = (positionWS.xz - _Map.xy) * _Map.zw;
            half3 fields = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0).rgb;
            half grain = SAMPLE_TEXTURE2D_LOD(_Grain, sampler_Grain, positionWS.xz / 8, 0).r;
            half3 albedo = fields * grain * 2 * _BaseColor.rgb;
            #if defined(_GROUND_STRIPES)
                half4 mow = SAMPLE_TEXTURE2D_LOD(_Mowing, sampler_Mowing, uv, 0);
                float d = (mow.r + (grain - 0.5) * 0.3) / _StripeWidth;
                float across = pixel / _StripeWidth;
                float wave = clamp((abs(frac(d * 0.5) - 0.5) * 4 - 1) / max(across * 2, 0.08), -1, 1);
                half fresh = length(mow.ba);
                half fade = fresh * saturate(1.5 - across * 2.5) * saturate((mow.g - mow.r) / max(pixel, 0.05) + 0.5);
                half2 along = mow.ba / max(fresh, 1e-3);
                float2 view = positionWS.xz - _WorldSpaceCameraPos.xz;
                half lean = dot(view, along) / max(length(view), 1e-3);
                albedo *= 1 + _StripeContrast * wave * fade * (0.35 + 0.65 * lean);
            #endif
            return albedo;
        }

        // Which way the mown grass lies at a point: along the road, one way on one stripe and the other way on the next.
        float2 MownLean(float3 positionWS)
        {
            #if defined(_GROUND_STRIPES)
                float2 uv = (positionWS.xz - _Map.xy) * _Map.zw;
                half4 mow = SAMPLE_TEXTURE2D_LOD(_Mowing, sampler_Mowing, uv, 0);
                half fresh = length(mow.ba);
                if (fresh < 1e-3 || mow.r > mow.g) return 0;
                // The same stripes as GroundAlbedo's wave: where it is 1 the grass lies along the road's way, where -1 against.
                float stripe = abs(frac(mow.r / _StripeWidth * 0.5) - 0.5) * 4 - 1 >= 0 ? 1 : -1;
                return mow.ba * stripe;
            #else
                return 0;
            #endif
        }

        Blade Place(Attributes v)
        {
            Blade b = (Blade)0;
            float4 tile = _Tiles[_Start + v.instance];
            // Each tile mirrors and swaps its pattern by its seed, so the one mesh doesn't repeat in rows.
            float2 r = v.root;
            float ring = floor(tile.w), seed = frac(tile.w);
            if (frac(seed * 2) >= 0.5) r.x = 1 - r.x;
            if (frac(seed * 4) >= 0.5) r.y = 1 - r.y;
            if (frac(seed * 8) >= 0.5) r = r.yx;
            float size = tile.z;
            float3 root = float3(tile.x + r.x * size, 0, tile.y + r.y * size);

            float2 cuv = (root.xz - _GrassCaptureAt.xy) * _GrassCaptureAt.z + 0.5;
            float2 ground = SAMPLE_TEXTURE2D_LOD(_GrassCapture, sampler_GrassCapture, cuv, 0).rg;
            root.y = ground.x;
            float dist = distance(root, _WorldSpaceCameraPos);

            // The ring's reach.
            float inner = ring < 0.5 ? 0 : ring < 1.5 ? _GrassRings.x : _GrassRings.y;
            float outer = ring < 0.5 ? _GrassRings.x : ring < 1.5 ? _GrassRings.y : _GrassRings.z;
            bool last = ring > _GrassRings.w - 1.5;
            // Over the last stretch of a ring, three blades in four drop out to meet the next ring; past the last ring, all.
            float t = saturate((dist - outer * (1 - _GrassReach.w)) / (outer * _GrassReach.w));
            float keep = last ? 1 - t : lerp(1, 0.25, t);
            float growth = SAMPLE_TEXTURE2D_LOD(_Growth, sampler_Growth, (root.xz - _Map.xy) * _Map.zw, 0).r;
            b.alive = _GrassCaptureAt.w > 0 && all(abs(cuv - 0.5) < 0.5) && ground.y > 0.5 && dist >= inner && dist < outer
                && v.blade.z < keep && growth > 0.01;

            // Shorter towards the far edge, down to the ground itself.
            float shrink = saturate((_GrassReach.y - dist) / (_GrassReach.y - _GrassReach.x));
            float height = growth * (0.65 + 0.7 * v.blade.w) * shrink;
            // Blades left wider where their neighbours drop out, so the cover holds.
            float width = _GrassReach.z * exp2(ring) * (0.7 + 0.6 * frac(v.blade.z * 7.3)) / sqrt(max(keep, 0.25));

            float yaw = (v.blade.z * 13.7 + seed * 31.1) * 6.2831853;
            float2 face = float2(cos(yaw), sin(yaw));
            float2 side = float2(-face.y, face.x);
            float up = v.blade.y;
            // The blade curves over as it rises: towards the way the mower laid it, and with the wind.
            float2 lean = (MownLean(root) * 0.45 + face * 0.25) * height + FoliageSway(root, 1) * height * 0.4;
            float3 p = root;
            p.xz += side * (v.blade.x * width * 0.5 * (1 - up * up));
            p.xz += lean * up * up;
            p.y += height * up * (1 - 0.25 * up * dot(lean, lean) / max(height * height, 1e-4));
            b.positionWS = p;

            // Lit mostly as the ground it stands on, a little as the blade, and wholly as the ground where it has shrunk away.
            float3 bladeNormal = normalize(float3(face.x, 0.4, face.y));
            b.normalWS = normalize(lerp(float3(0, 1, 0), bladeNormal, 0.35 * shrink));

            float pixel = dist * 0.0012;
            half3 albedo = GroundAlbedo(root, pixel);
            // Darker at the root, lighter at the tip, about the ground's own colour on average; each blade its own shade,
            // all of it going to the ground's colour as the blade shrinks away.
            half vary = (0.78 + 0.44 * up) * (0.85 + 0.3 * frac(v.blade.w * 5.1));
            b.albedo = albedo * lerp(1, vary, shrink);
            if (!b.alive) b.positionWS = float3(0, -1e5, 0);
            return b;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Assets/Rendering/Shaders/LitKeywords.hlsl"
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half3 albedo : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Blade b = Place(v);
                Varyings o;
                o.positionCS = TransformWorldToHClip(b.positionWS);
                o.positionWS = b.positionWS;
                o.normalWS = b.normalWS;
                o.albedo = b.albedo;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return ShadePbr(i.positionWS, normalize(i.normalWS), i.positionCS, i.fog, i.albedo, 0, _Smoothness);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 vert(Attributes v) : SV_POSITION
            {
                return TransformWorldToHClip(Place(v).positionWS);
            }

            half4 frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
