// Where the race's clouds are, laid flat over the world at Atmosphere.CloudBase: the same two layers of noise and the same
// cover threshold as the sky shader's clouds. The cloud shadows on the ground and the volumetric clouds both read it, so
// the shadows fall under the clouds drawn. TimeOfDay sets the globals through Atmosphere every frame. CloudCover.cs is
// the same sum on the CPU.
#ifndef TG_CLOUD_COVER_INCLUDED
#define TG_CLOUD_COVER_INCLUDED

TEXTURE2D(_TgCloudTex);
SAMPLER(sampler_TgCloudTex);
// x the cloud map's tiles a metre, y the cover threshold (lower is more cloud), z how overcast it is, w how much
// light a cloud's shadow takes.
float4 _TgCloudMap;
// The two layers' drift, tiles: xy the big layer, zw the small one.
float4 _TgCloudOffset;

// Height the cloud map lies at, m: Atmosphere.CloudBase.
#define TG_CLOUD_BASE 1500.0

// The clouds' noise, 0 to 1, at a spot on the map: big soft masses, with finer detail that fades towards an overcast deck.
float TgCloudNoise(float2 uv, float o)
{
    float small = lerp(SAMPLE_TEXTURE2D_LOD(_TgCloudTex, sampler_TgCloudTex, uv * 2.3 + _TgCloudOffset.zw, 0).g, 0.5, 0.7 * o);
    return SAMPLE_TEXTURE2D_LOD(_TgCloudTex, sampler_TgCloudTex, uv + _TgCloudOffset.xy, 0).r * 0.7 + small * 0.3;
}

// How much cloud is over this spot, 0 to 1, with x and z in world metres.
float TgCloudCover(float2 xz)
{
    float o = _TgCloudMap.z;
    float n = TgCloudNoise(xz * _TgCloudMap.x, o);
    return smoothstep(_TgCloudMap.y - 0.2 * o, _TgCloudMap.y + 0.18 + 0.1 * o, n);
}

#endif
