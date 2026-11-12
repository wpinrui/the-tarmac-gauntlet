// What the grass, the near 3D trees and the tree impostors share (Foliage.cs sets the globals each frame): the wind
// they sway in, and how a near 3D tree hands over to its impostor further out. The two are cross-faded with a screen
// dither: at each pixel one of the two draws, the 3D tree where the noise is below its share and the impostor where it
// is above, so the pair always covers what one tree would and nothing is blended.
#ifndef FOLIAGE_INCLUDED
#define FOLIAGE_INCLUDED

// xy the way the wind blows across the ground (unit, world xz), z its strength (1 a breeze), w the time in seconds.
float4 _FoliageWind;
// xyz where the near trees are measured from: the race camera, the same for every view, so a reflection or shadow
// shows the same trees as the screen.
float4 _NearTreesAt;
// x where an impostor starts to show, m from _NearTreesAt to the tree's middle; y 1 / the width of the cross-fade;
// z 1 while near 3D trees are drawn, 0 when there are none and every impostor shows whole; w the frame, 0 to 63.
float4 _NearTreeFade;

// Interleaved gradient noise: an even spread of thresholds from 0 to 1 over neighbouring pixels. It moves on each frame,
// so STP's history (on at every level) averages the two sides of a hand-over into a smooth blend.
float FoliageNoise(float2 pixel)
{
    pixel = floor(pixel) + 5.588238 * _NearTreeFade.w;
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// How much of an impostor shows, from 0 (its 3D tree stands there instead) to 1 (all of it), by its middle.
float ImpostorShare(float3 centre)
{
    if (_NearTreeFade.z == 0) return 1;
    return saturate((distance(centre, _NearTreesAt.xyz) - _NearTreeFade.x) * _NearTreeFade.y);
}

// Keeps a pixel of something that shows a share of the time: a share s above 0 draws where the noise is below s, one
// below 0 where the noise is above 1 + s. So s and -(1 - s) split every pixel between them.
void FoliageFade(float share, float2 pixel)
{
    float n = FoliageNoise(pixel);
    clip(share >= 0 ? share - n : n - (1 + share));
}

// The wind's push at a point, m, for something that sways `sway` (0 rooted, 1 the most). A steady lean, a slow sway and
// gusts that roll across the ground with the wind.
float2 FoliageSway(float3 positionWS, float sway)
{
    float t = _FoliageWind.w, strength = _FoliageWind.z;
    float along = dot(positionWS.xz, _FoliageWind.xy);
    float gust = sin(along * 0.07 - t * 1.9) * 0.5 + 0.5;
    gust *= gust;
    float swing = sin(t * 1.7 + positionWS.x * 0.21 + positionWS.z * 0.17) * 0.35;
    return _FoliageWind.xy * (strength * sway * (0.35 + swing + gust * 0.9 * strength));
}

#endif
