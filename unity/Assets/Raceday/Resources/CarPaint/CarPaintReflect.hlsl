// The paint's TarmacReflect pass, for CarPaint and Livery: the screen-space reflections take the clear coat's
// environment reflection (TarmacReflect.hlsl). Without the coat the paint takes none, as its rougher base coat blurs
// what it reflects.
#ifndef TARMAC_CAR_PAINT_REFLECT_INCLUDED
#define TARMAC_CAR_PAINT_REFLECT_INCLUDED

#include "Assets/Raceday/Resources/CarPaint/CarPaint.hlsl"
#include "Assets/Rendering/Shaders/TarmacReflect.hlsl"

struct CarReflectAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
};

struct CarReflectVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    half fog : TEXCOORD2;
};

CarReflectVaryings CarReflectVertex(CarReflectAttributes v)
{
    CarReflectVaryings o;
    VertexPositionInputs w = GetVertexPositionInputs(v.positionOS.xyz);
    o.positionCS = w.positionCS;
    o.positionWS = w.positionWS;
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    o.fog = ComputeFogFactor(w.positionCS.z);
    return o;
}

// What a clear coat of this much (0 to 1) reflects, as ShadeCarPaint draws it.
TarmacReflectOutput CarCoatReflect(float3 positionWS, half3 normalWS, float4 positionCS, half fog, half coat)
{
#if defined(_CLEARCOAT)
    half3 n = NormalizeNormalPerPixel(normalWS);
    half smooth = FilteredSmoothness(CAR_COAT_SMOOTHNESS, n);
    half NoV = saturate(dot(n, GetWorldSpaceNormalizeViewDir(positionWS)));
    half roughness = PerceptualSmoothnessToPerceptualRoughness(smooth);
    // All the coat reflects of its probe, URP's share and the sheen ShadeCarPaint adds.
    return TarmacReflect(positionWS, n, positionCS, fog, roughness, CarCoatEnvWeight(NoV, roughness) * coat);
#else
    return TarmacReflectNone();
#endif
}

TarmacReflectOutput CarReflectFragment(CarReflectVaryings i)
{
    return CarCoatReflect(i.positionWS, i.normalWS, i.positionCS, i.fog, 1);
}

#endif
