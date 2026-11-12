// The paved surface the Track shader draws, worked out once for its forward pass and its TarmacReflect pass, so the
// screen-space reflections see the same normal and smoothness the lighting did. Include after Track.shader's
// HLSLINCLUDE block and TarmacLit.hlsl, inside a pass that declares the _TRACK_* keywords.
#ifndef TARMAC_TRACK_SURFACE_INCLUDED
#define TARMAC_TRACK_SURFACE_INCLUDED

// The polish map (the patches' alpha) is read this many patch tiles to its own tile, so the road's sheen swells and
// fades over hundreds of metres along the lap.
#define TRACK_POLISH_TILES 6.0

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
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
    o.uv = v.uv;
    o.fog = ComputeFogFactor(p.positionCS.z);
    return o;
}

// A tangent-space normal turned into world space by the frame the screen-space derivatives of position
// and uv give, so the mesh needs no tangents (Schüler's cotangent frame).
float3 Perturb(float3 n, float3 p, float2 uv, float3 tangentNormal)
{
    float3 dp1 = ddx(p), dp2 = ddy(p);
    float2 duv1 = ddx(uv), duv2 = ddy(uv);
    float3 dp2perp = cross(dp2, n), dp1perp = cross(n, dp1);
    float3 t = dp2perp * duv1.x + dp1perp * duv2.x;
    float3 b = dp2perp * duv1.y + dp1perp * duv2.y;
    float scale = rsqrt(max(max(dot(t, t), dot(b, b)), 1e-20));
    return normalize(t * (scale * tangentNormal.x) + b * (scale * tangentNormal.y) + n * tangentNormal.z);
}

half Bell(float d, float width)
{
    return exp(-d * d / (2 * width * width));
}

// One layer of raindrop rings (Lagarde's rain ripples): each drop's ring spreads out from its centre over
// its cycle and fades as it goes. A slope across the surface, in world x and z. Sampled with the gradients
// given, as it is read inside a branch.
float2 Ripple(float2 at, float2 dx, float2 dy, float time, half weight)
{
    half4 r = SAMPLE_TEXTURE2D_GRAD(_TrackRippleTex, sampler_TrackRippleTex, at, dx, dy);
    half2 away = r.gb * 2 - 1;
    float cycle = frac(r.a + time);
    float wave = cycle - 1 + r.r;
    half fade = saturate(0.2 + weight * 0.8 - cycle);
    return away * (fade * r.r * sin(clamp(wave * 9, 0, 3) * PI));
}

// The surface at a pixel: its colour, its normal and its smoothness. The smoothness varies everywhere, wet or dry: the
// polish map swells and fades it along the lap, the racing line is polished a little shinier, its rubber is satin,
// fresh patches are matte and old ones worn smooth. In the wet the same variation decides where the film lasts.
void TrackSurface(Varyings i, out half3 albedo, out float3 normal, out half smoothness)
{
    float2 uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
    half4 grain = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
    albedo = grain.rgb * _BaseColor.rgb;
    smoothness = _Smoothness * lerp(0.7, 1.3, grain.a);
    float3 flat = normalize(i.normalWS);
    normal = flat;

    // Polish: two reads of the map, one turned against the other, broad swells of sheen with smaller ones in them.
    float2 big = i.positionWS.xz / (_MacroScale * TRACK_POLISH_TILES);
    half polish = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, big).a * 0.7 + SAMPLE_TEXTURE2D(_Macro, sampler_Macro, float2(big.y, -big.x) * 4.3 + 0.17).a * 0.3;
    smoothness *= lerp(0.75, 1.3, polish);

    #if _TRACK_DETAIL
        // Two scales of patches, one turned against the other, so neither repeat lines up.
        float2 w = i.positionWS.xz / _MacroScale;
        half broad = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, w).r;
        half fine = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, float2(w.y, -w.x) * 3.7 + 0.31).r;
        half patch = (broad * 0.65 + fine * 0.35) * 2 - 1;
        albedo *= 1 + patch * _MacroStrength;
        smoothness *= 1 - patch * _MacroStrength * 0.5;
        float3 t = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv).xyz * 2 - 1;
        t.xy *= _BumpScale;
        normal = Perturb(normal, i.positionWS, uv, normalize(t));

        // Repairs, 12 m across by 72 m along the road. Each repeat of the tile is shifted across and
        // mirrored by a hash of its number; its ends hold nothing, so no seam shows. Read with the
        // unshifted gradients, so the jump between repeats picks no wrong mip.
        float2 r = i.uv * _UvMetres.xy / float2(12, 72);
        float tile = floor(r.y);
        float2 hash = frac(float2(0.6180339, 0.7548776) * tile + float2(0.13, 0.41));
        float2 inTile = float2(r.x + hash.x, frac(r.y));
        inTile = lerp(inTile, float2(-inTile.x, 1 - inTile.y), step(0.5, hash.y));
        half3 repair = SAMPLE_TEXTURE2D_GRAD(_Repairs, sampler_Repairs, inTile, ddx(r), ddy(r)).rgb;
        // Relaid tarmac: mostly newer and darker than the road round it. Fresh, its open surface is matte; old,
        // the traffic has worn it smoother than the road.
        half relaid = repair.g * _RepairAmount;
        albedo *= lerp(1, lerp(0.76, 1.1, repair.b), relaid);
        smoothness *= lerp(1, lerp(0.65, 1.25, repair.b), relaid);
        // Sealant: near-black bitumen, glossy and smooth over the grain.
        half seal = repair.r * _RepairAmount;
        albedo = lerp(albedo, half3(0.03, 0.03, 0.033), seal * 0.75);
        smoothness = lerp(smoothness, 0.5, seal);
        normal = normalize(lerp(normal, flat, seal * 0.8));
    #endif

    #if _TRACK_RUBBER || _TRACK_WET
        float2 m = i.uv * _UvMetres.xy;
        float lap = m.y / _LineLength;
        float2 onLine = SAMPLE_TEXTURE2D_LOD(_Line, sampler_Line, float2(lap, 0.5), 0).rg;
        float d = m.x - onLine.r;
        // A broad band where the field's lines spread, darkest under the tyres of a car on the line.
        half band = Bell(d, 1.1) * 0.5 + (Bell(d - 0.8, 0.24) + Bell(d + 0.8, 0.24)) * 0.5;
    #endif

    #if _TRACK_RUBBER
        // The tyres polish the stone along the line, more where more cars run.
        smoothness *= 1 + Bell(d, 1.6) * onLine.g * 0.3;
        // Streaks along the line, long in the direction of travel.
        half streak = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, float2(m.x * 0.45, m.y * 0.012)).g;
        half rubber = saturate(band * onLine.g * (0.5 + streak * 1.1));
        // Marks about the line, 8 m across it by 160 m along the lap, fewest where it holds least rubber.
        half marks = SAMPLE_TEXTURE2D(_Marks, sampler_Marks, float2(d / 8 + 0.5, m.y / 160)).r * saturate(onLine.g * 1.6 - 0.45);
        albedo *= (1 - rubber * 0.7) * (1 - marks * 0.7);
        // Laid rubber is satin: a soft sheen, neither the stone's glint nor matte.
        smoothness = lerp(smoothness, 0.4, saturate(rubber * 0.8 + marks * 0.7));
    #endif

    #if _TRACK_WET
    {
        // How worn smooth the surface is here, 0 to 1: a smooth surface sheds water slowest and stays glossy
        // longest, an open one soaks it up and dries first.
        half worn = saturate(smoothness / max(_Smoothness, 0.01) - 0.6);
        // The water here: the film, thinner on the line the cars sweep (a road with a line map only), and
        // more or less of it with the surface's dips and the stone's peaks.
        half dips = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, i.positionWS.xz / 13.0).b;
        half onRoad = saturate(onLine.g * 3);
        float water = _TrackWater * (1 - saturate(band * 1.25) * onRoad * _TrackLineDry);
        float held = water * (0.75 + 0.5 * dips) * lerp(1.15, 0.85, grain.a) * lerp(0.75, 1.25, worn);
        // Dark as soon as it is damp; shiny once a film covers the grain.
        half damp = saturate(held / 0.06);
        half film = smoothstep(0.04, 0.35, held);

        // Puddles: in a dip along the road, or at the edge the camber drains to, where the surface dips too.
        half puddle = 0;
        UNITY_BRANCH
        if (_Puddles * _TrackPuddlesOn > 0.5 && water > 0.12)
        {
            float3 pools = SAMPLE_TEXTURE2D_LOD(_Pools, sampler_Pools, float2(lap, 0.5), 0).rgb;
            float right = max(pools.b - m.x, 0), left = max(m.x, 0);
            half edge = exp(-right / 1.3) * saturate(0.5 + pools.g) + exp(-left / 1.3) * saturate(0.5 - pools.g);
            half ready = saturate(max(pools.r, edge * 0.9) * (dips * 1.7 - 0.25));
            // Ready ground stands water from a fifth of a millimetre; open road only near flooding.
            float stands = lerp(2.2, 0.2, ready);
            puddle = smoothstep(0, 0.3, water - stands);
        }

        albedo *= lerp(1, 1 - 0.55 * _Porosity, damp) * lerp(1, 0.8, puddle);
        // The film is glossiest where the surface under it is smoothest.
        smoothness = lerp(smoothness, lerp(0.8, 0.88, _Porosity) + (worn - 0.5) * 0.06, film);
        smoothness = lerp(smoothness, 0.96, puddle);
        // Water fills the grain: a film keeps some of its bumps, a puddle lies flat.
        normal = normalize(lerp(normal, flat, film * 0.55 + puddle * 0.45));

        // Raindrops ring the water: plain in a puddle, faint on the film, gone in the distance.
        float2 at = i.positionWS.xz * _TrackRippleTiles;
        float2 atX = ddx(at), atY = ddy(at);
        half rippling = _TrackRain * _TrackRipplesOn * (puddle + film * 0.3) * saturate(2 - distance(i.positionWS, _WorldSpaceCameraPos) / 25);
        UNITY_BRANCH
        if (rippling > 0.01)
        {
            float time = _Time.y * 1.3;
            float2 slope = Ripple(at, atX, atY, time, _TrackRain)
                + Ripple(at * 0.83 + float2(0.37, 0.61), atX * 0.83, atY * 0.83, time * 0.91 + 0.5, saturate(_TrackRain * 1.6 - 0.6));
            normal = normalize(normal + float3(slope.x, 0, slope.y) * (rippling * 0.6));
        }
    }
    #endif
    smoothness = saturate(smoothness);
}

#endif
