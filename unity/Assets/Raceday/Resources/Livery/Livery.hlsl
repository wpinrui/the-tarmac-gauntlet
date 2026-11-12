// The livery's projection, shared by the Livery shader's passes. The livery is flat art drawn on the
// car's five views (left, right, top, front, rear) and baked into one atlas by
// tools/liveries/common/bake.js, with an optional finish map in the same layout. Each paint face
// takes the view it faces most, blended where views meet; faces pointing down, and side faces
// turned in towards the middle, keep the base colour and finish. Include after CarPaint.hlsl.
// Keep in step with the projection in tools/liveries/common/viewer.py. See docs/car-paint.md.
#ifndef TARMAC_LIVERY_INCLUDED
#define TARMAC_LIVERY_INCLUDED

// Not in a UnityPerMaterial buffer: every part sets its own _ToCar, so the parts are drawn one by one anyway.
TEXTURE2D(_Atlas);
SAMPLER(sampler_Atlas);
TEXTURE2D(_Digits);
SAMPLER(sampler_Digits);
// The paint's finish, laid out as the atlas: red the share of _Smoothness, green the clear coat,
// blue the share of _Metallic and the flakes. White (the default) is the plain paint.
TEXTURE2D(_Finish);
SAMPLER(sampler_Finish);
half4 _Base;
// The finish where the base colour shows (faces pointing down, inner side faces): a vector, not a
// colour, so Unity never turns it linear.
half4 _FinishBase;
half _Smoothness, _Metallic;

// Atlas uv (origin top left) = (dot(A.xyz, p) + A.w, dot(B.xyz, p) + B.w), p in the
// model's OBJ axes: +X the car's left, +Y up, +Z the nose.
float4 _ALeft, _BLeft, _ARight, _BRight, _ATop, _BTop, _AFront, _BFront, _ARear, _BRear;

// Up to four number boxes: centre and height in atlas uv, then the turn in radians.
float4 _NumBox[4];
// Each box's ink, and the colour of the digits' outline where the digits have one.
float4 _NumInk[4];
float4 _NumEdge[4];
// Each box's width in atlas uv: a number too wide for its box is drawn smaller to fit.
float4 _NumWidth;
float _NumBoxes;
// The number's digits, most significant first, and how many there are (0: none drawn).
float4 _NumDigits;
// Digits atlas, as fractions of its width and height: a cell's width, the left padding, a
// digit's width and the top padding; then a digit's height. Digits share one advance,
// over their height.
float4 _DigitCell;
float _DigitHeight, _DigitAdvance;
// How far apart digits sit, over their height: their advance, or less for digits that overlap.
float _DigitStep;

// From the part's mesh space into the car's own space, metres, doors shut (set per part by CarLivery).
float4x4 _ToCar;

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 objPos : TEXCOORD0;
    float3 objNormal : TEXCOORD1;
    float3 positionWS : TEXCOORD2;
    half3 normalWS : TEXCOORD3;
    half fog : TEXCOORD4;
};

Varyings vert(Attributes v)
{
    Varyings o;
    VertexPositionInputs w = GetVertexPositionInputs(v.positionOS.xyz);
    o.positionCS = w.positionCS;
    o.positionWS = w.positionWS;
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    o.fog = ComputeFogFactor(w.positionCS.z);
    float3 p = mul(_ToCar, float4(v.positionOS.xyz, 1)).xyz;
    float3 n = mul((float3x3)_ToCar, v.normalOS);
    // Unity mirrors X on import; turn back to the OBJ's axes.
    o.objPos = float3(-p.x, p.y, p.z);
    o.objNormal = float3(-n.x, n.y, n.z);
    return o;
}

float2 AtlasUv(float4 a, float4 b, float3 p)
{
    return float2(dot(a.xyz, p) + a.w, dot(b.xyz, p) + b.w);
}

half3 Tap(float2 uv)
{
    return SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, float2(uv.x, 1 - uv.y)).rgb;
}

half3 TapFinish(float2 uv)
{
    return SAMPLE_TEXTURE2D(_Finish, sampler_Finish, float2(uv.x, 1 - uv.y)).rgb;
}

// The race number over the art at atlas uv q, in any box q falls in. Digits sit _DigitStep
// apart, which can be less than a digit's width: each digit is then laid over the one before.
half3 Number(float2 q, half3 art)
{
    float count = _NumDigits.w;
    float width = (count - 1) * _DigitStep + _DigitAdvance;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float4 box = _NumBox[i];
        float2 d = q - box.xy;
        float s = sin(box.w), c = cos(box.w);
        // Into the box's own frame, in digit heights: x right, y down, centred.
        float h = min(box.z, _NumWidth[i] / max(width, 1e-5));
        float2 l = float2(c * d.x + s * d.y, -s * d.x + c * d.y) / max(h, 1e-5);
        float x = l.x + width / 2;
        float on = step(abs(l.y), 0.5) * step(i + 0.5, _NumBoxes);
        // Gradients of the unbroken position, so the jump between digit cells keeps a sharp mip.
        float2 flow = float2(x / _DigitAdvance * _DigitCell.z, -l.y * _DigitHeight);
        float gy = _DigitCell.w + saturate(l.y + 0.5) * _DigitHeight;
        [unroll]
        for (int k = 0; k < 3; k++)
        {
            float digit = k == 0 ? _NumDigits.x : (k == 1 ? _NumDigits.y : _NumDigits.z);
            float gx = (x - k * _DigitStep) / _DigitAdvance;
            float inside = on * step(0, gx) * step(gx, 1) * step(k + 0.5, count);
            float2 g = float2(digit * _DigitCell.x + _DigitCell.y + saturate(gx) * _DigitCell.z, gy);
            // Digits are white, with any outline black: the face takes the ink, the outline the edge.
            half4 t = SAMPLE_TEXTURE2D_GRAD(_Digits, sampler_Digits, float2(g.x, 1 - g.y), ddx(flow), ddy(flow));
            art = lerp(art, lerp(_NumEdge[i].rgb, _NumInk[i].rgb, t.r), t.a * inside);
        }
    }
    return art;
}

// Each view's weight on a face with normal n, in the order left or right, top, bottom, front, rear.
// Side faces turned in towards the middle take the bottom's share instead: the side's art on them
// would show back to front.
void LiveryWeights(float3 p, float3 n, out float wSide, out float wTop, out float wBase, out float wFront, out float wRear)
{
    float3 k = pow(abs(n), float3(6, 6, 6));
    bool inward = n.x * p.x < 0;
    wSide = inward ? 0 : k.x;
    wTop = n.y > 0 ? k.y : 0;
    wBase = (n.y < 0 ? k.y : 0) + (inward ? k.x : 0);
    wFront = n.z > 0 ? k.z : 0;
    wRear = n.z < 0 ? k.z : 0;
}

// Which side's view by where the face is.
float2 SideUv(float3 p)
{
    return p.x > 0 ? AtlasUv(_ALeft, _BLeft, p) : AtlasUv(_ARight, _BRight, p);
}

// The livery's colour at p on a face with normal n, numbered.
half3 LiveryAlbedo(float3 p, float3 n)
{
    float wSide, wTop, wBase, wFront, wRear;
    LiveryWeights(p, n, wSide, wTop, wBase, wFront, wRear);
    float2 uvSide = SideUv(p), uvTop = AtlasUv(_ATop, _BTop, p), uvFront = AtlasUv(_AFront, _BFront, p), uvRear = AtlasUv(_ARear, _BRear, p);
    half3 col = (wSide > 0 ? Number(uvSide, Tap(uvSide)) * wSide : 0) + Number(uvTop, Tap(uvTop)) * wTop + _Base.rgb * wBase
        + Number(uvFront, Tap(uvFront)) * wFront + Number(uvRear, Tap(uvRear)) * wRear;
    return col / max(wSide + wTop + wBase + wFront + wRear, 1e-4);
}

// The livery's finish at p on a face with normal n: shares of smoothness, clear coat and metal.
half3 LiveryFinish(float3 p, float3 n)
{
    float wSide, wTop, wBase, wFront, wRear;
    LiveryWeights(p, n, wSide, wTop, wBase, wFront, wRear);
    half3 f = TapFinish(SideUv(p)) * wSide + TapFinish(AtlasUv(_ATop, _BTop, p)) * wTop + _FinishBase.rgb * wBase
        + TapFinish(AtlasUv(_AFront, _BFront, p)) * wFront + TapFinish(AtlasUv(_ARear, _BRear, p)) * wRear;
    return f / max(wSide + wTop + wBase + wFront + wRear, 1e-4);
}

#endif
