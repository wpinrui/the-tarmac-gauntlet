// Rain streaks and wheel spray: unlit, soft edged particles drawn in the light around them.
// _Shape 0 fades a stretched quad to its edges for a streak, 1 draws a soft round puff. No texture is read.
Shader "Hidden/TarmacGauntlet/Drops"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Shape ("Shape", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            float4 _Tint;
            float _Shape;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Tint;
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                // A stretched particle is already a thin quad along its fall: fade it to every edge.
                float streak = saturate(1.0 - abs(p.x)) * saturate(1.0 - abs(p.y));
                float r = saturate(1.0 - length(p));
                float puff = r * r;
                fixed4 col = i.color;
                col.a *= lerp(streak, puff, _Shape);
                UNITY_APPLY_FOG(unity_FogColor, col);
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
