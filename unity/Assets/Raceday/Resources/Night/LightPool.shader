// A pool of light lying on the ground: the floodlights' pools and the cars' headlamp beams. It brightens what is
// already drawn under it, so the road, the lines and the grass keep their own colours: the first pass multiplies
// the colour behind by 1 + _Color, the second adds a little _Lift so black ground still shows the light. Both
// fade to nothing at the pool's edge, where its UVs reach 1, and into the fog with distance.
Shader "Hidden/TarmacGauntlet/LightPool"
{
    Properties
    {
        _Color ("Brightening", Color) = (0, 0, 0, 1)
        _Lift ("Lift", Color) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest LEqual
        Offset -1, -1

        CGINCLUDE
        #include "UnityCG.cginc"

        float4 _Color, _Lift;

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            UNITY_FOG_COORDS(1)
        };

        v2f vert(appdata v)
        {
            v2f o;
            UNITY_SETUP_INSTANCE_ID(v);
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.uv;
            UNITY_TRANSFER_FOG(o, o.pos);
            return o;
        }

        float Falloff(float2 uv)
        {
            float f = saturate(1.0 - dot(uv, uv));
            return f * f;
        }
        ENDCG

        Pass
        {
            Blend DstColor One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            float4 frag(v2f i) : SV_Target
            {
                float4 c = float4(_Color.rgb * Falloff(i.uv), 0);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, float4(0, 0, 0, 0));
                return c;
            }
            ENDCG
        }

        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            float4 frag(v2f i) : SV_Target
            {
                float4 c = float4(_Lift.rgb * Falloff(i.uv), 0);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, float4(0, 0, 0, 0));
                return c;
            }
            ENDCG
        }
    }
}
