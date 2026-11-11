// The pit crews' suits in their team's colours. The suit's own texture is navy cloth with red panels;
// the kit mask (tools/pit-crew/kit_masks.py) marks the cloth in red and the panels in green. Each is
// painted over by its own colour, kept shaded by the texture's folds, as far as that colour's alpha says:
// alpha 0 leaves the suit as it is. The colours are per instance, so every crew member in the race
// shares one material for each part of the suit.
Shader "Hidden/TarmacGauntlet/CrewKit"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _MainTex ("Colour", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _Mask ("Kit mask", 2D) = "black" {}
        _Glossiness ("Smoothness", Range(0, 1)) = 0.5
        _Cloth ("Cloth", Color) = (1, 1, 1, 0)
        _Panel ("Panels", Color) = (1, 1, 1, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.5
        #pragma multi_compile_instancing

        sampler2D _MainTex, _BumpMap, _Mask;
        half _Glossiness;
        fixed4 _Color;

        UNITY_INSTANCING_BUFFER_START(Kit)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _Cloth)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _Panel)
        UNITY_INSTANCING_BUFFER_END(Kit)

        struct Input
        {
            float2 uv_MainTex;
        };

        // The texture's own brightness over the part's usual brightness: the folds and seams to keep.
        half3 Shade(half3 colour, half value, half usual)
        {
            #ifndef UNITY_COLORSPACE_GAMMA
            usual = GammaToLinearSpaceExact(usual);
            #endif
            return colour * min(value / usual, 1.6);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            half3 raw = tex2D(_MainTex, IN.uv_MainTex).rgb;
            half2 m = tex2D(_Mask, IN.uv_MainTex).rg;
            half v = max(raw.r, max(raw.g, raw.b));
            half3 c = raw * _Color.rgb;
            fixed4 cloth = UNITY_ACCESS_INSTANCED_PROP(Kit, _Cloth);
            fixed4 panel = UNITY_ACCESS_INSTANCED_PROP(Kit, _Panel);
            half3 rgb = lerp(c, Shade(cloth.rgb, v, 0.36), m.r * cloth.a);
            rgb = lerp(rgb, Shade(panel.rgb, v, 0.74), m.g * panel.a);
            o.Albedo = rgb;
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            o.Smoothness = _Glossiness;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
