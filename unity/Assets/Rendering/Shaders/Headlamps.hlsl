// The light the cars' headlamps throw on the ground ahead at night, worked out in the ground's own shading (TarmacLit's
// ShadePbr and ShadeDielectric, in shaders that define TARMAC_HEADLAMPS: the track, the ground and the grass). Lighting
// the surface itself, it takes the surface's own colour: the white lines and the kerbs catch it far further out than the
// asphalt, and the fog covers it as it covers the rest.
//
// Each car's lamps are one source at the middle of its nose (_TgHeadlampAt; in w its height over the car's ground) aimed
// along the car (_TgHeadlampAim; in w how far ahead it reaches). On flat ground it gives half its light at the nose,
// all of it from about 9 m ahead, then fades smoothly from 15 m to nothing at its reach: a road lit clearly out to about
// 60 m. Across, it is a beam down the road: brightest on the car's line, its core as wide as a track from about 12 m on,
// so the verges at the track's edge take under a third of the middle's light and none 5 m further out. Nothing above the lamps is lit, so it
// stays on the ground over a crest. A slope that faces the car takes more of it, one that turns away less.
// tools/night-lights/headlamps.py draws the same light offline.
#ifndef TARMAC_HEADLAMPS_INCLUDED
#define TARMAC_HEADLAMPS_INCLUDED

#define TG_MOST_HEADLAMPS 24

float4 _TgHeadlamps;                            // how many cars' lamps, and their light
float4 _TgHeadlampAt[TG_MOST_HEADLAMPS];
float4 _TgHeadlampAim[TG_MOST_HEADLAMPS];

// The light falling on a surface here with this normal, per unit of its colour.
half3 HeadlampLight(float3 positionWS, half3 normalWS)
{
    int count = (int)_TgHeadlamps.x;
    float total = 0.0;
    [loop]
    for (int k = 0; k < count; k++)
    {
        float3 v = positionWS - _TgHeadlampAt[k].xyz;
        float3 fwd = _TgHeadlampAim[k].xyz;
        float reach = _TgHeadlampAim[k].w, h = _TgHeadlampAt[k].w;
        float z = dot(v, fwd);
        if (z <= 0.2 || z >= reach) continue;
        float3 up = normalize(float3(0, 1, 0) - fwd * fwd.y);
        float x = dot(v, cross(up, fwd)), y = dot(v, up);
        float d = max(length(v), 1e-3);
        float along = (0.5 + 0.5 * smoothstep(0.5, 9.0, z)) * (1.0 - smoothstep(15.0, reach, z));
        float u = abs(x) / min(2.0 + 0.35 * z, 6.0);
        float side = exp(-1.2 * u * u);
        float cut = (1.0 - smoothstep(0.0, 0.06, y / z)) * smoothstep(-h - 4.0, -h - 2.5, y);
        float facing = min(saturate(dot(normalWS, -v / d)) / max(h / d, 0.01), 1.5);
        total += along * side * cut * facing;
    }
    return _TgHeadlamps.yzw * total;
}

#endif
