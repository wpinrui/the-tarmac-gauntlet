// The cars' headlamps at night, the only way they light anything: every lit shader adds their light, through NightLit
// (NightLit.hlsl), so the road, the grass, the trees, the crowd and the cars all take it in their own colour. There are
// no real lights for the headlamps.
//
// It is bounded by construction, so no number of cars, no distance and no surface can white out the screen:
// - Each car's lamps are one source at the middle of its nose (_TgHeadlampAt; in w its height over the car's ground),
//   aimed along the car (_TgHeadlampAim; in w the share of its light it throws: less under the floodlights). Nothing
//   behind the nose is lit, so a car never lights its own body.
// - On the ground (a surface facing up, near the car's ground) a beam gives half its light at the nose and all of it from
//   about 9 m ahead, fading from 15 m to nothing at its reach (_TgHeadlampReach): a road lit out to about 60 m. Across
//   it is a beam down the road, its core as wide as a track from about 12 m on.
// - Anything else (a car ahead, a wall, a tree) takes the light falling on it face on, which falls with the square of the
//   distance only beyond TG_HEADLAMP_NEAREST: nearer than that it stays at the level there, twice the level at 15 m, so
//   a wall or a car right at the nose is lit no more than one 10 m off.
// - The beams of all the cars add up but saturate: however many light one spot, it takes at most TG_HEADLAMP_MOST times
//   one car's peak there.
// - What a surface gives back is then capped softly by NightLit, whatever its colour: a white line or a white car close in
//   front is lit, never blown out.
// tools/night-lights/Check.cs sweeps this same function over every distance, angle, surface, number of cars and hour,
// and tools/night-lights/headlamps.py draws it offline. Keep the three in step.
#ifndef TARMAC_HEADLAMPS_INCLUDED
#define TARMAC_HEADLAMPS_INCLUDED

#define TG_MOST_HEADLAMPS 24
// How quickly the cars' summed light saturates: 1 - exp(-k) of it at one car's peak.
#define TG_HEADLAMP_SATURATE 1.466
// The most the summed light of any number of cars reaches, against one car's peak: 1 / (1 - exp(-TG_HEADLAMP_SATURATE)).
#define TG_HEADLAMP_MOST 1.3
// Ground: what flat ground takes at a beam's brightest, per unit of the headlamps' light.
#define TG_HEADLAMP_GROUND 1.0
// Anything else, face on: the most it takes, per unit of the headlamps' light, which it reaches at TG_HEADLAMP_NEAREST m
// and nearer; at 15 m it takes half as much, a white car there lit to about a tenth of white.
#define TG_HEADLAMP_FACE 0.0428
#define TG_HEADLAMP_NEAREST 10.6
// A surface counts as ground when it faces up this much, and lies no higher than this over the car's ground, m.
#define TG_HEADLAMP_GROUND_UP_FROM 0.5
#define TG_HEADLAMP_GROUND_UP_TO 0.9
#define TG_HEADLAMP_GROUND_HIGH_FROM 0.3
#define TG_HEADLAMP_GROUND_HIGH_TO 0.8

float4 _TgHeadlamps;                            // how many cars' lamps, and their light
float4 _TgHeadlampAt[TG_MOST_HEADLAMPS];
float4 _TgHeadlampAim[TG_MOST_HEADLAMPS];      // xyz the car's heading, w the share of its light it throws
float _TgHeadlampReach;                         // how far ahead the lamps reach, m

// One car's beam at this point: its shape ahead and across, and what a surface with this normal takes of it, as ground
// (in x) and face on (in y). Both are at most 1 at the beam's brightest.
float2 HeadlampBeam(float3 v, float3 fwd, float h, float reach, float3 n)
{
    float z = dot(v, fwd);
    if (z <= 0.2 || z >= reach) return 0.0;
    float3 up = normalize(float3(0, 1, 0) - fwd * fwd.y);
    float x = dot(v, cross(up, fwd)), y = dot(v, up);
    float d = max(length(v), 1e-3);
    float3 toLamp = -v / d;
    float u = abs(x) / min(2.0 + 0.35 * z, 6.0);
    float side = exp(-1.2 * u * u);
    float fade = 1.0 - smoothstep(15.0, reach, z);
    // Low beams: little above the lamps, and nothing far below the car's ground.
    float cut = (1.0 - smoothstep(0.05, 0.3, y / z)) * smoothstep(-h - 4.0, -h - 2.5, y);
    float facing = saturate(dot(n, toLamp));
    float along = 0.5 + 0.5 * smoothstep(0.5, 9.0, z);
    float ground = along * min(facing / max(h / d, 0.01), 1.0);
    float near = TG_HEADLAMP_NEAREST * TG_HEADLAMP_NEAREST / max(d * d, TG_HEADLAMP_NEAREST * TG_HEADLAMP_NEAREST);
    float face = facing * near;
    return float2(ground, face) * (side * fade * cut);
}

// How far the summed light s of the cars' beams has saturated, where 1 is one car's peak.
float HeadlampSaturate(float s)
{
    return (1.0 - exp(-TG_HEADLAMP_SATURATE * s)) / (1.0 - exp(-TG_HEADLAMP_SATURATE));
}

// The headlamps' light falling on a surface here with this normal, per unit of its colour, before NightLit's cap. A car
// (ground 0) is never ground: its bonnet faces up and lies low, but takes the light face on.
half3 HeadlampLight(float3 positionWS, half3 normalWS, half ground)
{
    int count = (int)_TgHeadlamps.x;
    if (count <= 0) return 0;
    float3 n = normalize(normalWS);
    // Ground faces up and lies low; a car's roof faces up too but stands high, and takes the light face on.
    float up = smoothstep(TG_HEADLAMP_GROUND_UP_FROM, TG_HEADLAMP_GROUND_UP_TO, n.y) * ground;
    float2 total = 0.0;
    [loop]
    for (int k = 0; k < count; k++)
    {
        float3 v = positionWS - _TgHeadlampAt[k].xyz;
        float h = _TgHeadlampAt[k].w;
        float2 beam = HeadlampBeam(v, _TgHeadlampAim[k].xyz, h, _TgHeadlampReach, n) * _TgHeadlampAim[k].w;
        float g = up * (1.0 - smoothstep(TG_HEADLAMP_GROUND_HIGH_FROM, TG_HEADLAMP_GROUND_HIGH_TO, v.y + h));
        total += float2(beam.x * g, beam.y * (1.0 - g));
    }
    float e = TG_HEADLAMP_GROUND * HeadlampSaturate(total.x) + TG_HEADLAMP_FACE * HeadlampSaturate(total.y);
    return _TgHeadlamps.yzw * e;
}

#endif
