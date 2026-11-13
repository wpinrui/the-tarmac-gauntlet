// The rain in one slab of air round the camera (RainSheets.cs), drawn on an open cylinder round it: thin streaks, fixed in
// the world and falling through it. Each cell of the sheet holds one streak as often as the rain is heavy. A streak is a
// raindrop's real width, or a pixel wide and as much fainter where that is narrower, so far rain thins to a haze on a wide
// shot and resolves into streaks through a long lens. Where the cells are smaller than a pixel the sheet draws their
// average, so it never shimmers. No texture is read.
Shader "Hidden/TarmacGauntlet/RainSheet"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Share ("Share of cells holding a drop", Float) = 1
        _Cell ("Cell width and height, m", Vector) = (0.1, 1.4, 0, 0)
        _Fall ("How far the rain has fallen, m", Float) = 0
        _Slant ("Sideways drift per metre fallen, x and z", Vector) = (0, 0, 0, 0)
        _Streak ("Streak width and length, m", Vector) = (0.0035, 0.45, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Share;
                float4 _Cell;
                float _Fall;
                float4 _Slant;
                float4 _Streak;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fog : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            uint RainHash(uint x)
            {
                x ^= x >> 16; x *= 0x7feb352du;
                x ^= x >> 15; x *= 0x846ca68bu;
                x ^= x >> 16;
                return x;
            }

            float RainRandom(uint x) { return (RainHash(x) & 0xffffffu) / 16777216.0; }

            // The streaks on one side of the sheet: u runs across it in the world, y up it.
            float Streaks(float u, float y, float slant, uint seed)
            {
                float width = _Streak.x, len = _Streak.y, cw = _Cell.x, ch = _Cell.y;
                // Along a drop's path u + slant * y holds, and y + fall holds while it falls.
                float a = u + slant * y;
                float b = y + _Fall;
                float pu = max(fwidth(a), 1e-6), pv = max(fwidth(b), 1e-6);
                float column = floor(a / cw);
                uint col = (uint)(int)column + seed * 0x9e3779b9u;
                // Each column's rows start at their own height, so the streaks don't line up.
                float bv = b / ch + RainRandom(col);
                float row = floor(bv);
                uint cell = RainHash(col ^ RainHash(((uint)(int)row & 4095u) + 0x51ed27u));
                float here = RainRandom(cell) < _Share ? 1.0 : 0.0;
                float centre = 0.15 + 0.7 * RainRandom(cell + 1u);
                float start = RainRandom(cell + 2u) * (ch - len);
                float across = (frac(a / cw) - centre) * cw;
                float along = (frac(bv) * ch - start) / len;
                // A pixel wide at least, and as much fainter.
                float reach = max(width, pu);
                float thread = width / reach * saturate(1.0 - abs(across) / reach);
                float sharp = here * thread * saturate(1.0 - abs(along * 2.0 - 1.0));
                // Each cell's streak covers width * length / 2 of it on average.
                float mean = _Share * width * len * 0.5 / (cw * ch);
                float blur = saturate(max(pu / cw, 0.5 * pv / len) * 2.0 - 1.0);
                return lerp(sharp, mean, blur);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 p = i.positionWS;
                float2 out2 = normalize(p.xz - _WorldSpaceCameraPos.xz);
                // Sides facing along z run along x, sides facing along x along z, blended round the corners.
                float2 w = out2 * out2;
                w *= w;
                w /= w.x + w.y;
                // Both sides every pixel: the streaks take their pixel size from screen derivatives, which need it.
                float rain = w.y * Streaks(p.x, p.y, _Slant.x, 0u) + w.x * Streaks(p.z, p.y, _Slant.y, 1u);
                half4 col = _Tint;
                col.a *= rain * ComputeFogIntensity(i.fog);
                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
