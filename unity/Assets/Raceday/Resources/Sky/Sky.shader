// The race's sky: a zenith to horizon gradient, the sunset glow towards the sun, the sun and moon,
// stars at night and two layers of drifting cloud. Every colour comes from TimeOfDay each frame.
Shader "Hidden/TarmacGauntlet/Sky"
{
    Properties
    {
        _CloudTex ("Cloud noise", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _CloudTex;
            float4 _Zenith, _Horizon, _Glow, _Ground, _SunColor, _CloudLit, _CloudShade;
            float4 _SunDir, _MoonDir;
            float4 _CloudOffset;   // xy the big layer, zw the small one
            float _Cover, _Night, _CloudScale;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Density(float2 uv)
            {
                return tex2D(_CloudTex, uv + _CloudOffset.xy).r * 0.7 + tex2D(_CloudTex, uv * 2.3 + _CloudOffset.zw).g * 0.3;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = d.y;
                float h = 1.0 - saturate(up);
                float3 sun = _SunDir.xyz;

                float3 sky = lerp(_Zenith.rgb, _Horizon.rgb, h * h * h * h);
                float2 dh = normalize(d.xz + 1e-5), sh = normalize(sun.xz + 1e-5);
                float towards = dot(dh, sh) * 0.5 + 0.5;
                float band = h * h * h * h * h * h;
                sky += _Glow.rgb * (towards * towards * towards * band);
                sky = lerp(sky, _Ground.rgb, saturate(-up * 6.0));

                float cs = dot(d, sun);
                float above = saturate(sun.y * 20.0 + 1.0);
                sky += _SunColor.rgb * (pow(saturate(cs), 12.0) * 0.18 + pow(saturate(cs), 400.0) * 0.6) * above;
                float disc = smoothstep(0.99985, 0.99992, cs) * above;

                float3 p = d * 500.0;
                float star = step(0.9975, Hash(floor(p))) * saturate(1.0 - length(frac(p) - 0.5) * 2.2);
                star *= _Night * saturate(up * 4.0);
                float moon = smoothstep(0.99990, 0.99994, dot(d, _MoonDir.xyz)) * saturate(_MoonDir.y * 20.0);

                float2 uv = d.xz / (max(up, 0.0) * 0.85 + 0.15) * _CloudScale;
                float n = Density(uv);
                float c = smoothstep(_Cover, _Cover + 0.18, n) * smoothstep(0.0, 0.12, up);
                float lit = saturate(0.55 + (n - Density(uv + sh * 0.035)) * 6.0);
                float3 cloud = lerp(_CloudShade.rgb, _CloudLit.rgb, lit);
                cloud += _SunColor.rgb * pow(saturate(cs), 6.0) * (1.0 - c) * 0.6 * above;
                cloud = lerp(cloud, _Horizon.rgb, band * 0.7);

                float3 col = sky + (_SunColor.rgb * 3.0 * disc + 0.9 * star + float3(0.85, 0.88, 0.95) * moon * _Night) * (1.0 - c);
                col = lerp(col, cloud, c);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
