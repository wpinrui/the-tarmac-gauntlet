// A team's livery on a car's paint. The livery is flat art drawn on the car's five views (left,
// right, top, front, rear) and baked into one atlas by tools/liveries/common/bake.js. Each paint
// face takes the view it faces most, blended where views meet; faces pointing down, and side
// faces turned in towards the middle, keep the base colour. The race number is drawn into the
// livery's number boxes from the Digits atlas: the livery's own, or the shared one. The paint has the clear coat and
// metallic flakes of CarPaint.hlsl.
// Keep in step with the projection in tools/liveries/common/viewer.py. See docs/car-paint.md.
Shader "TarmacGauntlet/Livery"
{
    Properties
    {
        _Atlas ("Livery atlas", 2D) = "white" {}
        _Digits ("Digits", 2D) = "black" {}
        _Base ("Base colour", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.8
        _Metallic ("Metallic", Range(0, 1)) = 0.1
        [HideInInspector] _ALeft ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _BLeft ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ARight ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _BRight ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ATop ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _BTop ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _AFront ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _BFront ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ARear ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _BRear ("", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        // Batching would move vertices out of the model's own space, which the projection needs.
        Tags { "RenderType" = "Opaque" "DisableBatching" = "True" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _TARMAC_CLEARCOAT
            #pragma multi_compile_fragment _ _TARMAC_FLAKE
            #include_with_pragmas "Assets/Rendering/Shaders/LitKeywords.hlsl"
            #include "Assets/Raceday/Resources/CarPaint/CarPaint.hlsl"

            // Not in a UnityPerMaterial buffer: every part sets its own _ToCar, so the parts are drawn one by one anyway.
            TEXTURE2D(_Atlas);
            SAMPLER(sampler_Atlas);
            TEXTURE2D(_Digits);
            SAMPLER(sampler_Digits);
            half4 _Base;
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

            half4 frag(Varyings i) : SV_Target
            {
                float3 p = i.objPos, n = normalize(i.objNormal);
                float3 k = pow(abs(n), float3(6, 6, 6));
                float wSide = k.x, wTop = n.y > 0 ? k.y : 0, wBottom = n.y < 0 ? k.y : 0;
                float wFront = n.z > 0 ? k.z : 0, wRear = n.z < 0 ? k.z : 0;
                // Which side by where the face is. Faces turned in towards the middle (vent and
                // channel walls, inner door skins) keep the base colour: the side's art on them would
                // show back to front.
                float2 uvSide = p.x > 0 ? AtlasUv(_ALeft, _BLeft, p) : AtlasUv(_ARight, _BRight, p);
                float2 uvTop = AtlasUv(_ATop, _BTop, p), uvFront = AtlasUv(_AFront, _BFront, p), uvRear = AtlasUv(_ARear, _BRear, p);
                half3 side = n.x * p.x < 0 ? _Base.rgb : Number(uvSide, Tap(uvSide));
                half3 col = side * wSide + Number(uvTop, Tap(uvTop)) * wTop + _Base.rgb * wBottom
                    + Number(uvFront, Tap(uvFront)) * wFront + Number(uvRear, Tap(uvRear)) * wRear;
                half3 albedo = col / max(wSide + wTop + wBottom + wFront + wRear, 1e-4);
                return ShadeCarPaint(i.positionWS, i.normalWS, i.positionCS, i.fog, albedo, _Metallic, _Smoothness, p);
            }
            ENDHLSL
        }

        // The clear coat's view for the screen-space reflections (CarPaintReflect.hlsl).
        Pass
        {
            Name "TarmacReflect"
            Tags { "LightMode" = "TarmacReflect" }
            ZWrite Off ZTest LEqual Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CarReflectVertex
            #pragma fragment CarReflectFragment
            #pragma multi_compile_fragment _ _TARMAC_CLEARCOAT
            #include_with_pragmas "Assets/Rendering/Shaders/LitKeywords.hlsl"
            #include "Assets/Raceday/Resources/CarPaint/CarPaintReflect.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex TarmacShadowVertex
            #pragma fragment TarmacNullFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma vertex TarmacDepthVertex
            #pragma fragment TarmacNullFragment
            #include "Assets/Rendering/Shaders/TarmacLit.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TarmacMotionVertex
            #pragma fragment TarmacMotionFragment
            #include "Assets/Rendering/Shaders/TarmacMotion.hlsl"
            ENDHLSL
        }
    }
}
