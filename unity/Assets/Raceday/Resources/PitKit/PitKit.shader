// The pit box kit's paint, one material for every garage: the colour is per instance, so the tyre blankets in
// 100 team colours are drawn in one instanced call.
Shader "Hidden/TarmacGauntlet/PitKit"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.35
        _Metallic ("Metallic", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.5
        #pragma multi_compile_instancing

        half _Glossiness, _Metallic;

        UNITY_INSTANCING_BUFFER_START(Kit)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
        UNITY_INSTANCING_BUFFER_END(Kit)

        struct Input
        {
            float3 worldPos;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = UNITY_ACCESS_INSTANCED_PROP(Kit, _Color).rgb;
            o.Smoothness = _Glossiness;
            o.Metallic = _Metallic;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
