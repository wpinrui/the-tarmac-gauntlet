// The motion vectors pass for the game's own opaque shaders that move no vertices of their own: how far each pixel moved
// since the last frame, for the temporal upscaler and the motion blur. A skinned mesh brings last frame's positions with it.
// Use with #pragma vertex TarmacMotionVertex and #pragma fragment TarmacMotionFragment.
#ifndef TARMAC_MOTION_INCLUDED
#define TARMAC_MOTION_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"

struct TarmacMotionAttributes
{
    float4 positionOS : POSITION;
    float3 positionOld : TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct TarmacMotionVaryings
{
    float4 positionCS : SV_POSITION;
    float4 nowCS : TEXCOORD0;
    float4 beforeCS : TEXCOORD1;
};

TarmacMotionVaryings TarmacMotionVertex(TarmacMotionAttributes v)
{
    UNITY_SETUP_INSTANCE_ID(v);
    TarmacMotionVaryings o;
    float4 now = float4(v.positionOS.xyz, 1);
    o.positionCS = TransformObjectToHClip(now.xyz);
    o.nowCS = mul(_NonJitteredViewProjMatrix, mul(UNITY_MATRIX_M, now));
    // unity_MotionVectorsParams.x is 1 when the mesh carries last frame's positions.
    float4 before = unity_MotionVectorsParams.x == 1 ? float4(v.positionOld, 1) : now;
    o.beforeCS = mul(_PrevViewProjMatrix, mul(UNITY_PREV_MATRIX_M, before));
    return o;
}

float4 TarmacMotionFragment(TarmacMotionVaryings i) : SV_Target
{
    return float4(CalcNdcMotionVectorFromCsPositions(i.nowCS, i.beforeCS), 0, 0);
}

#endif
