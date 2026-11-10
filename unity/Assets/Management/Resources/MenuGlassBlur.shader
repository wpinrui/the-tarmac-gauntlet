// The frosted glass behind the menus: the scene, shrunk, blurred and made more colourful.
// Pass 0 shrinks with a 4-tap box, pass 1 blurs across, pass 2 blurs down and saturates.
Shader "Hidden/TarmacGauntlet/MenuGlassBlur"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float _Sigma;      // blur radius in texels of the source
        float _Saturation; // 1 leaves colours alone

        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

        v2f vert(appdata_img v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord;
            return o;
        }

        fixed4 shrink(v2f i) : SV_Target
        {
            float2 d = _MainTex_TexelSize.xy * 0.5;
            fixed4 c = tex2D(_MainTex, i.uv + float2(-d.x, -d.y));
            c += tex2D(_MainTex, i.uv + float2(d.x, -d.y));
            c += tex2D(_MainTex, i.uv + float2(-d.x, d.y));
            c += tex2D(_MainTex, i.uv + float2(d.x, d.y));
            return c * 0.25;
        }

        // 25 taps two texels apart; bilinear filtering fills in between, so the reach is about 3 sigma for sigma up to 16.
        fixed4 gauss(float2 uv, float2 dir)
        {
            float3 sum = 0;
            float total = 0;
            float s2 = 2.0 * max(_Sigma * _Sigma, 0.01);
            [unroll] for (int k = -12; k <= 12; k++)
            {
                float x = k * 2.0;
                float w = exp(-(x * x) / s2);
                sum += tex2D(_MainTex, uv + dir * x * _MainTex_TexelSize.xy).rgb * w;
                total += w;
            }
            return fixed4(sum / total, 1);
        }

        fixed4 across(v2f i) : SV_Target { return gauss(i.uv, float2(1, 0)); }

        fixed4 down(v2f i) : SV_Target
        {
            fixed4 c = gauss(i.uv, float2(0, 1));
            float lum = dot(c.rgb, float3(0.2126, 0.7152, 0.0722));
            c.rgb = max(0, lerp(lum.xxx, c.rgb, _Saturation));
            return c;
        }
        ENDCG

        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment shrink
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment across
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment down
            ENDCG }
    }
    Fallback Off
}
