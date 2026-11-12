// The grandstand crowd: each fan is one card (Grandstands.cs, CrowdCard) standing on its tread and facing the track. The
// card turns about the vertical towards the camera, as far as 60 degrees either way, and leans back a little for a camera
// above it. On it a seated fan is drawn from rounded shapes in metres: shoes and shins, lap, shirt, arms, neck, head and
// hair, each coloured from the crowd palette and a little darker at its edges so it reads as rounded. Now and then a fan
// throws their arms up and bounces for a few seconds. Lit by the sun with its shadows and the ambient light, fogged, and
// glowing at night under the roofs' floodlights (_Glow). Two triangles a fan, so a full stand costs next to nothing.
// The fans move with no transform of their own, so the stand always draws its motion vectors: each corner where it is
// now against where it was last frame, turned to where the camera was then (CameraCuts, _TgPrevCameraPos).
//
// Per vertex: position is the card's corner as built, facing the track; normal the way to the track; uv0 packs the
// corner and the fan's look (CrowdCard).
Shader "Hidden/TarmacGauntlet/Crowd"
{
    Properties
    {
        _Palette ("Palette", 2D) = "grey" {}
        _Glow ("Night glow", Color) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "DisableBatching" = "True" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" "AlwaysRenderMotionVectors" = "true" }
        Cull Off

        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_Palette);
        SAMPLER(sampler_Palette);

        CBUFFER_START(UnityPerMaterial)
            float4 _Glow;
        CBUFFER_END

        // CrowdCard's sizes, m, and the palette's.
        #define CARD_WIDTH 0.62
        #define CARD_HEIGHT 1.55
        #define SEAT 0.42
        #define HEIGHTS 16.0
        #define PALETTE_WIDTH 8.0
        #define PALETTE_HEIGHT 4.0
        // Furthest the card turns from facing the track, and leans back, radians.
        #define MOST_TURN 1.05
        #define MOST_LEAN 0.5

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
        };

        // One fan's card, unpacked and turned to the camera.
        struct Card
        {
            float3 positionWS;
            float3 facing;     // the way the card faces now
            float2 local;      // this corner on the fan, m: across from the middle, up from the tread
            float4 look;       // shirt, skin, trousers, hair: palette columns
            float tall;        // sitting height of the body, m
            float raise;       // 0 arms down, 1 arms up
        };

        float Hash(float2 p)
        {
            return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
        }

        // The card at a time, s, turned to a camera at eye.
        Card UnpackAt(Attributes v, float time, float3 eye)
        {
            float kx = floor(v.uv.x * 0.5 + 0.01), ky = floor(v.uv.y * 0.5 + 0.01);
            float cx = v.uv.x - 2.0 * kx, cy = v.uv.y - 2.0 * ky;
            float legs = floor(kx / 8.0 + 0.01), shirt = kx - 8.0 * legs;
            float rest = floor(ky / 8.0 + 0.01), skin = ky - 8.0 * rest;
            float seed = floor(rest / HEIGHTS + 0.01), height = rest - HEIGHTS * seed;

            float3 up = float3(0, 1, 0);
            float3 n = TransformObjectToWorldNormal(v.normalOS);
            n = normalize(float3(n.x, 0, n.z) + float3(1e-5, 0, 0));
            float3 right = cross(up, n);
            float3 corner = TransformObjectToWorld(v.positionOS.xyz);
            float3 foot = corner - right * ((cx - 0.5) * CARD_WIDTH) - up * (cy * CARD_HEIGHT);

            // Now and then, for a few seconds at a time, a fan throws their arms up and bounces.
            float phase = Hash(foot.xz);
            float t = time / 4.0 + phase * 7.0;
            float epoch = floor(t);
            float cheer = step(0.9, Hash(foot.xz + epoch));
            float within = t - epoch;
            float raise = cheer * smoothstep(0.0, 0.08, within) * (1.0 - smoothstep(0.85, 1.0, within));
            foot.y += raise * 0.04 * abs(sin((time + phase * 3.0) * 7.0));

            // Turn towards the camera about the vertical, and lean back for a camera above.
            float3 toEye = eye - foot;
            float turn = clamp(atan2(dot(toEye, right), dot(toEye, n)), -MOST_TURN, MOST_TURN);
            float3 f = n * cos(turn) + right * sin(turn);
            float3 r = cross(up, f);
            float across = length(toEye.xz);
            float lean = clamp(atan2(toEye.y - SEAT, max(across, 0.1)), 0.0, MOST_LEAN * 2.0) * 0.5;
            float3 u = up * cos(lean) - f * sin(lean);

            Card c;
            c.local = float2((cx - 0.5) * CARD_WIDTH, cy * CARD_HEIGHT);
            c.positionWS = foot + r * c.local.x + u * c.local.y;
            c.facing = f;
            c.look = float4(shirt, skin, legs, fmod(seed + phase * 6.0, 6.0));
            c.tall = 0.56 + 0.1 * (height + 0.5) / HEIGHTS;
            c.raise = raise;
            return c;
        }

        Card Unpack(Attributes v)
        {
            return UnpackAt(v, _Time.y, _WorldSpaceCameraPos);
        }

        float3 Paint(float row, float column)
        {
            float2 uv = float2((floor(column) + 0.5) / PALETTE_WIDTH, (row + 0.5) / PALETTE_HEIGHT);
            return SAMPLE_TEXTURE2D_LOD(_Palette, sampler_Palette, uv, 0).rgb;
        }

        float Capsule(float2 p, float2 a, float2 b, float r)
        {
            float2 pa = p - a, ba = b - a;
            float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
            return length(pa - ba * h) - r;
        }

        float RoundBox(float2 p, float2 centre, float2 size, float r)
        {
            float2 d = abs(p - centre) - size + r;
            return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - r;
        }

        float Ellipse(float2 p, float2 centre, float2 radii)
        {
            float2 q = (p - centre) / radii;
            return (length(q) - 1.0) * min(radii.x, radii.y);
        }

        // The fan at a point on the card, m: distance outside the figure (negative inside) and which part is nearest,
        // 0 shoes, 1 trousers, 2 shirt, 3 skin, 4 hair. The figure is mirrored, so x is taken as its size.
        float Figure(float2 p, float tall, float raise, out float part, out float inside)
        {
            float x = abs(p.x);
            float2 q = float2(x, p.y);
            float top = SEAT + tall;
            float d, best;

            best = Capsule(q, float2(0.085, 0.03), float2(0.09, 0.03), 0.045);
            part = 0;
            d = min(Capsule(q, float2(0.09, 0.07), float2(0.1, SEAT), 0.06), RoundBox(q, float2(0, SEAT + 0.02), float2(0.17, 0.07), 0.05));
            if (d < best) { best = d; part = 1; }
            float2 shoulder = float2(0.17, top - 0.07);
            float2 hand = lerp(float2(0.16, SEAT + 0.1), float2(0.24, top + 0.36), raise);
            float2 elbow = lerp(float2(0.22, SEAT + 0.24), float2(0.26, top + 0.1), raise);
            d = min(RoundBox(q, float2(0, SEAT + tall * 0.5 + 0.03), float2(0.18 - 0.02 * step(top - 0.1, p.y), tall * 0.5), 0.07),
                    min(Capsule(q, shoulder, elbow, 0.05), Capsule(q, elbow, hand, 0.045)));
            if (d < best) { best = d; part = 2; }
            float2 head = float2(0, top + 0.13);
            float headD = Ellipse(q, head, float2(0.09, 0.11));
            d = min(min(headD, RoundBox(q, float2(0, top + 0.01), float2(0.045, 0.04), 0.01)), Capsule(q, hand, hand, 0.042));
            if (d < best) { best = d; part = 3; }
            // Hair covers the top of the head and a little down its sides.
            if (headD < 0.0 && p.y > head.y + 0.015 - 0.05 * saturate(x / 0.09)) part = 4;
            inside = -best;
            return best;
        }

        // Clips the card to the fan, grown by half a pixel so a distant fan stays a dot rather than vanishing.
        float Clip(Card c, float2 local, out float part, out float inside)
        {
            float d = Figure(local, c.tall, c.raise, part, inside);
            float px = max(fwidth(local.y), fwidth(local.x));
            clip(0.5 * px - d);
            return d;
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
                float2 local : TEXCOORD2;
                nointerpolation float4 look : TEXCOORD3;
                nointerpolation float2 shape : TEXCOORD4;   // tall, raise
                half fog : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Card c = Unpack(v);
                Varyings o;
                o.positionWS = c.positionWS;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                // Lit as a rounded body: half towards the camera, half up.
                o.normalWS = normalize(c.facing + float3(0, 0.8, 0));
                o.local = c.local;
                o.look = c.look;
                o.shape = float2(c.tall, c.raise);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                Card c = (Card)0;
                c.tall = i.shape.x;
                c.raise = i.shape.y;
                float part, inside;
                Clip(c, i.local, part, inside);
                float3 albedo = part < 0.5 ? float3(0.1, 0.09, 0.09)
                    : part < 1.5 ? Paint(2, i.look.z)
                    : part < 2.5 ? Paint(0, i.look.x)
                    : part < 3.5 ? Paint(1, i.look.y)
                    : Paint(3, i.look.w);
                // Darker towards each part's edge, so it reads as round.
                albedo *= lerp(0.7, 1.0, saturate(inside / 0.04));
                half3 col = ShadeLambert(i.positionWS, i.normalWS, i.positionCS, i.fog, albedo);
                col += albedo * _Glow.rgb;
                return half4(col, 1);
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

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 local : TEXCOORD0;
                nointerpolation float2 shape : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Card c = Unpack(v);
                Varyings o;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                o.local = c.local;
                o.shape = float2(c.tall, c.raise);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                Card c = (Card)0;
                c.tall = i.shape.x;
                c.raise = i.shape.y;
                float part, inside;
                Clip(c, i.local, part, inside);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"

            float4 _TgPrevCameraPos;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 local : TEXCOORD0;
                nointerpolation float2 shape : TEXCOORD1;
                float4 nowCS : TEXCOORD2;
                float4 beforeCS : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Card c = Unpack(v);
                Card b = UnpackAt(v, _Time.y - unity_DeltaTime.x, _TgPrevCameraPos.xyz);
                Varyings o;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                o.local = c.local;
                o.shape = float2(c.tall, c.raise);
                o.nowCS = mul(_NonJitteredViewProjMatrix, float4(c.positionWS, 1));
                o.beforeCS = mul(_PrevViewProjMatrix, float4(b.positionWS, 1));
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                Card c = (Card)0;
                c.tall = i.shape.x;
                c.raise = i.shape.y;
                float part, inside;
                Clip(c, i.local, part, inside);
                return float4(CalcNdcMotionVectorFromCsPositions(i.nowCS, i.beforeCS), 0, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
