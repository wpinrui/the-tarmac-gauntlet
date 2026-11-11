// The ground round the circuit: the fields' colours from a map laid over the whole ground, times a
// fine grass grain repeating every 8 m. Both are made in code (Landscape.cs). The mesh's uvs are its
// position over 8 m, so the grain reads them as they are and the map scales them to the whole ground.
Shader "Hidden/TarmacGauntlet/Ground"
{
    Properties
    {
        _MainTex ("Fields", 2D) = "white" {}
        _Grain ("Grain", 2D) = "gray" {}
        _Glossiness ("Smoothness", Range(0, 1)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex, _Grain;
        float4 _MainTex_ST;
        half _Glossiness;

        struct Input { float2 uv_Grain; };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed3 fields = tex2D(_MainTex, IN.uv_Grain * _MainTex_ST.xy + _MainTex_ST.zw).rgb;
            o.Albedo = fields * tex2D(_Grain, IN.uv_Grain).rgb * 2;
            o.Smoothness = _Glossiness;
        }
        ENDCG
    }
}
