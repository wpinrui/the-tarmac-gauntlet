// The circuit's own lights at night, the one way they light any surface: the floodlights (their map, FloodField) and
// the cars' headlamps (Headlamps.hlsl). Every lit shader adds NightLit to its colour before the fog (TarmacLit's
// ShadePbr, ShadeDielectric and ShadeLambert, and CarPaint's ShadeCarPaint), so everything takes the light in its own
// colour, added to what the sky gives it: never multiplying it, so a daylit scene stays as it is.
//
// It is bounded by construction:
// - The floodlights' light on flat ground is read from the map, which never holds more than FloodField.Most anywhere,
//   however many lamps overlap. A surface facing the lamps takes at most TG_FLOOD_FACING times that; one facing away
//   takes TG_FLOOD_WRAP of it. It fades out from TG_FLOOD_HIGH_FROM to TG_FLOOD_HIGH_TO m over the ground.
// - The headlamps are bounded in Headlamps.hlsl.
// - What a surface gives back of the two together rises one for one up to half of _TgNightMost of linear light, then
//   eases towards _TgNightMost and never reaches it, whatever its colour.
// tools/night-lights/Check.cs sweeps this same function over every distance, angle, surface, number of cars and hour.
// Keep the two in step.
#ifndef TARMAC_NIGHT_LIT_INCLUDED
#define TARMAC_NIGHT_LIT_INCLUDED

#include "Assets/Rendering/Shaders/Headlamps.hlsl"

// A surface facing the floodlights takes at most this many times what flat ground takes; one facing away takes this share.
#define TG_FLOOD_FACING 2.0
#define TG_FLOOD_WRAP 0.2
// The floodlights' slant is taken as no flatter than this, for a surface facing them: the y of a unit vector.
#define TG_FLOOD_SLANT 0.3
// The floodlights light things up to this high over the ground, fading out over it, m.
#define TG_FLOOD_HIGH_FROM 8.0
#define TG_FLOOD_HIGH_TO 25.0

TEXTURE2D(_TgFloodField);                        // r the light on flat ground, g the ground's height, b and a the way to the lamps in x and z
float4 _TgFloodRect;                             // the map's corner in x and z, and 1 over its size in x and z, m
float4 _TgFloodLight;                            // the floodlights' light where the map holds 1; w 1 while the map is set
float _TgNightMost;                              // the most linear light the night's lamps give back from any surface

// The floodlights' light falling on a surface here with this normal, per unit of its colour, before the cap.
half3 FloodLight(float3 positionWS, half3 normalWS)
{
    if (_TgFloodLight.w < 0.5) return 0;
    float2 uv = (positionWS.xz - _TgFloodRect.xy) * _TgFloodRect.zw;
    if (any(uv < 0.0) || any(uv > 1.0)) return 0;
    float4 f = SAMPLE_TEXTURE2D_LOD(_TgFloodField, sampler_LinearClamp, uv, 0);
    float3 toward = float3(f.b, sqrt(saturate(1.0 - f.b * f.b - f.a * f.a)), f.a);
    float3 n = normalize(normalWS);
    float facing = min(saturate(dot(n, toward)) / max(toward.y, TG_FLOOD_SLANT), TG_FLOOD_FACING);
    float high = 1.0 - smoothstep(TG_FLOOD_HIGH_FROM, TG_FLOOD_HIGH_TO, positionWS.y - f.g);
    return _TgFloodLight.rgb * (saturate(f.r) * high * lerp(TG_FLOOD_WRAP, 1.0, facing));
}

// Rises one for one up to half of most, then eases towards most and never reaches it (FloodField.Knee).
half3 NightKnee(half3 x, half most)
{
    half k = most * 0.5;
    return min(x, k) + (most - k) * (1.0 - exp(-max(x - k, 0.0) / (most - k)));
}

// The light the circuit's lamps add to a surface of this colour, here, with this normal: linear light, before the fog,
// never more than _TgNightMost. floods 0 is for the cars: the floodlights' real lights light them instead, and their
// bodies never take the headlamps' light as ground.
half3 NightLit(float3 positionWS, half3 normalWS, half3 albedo, half floods)
{
    if (_TgHeadlamps.x < 0.5 && (_TgFloodLight.w < 0.5 || floods <= 0)) return 0;
    half3 light = HeadlampLight(positionWS, normalWS, floods > 0 ? 1.0 : 0.0);
    if (floods > 0) light += floods * FloodLight(positionWS, normalWS);
    return NightKnee(albedo * light, max(_TgNightMost, 1e-3));
}

#endif
